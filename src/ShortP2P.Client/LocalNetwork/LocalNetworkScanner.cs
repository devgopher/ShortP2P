using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using ShortP2P.Auth.Data;
using ShortP2P.Client.Qr;
using ShortP2P.Client.Routing;
using ShortP2P.Discovery.Gossip;
using ShortP2P.Discovery.Pings;
using ShortP2P.Discovery.RouteTables;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery;

/// <summary>
///     Discovery: UDP presence (<see cref="PresencePingCodec.UdpPort" />) на broadcast подсетей и limited broadcast,
///     плюс unicast на известные IPv4 (чаты, QR, публичный адрес этого узла); wire gossip/маршруты —
///     <see cref="UdpPeerDiscoveryOptions.DefaultDiscoveryUdpPort" /> на те же broadcast- и unicast-цели.
///     Приём на 0.0.0.0 — в том числе датаграммы с интернета при пробросе портов (NAT). Фоновая рассылка —
///     <see cref="LinkTechnologyPresetExtensions.GetPresencePingPeriod" />; ручное сканирование —
///     <see cref="ScanAsync" />, <see cref="TriggerScanAsync" />. Событие <see cref="DiscoveryPingReceived" /> —
///     по presence-пингам и по ответам gossip (Ack) на наши зонды.
/// </summary>
public sealed class LocalNetworkScanner(
    P2pRoutingSettings routingSettings,
    ITransport? bluetoothTransport = null,
    IEnumerable<ITransport>? additionalDiscoveryTransports = null,
    IRouteTableSnapshotSource? routeTableSnapshotSource = null,
    IDiscoveryPingStore? discoveryPingStore = null) : IAsyncDisposable
{
    public bool IsUdpListening => _presenceUdp != null;
    public bool IsBluetoothListening => _isBluetoothListening;

    /// <summary>Удалять пира из списка, если не было пинга дольше этого (несколько периодов рассылки).</summary>
    private TimeSpan DiscoveryStaleAfter =>
        TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(45).Ticks,
            routingSettings.LinkTechnology.GetPresencePingPeriod(routingSettings.TrafficSavingEnabled).Ticks * 6));

    /// <summary>Длительность приёма пингов при ручном сканировании по умолчанию.</summary>
    public static readonly TimeSpan DefaultScanListenDuration = TimeSpan.FromSeconds(45);

    private volatile bool _scanSessionActive;

    private readonly ConcurrentDictionary<Guid, DiscoveredLocalPeer> _entries = new();
    private readonly object _snapshotSync = new();
    private IReadOnlyList<DiscoveredLocalPeer> _snapshot = [];

    private CancellationTokenSource? _cts;
    private UdpTransport? _presenceUdp;
    private UdpTransport? _discoveryWireUdp;
    private readonly List<ITransport> _secondaryPresenceTransports = BuildSecondaryPresenceTransports(bluetoothTransport,
        additionalDiscoveryTransports);
    private readonly ConcurrentDictionary<string, TransportAddress> _bluetoothTargets = new();
    private readonly ConcurrentDictionary<string, string> _udpPresenceTargets = new(StringComparer.OrdinalIgnoreCase);
    private Task? _presenceReceiveLoop;
    private Task? _discoveryWireReceiveLoop;
    private readonly List<Task> _secondaryPresenceReceiveLoops = [];
    private Task? _periodicScanLoop;
    private Task? _staleLoop;
    private PeerIdentity? _localPeer;
    private bool _isBluetoothListening;
    private DateTimeOffset _nextPublicIpv4PresenceLookupUtc = DateTimeOffset.MinValue;

    /// <summary>Два последних nonce gossip-зонда: поздние Ack после смены раунда всё ещё принимаются.</summary>
    private long _priorGossipBroadcastNonce;
    private long _lastGossipBroadcastNonce;

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

    public async Task StartAsync(PeerIdentity localPeer, CancellationToken cancellationToken = default)
    {
        if (_cts != null)
            return;
        _localPeer = localPeer;
        _nextPublicIpv4PresenceLookupUtc = DateTimeOffset.MinValue;
        EnsurePublicIpv4InPresenceTargets();
        _presenceUdp = new UdpTransport(PresencePingCodec.UdpPort, enableBroadcast: true);
        _discoveryWireUdp = new UdpTransport(UdpPeerDiscoveryOptions.DefaultDiscoveryUdpPort, enableBroadcast: true);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;
        await _presenceUdp.StartAsync(cancellationToken).ConfigureAwait(false);
        await _discoveryWireUdp.StartAsync(cancellationToken).ConfigureAwait(false);
        foreach (var transport in _secondaryPresenceTransports)
        {
            await transport.StartAsync(cancellationToken).ConfigureAwait(false);
            if (transport.Kind == TransportKind.Bluetooth)
                _isBluetoothListening = true;
        }
        _presenceReceiveLoop = Task.Run(() => PresenceReceiveLoopAsync(token), token);
        _discoveryWireReceiveLoop = Task.Run(() => DiscoveryWireReceiveLoopAsync(token), token);
        foreach (var transport in _secondaryPresenceTransports)
            _secondaryPresenceReceiveLoops.Add(Task.Run(() => PresenceSecondaryReceiveLoopAsync(transport, token), token));
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

        var loops = new List<Task?>(_secondaryPresenceReceiveLoops.Count + 4)
        {
            _presenceReceiveLoop,
            _discoveryWireReceiveLoop,
            _periodicScanLoop,
            _staleLoop
        };
        loops.AddRange(_secondaryPresenceReceiveLoops);
        var activeLoops = loops.Where(t => t != null).ToArray();
        _presenceReceiveLoop = null;
        _discoveryWireReceiveLoop = null;
        _secondaryPresenceReceiveLoops.Clear();
        _periodicScanLoop = null;
        _staleLoop = null;
        if (activeLoops.Length > 0)
        {
            try
            {
                await Task.WhenAll(activeLoops!).ConfigureAwait(false);
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

        if (_discoveryWireUdp != null)
        {
            var d = _discoveryWireUdp;
            _discoveryWireUdp = null;
            try
            {
                await d.StopAsync(cancellationToken).ConfigureAwait(false);
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
        _isBluetoothListening = false;
        lock (_snapshotSync)
            _snapshot = [];

        foreach (var transport in _secondaryPresenceTransports)
        {
            try
            {
                await transport.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }
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
        _ => true
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
                var localPeer = _localPeer;
                if (localPeer == null)
                    continue;
                var myId = localPeer.NetworkId.Value;
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

    private async Task DiscoveryWireReceiveLoopAsync(CancellationToken cancellationToken)
    {
        if (_discoveryWireUdp == null)
            return;
        try
        {
            await foreach (var msg in _discoveryWireUdp.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!IsTransportEnabled(TransportKind.Udp))
                    continue;
                var buf = msg.Payload.ToArray();
                if (TryHandleGossipAckAsDiscovery(buf, msg.RemoteAddress))
                    continue;

                if (await TryReplyToGossipProbeAsync(_discoveryWireUdp, buf, msg.RemoteAddress, cancellationToken)
                        .ConfigureAwait(false))
                    continue;

                if (await TryReplyToRouteTableRequestAsync(_discoveryWireUdp, buf, msg.RemoteAddress,
                        cancellationToken).ConfigureAwait(false))
                    continue;
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
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
        if (localPeer != null && responderId == localPeer.NetworkId.Value)
            return true;

        if (remote.Kind != TransportKind.Udp)
            return true;

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
        nick = string.IsNullOrWhiteSpace(nick) ? "?" : nick.Trim();
        var peer = new DiscoveredLocalPeer(responderId, nick, dataAddr, TransportKind.Udp, DateTimeOffset.UtcNow,
            dataPort, LinkTechnologyPreset.Unlimited, PresencePeerCapabilities.Chat);
        OnDiscoveryPingReceived(peer);
        DiscoveryPingReceived?.Invoke(this, new DiscoveryPingReceivedEventArgs(peer));
        return true;
    }

    private void RegisterGossipBroadcastNonce(long nonce)
    {
        var prev = Interlocked.Exchange(ref _lastGossipBroadcastNonce, nonce);
        Volatile.Write(ref _priorGossipBroadcastNonce, prev);
    }

    private bool IsRecentGossipBroadcastNonce(long n) =>
        n == Volatile.Read(ref _lastGossipBroadcastNonce) || n == Volatile.Read(ref _priorGossipBroadcastNonce);

    private async Task PresenceSecondaryReceiveLoopAsync(ITransport transport, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var msg in transport.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
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
                var localPeer = _localPeer;
                if (localPeer == null)
                    continue;
                var myId = localPeer.NetworkId.Value;
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

    private async Task<bool> TryReplyToGossipProbeAsync(ITransport replyTransport, byte[] buf,
        TransportAddress remote, CancellationToken cancellationToken)
    {
        if (!GossipWireCodec.TryParseProbe(buf, out var nonce, out var sender, out var target))
            return false;

        var localPeer = _localPeer;
        if (localPeer == null)
            return true;
        var myGuid = localPeer.NetworkId.Value;

        if (sender == myGuid)
            return true;

        if (target != Guid.Empty && target != myGuid)
            return true;

        var nick = string.IsNullOrWhiteSpace(localPeer.Nickname) ? "?" : localPeer.Nickname.Trim();
        var port = localPeer.DataUdpPort is >= 1 and <= 65535
            ? localPeer.DataUdpPort
            : PresencePingCodec.DefaultDataUdpPort;
        var ack = GossipWireCodec.BuildAck(nonce, myGuid, port, nick);
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
        var myGuid = localPeer.NetworkId.Value;

        if (sender == myGuid)
            return true;

        var caps = routingSettings.AdvertisedPeerCapabilities | PresencePeerCapabilities.Chat;
        if (!caps.HasFlag(PresencePeerCapabilities.PeerSearch))
            return true;

        IReadOnlyList<Route> routes = Array.Empty<Route>();
        if (routeTableSnapshotSource != null)
        {
            try
            {
                routes = await routeTableSnapshotSource.GetRoutesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                routes = Array.Empty<Route>();
            }
        }

        var reply = RouteTableWireCodec.BuildReply(nonce, myGuid, routes);
        await replyTransport.SendAsync(reply, remote, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void OnDiscoveryPingReceived(DiscoveredLocalPeer peer)
    {
        discoveryPingStore?.Write(
            new PeerIdentity(peer.Nickname, new CompressedNetworkId(peer.NetworkId), peer.PeerDataUdpPort),
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
            _snapshot = list;
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
        EnsurePublicIpv4InPresenceTargets();
        var caps = routingSettings.AdvertisedPeerCapabilities | PresencePeerCapabilities.Chat;
        var payload = PresencePingCodec.Build(
            localPeer.NetworkId.Value,
            localPeer.Nickname,
            localPeer.DataUdpPort,
            routingSettings.LinkTechnology,
            caps);
        
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

        var bt = _secondaryPresenceTransports.FirstOrDefault(t => t.Kind == TransportKind.Bluetooth);
        if (bt != null && IsTransportEnabled(TransportKind.Bluetooth))
        {
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

        foreach (var transport in _secondaryPresenceTransports.Where(t => t.Kind != TransportKind.Bluetooth))
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

    /// <summary>
    ///     Gossip-зонды на wire-порт: те же broadcast, что LAN, и unicast на известные внешние/внутренние IPv4.
    /// </summary>
    private async Task SendGossipDiscoveryProbesAsync(PeerIdentity localPeer, CancellationToken cancellationToken)
    {
        var wireUdp = _discoveryWireUdp;
        if (wireUdp == null || !IsTransportEnabled(TransportKind.Udp))
            return;

        long nonce;
        do
        {
            nonce = Random.Shared.NextInt64();
        } while (nonce == 0);

        RegisterGossipBroadcastNonce(nonce);
        var probe = GossipWireCodec.BuildProbe(nonce, localPeer.NetworkId.Value, Guid.Empty);

        foreach (var ep in LanBroadcastHelper.GetIpv4BroadcastEndpoints(GossipWireCodec.UdpPort))
        {
            try
            {
                var addr = UdpTransportAddress.FromIPEndPoint(ep);
                await wireUdp.SendAsync(probe, addr, cancellationToken).ConfigureAwait(false);
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
                if (IPAddress.IsLoopback(ip))
                    continue;
                var addr = UdpTransportAddress.FromIPEndPoint(new IPEndPoint(ip, GossipWireCodec.UdpPort));
                await wireUdp.SendAsync(probe, addr, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // узел недоступен или фильтрация
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
        IEnumerable<ITransport>? additionalDiscoveryTransports)
    {
        var list = new List<ITransport>();
        if (bluetoothTransport != null && bluetoothTransport.Kind != TransportKind.Udp)
            list.Add(bluetoothTransport);
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
