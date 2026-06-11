using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using ShortP2P.Auth.Data;
using ShortP2P.Client.Qr;
using ShortP2P.Client.Routing;
using ShortP2P.Discovery.Ble;
using ShortP2P.Discovery.Gossip;
using ShortP2P.Discovery.Pings;
using ShortP2P.Discovery.RouteTables;
using ShortP2P.Discovery.Transceivers;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery;

/// <summary>
///     Discovery: UDP presence (<see cref="PresencePingCodec.UdpPort" />) на broadcast подсетей и limited broadcast,
///     плюс unicast на известные IPv4 (чаты, QR, публичный адрес этого узла); wire gossip/маршруты —
///     <see cref="UdpPeerDiscoveryOptions.DefaultDiscoveryUdpPort" /> на те же broadcast- и unicast-цели;
///     При включённом Bluetooth presence-пинг уходит на все известные BLE MAC (чаты, скан, сопряжённые устройства);
///     при ручном <see cref="ScanAsync" /> дополнительно — пассивное BLE-сканирование по UUID сервиса ShortP2P.
///     Приём на 0.0.0.0 — в том числе датаграммы с интернета при пробросе портов (NAT). Фоновая рассылка —
///     <see cref="LinkTechnologyPresetExtensions.GetPresencePingPeriod" />; ручное сканирование —
///     <see cref="ScanAsync" />, <see cref="TriggerScanAsync" />. Событие <see cref="DiscoveryPingReceived" /> —
///     по presence-пингам и по ответам gossip (Ack) на наши зонды.
/// </summary>
public sealed class LocalNetworkScanner(
    P2pRoutingSettings routingSettings,
    IUdpTransportFactory udpTransportFactory,
    Func<ITransport?>? getBluetoothTransport = null,
    Func<ITransport?>? getWifiDirectTransport = null,
    IEnumerable<ITransport>? additionalDiscoveryTransports = null,
    IRouteTableSnapshotSource? routeTableSnapshotSource = null,
    IDiscoveryPingStore? discoveryPingStore = null,
    IBleShortP2PPeripheralScanner? blePeripheralScanner = null,
    IBleDiscoveredPeerStore? bleDiscoveredPeerStore = null,
    IBluetoothPresencePingTargetsProvider? bluetoothPresencePingTargetsProvider = null,
    IWifiDirectPresencePingTargetsProvider? wifiDirectPresencePingTargetsProvider = null) : IAsyncDisposable
{
    /// <summary>Длительность приёма пингов при ручном сканировании по умолчанию.</summary>
    public static readonly TimeSpan DefaultScanListenDuration = TimeSpan.FromSeconds(45);

    private readonly IEnumerable<ITransport>? _additionalDiscoveryTransports = additionalDiscoveryTransports;
    private readonly ConcurrentDictionary<string, TransportAddress> _bluetoothTargets = new();
    private readonly ConcurrentDictionary<string, TransportAddress> _wifiDirectTargets = new();

    private readonly ConcurrentDictionary<CompressedNetworkId, DiscoveredLocalPeer> _entries = new();
    private readonly Func<ITransport?>? _getBluetoothTransport = getBluetoothTransport;
    private readonly Func<ITransport?>? _getWifiDirectTransport = getWifiDirectTransport;
    private readonly List<PresencePingCodec.Transceiver> _secondaryPingTransceivers = [];
    private readonly List<Task> _secondaryPresenceReceiveTasks = [];
    private readonly object _snapshotSync = new();
    private readonly ConcurrentDictionary<string, string> _udpPresenceTargets = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cts;
    private Task? _discoveryWireReceiveTask;
    private DiscoveryWireTransceiver? _discoveryWireTransceiver;
    private UdpTransport? _discoveryWireUdp;
    private long _lastGossipBroadcastNonce;
    private PeerIdentity? _localPeer;
    private DateTimeOffset _nextPublicIpv4PresenceLookupUtc = DateTimeOffset.MinValue;
    private Task? _periodicScanLoop;
    private PresencePingCodec.Transceiver? _presencePingTransceiver;

    /// <summary>Глубина вложенности раундов TX discovery; &gt;0 — приём пингов не обрабатывается.</summary>
    private int _presencePingTransmitDepth;

    private Task? _presenceReceiveTask;
    private UdpTransport? _presenceUdp;

    /// <summary>Два последних nonce gossip-зонда: поздние Ack после смены раунда всё ещё принимаются.</summary>
    private long _priorGossipBroadcastNonce;

    private volatile bool _scanSessionActive;
    private List<ITransport> _secondaryPresenceTransports = [];
    private IReadOnlyList<DiscoveredLocalPeer> _snapshot = [];
    private Task? _staleLoop;
    public bool IsUdpListening => _presenceUdp != null;
    public bool IsBluetoothListening { get; private set; }
    public bool IsWifiDirectListening { get; private set; }

    private PresencePingCodec.Transceiver? _wifiDirectPingTransceiver;
    private DiscoveryWireTransceiver? _wifiDirectDiscoveryTransceiver;

    /// <summary>Удалять пира из списка, если не было пинга дольше этого (несколько периодов рассылки).</summary>
    private TimeSpan DiscoveryStaleAfter =>
        TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(45).Ticks,
            routingSettings.LinkTechnology.GetPresencePingPeriod(routingSettings.TrafficSavingEnabled).Ticks * 6));

    /// <summary>Снимок последних найденных пиров (кроме текущего пользователя).</summary>
    public IReadOnlyList<DiscoveredLocalPeer> Clients
    {
        get
        {
            lock (_snapshotSync)
            {
                return _snapshot;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Есть недавний presence-пинг с данным short network id (тот же порог, что и для «протухания» списка LAN).
    /// </summary>
    public bool IsPeerSeenRecentlyOnLan(string peerNetworkIdShort)
    {
        if (string.IsNullOrWhiteSpace(peerNetworkIdShort))
            return false;
        CompressedNetworkId id;
        try
        {
            id = CompressedNetworkId.FromShortString(peerNetworkIdShort.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        var cutoff = DateTimeOffset.UtcNow - DiscoveryStaleAfter;
        return _entries.TryGetValue(id, out var p) && p.LastSeenUtc >= cutoff;
    }

    public event EventHandler? ClientsChanged;

    /// <summary>
    ///     Принят чужой presence/discovery-пинг (не свой network id); обработчик не должен долго блокировать поток
    ///     приёма.
    /// </summary>
    public event EventHandler<DiscoveryPingReceivedEventArgs>? DiscoveryPingReceived;

    public async Task StartAsync(PeerIdentity localPeer, CancellationToken cancellationToken = default)
    {
        if (_cts != null)
            return;
        _localPeer = localPeer;
        _nextPublicIpv4PresenceLookupUtc = DateTimeOffset.MinValue;
        _secondaryPresenceTransports = BuildSecondaryPresenceTransports(_getBluetoothTransport?.Invoke(),
            _getWifiDirectTransport?.Invoke(), _additionalDiscoveryTransports);
        EnsurePublicIpv4InPresenceTargets();
        _presenceUdp = udpTransportFactory.Acquire(IPAddress.Any, PresencePingCodec.UdpPort, true);
        _discoveryWireUdp = udpTransportFactory.Acquire(IPAddress.Any,
            UdpPeerDiscoveryOptions.DefaultDiscoveryUdpPort, true);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;
        await _presenceUdp.StartAsync(cancellationToken).ConfigureAwait(false);
        await _discoveryWireUdp.StartAsync(cancellationToken).ConfigureAwait(false);
        foreach (var transport in _secondaryPresenceTransports)
        {
            await transport.StartAsync(cancellationToken).ConfigureAwait(false);
            if (transport.Kind == TransportKind.Bluetooth)
                IsBluetoothListening = true;
            if (transport.Kind == TransportKind.WifiDirect)
                IsWifiDirectListening = true;
        }

        var wifiDirect = _getWifiDirectTransport?.Invoke();
        if (wifiDirect != null)
        {
            _wifiDirectPingTransceiver = new PresencePingCodec.Transceiver(wifiDirect);
            _wifiDirectPingTransceiver.GotData += OnPresencePingReceived;
            await _wifiDirectPingTransceiver.StartAsync(token).ConfigureAwait(false);
            _wifiDirectDiscoveryTransceiver = new DiscoveryWireTransceiver(wifiDirect);
            _wifiDirectDiscoveryTransceiver.GotData += OnDiscoveryWireReceived;
            await _wifiDirectDiscoveryTransceiver.StartAsync(token).ConfigureAwait(false);
        }

        _presencePingTransceiver = new PresencePingCodec.Transceiver(_presenceUdp);
        _presencePingTransceiver.GotData += OnPresencePingReceived;
        await _presencePingTransceiver.StartAsync(token).ConfigureAwait(false);
        _presenceReceiveTask = Task.Run(() => PresenceReceiveLoopAsync(_presenceUdp, _presencePingTransceiver, token),
            token);

        _discoveryWireTransceiver = new DiscoveryWireTransceiver(_discoveryWireUdp);
        _discoveryWireTransceiver.GotData += OnDiscoveryWireReceived;
        await _discoveryWireTransceiver.StartAsync(token).ConfigureAwait(false);
        _discoveryWireReceiveTask = Task.Run(
            () => DiscoveryWireReceiveLoopAsync(_discoveryWireUdp, _discoveryWireTransceiver, token), token);

        foreach (var transport in _secondaryPresenceTransports)
        {
            if (transport.Kind == TransportKind.WifiDirect)
                continue;
            var tx = new PresencePingCodec.Transceiver(transport);
            tx.GotData += OnPresencePingReceived;
            await tx.StartAsync(token).ConfigureAwait(false);
            _secondaryPingTransceivers.Add(tx);
            _secondaryPresenceReceiveTasks.Add(Task.Run(() => PresenceReceiveLoopAsync(transport, tx, token), token));
        }

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

        if (_presencePingTransceiver != null)
        {
            _presencePingTransceiver.GotData -= OnPresencePingReceived;
            try
            {
                await _presencePingTransceiver.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }

            _presencePingTransceiver = null;
        }

        if (_discoveryWireTransceiver != null)
        {
            _discoveryWireTransceiver.GotData -= OnDiscoveryWireReceived;
            try
            {
                await _discoveryWireTransceiver.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }

            _discoveryWireTransceiver = null;
        }

        foreach (var tx in _secondaryPingTransceivers)
        {
            tx.GotData -= OnPresencePingReceived;
            try
            {
                await tx.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }

        _secondaryPingTransceivers.Clear();

        var activeLoops = new List<Task?>(_secondaryPresenceReceiveTasks.Count + 4)
        {
            _presenceReceiveTask,
            _discoveryWireReceiveTask,
            _periodicScanLoop,
            _staleLoop
        };
        activeLoops.AddRange(_secondaryPresenceReceiveTasks);
        var toWait = activeLoops.Where(t => t != null).ToArray();
        _presenceReceiveTask = null;
        _discoveryWireReceiveTask = null;
        _secondaryPresenceReceiveTasks.Clear();
        _periodicScanLoop = null;
        _staleLoop = null;
        if (toWait.Length > 0)
            try
            {
                await Task.WhenAll(toWait!).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }

        if (_presenceUdp != null)
        {
            var p = _presenceUdp;
            _presenceUdp = null;
            try
            {
                await udpTransportFactory.ReleaseAsync(p, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        if (_discoveryWireUdp != null)
        {
            var d = _discoveryWireUdp;
            _discoveryWireUdp = null;
            try
            {
                await udpTransportFactory.ReleaseAsync(d, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        _cts.Dispose();
        _cts = null;
        _localPeer = null;
        _entries.Clear();
        _bluetoothTargets.Clear();
        _udpPresenceTargets.Clear();
        IsBluetoothListening = false;
        lock (_snapshotSync)
        {
            _snapshot = [];
        }

        foreach (var transport in _secondaryPresenceTransports)
            try
            {
                await transport.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
    }

    public void RememberBluetoothPeer(TransportAddress address)
    {
        if (address.Kind != TransportKind.Bluetooth || address.Data.Length == 0)
            return;
        _bluetoothTargets[Convert.ToBase64String(address.Data)] = address;
    }

    public void RememberWifiDirectPeer(TransportAddress address)
    {
        if (address.Kind != TransportKind.WifiDirect || address.Data.Length == 0)
            return;
        if (!WifiDirectTransportAddress.TryParseAddress(address.Data, out _))
            return;
        _wifiDirectTargets[Convert.ToBase64String(address.Data)] = address;
    }

    /// <summary>Единая точка приёма кадров с Wi-Fi Direct (demux в UserP2pRuntime).</summary>
    public void DispatchWifiDirectInbound(TransportReceiveMessage msg)
    {
        if (msg.Payload.IsEmpty)
            return;
        var frame = msg.Payload.Span[0];
        if (frame == 0x31)
            _wifiDirectPingTransceiver?.HandleIncoming(msg);
        else if (frame is 0x40 or 0x41 or 0x42 or 0x43)
            _wifiDirectDiscoveryTransceiver?.HandleIncoming(msg);
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

    private bool ShouldRunBleDiscoveryScan()
    {
        return blePeripheralScanner != null
               && IsTransportEnabled(TransportKind.Bluetooth)
               && _secondaryPresenceTransports.Any(t => t.Kind == TransportKind.Bluetooth);
    }

    private bool IsTransportEnabled(TransportKind kind)
    {
        return kind switch
        {
            TransportKind.Udp => routingSettings.EnableUdpTransport,
            TransportKind.Bluetooth => routingSettings.EnableBluetoothTransport,
            TransportKind.WifiDirect => routingSettings.EnableWifiDirectTransport,
            _ => true
        };
    }

    /// <summary>Один раунд discovery: UDP broadcast по каждой подсети (+ limited broadcast).</summary>
    public Task TriggerScanAsync(CancellationToken cancellationToken = default)
    {
        return SendDiscoveryBroadcastRoundAsync(cancellationToken);
    }

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

            var sw = Stopwatch.StartNew();
            if (ShouldRunBleDiscoveryScan())
            {
                var blePart = TimeSpan.FromSeconds(
                    Math.Clamp(listenDuration.TotalSeconds * 0.35, 4, 20));
                if (blePart > listenDuration)
                    blePart = listenDuration;
                try
                {
                    await blePeripheralScanner!.ScanAsync(blePart,
                        (addr, scanResult) =>
                        {
                            RememberBluetoothPeer(addr);
                            _ = bleDiscoveredPeerStore?.RecordScanSeenAsync(addr, scanResult, cancellationToken);
                        }, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // BLE scan unavailable / permission
                }
            }

            await SendDiscoveryBroadcastRoundAsync(cancellationToken).ConfigureAwait(false);
            var remaining = listenDuration - sw.Elapsed;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _scanSessionActive = false;
        }

        RebuildSnapshot();
        ClientsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPresencePingReceived(object? sender, PingMessage msg)
    {
        // if (Volatile.Read(ref _presencePingTransmitDepth) != 0)
        //     return;
        if (!IsTransportEnabled(msg.RemoteAddress.Kind))
            return;

        var localPeer = _localPeer;
        if (localPeer == null)
            return;
        if (msg.PeerNetworkId == localPeer.NetworkId)
            return;

        if (msg.RemoteAddress.Kind == TransportKind.Bluetooth)
        {
            RememberBluetoothPeer(msg.RemoteAddress);
            if (bleDiscoveredPeerStore != null)
            {
                var nick = string.IsNullOrWhiteSpace(msg.Nickname) ? "?" : msg.Nickname.Trim();
                _ = bleDiscoveredPeerStore.RecordPingAsync(msg.RemoteAddress, msg.PeerNetworkId, nick,
                    CancellationToken.None);
            }
        }
        else if (msg.RemoteAddress.Kind == TransportKind.WifiDirect)
        {
            RememberWifiDirectPeer(msg.RemoteAddress);
            if (bleDiscoveredPeerStore != null)
            {
                var nick = string.IsNullOrWhiteSpace(msg.Nickname) ? "?" : msg.Nickname.Trim();
                _ = bleDiscoveredPeerStore.RecordScanSeenAsync(msg.RemoteAddress,
                    new BleAdScanResult { NetworkId = msg.PeerNetworkId });
            }
        }

        var peer = new DiscoveredLocalPeer(msg.PeerNetworkId, msg.Nickname, msg.RemoteAddress,
            msg.RemoteAddress.Kind, DateTimeOffset.UtcNow, msg.PeerDataUdpPort, msg.AdvertisedLink,
            msg.AdvertisedCapabilities);

        OnDiscoveryPingReceived(peer);
        DiscoveryPingReceived?.Invoke(this, new DiscoveryPingReceivedEventArgs(peer));
    }

    private static async Task PresenceReceiveLoopAsync(ITransport transport, PresencePingCodec.Transceiver transceiver,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var msg in transport.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                transceiver.HandleIncoming(msg);
        }
        catch (OperationCanceledException)
        {
            // expected on stop
        }
    }

    private void OnDiscoveryWireReceived(object? sender, DiscoveryWireMessage msg)
    {
        if (Volatile.Read(ref _presencePingTransmitDepth) != 0)
            return;
        if (!IsTransportEnabled(msg.RemoteAddress.Kind))
            return;

        var token = _cts?.Token ?? CancellationToken.None;
        _ = Task.Run(() => HandleDiscoveryWireAsync(msg, token), token);
    }

    private static async Task DiscoveryWireReceiveLoopAsync(ITransport transport, DiscoveryWireTransceiver transceiver,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var msg in transport.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                transceiver.HandleIncoming(msg);
        }
        catch (OperationCanceledException)
        {
            // expected on stop
        }
    }

    private async Task HandleDiscoveryWireAsync(DiscoveryWireMessage msg, CancellationToken cancellationToken)
    {
        try
        {
            var replyTransport = GetDiscoveryWireReplyTransport(msg.RemoteAddress);
            if (replyTransport == null)
                return;
            var buf = msg.RawPayload.ToArray();
            switch (msg.Kind)
            {
                case DiscoveryWireKind.GossipAck:
                    TryHandleGossipAckAsDiscovery(buf, msg.RemoteAddress);
                    return;
                case DiscoveryWireKind.GossipProbe:
                    await TryReplyToGossipProbeAsync(replyTransport, buf, msg.RemoteAddress, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                case DiscoveryWireKind.RouteTableRequest:
                    await TryReplyToRouteTableRequestAsync(replyTransport, buf, msg.RemoteAddress, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                case DiscoveryWireKind.RouteTableReply:
                    // Reply парсится потребителем (Discovery), здесь — только ack-стейт через DiscoveryPingReceived при необходимости.
                    return;
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch
        {
            // safety: подписчик не должен ронять цикл приёма транспивера
        }
    }

    /// <summary>Ответ на наш gossip-зонд (в т.ч. с внешнего IP): превращаем в тот же поток, что и presence.</summary>
    private bool TryHandleGossipAckAsDiscovery(byte[] buf, TransportAddress remote)
    {
        if (!GossipWireCodec.TryParseAck(buf, out var nonce, out var responderId, out var dataPort, out var nick))
            return false;
        if (!IsRecentGossipBroadcastNonce(nonce))
            return false;
        if (dataPort is < 1 or > 65535)
            return false;

        var localPeer = _localPeer;
        if (localPeer != null && responderId == localPeer.NetworkId)
            return true;

        nick = string.IsNullOrWhiteSpace(nick) ? "?" : nick.Trim();
        DiscoveredLocalPeer peer;
        if (remote.Kind == TransportKind.WifiDirect)
        {
            RememberWifiDirectPeer(remote);
            peer = new DiscoveredLocalPeer(responderId, nick, remote, TransportKind.WifiDirect,
                DateTimeOffset.UtcNow, dataPort);
        }
        else if (remote.Kind == TransportKind.Udp)
        {
            IPEndPoint remoteEp;
            try
            {
                remoteEp = UdpTransportAddress.ToIPEndPoint(remote);
            }
            catch
            {
                return true;
            }

            var dataAddr = UdpTransportAddress.FromIPEndPoint(new IPEndPoint(remoteEp.Address, dataPort));
            peer = new DiscoveredLocalPeer(responderId, nick, dataAddr, TransportKind.Udp, DateTimeOffset.UtcNow,
                dataPort);
        }
        else
        {
            return true;
        }
        OnDiscoveryPingReceived(peer);
        DiscoveryPingReceived?.Invoke(this, new DiscoveryPingReceivedEventArgs(peer));
        return true;
    }

    private void RegisterGossipBroadcastNonce(long nonce)
    {
        var prev = Interlocked.Exchange(ref _lastGossipBroadcastNonce, nonce);
        Volatile.Write(ref _priorGossipBroadcastNonce, prev);
    }

    private bool IsRecentGossipBroadcastNonce(long n)
    {
        return n == Volatile.Read(ref _lastGossipBroadcastNonce) || n == Volatile.Read(ref _priorGossipBroadcastNonce);
    }

    private async Task<bool> TryReplyToGossipProbeAsync(ITransport replyTransport, byte[] buf,
        TransportAddress remote, CancellationToken cancellationToken)
    {
        if (!GossipWireCodec.TryParseProbe(buf, out var nonce, out var sender, out var target))
            return false;

        var localPeer = _localPeer;
        if (localPeer == null)
            return true;
        var myId = localPeer.NetworkId;

        if (sender == myId)
            return true;

        if (!target.IsEmpty && target != myId)
            return true;

        var nick = string.IsNullOrWhiteSpace(localPeer.Nickname) ? "?" : localPeer.Nickname.Trim();
        var port = localPeer.DataUdpPort is >= 1 and <= 65535
            ? localPeer.DataUdpPort
            : PresencePingCodec.DefaultDataUdpPort;
        var ack = GossipWireCodec.BuildAck(nonce, myId, port, nick);
        await replyTransport.SendAsync(ack, remote, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> TryReplyToRouteTableRequestAsync(ITransport replyTransport, byte[] buf,
        TransportAddress remote, CancellationToken cancellationToken)
    {
        if (!RouteTableWireCodec.TryParseRequest(buf, out var nonce, out var sender))
            return false;

        var localPeer = _localPeer;
        if (localPeer == null)
            return true;
        var myId = localPeer.NetworkId;

        if (sender == myId)
            return true;

        var caps = routingSettings.AdvertisedPeerCapabilities | PresencePeerCapabilities.Chat;
        if (!caps.HasFlag(PresencePeerCapabilities.PeerSearch))
            return true;

        IReadOnlyList<Route> routes = Array.Empty<Route>();
        if (routeTableSnapshotSource != null)
            try
            {
                routes = await routeTableSnapshotSource.GetRoutesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                routes = Array.Empty<Route>();
            }

        var reply = RouteTableWireCodec.BuildReply(nonce, myId, routes);
        await replyTransport.SendAsync(reply, remote, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void OnDiscoveryPingReceived(DiscoveredLocalPeer peer)
    {
        discoveryPingStore?.Write(
            new PeerIdentity(peer.Nickname, peer.NetworkId, peer.PeerDataUdpPort),
            peer.SourceAddress, peer.LastSeenUtc);
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
        {
            _snapshot = list;
        }
    }

    /// <summary>
    ///     Добавляет публичный IPv4 в цели unicast-пингов (hairpin/NAT и пиры, достижимые по белому адресу).
    ///     HTTP-запрос ограничен по частоте.
    /// </summary>
    private void EnsurePublicIpv4InPresenceTargets()
    {
        var now = DateTimeOffset.UtcNow;
        if (now < _nextPublicIpv4PresenceLookupUtc)
            return;
        _nextPublicIpv4PresenceLookupUtc = now.AddMinutes(3);
        try
        {
            var pub = LocalIPv4Resolver.TryGetPublicIpv4(TimeSpan.FromSeconds(1));
            if (!string.IsNullOrWhiteSpace(pub))
                RememberUdpPresenceTarget(pub.Trim());
        }
        catch
        {
            // сеть / таймаут echo-сервиса
        }
    }

    private async Task SendDiscoveryBroadcastRoundAsync(CancellationToken cancellationToken)
    {
        var localPeer = _localPeer;
        var presenceUdp = _presenceUdp;
        if (localPeer == null) return;
        Interlocked.Increment(ref _presencePingTransmitDepth);
        try
        {
            EnsurePublicIpv4InPresenceTargets();
            var caps = routingSettings.AdvertisedPeerCapabilities | PresencePeerCapabilities.Chat;
            var payload = PresencePingCodec.Build(
                localPeer.NetworkId,
                localPeer.Nickname,
                localPeer.DataUdpPort,
                routingSettings.LinkTechnology,
                caps);

            if (presenceUdp != null && IsTransportEnabled(TransportKind.Udp))
            {
                var broadcastEps = LanBroadcastHelper.GetIpv4BroadcastEndpoints(PresencePingCodec.UdpPort);
                foreach (var ep in broadcastEps)
                    try
                    {
                        var addr = UdpTransportAddress.FromIPEndPoint(ep);
                        await presenceUdp.SendAsync(payload, addr, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        // один broadcast может быть недоступен
                    }

                foreach (var host in _udpPresenceTargets.Values)
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

            var bt = _secondaryPresenceTransports.FirstOrDefault(t => t.Kind == TransportKind.Bluetooth);
            if (bt != null && IsTransportEnabled(TransportKind.Bluetooth))
            {
                try
                {
                    if (bluetoothPresencePingTargetsProvider != null)
                    {
                        var fromProvider = await bluetoothPresencePingTargetsProvider
                            .GetBluetoothPingTargetsAsync(cancellationToken).ConfigureAwait(false);
                        foreach (var addr in fromProvider)
                            RememberBluetoothPeer(addr);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // БД / Bluetooth: не блокируем UDP-раунд
                }

                if (_bluetoothTargets.IsEmpty)
                    try
                    {
                        var paired = await TryGetPairedBluetoothAddressesAsync(bt, cancellationToken)
                            .ConfigureAwait(false);
                        foreach (var addr in paired)
                            RememberBluetoothPeer(addr);
                    }
                    catch
                    {
                        // bluetooth subsystem unavailable
                    }

                foreach (var target in _bluetoothTargets.Values)
                    try
                    {
                        await bt.SendAsync(payload, target, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        // устройство выключено/вне зоны/не сопряжено
                    }
            }

            var wfd = _secondaryPresenceTransports.FirstOrDefault(t => t.Kind == TransportKind.WifiDirect);
            if (wfd != null && IsTransportEnabled(TransportKind.WifiDirect))
            {
                try
                {
                    if (wifiDirectPresencePingTargetsProvider != null)
                    {
                        var fromProvider = await wifiDirectPresencePingTargetsProvider
                            .GetWifiDirectPingTargetsAsync(cancellationToken).ConfigureAwait(false);
                        foreach (var addr in fromProvider)
                            RememberWifiDirectPeer(addr);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // ignore
                }

                foreach (var target in _wifiDirectTargets.Values)
                    try
                    {
                        await wfd.SendAsync(payload, target, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        // peer offline
                    }

                if (_wifiDirectTargets.IsEmpty)
                    try
                    {
                        await wfd.SendAsync(payload, new TransportAddress(TransportKind.WifiDirect, []),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        // ignore
                    }
            }

            foreach (var transport in _secondaryPresenceTransports.Where(t =>
                         t.Kind is not TransportKind.Bluetooth and not TransportKind.WifiDirect))
            {
                if (!IsTransportEnabled(transport.Kind))
                    continue;
                try
                {
                    await transport.SendAsync(payload, new TransportAddress(transport.Kind, []), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // транспорт может требовать предварительной доступности канала/устройства
                }
            }

            await SendGossipDiscoveryProbesAsync(localPeer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _presencePingTransmitDepth);
        }
    }

    /// <summary>
    ///     Gossip-зонды на wire-порт: те же broadcast, что LAN, и unicast на известные внешние/внутренние IPv4.
    /// </summary>
    private ITransport? GetDiscoveryWireReplyTransport(TransportAddress remote)
    {
        return remote.Kind switch
        {
            TransportKind.Udp => _discoveryWireUdp,
            TransportKind.WifiDirect => _getWifiDirectTransport?.Invoke(),
            _ => null
        };
    }

    private async Task SendGossipDiscoveryProbesAsync(PeerIdentity localPeer, CancellationToken cancellationToken)
    {
        var wireUdp = _discoveryWireUdp;
        if (wireUdp == null && _getWifiDirectTransport?.Invoke() == null)
            return;

        long nonce;
        do
        {
            nonce = Random.Shared.NextInt64();
        } while (nonce == 0);

        RegisterGossipBroadcastNonce(nonce);
        var probe = GossipWireCodec.BuildProbe(nonce, localPeer.NetworkId, CompressedNetworkId.Empty);

        if (wireUdp != null && IsTransportEnabled(TransportKind.Udp))
        foreach (var ep in LanBroadcastHelper.GetIpv4BroadcastEndpoints(GossipWireCodec.UdpPort))
            try
            {
                var addr = UdpTransportAddress.FromIPEndPoint(ep);
                await wireUdp.SendAsync(probe, addr, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // один broadcast может быть недоступен
            }

        if (wireUdp != null && IsTransportEnabled(TransportKind.Udp))
        foreach (var host in _udpPresenceTargets.Values)
            try
            {
                if (!IPAddress.TryParse(host, out var ip))
                    continue;
                if (IPAddress.IsLoopback(ip))
                    continue;
                var addr = UdpTransportAddress.FromIPEndPoint(new IPEndPoint(ip, GossipWireCodec.UdpPort));
                await wireUdp.SendAsync(probe, addr, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // узел недоступен или фильтрация
            }

        var wfd = _getWifiDirectTransport?.Invoke();
        if (wfd != null && IsTransportEnabled(TransportKind.WifiDirect))
        {
            foreach (var target in _wifiDirectTargets.Values)
                try
                {
                    await wfd.SendAsync(probe, target, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }

            if (_wifiDirectTargets.IsEmpty)
                try
                {
                    await wfd.SendAsync(probe, new TransportAddress(TransportKind.WifiDirect, []), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // ignore
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
            var period = routingSettings.LinkTechnology.GetPresencePingPeriod(routingSettings.TrafficSavingEnabled);
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

    private static List<ITransport> BuildSecondaryPresenceTransports(ITransport? bluetoothTransport,
        ITransport? wifiDirectTransport, IEnumerable<ITransport>? additionalDiscoveryTransports)
    {
        var list = new List<ITransport>();
        if (bluetoothTransport != null && bluetoothTransport.Kind != TransportKind.Udp)
            list.Add(bluetoothTransport);
        if (wifiDirectTransport != null && wifiDirectTransport.Kind != TransportKind.Udp)
            list.Add(wifiDirectTransport);
        if (additionalDiscoveryTransports == null)
            return list;

        foreach (var transport in additionalDiscoveryTransports)
        {
            if (transport == null || transport.Kind == TransportKind.Udp)
                continue;
            if (list.Contains(transport))
                continue;
            list.Add(transport);
        }

        return list;
    }
}