using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using ShortP2P.Auth;
using ShortP2P.Auth.Data;
using ShortP2P.Client.ChatMedia;
using ShortP2P.Client.Data;
using ShortP2P.Client.Qr;
using ShortP2P.Client.Routing;
using ShortP2P.Discovery;
using ShortP2P.Discovery.Pings;
using ShortP2P.Discovery.RouteTables;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Services;

/// <summary>Настройки маршрутизации и LAN discovery для сессии пользователя.</summary>
public sealed class UserP2pRuntime : IAsyncDisposable
{
    private readonly P2pRoutingSettingsStore _store;
    private readonly AuthService _auth;
    private readonly ChatRepository _chats;
    private readonly ChatMediaOptions _chatMedia;
    private readonly IUdpTransportFactory _udpTransportFactory;
    private readonly ITransport? _bluetooth;
    private readonly P2pCryptoSessionCache _cryptoSessionCache;

    private readonly ChatSessionCache _sessionCache;
    /// <summary>Сериализация старта сессии по discovery-пингу для одного чата (без await внутри lock).</summary>
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _discoverySessionStartGates = new();
    private readonly HashSet<string> _selfUdpAddresses = new(StringComparer.OrdinalIgnoreCase);

    private volatile bool _discoveryHooked;
    private CancellationTokenSource? _presencePingWorkCts;

    private readonly SemaphoreSlim _inviteListenerGate = new(1, 1);
    private CancellationTokenSource? _inviteCts;
    private UdpTransport? _inviteUdp;
    private Task? _inviteReceiveTask;

    public P2pRoutingSettings Settings { get; } = new();
    public ITransport? BluetoothTransport => _bluetooth;

    /// <summary>
    ///     Сканирование LAN: presence UDP 50101; wire discovery (gossip / маршруты)
    ///     <see cref="UdpPeerDiscoveryOptions.DefaultDiscoveryUdpPort" />
    /// </summary>
    public LocalNetworkScanner LocalScan { get; }

    public IUdpTransportFactory UdpTransportFactory => _udpTransportFactory;

    public UserP2pRuntime(P2pRoutingSettingsStore store, AuthService auth, ChatRepository chats,
        ChatMediaOptions chatMedia, IUdpTransportFactory udpTransportFactory, ChatSessionCache sessionCache,
        P2pCryptoSessionCache cryptoSessionCache,
        ITransport? bluetooth = null,
        IEnumerable<ITransport>? additionalDiscoveryTransports = null,
        IRouteTableSnapshotSource? routeTableSnapshotSource = null,
        IDiscoveryPingStore? discoveryPingStore = null)
    {
        _store = store;
        _auth = auth;
        _chats = chats;
        _chatMedia = chatMedia;
        _udpTransportFactory = udpTransportFactory;
        _sessionCache = sessionCache;
        _cryptoSessionCache = cryptoSessionCache;
        _bluetooth = bluetooth;
        LocalScan = new LocalNetworkScanner(Settings, udpTransportFactory, bluetooth, additionalDiscoveryTransports,
            routeTableSnapshotSource, discoveryPingStore);
    }

    public ChatP2pSession GetSession(ChatEntity chat, UserEntity user, AuthService auth, ChatRepository repo,
        SynchronizationContext? uiSync)
    {
        return _sessionCache.GetSession(chat.Id,
            () => ChatP2pSession.Create(chat, user, auth, repo, uiSync, Settings, LocalScan, _chatMedia, _bluetooth,
                _cryptoSessionCache),
            s => s.ApplyChatRow(chat));
    }

    public bool IsChatSessionStarted(int chatId)
    {
        return _sessionCache.IsStarted(chatId);
    }

    public void MarkChatSessionStarted(int chatId)
    {
        _sessionCache.MarkStarted(chatId);
    }

    /// <summary>Останавливает и снимает P2P-сессию для удалённого из БД чата.</summary>
    public async Task RemoveChatSessionAsync(int chatId, CancellationToken cancellationToken = default)
    {
        _sessionCache.TryRemove(chatId, out var session);

        if (_discoverySessionStartGates.TryRemove(chatId, out var gate))
        {
            try
            {
                gate.Dispose();
            }
            catch
            {
                // ignore
            }
        }

        if (session != null)
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }
    }

    /// <summary>
    ///     Останавливает приёмник инвайтов на <see cref="ChatInviteCodec.InviteUdpPort" /> (перед временным bind
    ///     из <see cref="LanChatStartFromDiscovery" /> на том же порту).
    /// </summary>
    public async Task StopInviteListenerAsync(CancellationToken cancellationToken = default)
    {
        await _inviteListenerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopInviteListenerCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _inviteListenerGate.Release();
        }
    }

    /// <summary>
    ///     Поднимает приёмник входящих <see cref="ChatInviteCodec" /> на <see cref="ChatInviteCodec.InviteUdpPort" />
    ///     (отдельно от data/чата на <see cref="UserEntity.DataUdpPort" />).
    /// </summary>
    public async Task EnsureInviteListenerRunningAsync(UserEntity user, CancellationToken cancellationToken = default)
    {
        await _inviteListenerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_inviteUdp != null)
                return;

            _inviteCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _inviteCts.Token;
            _inviteUdp = _udpTransportFactory.Acquire(IPAddress.Any, ChatInviteCodec.InviteUdpPort);
            await _inviteUdp.StartAsync(cancellationToken).ConfigureAwait(false);
            _inviteReceiveTask = Task.Run(() => InviteReceiveLoopAsync(token), token);
        }
        catch
        {
            await StopInviteListenerCoreAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _inviteListenerGate.Release();
        }
    }

    /// <summary>Запускает фоновые P2P-сессии для всех чатов из БД (ещё не стартовавшие).</summary>
    public async Task EnsureAllChatSessionsStartedAsync(UserEntity user, AuthService auth, ChatRepository repo,
        SynchronizationContext? uiSync, CancellationToken cancellationToken = default)
    {
        var list = await repo.ListChatsAsync(user.Id).ConfigureAwait(false);
        foreach (var c in list)
        {
            var session = GetSession(c, user, auth, repo, uiSync);
            var needStart = !IsChatSessionStarted(c.Id);

            if (!needStart)
                continue;
            try
            {
                await session.StartAsync(cancellationToken).ConfigureAwait(false);
                MarkChatSessionStarted(c.Id);
            }
            catch
            {
                // пир недоступен и т.п.
            }
        }
    }

    private async Task InviteReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var udp = _inviteUdp;
        if (udp == null)
            return;
        try
        {
            await foreach (var msg in udp.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (IsOwnInviteDatagram(msg))
                    continue;
                var buf = msg.Payload.ToArray();
                if (buf.Length == 0 || buf[0] != ChatInviteCodec.FrameChatInvite)
                    continue;
                // Не проталкивать cancellationToken в TryAccept: при остановке слушателя иначе можно прервать AddChat по пути.
                await IncomingChatInviteHandler.TryAcceptAsync(buf, _auth, _chats,
                    async (payload, dest, _) => await udp.SendAsync(payload, dest, CancellationToken.None)
                        .ConfigureAwait(false),
                    msg.RemoteAddress,
                    CancellationToken.None).ConfigureAwait(false);
                if (!ChatInviteCodec.TryParse(buf, out var peerGuid, out _, out _, out _, out _)) 
                    continue;
                var user = _auth.CurrentUser;
                if (user == null) 
                    continue;
                var peerShort = CompressedNetworkId.FromGuid(peerGuid).ToShortString();
                var chat = await _chats.FindChatByPeerNetworkIdAsync(user.Id, peerShort).ConfigureAwait(false);
                if (chat == null)
                    continue;
                var session = GetSession(chat, user, _auth, _chats, uiSync: null);
                if (IsChatSessionStarted(chat.Id))
                    continue;
                try
                {
                    await session.StartAsync(cancellationToken).ConfigureAwait(false);
                    MarkChatSessionStarted(chat.Id);
                }
                catch
                {
                    // peer may be temporarily unavailable
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
    }

    private void RefreshSelfUdpAddresses()
    {
        _selfUdpAddresses.Clear();
        foreach (var ip in LocalIPv4Resolver.GetAllUnicastIpv4Ordered())
        {
            if (System.Net.IPAddress.TryParse(ip, out var parsed))
                _selfUdpAddresses.Add(parsed.ToString());
        }

        _selfUdpAddresses.Add(System.Net.IPAddress.Loopback.ToString());
        _selfUdpAddresses.Add(System.Net.IPAddress.Any.ToString());
    }

    private bool IsOwnInviteDatagram(TransportReceiveMessage msg)
    {
        if (msg.RemoteAddress.Kind != TransportKind.Udp)
            return false;
        var user = _auth.CurrentUser;
        if (user == null)
            return false;
        try
        {
            var ep = UdpTransportAddress.ToIPEndPoint(msg.RemoteAddress);
            return ep.Port == user.DataUdpPort && _selfUdpAddresses.Contains(ep.Address.ToString());
        }
        catch
        {
            return false;
        }
    }

    private async Task StopInviteListenerCoreAsync(CancellationToken cancellationToken)
    {
        if (_inviteCts != null)
        {
            try
            {
                await _inviteCts.CancelAsync().ConfigureAwait(false);
            }
            catch
            {
                await _inviteCts.CancelAsync().ConfigureAwait(false);
            }
        }

        if (_inviteReceiveTask != null)
        {
            try
            {
                await _inviteReceiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }

        _inviteReceiveTask = null;

        if (_inviteUdp != null)
        {
            var u = _inviteUdp;
            _inviteUdp = null;
            try
            {
                await _udpTransportFactory.ReleaseAsync(u, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        _inviteCts?.Dispose();
        _inviteCts = null;
    }

    public async Task EnsureStartedAsync(UserEntity user, CancellationToken cancellationToken = default)
    {
        RefreshSelfUdpAddresses();
        var persisted = await _store.LoadAsync().ConfigureAwait(false);
        Settings.MaxSearchHops = persisted.MaxSearchHops;
        Settings.SendFailureSearchAttempts = persisted.SendFailureSearchAttempts;
        Settings.SendFailureRetryDelay = persisted.SendFailureRetryDelay;
        Settings.SearchWaitTimeout = persisted.SearchWaitTimeout;
        Settings.LinkTechnology = persisted.LinkTechnology;
        Settings.EnableUdpTransport = persisted.EnableUdpTransport;
        Settings.EnableBluetoothTransport = persisted.EnableBluetoothTransport;
        Settings.SuggestBluetoothPairing = persisted.SuggestBluetoothPairing;
        Settings.TrafficSavingEnabled = persisted.TrafficSavingEnabled;
        Settings.AdvertisedPeerCapabilities = persisted.AdvertisedPeerCapabilities | PresencePeerCapabilities.Chat;

        // Инвайты (отдельный UDP) должны работать даже если presence/LAN bind на Android не удался.
        await EnsureInviteListenerRunningAsync(user, cancellationToken).ConfigureAwait(false);

        try
        {
            var localPeer = new PeerIdentity(user.Nickname,
                CompressedNetworkId.FromShortString(user.NetworkIdShort),
                user.DataUdpPort);
            await LocalScan.StartAsync(localPeer, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        if (!_discoveryHooked)
        {
            _presencePingWorkCts = new CancellationTokenSource();
            LocalScan.DiscoveryPingReceived += OnDiscoveryPingReceived;
            _discoveryHooked = true;
        }
    }

    private void OnDiscoveryPingReceived(object? sender, DiscoveryPingReceivedEventArgs e)
    {
        var cts = _presencePingWorkCts;
        if (cts == null || cts.IsCancellationRequested)
            return;
        var token = cts.Token;
        _ = Task.Run(() => HandleDiscoveryPingAsync(e.Peer, token), token);
    }

    private async Task HandleDiscoveryPingAsync(DiscoveredLocalPeer peer, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;
        var user = _auth.CurrentUser;
        if (user == null)
            return;
        if (peer.TransportKind is not TransportKind.Udp and not TransportKind.Bluetooth)
            return;

        var shortId = CompressedNetworkId.FromGuid(peer.NetworkId).ToShortString();
        var chat = await _chats.FindChatByPeerNetworkIdAsync(user.Id, shortId).ConfigureAwait(false);
        if (chat == null)
            return;

        var seenDirect = peer.TransportKind == TransportKind.Udp
            ? UdpTransportAddress.ToIPEndPoint(peer.SourceAddress).Address.ToString()
            : BluetoothTransportAddress.ToMacString(peer.SourceAddress.Data);

        await ApplyDiscoveryPingRouteAsync(chat, peer.TransportKind, seenDirect, cancellationToken)
            .ConfigureAwait(false);
        await TryEnsureChatSessionStartedFromDiscoveryAsync(chat.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Обновление маршрута из пинга; старт сессии выполняется отдельно
    ///     <see cref="TryEnsureChatSessionStartedFromDiscoveryAsync" />.
    /// </summary>
    private async Task ApplyDiscoveryPingRouteAsync(ChatEntity chat, TransportKind pingKind, string seenDirect,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(chat.RelayRouteBlob))
        {
            var mergedRelay = PeerHostList.WithPrimaryFirst(chat.PeerHost, seenDirect);
            if (string.Equals(mergedRelay, chat.PeerHost, StringComparison.Ordinal))
                return;
            await _chats.UpdateChatP2pRouteAsync(chat.Id, mergedRelay, chat.PeerPort, chat.RelayRouteBlob)
                .ConfigureAwait(false);
            _chats.NotifyChatListChanged();
            await RefreshSessionChatRowAsync(chat.Id, cancellationToken).ConfigureAwait(false);
            return;
        }

        _sessionCache.TryGetSession(chat.Id, out var session);
        var started = _sessionCache.IsStarted(chat.Id);

        if (started && session != null)
        {
            if (pingKind == TransportKind.Udp)
            {
                var primary = PeerHostList.PrimaryHost(chat.PeerHost);
                if (string.Equals(primary, seenDirect, StringComparison.Ordinal))
                    return;
                await session.ApplyPeerEndpointAsync(seenDirect, chat.PeerPort, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var mergedBt = PeerHostList.WithPrimaryFirst(chat.PeerHost, seenDirect);
                if (string.Equals(mergedBt, chat.PeerHost, StringComparison.Ordinal))
                    return;
                await _chats.UpdateChatP2pRouteAsync(chat.Id, mergedBt, chat.PeerPort, null).ConfigureAwait(false);
                _chats.NotifyChatListChanged();
                await RefreshSessionChatRowAsync(chat.Id, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var mergedHost = PeerHostList.WithPrimaryFirst(chat.PeerHost, seenDirect);
        if (string.Equals(mergedHost, chat.PeerHost, StringComparison.Ordinal))
            return;

        await _chats.UpdateChatP2pRouteAsync(chat.Id, mergedHost, chat.PeerPort, null).ConfigureAwait(false);
        _chats.NotifyChatListChanged();
        await RefreshSessionChatRowAsync(chat.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Локальная отметка: <see cref="_sessionsStarted" />. Первый успешный пинг собеседника поднимает сессию, если ещё не в множестве.
    /// </summary>
    private async Task TryEnsureChatSessionStartedFromDiscoveryAsync(int chatId,
        CancellationToken cancellationToken)
    {
        if (IsChatSessionStarted(chatId))
            return;

        var sem = _discoverySessionStartGates.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsChatSessionStarted(chatId))
                return;

            var user = _auth.CurrentUser;
            if (user == null)
                return;

            var chat = await _chats.GetChatAsync(chatId).ConfigureAwait(false);
            if (chat == null)
                return;

            if (IsChatSessionStarted(chatId))
                return;
            var session = GetSession(chat, user, _auth, _chats, uiSync: null);

            try
            {
                await session.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            MarkChatSessionStarted(chatId);
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task RefreshSessionChatRowAsync(int chatId, CancellationToken cancellationToken)
    {
        var fresh = await _chats.GetChatAsync(chatId).ConfigureAwait(false);
        if (fresh == null)
            return;
        cancellationToken.ThrowIfCancellationRequested();
        if (_sessionCache.TryGetSession(chatId, out var session))
            session.ApplyChatRow(fresh);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await StopInviteListenerAsync(cancellationToken).ConfigureAwait(false);

        if (_discoveryHooked)
        {
            LocalScan.DiscoveryPingReceived -= OnDiscoveryPingReceived;
            _discoveryHooked = false;
            try
            {
                _presencePingWorkCts?.Cancel();
            }
            catch
            {
                // ignore
            }

            _presencePingWorkCts?.Dispose();
            _presencePingWorkCts = null;
        }

        var sessions = _sessionCache.DrainAll();

        foreach (var kv in _discoverySessionStartGates.ToArray())
        {
            if (_discoverySessionStartGates.TryRemove(kv.Key, out var g))
            {
                try
                {
                    g.Dispose();
                }
                catch
                {
                    // ignore
                }
            }
        }

        foreach (var s in sessions)
        {
            try
            {
                await s.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        await LocalScan.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
