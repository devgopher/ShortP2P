using System.Collections.Concurrent;
using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Discovery;
using ShortP2P.Transport;

namespace ShortP2P.Client.LocalNetwork;

/// <summary>
///     Сканирование локальной сети по discovery-пингам (UDP broadcast на порт 565 и приём на том же порту;
///     при наличии Bluetooth — те же кадры по RFCOMM). Список клиентов обновляется по входящим пингам.
/// </summary>
public sealed class LocalNetworkScanner : IAsyncDisposable
{
    private static readonly TimeSpan BroadcastInterval = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(45);

    private readonly SharedUserUdpGateway _gateway;

    private readonly ConcurrentDictionary<Guid, DiscoveredLocalPeer> _entries = new();
    private readonly object _snapshotSync = new();
    private IReadOnlyList<DiscoveredLocalPeer> _snapshot = Array.Empty<DiscoveredLocalPeer>();

    private CancellationTokenSource? _cts;
    private Task? _broadcastLoop;
    private Task? _staleLoop;
    private UserEntity? _user;

    public LocalNetworkScanner(SharedUserUdpGateway gateway) => _gateway = gateway;

    /// <summary>Снимок последних найденных пиров (кроме текущего пользователя).</summary>
    public IReadOnlyList<DiscoveredLocalPeer> Clients
    {
        get
        {
            lock (_snapshotSync)
                return _snapshot;
        }
    }

    public event EventHandler? ClientsChanged;

    public async Task StartAsync(UserEntity user, CancellationToken cancellationToken = default)
    {
        if (_cts != null)
            return;
        _user = user;
        _gateway.DiscoveryPingReceived += OnDiscoveryPingReceived;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;
        _broadcastLoop = Task.Run(() => BroadcastLoopAsync(token), token);
        _staleLoop = Task.Run(() => StaleLoopAsync(token), token);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts == null)
            return;
        _gateway.DiscoveryPingReceived -= OnDiscoveryPingReceived;
        try
        {
            _cts?.Cancel();
        }
        catch
        {
        }

        var loops = new[] { _broadcastLoop, _staleLoop }.Where(t => t != null).ToArray();
        _broadcastLoop = null;
        _staleLoop = null;
        if (loops.Length > 0)
        {
            try
            {
                await Task.WhenAll(loops!).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts?.Dispose();
        _cts = null;
        _user = null;
        _entries.Clear();
        lock (_snapshotSync)
            _snapshot = Array.Empty<DiscoveredLocalPeer>();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private void OnDiscoveryPingReceived(object? sender, DiscoveryPingReceivedEventArgs e)
    {
        var u = _user;
        if (u == null) return;
        var myId = CompressedNetworkId.FromShortString(u.NetworkIdShort).Value;
        if (e.Peer.NetworkId == myId) return;

        var peer = e.Peer;
        _entries.AddOrUpdate(peer.NetworkId, peer, (_, _) => peer);
        RebuildSnapshot();
        ClientsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildSnapshot()
    {
        var list = _entries.Values
            .OrderBy(p => p.Nickname, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.NetworkId)
            .ToList();
        lock (_snapshotSync)
            _snapshot = list;
    }

    private async Task BroadcastLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var u = _user;
                if (u != null)
                {
                    var payload = PresencePingCodec.Build(
                        CompressedNetworkId.FromShortString(u.NetworkIdShort).Value,
                        u.Nickname);
                    foreach (var ep in LanBroadcastHelper.GetIpv4BroadcastEndpoints(PresencePingCodec.UdpPort))
                    {
                        try
                        {
                            var addr = UdpTransportAddress.FromIPEndPoint(ep);
                            await _gateway.SendOnPresencePortAsync(payload, addr, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch
                        {
                            // один broadcast может быть недоступен
                        }
                    }
                }

                await Task.Delay(BroadcastInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                try
                {
                    await Task.Delay(BroadcastInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task StaleLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var cutoff = DateTimeOffset.UtcNow - StaleAfter;
            var removed = false;
            foreach (var kv in _entries.ToArray())
            {
                if (kv.Value.LastSeenUtc >= cutoff) continue;
                if (_entries.TryRemove(kv.Key, out _))
                    removed = true;
            }

            if (!removed) continue;
            RebuildSnapshot();
            ClientsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
