using System.Collections.Concurrent;
using System.Net;
using ShortP2P.Client.Data;
using ShortP2P.Client.Services;
using ShortP2P.Crypto;
using ShortP2P.Discovery;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Routing;

/// <summary>Единый UDP-сокет пользователя: поиск пиров по графу (≤3 рёбер), ретрансляция, демультиплексирование чата.</summary>
public sealed class SharedUserUdpGateway(AuthService auth, ChatRepository chats, P2pRoutingSettings settings)
    : IAsyncDisposable
{
    private IPeerDiscoveryService? _discovery;

    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<PeerSearchResult>> _searchWaits = new();
    private readonly ConcurrentDictionary<(Guid Sid, string HopKey), TransportAddress> _foundReturnPath = new();

    private UdpTransport? _udp;
    private UserEntity? _user;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private Func<ReadOnlyMemory<byte>, TransportAddress, Task>? _chatSink;
    private readonly object _startSync = new();

    public void SetDiscovery(IPeerDiscoveryService? discovery) => _discovery = discovery;

    public void SetChatSink(Func<ReadOnlyMemory<byte>, TransportAddress, Task>? sink) => _chatSink = sink;

    public async Task EnsureStartedAsync(UserEntity user, CancellationToken cancellationToken = default)
    {
        lock (_startSync)
        {
            if (_udp != null)
            {
                _user = user;
                return;
            }

            _user = user;
            _udp = new UdpTransport(user.DataUdpPort);
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
        }

        await _udp.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? loop;
        lock (_startSync)
        {
            _cts?.Cancel();
            loop = _receiveLoop;
            _receiveLoop = null;
            _cts?.Dispose();
            _cts = null;
            if (_udp != null)
            {
                var u = _udp;
                _udp = null;
                _ = u.StopAsync(cancellationToken);
            }

            _user = null;
        }

        if (loop != null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        foreach (var kv in _searchWaits)
            kv.Value.TrySetCanceled();
        _searchWaits.Clear();
        _foundReturnPath.Clear();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    public async Task<PeerSearchResult?> SearchPeerAsync(Guid targetNetworkId, string targetNickname,
        CancellationToken cancellationToken = default)
    {
        var user = _user;
        var udp = _udp;
        if (user == null || udp == null)
            return null;

        var searchId = Guid.NewGuid();
        var tcs = new TaskCompletionSource<PeerSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _searchWaits[searchId] = tcs;

        try
        {
            var visited = new List<Guid> { CompressedNetworkId.FromShortString(user.NetworkIdShort).Value };
            var path = new List<TransportAddress>();
            var ttl = (byte)Math.Clamp(settings.MaxSearchHops, 1, 3);
            var packet = LanRoutingCodec.BuildFind(searchId, targetNetworkId, targetNickname, ttl, visited, path);

            foreach (var nb in await CollectNeighborsAsync(user).ConfigureAwait(false))
                try
                {
                    await udp.SendAsync(packet, nb, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // ignore single neighbor failure
                }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(settings.SearchWaitTimeout);
            try
            {
                return await tcs.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }
        finally
        {
            _searchWaits.TryRemove(searchId, out _);
        }
    }

    public async ValueTask SendP2pPayloadAsync(ReadOnlyMemory<byte> prefixedFrame, ChatRelayRoute route,
        CancellationToken cancellationToken = default)
    {
        var udp = _udp ?? throw new InvalidOperationException("Gateway not started.");
        if (route.FirstHop != null && route.RelayStrip.Count > 0)
        {
            var relay = LanRoutingCodec.BuildRelay(route.RelayStrip, prefixedFrame.Span);
            await udp.SendAsync(relay, route.FirstHop, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await udp.SendAsync(prefixedFrame, route.Direct, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<TransportAddress>> CollectNeighborsAsync(UserEntity user)
    {
        var set = new Dictionary<string, TransportAddress>(StringComparer.Ordinal);
        void Add(TransportAddress a)
        {
            var key = Convert.ToHexString(a.Data);
            set.TryAdd(key, a);
        }

        if (_discovery != null)
            foreach (var p in _discovery.GetPeersSnapshot())
                Add(p.DataReachableAt);

        foreach (var c in await chats.ListChatsAsync(user.Id).ConfigureAwait(false))
        {
            try
            {
                var ep = new IPEndPoint(IPAddress.Parse(c.PeerHost), c.PeerPort);
                Add(UdpTransportAddress.FromIPEndPoint(ep));
            }
            catch
            {
                // skip bad chat row
            }
        }

        return set.Values.ToList();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var udp = _udp;
        if (udp == null) return;
        try
        {
            await foreach (var msg in udp.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var buf = msg.Payload.ToArray();
                if (buf.Length == 0)
                    continue;

                var relayLocalInner = ExtractLocalRelayInner(buf);
                if (relayLocalInner is { Length: > 0 })
                {
                    if (relayLocalInner[0] == ChatInviteCodec.FrameChatInvite)
                        await HandleChatInviteAsync(relayLocalInner, cancellationToken).ConfigureAwait(false);
                    else
                        await DispatchChatOrDropAsync(relayLocalInner, msg.RemoteAddress).ConfigureAwait(false);
                    continue;
                }

                if (buf[0] == LanRoutingCodec.FrameRelay && buf.Length > 2 && buf[1] > 0)
                {
                    if (LanRoutingCodec.TryStripRelayHop(buf, out var next, out var fwd) && next != null && fwd != null)
                    {
                        try
                        {
                            await udp.SendAsync(fwd, next, cancellationToken).ConfigureAwait(false);
                        }
                        catch
                        {
                            // ignore
                        }
                    }

                    continue;
                }

                if (buf[0] == LanRoutingCodec.FrameFind)
                {
                    await HandleFindAsync(buf, msg.RemoteAddress, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (buf[0] == LanRoutingCodec.FrameFound)
                {
                    await HandleFoundAsync(buf, msg.RemoteAddress, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (buf[0] == ChatInviteCodec.FrameChatInvite)
                {
                    await HandleChatInviteAsync(buf, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await DispatchChatOrDropAsync(buf, msg.RemoteAddress).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task HandleChatInviteAsync(byte[] packet, CancellationToken ct)
    {
        await IncomingChatInviteHandler.TryAcceptAsync(packet, auth, chats, ct).ConfigureAwait(false);
    }

    private async Task DispatchChatOrDropAsync(ReadOnlyMemory<byte> buf, TransportAddress from)
    {
        var sink = _chatSink;
        if (sink != null)
            await sink(buf, from).ConfigureAwait(false);
    }

    private async Task HandleFindAsync(byte[] packet, TransportAddress from, CancellationToken ct)
    {
        var udp = _udp;
        var user = _user;
        if (udp == null || user == null) return;
        if (!LanRoutingCodec.TryParseFind(packet, out var searchId, out var targetNet, out var nick, out var ttl,
                out var visited, out var path))
            return;

        var myId = CompressedNetworkId.FromShortString(user.NetworkIdShort).Value;
        if (visited.Contains(myId))
            return;

        var nickOk = string.Equals(nick.Trim(), user.Nickname.Trim(), StringComparison.Ordinal);
        var idOk = targetNet == myId;
        if (idOk && nickOk)
        {
            var host = LocalEndpointHelper.GetPreferredLanIPv4String();
            var selfAddr = UdpTransportAddress.FromIPEndPoint(new IPEndPoint(IPAddress.Parse(host), user.DataUdpPort));
            TransportAddress? first = null;
            List<TransportAddress> strip = new();
            if (path.Count > 0)
            {
                first = path[0];
                for (var i = 1; i < path.Count; i++)
                    strip.Add(path[i]);
                strip.Add(selfAddr);
            }

            var found = LanRoutingCodec.BuildFound(searchId, targetNet, user.Nickname,
                RsaKeySerializer.SerializePublic(auth.GetCurrentPublicKey()), host, user.DataUdpPort, first, strip);
            try
            {
                await udp.SendAsync(found, from, ct).ConfigureAwait(false);
            }
            catch
            {
            }

            return;
        }

        if (ttl <= 1)
            return;

        var newVisited = new List<Guid>(visited) { myId };
        var myAddr = UdpTransportAddress.FromIPEndPoint(
            new IPEndPoint(IPAddress.Parse(LocalEndpointHelper.GetPreferredLanIPv4String()), user.DataUdpPort));
        var newPath = new List<TransportAddress>(path) { myAddr };
        var newTtl = (byte)(ttl - 1);
        var forwarded = LanRoutingCodec.BuildFind(searchId, targetNet, nick, newTtl, newVisited, newPath);

        foreach (var nb in await CollectNeighborsAsync(user).ConfigureAwait(false))
        {
            if (AddrEquals(nb, from))
                continue;
            if (await NeighborIdAsync(nb, user).ConfigureAwait(false) is { } gid && newVisited.Contains(gid))
                continue;
            _foundReturnPath[(searchId, AddrKey(nb))] = from;
            try
            {
                await udp.SendAsync(forwarded, nb, ct).ConfigureAwait(false);
            }
            catch
            {
                _foundReturnPath.TryRemove((searchId, AddrKey(nb)), out _);
            }
        }
    }

    private async Task<Guid?> NeighborIdAsync(TransportAddress nb, UserEntity user)
    {
        if (_discovery != null)
            foreach (var p in _discovery.GetPeersSnapshot())
                if (AddrEquals(p.DataReachableAt, nb))
                    return p.Identity.NetworkId.Value;

        foreach (var c in await chats.ListChatsAsync(user.Id).ConfigureAwait(false))
        {
            try
            {
                var ep = UdpTransportAddress.FromIPEndPoint(new IPEndPoint(IPAddress.Parse(c.PeerHost), c.PeerPort));
                if (!AddrEquals(ep, nb))
                    continue;
                return CompressedNetworkId.FromShortString(c.PeerNetworkIdShort).Value;
            }
            catch
            {
            }
        }

        return null;
    }

    private async Task HandleFoundAsync(byte[] packet, TransportAddress from, CancellationToken ct)
    {
        var udp = _udp;
        if (udp == null) return;
        if (!LanRoutingCodec.TryParseFound(packet, out var searchId, out _, out _, out var pub, out var host,
                out var port, out var firstHop, out var strip))
            return;

        if (_searchWaits.TryGetValue(searchId, out var tcs))
        {
            tcs.TrySetResult(new PeerSearchResult
            {
                PeerHost = host,
                PeerPort = port,
                RsaPublicJson = pub,
                FirstRelayHop = firstHop,
                RelayStrip = strip,
            });
            return;
        }

        var key = (searchId, AddrKey(from));
        if (_foundReturnPath.TryRemove(key, out var upstream))
        {
            try
            {
                await udp.SendAsync(packet, upstream, ct).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private static bool AddrEquals(TransportAddress a, TransportAddress b) =>
        a.Kind == b.Kind && a.Data.AsSpan().SequenceEqual(b.Data);

    private static string AddrKey(TransportAddress a) => Convert.ToHexString(a.Data);

    /// <summary>Возвращает полезную нагрузку, если это локальная доставка RELAY (hop count 0).</summary>
    private static byte[]? ExtractLocalRelayInner(byte[] buf)
    {
        if (buf.Length < 3 || buf[0] != LanRoutingCodec.FrameRelay || buf[1] != 0)
            return null;
        return buf.AsSpan(2).ToArray();
    }
}
