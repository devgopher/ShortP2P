using System.Collections.Concurrent;
using System.Net;
using ShortP2P.Client.Data;
using ShortP2P.Client.Services;
using ShortP2P.Discovery;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Routing;

/// <summary>
///     Единый UDP-сокет пользователя: поиск пиров по графу (≤3 рёбер), ретрансляция, демультиплексирование чата.
///     Опционально — второй транспорт (например Bluetooth RFCOMM на Windows) для прямой доставки по <see cref="TransportAddress" />.
/// </summary>
public sealed class SharedUserUdpGateway(
    AuthService auth,
    ChatRepository chats,
    P2pRoutingSettings settings,
    ITransport? bluetoothTransport = null)
    : IAsyncDisposable
{
    private static readonly TimeSpan PresencePingInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PresenceStaleTimeout = TimeSpan.FromSeconds(25);

    private IPeerDiscoveryService? _discovery;

    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<PeerSearchResult>> _searchWaits = new();
    private readonly ConcurrentDictionary<(Guid Sid, string HopKey), TransportAddress> _foundReturnPath = new();

    private UdpTransport? _udp;
    private UdpTransport? _presenceUdp;
    private UserEntity? _user;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private Task? _presenceReceiveLoop;
    private Task? _bluetoothReceiveLoop;
    private Task? _presenceAnnounceLoop;
    private Task? _presenceStaleLoop;
    private Func<ReadOnlyMemory<byte>, TransportAddress, Task>? _chatSink;
    private readonly object _startSync = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _presenceSeenUtc = new();
    private readonly ConcurrentDictionary<Guid, TransportAddress> _presenceAddress = new();

    public event EventHandler<PeerPresenceChangedEventArgs>? PeerPresenceChanged;

    /// <summary>
    ///     Входящий discovery/presence ping (порт 565 или тот же кадр по Bluetooth).
    /// </summary>
    public event EventHandler<DiscoveryPingReceivedEventArgs>? DiscoveryPingReceived;

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
            _presenceUdp = new UdpTransport(PresencePingCodec.UdpPort, enableBroadcast: true);
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
            _presenceReceiveLoop = Task.Run(() => PresenceReceiveLoopAsync(_cts.Token), _cts.Token);
            if (bluetoothTransport != null)
                _bluetoothReceiveLoop = Task.Run(() => BluetoothReceiveLoopAsync(_cts.Token), _cts.Token);
            _presenceAnnounceLoop = Task.Run(() => PresenceAnnounceLoopAsync(_cts.Token), _cts.Token);
            _presenceStaleLoop = Task.Run(() => PresenceStaleLoopAsync(_cts.Token), _cts.Token);
        }

        await _udp.StartAsync(cancellationToken).ConfigureAwait(false);
        await _presenceUdp!.StartAsync(cancellationToken).ConfigureAwait(false);
        if (bluetoothTransport != null)
            await bluetoothTransport.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? loop;
        Task? presenceLoop;
        Task? bluetoothLoop;
        Task? presenceAnnounce;
        Task? presenceStale;
        lock (_startSync)
        {
            _cts?.Cancel();
            loop = _receiveLoop;
            presenceLoop = _presenceReceiveLoop;
            bluetoothLoop = _bluetoothReceiveLoop;
            presenceAnnounce = _presenceAnnounceLoop;
            presenceStale = _presenceStaleLoop;
            _receiveLoop = null;
            _presenceReceiveLoop = null;
            _bluetoothReceiveLoop = null;
            _presenceAnnounceLoop = null;
            _presenceStaleLoop = null;
            _cts?.Dispose();
            _cts = null;
            if (_udp != null)
            {
                var u = _udp;
                _udp = null;
                _ = u.StopAsync(cancellationToken);
            }

            if (_presenceUdp != null)
            {
                var p = _presenceUdp;
                _presenceUdp = null;
                _ = p.StopAsync(cancellationToken);
            }

            _user = null;
        }

        var loops = new[] { loop, presenceLoop, bluetoothLoop, presenceAnnounce, presenceStale }.Where(t => t != null)
            .ToArray();
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

        if (bluetoothTransport != null)
        {
            try
            {
                await bluetoothTransport.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        foreach (var kv in _searchWaits)
            kv.Value.TrySetCanceled();
        _searchWaits.Clear();
        _foundReturnPath.Clear();
        _presenceSeenUtc.Clear();
        _presenceAddress.Clear();
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
        _ = _udp ?? throw new InvalidOperationException("Gateway not started.");
        if (route is { FirstHop: not null, RelayStrip.Count: > 0 })
        {
            var relay = LanRoutingCodec.BuildRelay(route.RelayStrip, prefixedFrame.Span);
            await SendToTransportAsync(relay, route.FirstHop, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await SendToTransportAsync(prefixedFrame, route.Direct, cancellationToken).ConfigureAwait(false);
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
                await ProcessIncomingBufferAsync(buf, msg.RemoteAddress, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task BluetoothReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var bt = bluetoothTransport;
        if (bt == null) return;
        try
        {
            await foreach (var msg in bt.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var buf = msg.Payload.ToArray();
                await ProcessIncomingBufferAsync(buf, msg.RemoteAddress, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Отправка датаграммы на выделенный порт discovery/presence (565).</summary>
    public async ValueTask SendOnPresencePortAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        var p = _presenceUdp ?? throw new InvalidOperationException("Gateway not started.");
        await p.SendAsync(payload, destination, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Отправка на data-UDP пира (тот же сокет, что и сообщения чата).</summary>
    public async ValueTask SendOnDataUdpAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        var udp = _udp ?? throw new InvalidOperationException("Gateway not started.");
        await udp.SendAsync(payload, destination, cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessIncomingBufferAsync(byte[] buf, TransportAddress remoteAddress,
        CancellationToken cancellationToken)
    {
        if (buf.Length == 0)
            return;

        if (buf.Length >= 17 && buf[0] == PresencePingCodec.FramePresencePing &&
            PresencePingCodec.TryParse(buf, out var pingId, out var pingNick, out var peerDataPort))
        {
            await HandleDiscoveryPingAsync(pingId, pingNick, peerDataPort, remoteAddress).ConfigureAwait(false);
            return;
        }

        var relayLocalInner = ExtractLocalRelayInner(buf);
        if (relayLocalInner is { Length: > 0 })
        {
            if (relayLocalInner[0] == ChatInviteCodec.FrameChatInvite)
                await HandleChatInviteAsync(relayLocalInner, cancellationToken).ConfigureAwait(false);
            else
                await DispatchChatOrDropAsync(relayLocalInner, remoteAddress).ConfigureAwait(false);
            return;
        }

        if (buf[0] == LanRoutingCodec.FrameRelay && buf.Length > 2 && buf[1] > 0)
        {
            if (LanRoutingCodec.TryStripRelayHop(buf, out var next, out var fwd) && next != null && fwd != null)
            {
                try
                {
                    await SendToTransportAsync(fwd, next, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
            }

            return;
        }

        if (buf[0] == LanRoutingCodec.FrameFind)
        {
            await HandleFindAsync(buf, remoteAddress, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (buf[0] == LanRoutingCodec.FrameFound)
        {
            await HandleFoundAsync(buf, remoteAddress, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (buf[0] == ChatInviteCodec.FrameChatInvite)
        {
            await HandleChatInviteAsync(buf, cancellationToken).ConfigureAwait(false);
            return;
        }

        await DispatchChatOrDropAsync(buf, remoteAddress).ConfigureAwait(false);
    }

    /// <summary>
    ///     Приём только presence ping на порту <see cref="PresencePingCodec.UdpPort" />; адрес для данных — IP отправителя и
    ///     <see cref="ChatEntity.PeerPort" /> из чата.
    /// </summary>
    private async Task PresenceReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var presenceUdp = _presenceUdp;
        if (presenceUdp == null) return;
        try
        {
            await foreach (var msg in presenceUdp.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var buf = msg.Payload.ToArray();
                if (!PresencePingCodec.TryParse(buf, out var pingSender, out var nick, out var dataPort))
                    continue;
                await HandleDiscoveryPingAsync(pingSender, nick, dataPort, msg.RemoteAddress).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task HandleDiscoveryPingAsync(Guid networkId, string nickname, int peerDataUdpPort,
        TransportAddress remote)
    {
        var peer = new DiscoveredLocalPeer(networkId, nickname, remote, remote.Kind, DateTimeOffset.UtcNow,
            peerDataUdpPort);
        DiscoveryPingReceived?.Invoke(this, new DiscoveryPingReceivedEventArgs(peer));

        var user = _user;
        if (user == null) return;
        var shortId = CompressedNetworkId.FromGuid(networkId).ToShortString();
        var chat = await chats.FindChatByPeerNetworkIdAsync(user.Id, shortId).ConfigureAwait(false);
        if (chat == null) return;
        if (remote.Kind == TransportKind.Udp)
        {
            var dataAddr = UdpTransportAddress.WithUdpPort(remote, chat.PeerPort);
            MarkPeerOnline(networkId, dataAddr);
        }
        else if (remote.Kind == TransportKind.Bluetooth)
        {
            MarkPeerOnline(networkId, remote);
        }
    }

    private async Task SendToTransportAsync(ReadOnlyMemory<byte> packet, TransportAddress destination,
        CancellationToken cancellationToken)
    {
        switch (destination.Kind)
        {
            case TransportKind.Udp:
                var udp = _udp ?? throw new InvalidOperationException("Gateway not started.");
                await udp.SendAsync(packet, destination, cancellationToken).ConfigureAwait(false);
                return;
            case TransportKind.Bluetooth:
                var bt = bluetoothTransport ?? throw new InvalidOperationException("Bluetooth transport is not configured.");
                await bt.SendAsync(packet, destination, cancellationToken).ConfigureAwait(false);
                return;
            default:
                throw new NotSupportedException($"Transport kind '{destination.Kind}' is not supported.");
        }
    }

    private async Task HandleChatInviteAsync(byte[] packet, CancellationToken ct)
    {
        await IncomingChatInviteHandler.TryAcceptAsync(packet, auth, chats,
            async (payload, dest, token) =>
            {
                var udp = _udp ?? throw new InvalidOperationException("Gateway not started.");
                await udp.SendAsync(payload, dest, token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
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
                await SendToTransportAsync(found, from, ct).ConfigureAwait(false);
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
        if (_udp == null) return;
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
                await SendToTransportAsync(packet, upstream, ct).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private static bool AddrEquals(TransportAddress a, TransportAddress b) =>
        a.Kind == b.Kind && a.Data.AsSpan().SequenceEqual(b.Data);

    private static string AddrKey(TransportAddress a) => Convert.ToHexString(a.Data);

    public bool IsPeerOnline(Guid peerNetworkId)
    {
        if (!_presenceSeenUtc.TryGetValue(peerNetworkId, out var seen))
            return false;
        return DateTimeOffset.UtcNow - seen <= PresenceStaleTimeout;
    }

    public bool TryGetPeerLastSeenAddress(Guid peerNetworkId, out TransportAddress address)
    {
        return _presenceAddress.TryGetValue(peerNetworkId, out address!);
    }

    public bool IsTransportAvailable(TransportKind kind)
    {
        return kind switch
        {
            TransportKind.Udp => _udp != null,
            TransportKind.Bluetooth => bluetoothTransport != null,
            _ => false
        };
    }

    public async ValueTask SendRawToAsync(ReadOnlyMemory<byte> packet, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        _ = _udp ?? throw new InvalidOperationException("Gateway not started.");
        await SendToTransportAsync(packet, destination, cancellationToken).ConfigureAwait(false);
    }

    private void MarkPeerOnline(Guid peerNetworkId, TransportAddress from)
    {
        var now = DateTimeOffset.UtcNow;
        var becameOnline = !_presenceSeenUtc.ContainsKey(peerNetworkId);
        _presenceSeenUtc[peerNetworkId] = now;
        _presenceAddress[peerNetworkId] = from;
        if (becameOnline)
            PeerPresenceChanged?.Invoke(this, new PeerPresenceChangedEventArgs(peerNetworkId, true));
    }

    private async Task PresenceAnnounceLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await BroadcastPresencePingAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(PresencePingInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // ignore one failed presence iteration
            }
        }
    }

    /// <summary>
    /// Рассылка пинг пакетов для всех пиров
    /// </summary>
    /// <param name="cancellationToken"></param>
    private async Task BroadcastPresencePingAsync(CancellationToken cancellationToken)
    {
        var presenceUdp = _presenceUdp;
        var user = _user;
        if (presenceUdp == null || user == null)
            return;

        var payload = PresencePingCodec.Build(CompressedNetworkId.FromShortString(user.NetworkIdShort).Value,
            user.Nickname, user.DataUdpPort);
        var peers = await chats.ListChatsAsync(user.Id).ConfigureAwait(false);
        foreach (var c in peers)
        {
            try
            {
                var ep = UdpTransportAddress.FromIPEndPoint(
                    new IPEndPoint(IPAddress.Parse(c.PeerHost), PresencePingCodec.UdpPort));
                await presenceUdp.SendAsync(payload, ep, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // bad endpoint or temporary send issue
            }
        }
    }

    private async Task PresenceStaleLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var kv in _presenceSeenUtc.ToArray())
            {
                if (now - kv.Value <= PresenceStaleTimeout)
                    continue;
                if (_presenceSeenUtc.TryRemove(kv.Key, out _))
                {
                    _presenceAddress.TryRemove(kv.Key, out _);
                    PeerPresenceChanged?.Invoke(this, new PeerPresenceChangedEventArgs(kv.Key, false));
                }
            }
        }
    }

    /// <summary>Возвращает полезную нагрузку, если это локальная доставка RELAY (hop count 0).</summary>
    private static byte[]? ExtractLocalRelayInner(byte[] buf)
    {
        if (buf.Length < 3 || buf[0] != LanRoutingCodec.FrameRelay || buf[1] != 0)
            return null;
        return buf.AsSpan(2).ToArray();
    }
}

public sealed class PeerPresenceChangedEventArgs(Guid peerNetworkId, bool isOnline) : EventArgs
{
    public Guid PeerNetworkId { get; } = peerNetworkId;
    public bool IsOnline { get; } = isOnline;
}
