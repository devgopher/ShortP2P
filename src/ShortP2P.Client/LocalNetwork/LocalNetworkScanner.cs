using System.Collections.Concurrent;
using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Discovery;
using ShortP2P.Transport;

namespace ShortP2P.Client.LocalNetwork;

/// <summary>
///     Сканирование локальной сети по discovery-пингам: UDP на broadcast-адрес каждой локальной IPv4-подсети
///     и на 255.255.255.255, порт <see cref="PresencePingCodec.UdpPort" />; фоновая рассылка своего пинга каждые 15 с,
///     дополнительно по запросу UI (<see cref="ScanAsync" />, <see cref="TriggerScanAsync" />). Приём на порту 565;
///     сырые пинги чужих пиров — в <see cref="DiscoveryPingReceived" /> (подписчик не должен блокировать цикл приёма).
/// </summary>
public sealed class LocalNetworkScanner(P2pRoutingSettings routingSettings) : IAsyncDisposable
{
    /// <summary>Интервал фоновой рассылки discovery (broadcast по подсетям).</summary>
    private static readonly TimeSpan PeriodicLanScanInterval = TimeSpan.FromSeconds(15);

    /// <summary>Удалять пира из списка, если не было пинга дольше этого (несколько периодов рассылки).</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(90);

    /// <summary>Длительность приёма пингов при ручном сканировании по умолчанию.</summary>
    public static readonly TimeSpan DefaultScanListenDuration = TimeSpan.FromSeconds(45);

    private readonly P2pRoutingSettings _routingSettings = routingSettings;

    private volatile bool _scanSessionActive;

    private readonly ConcurrentDictionary<Guid, DiscoveredLocalPeer> _entries = new();
    private readonly object _snapshotSync = new();
    private IReadOnlyList<DiscoveredLocalPeer> _snapshot = [];

    private CancellationTokenSource? _cts;
    private UdpTransport? _presenceUdp;
    private Task? _presenceReceiveLoop;
    private Task? _periodicScanLoop;
    private Task? _staleLoop;
    private UserEntity? _user;

    /// <summary>Снимок последних найденных пиров (кроме текущего пользователя).</summary>
    public IReadOnlyList<DiscoveredLocalPeer> Clients
    {
        get
        {
            lock (_snapshotSync)
                return _snapshot;
        }
    }

    /// <summary>
    ///     Есть недавний presence-пинг с данным short network id (тот же порог, что и для «протухания» списка LAN).
    /// </summary>
    public bool IsPeerSeenRecentlyOnLan(string peerNetworkIdShort)
    {
        if (string.IsNullOrWhiteSpace(peerNetworkIdShort))
            return false;
        Guid id;
        try
        {
            id = CompressedNetworkId.FromShortString(peerNetworkIdShort.Trim()).Value;
        }
        catch (FormatException)
        {
            return false;
        }

        var cutoff = DateTimeOffset.UtcNow - StaleAfter;
        return _entries.TryGetValue(id, out var p) && p.LastSeenUtc >= cutoff;
    }

    public event EventHandler? ClientsChanged;

    /// <summary>Принят чужой presence/discovery-пинг (не свой network id); обработчик не должен долго блокировать поток приёма.</summary>
    public event EventHandler<DiscoveryPingReceivedEventArgs>? DiscoveryPingReceived;

    public async Task StartAsync(UserEntity user, CancellationToken cancellationToken = default)
    {
        if (_cts != null)
            return;
        _user = user;
        _presenceUdp = new UdpTransport(PresencePingCodec.UdpPort, enableBroadcast: true);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;
        await _presenceUdp.StartAsync(cancellationToken).ConfigureAwait(false);
        _presenceReceiveLoop = Task.Run(() => PresenceReceiveLoopAsync(token), token);
        _periodicScanLoop = Task.Run(() => PeriodicScanLoopAsync(token), token);
        _staleLoop = Task.Run(() => StaleLoopAsync(token), token);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts == null)
            return;

        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        var loops = new[] { _presenceReceiveLoop, _periodicScanLoop, _staleLoop }.Where(t => t != null).ToArray();
        _presenceReceiveLoop = null;
        _periodicScanLoop = null;
        _staleLoop = null;
        if (loops.Length > 0)
        {
            try
            {
                await Task.WhenAll(loops!).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }

        if (_presenceUdp != null)
        {
            var p = _presenceUdp;
            _presenceUdp = null;
            try
            {
                await p.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        _cts.Dispose();
        _cts = null;
        _user = null;
        _entries.Clear();
        lock (_snapshotSync)
            _snapshot = [];
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    /// <summary>Очистить список найденных (например перед ручным сканированием).</summary>
    public void ClearDiscoveredClients()
    {
        _entries.Clear();
        RebuildSnapshot();
        if (!_scanSessionActive)
            ClientsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Один раунд discovery: UDP broadcast по каждой подсети (+ limited broadcast).</summary>
    public Task TriggerScanAsync(CancellationToken cancellationToken = default) =>
        SendDiscoveryBroadcastRoundAsync(cancellationToken);

    /// <summary>
    ///     Ручное сканирование: очистка списка, раунд broadcast discovery, приём пингов <paramref name="listenDuration" />,
    ///     затем одно обновление <see cref="Clients" /> и событие <see cref="ClientsChanged" />.
    /// </summary>
    public async Task ScanAsync(TimeSpan listenDuration, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(listenDuration, TimeSpan.Zero);

        _scanSessionActive = true;
        try
        {
            _entries.Clear();
            RebuildSnapshot();
            ClientsChanged?.Invoke(this, EventArgs.Empty);

            await SendDiscoveryBroadcastRoundAsync(cancellationToken).ConfigureAwait(false);
            if (listenDuration > TimeSpan.Zero)
                await Task.Delay(listenDuration, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _scanSessionActive = false;
        }

        RebuildSnapshot();
        ClientsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task PresenceReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var presenceUdp = _presenceUdp;
        if (presenceUdp == null)
            return;
        try
        {
            await foreach (var msg in presenceUdp.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var buf = msg.Payload.ToArray();
                if (!PresencePingCodec.TryParse(buf, out var pingSender, out var nick, out var dataPort, out var advLink))
                    continue;

                var peer = new DiscoveredLocalPeer(pingSender, nick, msg.RemoteAddress,
                    msg.RemoteAddress.Kind, DateTimeOffset.UtcNow, dataPort, advLink);
                var u = _user;
                if (u == null)
                    continue;
                var myId = CompressedNetworkId.FromShortString(u.NetworkIdShort).Value;
                if (peer.NetworkId == myId)
                    continue;

                OnDiscoveryPingReceived(peer);
                DiscoveryPingReceived?.Invoke(this, new DiscoveryPingReceivedEventArgs(peer));
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
    }

    private void OnDiscoveryPingReceived(DiscoveredLocalPeer peer)
    {
        _entries.AddOrUpdate(peer.NetworkId, peer, (_, _) => peer);
        if (_scanSessionActive)
            return;
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

    private async Task SendDiscoveryBroadcastRoundAsync(CancellationToken cancellationToken)
    {
        var u = _user;
        var presenceUdp = _presenceUdp;
        if (u == null || presenceUdp == null) return;
        var payload = PresencePingCodec.Build(
            CompressedNetworkId.FromShortString(u.NetworkIdShort).Value,
            u.Nickname,
            u.DataUdpPort,
            _routingSettings.LinkTechnology);
        
        foreach (var ep in LanBroadcastHelper.GetIpv4BroadcastEndpoints(PresencePingCodec.UdpPort))
        {
            try
            {
                var addr = UdpTransportAddress.FromIPEndPoint(ep);
                await presenceUdp.SendAsync(payload, addr, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // один broadcast может быть недоступен
            }
        }
    }

    private async Task PeriodicScanLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SendDiscoveryBroadcastRoundAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(PeriodicLanScanInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                try
                {
                    await Task.Delay(PeriodicLanScanInterval, cancellationToken).ConfigureAwait(false);
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
            if (_scanSessionActive)
                continue;
            ClientsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
