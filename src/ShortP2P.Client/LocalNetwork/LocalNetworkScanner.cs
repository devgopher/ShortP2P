using System.Collections.Concurrent;
using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Discovery;
using ShortP2P.Transport;

namespace ShortP2P.Client.LocalNetwork;

/// <summary>
///     Сканирование локальной сети по discovery-пингам: UDP на broadcast-адрес каждой локальной IPv4-подсети
///     и на 255.255.255.255, порт <see cref="PresencePingCodec.UdpPort" />; раунд повторяется в фоне каждые 5 минут,
///     дополнительно по запросу UI (<see cref="ScanAsync" />, <see cref="TriggerScanAsync" />). Приём на том же порту;
///     при наличии Bluetooth — те же кадры по RFCOMM.
/// </summary>
public sealed class LocalNetworkScanner : IAsyncDisposable
{
    /// <summary>Интервал фоновой рассылки discovery (broadcast по подсетям).</summary>
    private static readonly TimeSpan PeriodicLanScanInterval = TimeSpan.FromMinutes(5);

    /// <summary>Удалять пира из списка, если не было пинга дольше этого (чуть больше периода сканирования).</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(6);

    /// <summary>Длительность приёма пингов при ручном сканировании по умолчанию.</summary>
    public static readonly TimeSpan DefaultScanListenDuration = TimeSpan.FromSeconds(45);

    private readonly SharedUserUdpGateway _gateway;

    private volatile bool _scanSessionActive;

    private readonly ConcurrentDictionary<Guid, DiscoveredLocalPeer> _entries = new();
    private readonly object _snapshotSync = new();
    private IReadOnlyList<DiscoveredLocalPeer> _snapshot = [];

    private CancellationTokenSource? _cts;
    private Task? _periodicScanLoop;
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
        _periodicScanLoop = Task.Run(() => PeriodicScanLoopAsync(token), token);
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
            await _cts.CancelAsync();
        }
        catch
        {
            // ignore
        }

        var loops = new[] { _periodicScanLoop, _staleLoop }.Where(t => t != null).ToArray();
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

        _cts?.Dispose();
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

    private void OnDiscoveryPingReceived(object? sender, DiscoveryPingReceivedEventArgs e)
    {
        var u = _user;
        if (u == null) return;
        var myId = CompressedNetworkId.FromShortString(u.NetworkIdShort).Value;
        if (e.Peer.NetworkId == myId) return;

        var peer = e.Peer;
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
        if (u == null) return;
        var payload = _gateway.BuildPresencePingDatagram(
            CompressedNetworkId.FromShortString(u.NetworkIdShort).Value,
            u.Nickname,
            u.DataUdpPort);
        foreach (var ep in LanBroadcastHelper.GetIpv4BroadcastEndpoints(PresencePingCodec.UdpPort))
        {
            try
            {
                var addr = UdpTransportAddress.FromIPEndPoint(ep);
                await _gateway.SendOnPresencePortAsync(payload, addr, cancellationToken).ConfigureAwait(false);
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
