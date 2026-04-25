using System.Data;
using System.Net;
using Microsoft.EntityFrameworkCore;
using ShortP2P.Discovery.Gossip;
using ShortP2P.Discovery.RouteTables;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Strategies;

/// <summary>
///     <see cref="FindAsync" /> — поиск цепочек из локальной БД.
///     <see cref="UpdateRoutesAsync" /> — периодическое обновление Route/PeerChain по свежим discovery-пингам.
/// </summary>
public sealed class GossipStrategy(
    IDbContextFactory<RouteDbContext> dbContextFactory,
    IPeerDiscoveryService? peerDiscoveryService = null) : IDiscoveryStrategy
{
    private const int MaxPeerChainDepth = 5;
    private readonly IDbContextFactory<RouteDbContext> _dbContextFactory =
        dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    private readonly IPeerDiscoveryService? _peerDiscoveryService = peerDiscoveryService;

    public string Name => "gossip";

    public async Task<Route[]> UpdateRoutesAsync(int deepness = 3, CancellationToken cancellationToken = default)
    {
        var cappedDepth = Math.Clamp(deepness, 1, MaxPeerChainDepth);
        var pings = _peerDiscoveryService?.GetPeersSnapshot() ?? [];

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await UpsertRoutesByPingsAsync(db, pings, cancellationToken).ConfigureAwait(false);

        var targets = pings
            .Select(p => p.Identity.NetworkId.Value)
            .ToHashSet();
        var fetchedChains = new Dictionary<string, PeerChain>(StringComparer.Ordinal);
        foreach (var chain in BuildDirectPeerChainsFromPings(pings))
            fetchedChains.TryAdd(chain.ChainKey, chain);

        var allKnownAddresses = await CollectAddressesByPeerAsync(db, pings, cancellationToken).ConfigureAwait(false);
        var localSenderId = _peerDiscoveryService?.LocalPeer.NetworkId.Value ?? Guid.Empty;
        foreach (var remoteEndpoint in allKnownAddresses.SelectMany(kv => kv.Value))
        {
            var routes = await RouteTableWireClient.QueryRoutesAsync(
                    remoteEndpoint,
                    localSenderId,
                    waitTimeout: TimeSpan.FromSeconds(2),
                    cancellationToken)
                .ConfigureAwait(false);
            if (routes.Count == 0)
                continue;

            foreach (var chain in BuildPeerChains(routes, cappedDepth, targets))
                fetchedChains.TryAdd(chain.ChainKey, chain);
        }

        await ReplacePeerChainsAsync(db, fetchedChains.Values, cancellationToken).ConfigureAwait(false);

        return await db.Routes
            .AsNoTracking()
            .Include(r => r.PeerRoutes.OrderByDescending(pr => pr.LastSeen))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PeerChain[]> FindAsync(CompressedNetworkId networkId, int deepness = 5,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var cappedDepth = Math.Clamp(deepness, 1, MaxPeerChainDepth);

        var chains = await db.PeerChains
            .AsNoTracking()
            .Include(c => c.PeerChainNodes.OrderBy(n => EF.Property<int>(n, "OrderIndex")))
            .OrderByDescending(c => c.UpdatedAtUtc)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return chains
            .Select(chain => TrimByTarget(chain, networkId.Value, cappedDepth))
            .Where(chain => chain != null)
            .Cast<PeerChain>()
            .ToArray();
    }

    private static PeerIdentityAddress Clone(PeerIdentityAddress source) => new()
    {
        RouteId = source.RouteId,
        PeerIdentity = source.PeerIdentity,
        PeerAddress = source.PeerAddress,
        LastSeen = source.LastSeen
    };

    private static PeerChain? TrimByTarget(PeerChain chain, Guid targetNetworkId, int maxDepth)
    {
        if (chain.PeerChainNodes.Count == 0)
            return null;

        var searchLimit = Math.Min(chain.PeerChainNodes.Count, maxDepth);
        var targetIndex = -1;
        for (var i = 0; i < searchLimit; i++)
        {
            if (chain.PeerChainNodes[i].PeerIdentity.NetworkId.Value != targetNetworkId)
                continue;
            targetIndex = i;
            break;
        }

        if (targetIndex < 0)
            return null;

        var trimmedNodes = chain.PeerChainNodes
            .Take(targetIndex + 1)
            .Select(Clone)
            .ToList();
        
        return new PeerChain
        {
            SourceRouteId = chain.SourceRouteId,
            TargetNetworkId = targetNetworkId,
            ChainKey = $"{chain.ChainKey}|to:{targetNetworkId:N}|d:{targetIndex + 1}",
            UpdatedAtUtc = chain.UpdatedAtUtc,
            PeerChainNodes = trimmedNodes
        };
    }

    private static async Task UpsertRoutesByPingsAsync(RouteDbContext db, IReadOnlyCollection<DiscoveredPeer> pings,
        CancellationToken cancellationToken)
    {
        if (pings.Count == 0)
            return;

        var routeIds = pings
            .Select(p => p.Identity.NetworkId.ToShortString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var existingRoutes = await db.Routes
            .Include(r => r.PeerRoutes)
            .Where(r => routeIds.Contains(r.RouteId))
            .ToDictionaryAsync(r => r.RouteId, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        foreach (var ping in pings)
        {
            var routeId = ping.Identity.NetworkId.ToShortString();
            var address = ToIpString(ping.DataReachableAt) ?? ToIpString(ping.ReachableAt);
            if (string.IsNullOrWhiteSpace(address))
                continue;

            if (!existingRoutes.TryGetValue(routeId, out var route))
            {
                route = new Route
                {
                    RouteId = routeId,
                    PeerRoutes = []
                };
                db.Routes.Add(route);
                existingRoutes[routeId] = route;
            }

            var existingPeer = route.PeerRoutes.FirstOrDefault(p =>
                p.PeerIdentity.NetworkId.Value == ping.Identity.NetworkId.Value &&
                string.Equals(p.PeerAddress, address, StringComparison.OrdinalIgnoreCase));
            if (existingPeer == null)
            {
                route.PeerRoutes.Add(new PeerIdentityAddress
                {
                    RouteId = routeId,
                    PeerIdentity = ping.Identity,
                    PeerAddress = address,
                    LastSeen = ping.LastSeenUtc.UtcDateTime
                });
                continue;
            }

            existingPeer.LastSeen = ping.LastSeenUtc.UtcDateTime;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Dictionary<Guid, HashSet<IPEndPoint>>> CollectAddressesByPeerAsync(RouteDbContext db,
        IReadOnlyCollection<DiscoveredPeer> pings, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, HashSet<IPEndPoint>>();
        var targetIds = pings.Select(p => p.Identity.NetworkId.Value).ToHashSet();
        foreach (var ping in pings)
        {
            AddUdpEndpoint(result, ping.Identity.NetworkId.Value, ping.ReachableAt);
            AddUdpEndpoint(result, ping.Identity.NetworkId.Value, ping.DataReachableAt);
        }

        if (targetIds.Count == 0)
            return result;

        var knownAddresses = await db.Routes
            .AsNoTracking()
            .SelectMany(r => r.PeerRoutes)
            .Where(pr => targetIds.Contains(pr.PeerIdentity.NetworkId.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var address in knownAddresses)
        {
            if (!IPAddress.TryParse(address.PeerAddress, out var ip))
                continue;
            AddEndpoint(result, address.PeerIdentity.NetworkId.Value, new IPEndPoint(ip, GossipWireCodec.UdpPort));
        }

        return result;
    }

    private static void AddUdpEndpoint(IDictionary<Guid, HashSet<IPEndPoint>> byPeer, Guid peerId, TransportAddress address)
    {
        try
        {
            var ep = UdpTransportAddress.ToIPEndPoint(address);
            AddEndpoint(byPeer, peerId, new IPEndPoint(ep.Address, GossipWireCodec.UdpPort));
        }
        catch
        {
            // only UDP addresses are supported for gossip query
        }
    }

    private static void AddEndpoint(IDictionary<Guid, HashSet<IPEndPoint>> byPeer, Guid peerId, IPEndPoint endpoint)
    {
        if (!byPeer.TryGetValue(peerId, out var set))
        {
            set = [];
            byPeer[peerId] = set;
        }

        set.Add(endpoint);
    }

    private static string? ToIpString(TransportAddress address)
    {
        try
        {
            var ep = UdpTransportAddress.ToIPEndPoint(address);
            return ep.Address.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyCollection<PeerChain> BuildPeerChains(IReadOnlyCollection<Route> routes, int deepness,
        IReadOnlySet<Guid> targetPeerIds)
    {
        var routesById = routes
            .Where(r => !string.IsNullOrWhiteSpace(r.RouteId))
            .ToDictionary(r => r.RouteId, StringComparer.Ordinal);
        var result = new Dictionary<string, PeerChain>(StringComparer.Ordinal);
        foreach (var root in routesById.Values)
        {
            var path = new Stack<PeerIdentityAddress>();
            var visited = new HashSet<string>(StringComparer.Ordinal) { root.RouteId };
            Expand(root.RouteId, 0);

            void Expand(string routeId, int depth)
            {
                if (depth >= deepness || !routesById.TryGetValue(routeId, out var route))
                    return;

                foreach (var hop in route.PeerRoutes)
                {
                    var nextRouteId = hop.PeerIdentity.NetworkId.ToShortString();
                    if (!visited.Add(nextRouteId))
                        continue;

                    path.Push(Clone(hop));
                    if (targetPeerIds.Contains(hop.PeerIdentity.NetworkId.Value))
                    {
                        var nodes = path.Reverse().ToList();
                        var key = BuildChainKey(root.RouteId, nodes);
                        result.TryAdd(key, new PeerChain
                        {
                            SourceRouteId = root.RouteId,
                            TargetNetworkId = nodes[^1].PeerIdentity.NetworkId.Value,
                            ChainKey = key,
                            UpdatedAtUtc = DateTime.UtcNow,
                            PeerChainNodes = nodes
                        });
                    }

                    Expand(nextRouteId, depth + 1);
                    path.Pop();
                    visited.Remove(nextRouteId);
                }
            }
        }

        return result.Values.ToArray();
    }

    private static string BuildChainKey(string sourceRouteId, IReadOnlyList<PeerIdentityAddress> nodes)
    {
        var hops = string.Join("->", nodes.Select(n =>
            $"{n.PeerIdentity.NetworkId.ToShortString()}@{n.PeerAddress}"));
        return $"{sourceRouteId}|{hops}";
    }

    private static async Task ReplacePeerChainsAsync(RouteDbContext db, IEnumerable<PeerChain> chains,
        CancellationToken cancellationToken)
    {
        var materialized = chains.ToArray();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        await db.PeerChains.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        if (materialized.Length > 0)
        {
            db.PeerChains.AddRange(materialized);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyCollection<PeerChain> BuildDirectPeerChainsFromPings(IReadOnlyCollection<DiscoveredPeer> pings)
    {
        var sourceRouteId = _peerDiscoveryService?.LocalPeer.NetworkId.ToShortString() ?? "local";
        var result = new Dictionary<string, PeerChain>(StringComparer.Ordinal);
        foreach (var ping in pings)
        {
            var address = ToIpString(ping.DataReachableAt) ?? ToIpString(ping.ReachableAt);
            if (string.IsNullOrWhiteSpace(address))
                continue;
            var node = new PeerIdentityAddress
            {
                RouteId = ping.Identity.NetworkId.ToShortString(),
                PeerIdentity = ping.Identity,
                PeerAddress = address,
                LastSeen = ping.LastSeenUtc.UtcDateTime
            };
            var key = BuildChainKey(sourceRouteId, [node]);
            result.TryAdd(key, new PeerChain
            {
                SourceRouteId = sourceRouteId,
                TargetNetworkId = ping.Identity.NetworkId.Value,
                ChainKey = key,
                UpdatedAtUtc = DateTime.UtcNow,
                PeerChainNodes = [node]
            });
        }

        return result.Values.ToArray();
    }
}
