using System.Collections.Concurrent;
using System.Net;
using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Discovery;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.LocalNetwork;

/// <summary>
///     Сканирование локальной сети по discovery-пингам: UDP на broadcast-адрес каждой локальной IPv4-подсети
///     и на 255.255.255.255, порт <see cref="PresencePingCodec.UdpPort" />; фоновая рассылка по периоду
///     <see cref="LinkTechnologyPresetExtensions.GetPresencePingPeriod" /> (5 или 15 с от пресета канала),
///     дополнительно по запросу UI (<see cref="ScanAsync" />, <see cref="TriggerScanAsync" />). Приём на порту 565;
///     сырые пинги чужих пиров — в <see cref="DiscoveryPingReceived" /> (подписчик не должен блокировать цикл приёма).
/// </summary>
public sealed class LocalNetworkScanner(P2pRoutingSettings routingSettings, ITransport? bluetoothTransport = null) : IAsyncDisposable
{
    public bool IsUdpListening => _presenceUdp != null;
    public bool IsBluetoothListening => _isBluetoothListening;

    /// <summary>Удалять пира из списка, если не было пинга дольше этого (несколько периодов рассылки).</summary>
    private TimeSpan DiscoveryStaleAfter =>
        TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(45).Ticks,
            routingSettings.LinkTechnology.GetPresencePingPeriod().Ticks * 6));

    /// <summary>Длительность приёма пингов при ручном сканировании по умолчанию.</summary>
    public static readonly TimeSpan DefaultScanListenDuration = TimeSpan.FromSeconds(45);

    private volatile bool _scanSessionActive;

    private readonly ConcurrentDictionary<Guid, DiscoveredLocalPeer> _entries = new();
    private readonly object _snapshotSync = new();
    private IReadOnlyList<DiscoveredLocalPeer> _snapshot = [];

    private CancellationTokenSource? _cts;
    private UdpTransport? _presenceUdp;
    private readonly ConcurrentDictionary<string, TransportAddress> _bluetoothTargets = new();
    private readonly ConcurrentDictionary<string, string> _udpPresenceTargets = new(StringComparer.OrdinalIgnoreCase);
    private Task? _presenceReceiveLoop;
    private Task? _presenceBluetoothReceiveLoop;
    private Task? _periodicScanLoop;
    private Task? _staleLoop;
    private UserEntity? _user;
    private bool _isBluetoothListening;

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

        var cutoff = DateTimeOffset.UtcNow - DiscoveryStaleAfter;
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
        if (bluetoothTransport != null)
        {
            await bluetoothTransport.StartAsync(cancellationToken).ConfigureAwait(false);
            _isBluetoothListening = true;
        }
        _presenceReceiveLoop = Task.Run(() => PresenceReceiveLoopAsync(token), token);
        if (bluetoothTransport != null)
            _presenceBluetoothReceiveLoop = Task.Run(() => PresenceBluetoothReceiveLoopAsync(token), token);
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

        var loops = new[] { _presenceReceiveLoop, _presenceBluetoothReceiveLoop, _periodicScanLoop, _staleLoop }
            .Where(t => t != null).ToArray();
        _presenceReceiveLoop = null;
        _presenceBluetoothReceiveLoop = null;
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
        _bluetoothTargets.Clear();
        _udpPresenceTargets.Clear();
        _isBluetoothListening = false;
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

    public void RememberBluetoothPeer(TransportAddress address)
    {
        if (address.Kind != TransportKind.Bluetooth || address.Data.Length == 0)
            return;
        _bluetoothTargets[Convert.ToBase64String(address.Data)] = address;
    }

    /// <summary>
    ///     Запоминает IP-адрес пира, чтобы отправлять presence-пинги напрямую (в т.ч. для non-LAN адресов после Add/QR).
    /// </summary>
    public void RememberUdpPresenceTarget(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return;
        host = host.Trim();
        if (!IPAddress.TryParse(host, out var ip))
            return;
        if (IPAddress.IsLoopback(ip))
            return;
        var normalized = ip.ToString();
        _udpPresenceTargets[normalized] = normalized;
    }

    private bool IsTransportEnabled(TransportKind kind) => kind switch
    {
        TransportKind.Udp => routingSettings.EnableUdpTransport,
        TransportKind.Bluetooth => routingSettings.EnableBluetoothTransport,
        _ => false
    };

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
        if (_presenceUdp == null)
            return;
        try
        {
            await foreach (var msg in _presenceUdp.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!IsTransportEnabled(msg.RemoteAddress.Kind))
                    continue;
                var buf = msg.Payload.ToArray();
                if (!PresencePingCodec.TryParse(buf, out var pingSender, out var nick, out var dataPort, out var advLink,
                        out var advCaps))
                    continue;

                var peer = new DiscoveredLocalPeer(pingSender, nick, msg.RemoteAddress,
                    msg.RemoteAddress.Kind, DateTimeOffset.UtcNow, dataPort, advLink, advCaps);
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

    private async Task PresenceBluetoothReceiveLoopAsync(CancellationToken cancellationToken)
    {
        if (bluetoothTransport == null)
            return;
        try
        {
            await foreach (var msg in bluetoothTransport.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!IsTransportEnabled(msg.RemoteAddress.Kind))
                    continue;
                var buf = msg.Payload.ToArray();
                if (!PresencePingCodec.TryParse(buf, out var pingSender, out var nick, out var dataPort, out var advLink,
                        out var advCaps))
                    continue;

                if (msg.RemoteAddress.Kind == TransportKind.Bluetooth)
                    RememberBluetoothPeer(msg.RemoteAddress);
                var peer = new DiscoveredLocalPeer(pingSender, nick, msg.RemoteAddress,
                    msg.RemoteAddress.Kind, DateTimeOffset.UtcNow, dataPort, advLink, advCaps);
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
        if (u == null) return;
        var payload = PresencePingCodec.Build(
            CompressedNetworkId.FromShortString(u.NetworkIdShort).Value,
            u.Nickname,
            u.DataUdpPort,
            routingSettings.LinkTechnology,
            PresencePeerCapabilities.Chat);
        
        if (presenceUdp != null && IsTransportEnabled(TransportKind.Udp))
        {
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
            foreach (var host in _udpPresenceTargets.Values)
            {
                try
                {
                    if (!IPAddress.TryParse(host, out var ip))
                        continue;
                    var addr = UdpTransportAddress.FromIPEndPoint(new IPEndPoint(ip, PresencePingCodec.UdpPort));
                    await presenceUdp.SendAsync(payload, addr, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // peer может быть офлайн / IP устарел
                }
            }
        }

        var bt = bluetoothTransport;
        if (bt == null || !IsTransportEnabled(TransportKind.Bluetooth))
            return;
        if (_bluetoothTargets.IsEmpty)
        {
            try
            {
                var paired = await TryGetPairedBluetoothAddressesAsync(bt, cancellationToken).ConfigureAwait(false);
                foreach (var addr in paired)
                    RememberBluetoothPeer(addr);
            }
            catch
            {
                // bluetooth subsystem unavailable
            }
        }
        foreach (var target in _bluetoothTargets.Values)
        {
            try
            {
                await bt.SendAsync(payload, target, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // устройство выключено/вне зоны/не сопряжено
            }
        }
    }

    private static async Task<IReadOnlyList<TransportAddress>> TryGetPairedBluetoothAddressesAsync(
        ITransport transport, CancellationToken cancellationToken)
    {
        var m = transport.GetType().GetMethod("GetPairedDeviceAddressesAsync",
            [typeof(CancellationToken)]);
        if (m == null || m.Invoke(transport, [cancellationToken]) is not Task t)
            return [];
        await t.ConfigureAwait(false);
        var prop = t.GetType().GetProperty("Result");
        return prop?.GetValue(t) as IReadOnlyList<TransportAddress> ?? [];
    }

    private async Task PeriodicScanLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var period = routingSettings.LinkTechnology.GetPresencePingPeriod();
            try
            {
                await SendDiscoveryBroadcastRoundAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(period, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                try
                {
                    await Task.Delay(period, cancellationToken).ConfigureAwait(false);
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

            var cutoff = DateTimeOffset.UtcNow - DiscoveryStaleAfter;
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
