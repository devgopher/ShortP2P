using System.Collections.Generic;
using ShortP2P.Client.Data;
using ShortP2P.Client.LocalNetwork;
using ShortP2P.Client.Routing;
using ShortP2P.Discovery;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Services;

/// <summary>Настройки маршрутизации и LAN discovery для сессии пользователя.</summary>
public sealed class UserP2pRuntime : IAsyncDisposable
{
    private readonly P2pRoutingSettingsStore _store;
    private readonly AuthService _auth;
    private readonly ChatRepository _chats;

    private readonly object _sessionLock = new();
    private readonly Dictionary<int, ChatP2pSession> _chatSessions = new();
    private readonly HashSet<int> _sessionsStarted = [];

    private volatile bool _discoveryHooked;
    private CancellationTokenSource? _presencePingWorkCts;

    private readonly SemaphoreSlim _inviteListenerGate = new(1, 1);
    private CancellationTokenSource? _inviteCts;
    private UdpTransport? _inviteUdp;
    private Task? _inviteReceiveTask;

    public P2pRoutingSettings Settings { get; } = new();

    /// <summary>Сканирование LAN по discovery-пингам (UDP 565, broadcast).</summary>
    public LocalNetworkScanner LocalScan { get; }

    public UserP2pRuntime(P2pRoutingSettingsStore store, AuthService auth, ChatRepository chats)
    {
        _store = store;
        _auth = auth;
        _chats = chats;
        LocalScan = new LocalNetworkScanner(Settings);
    }

    public ChatP2pSession GetOrCreateSession(ChatEntity chat, UserEntity user, AuthService auth, ChatRepository repo,
        SynchronizationContext? uiSync)
    {
        lock (_sessionLock)
        {
            if (_chatSessions.TryGetValue(chat.Id, out var existing))
            {
                existing.ApplyChatRow(chat);
                return existing;
            }

            var s = new ChatP2pSession(chat, user, auth, repo, uiSync, Settings, LocalScan);
            _chatSessions[chat.Id] = s;
            return s;
        }
    }

    public bool IsChatSessionStarted(int chatId)
    {
        lock (_sessionLock)
            return _sessionsStarted.Contains(chatId);
    }

    public void MarkChatSessionStarted(int chatId)
    {
        lock (_sessionLock)
            _sessionsStarted.Add(chatId);
    }

    /// <summary>Останавливает и снимает P2P-сессию для удалённого из БД чата.</summary>
    public async Task RemoveChatSessionAsync(int chatId, CancellationToken cancellationToken = default)
    {
        ChatP2pSession? session;
        lock (_sessionLock)
        {
            _chatSessions.Remove(chatId, out session);
            _sessionsStarted.Remove(chatId);
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
            _inviteUdp = new UdpTransport(ChatInviteCodec.InviteUdpPort);
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
            ChatP2pSession session;
            bool needStart;
            lock (_sessionLock)
            {
                if (_chatSessions.TryGetValue(c.Id, out var existing))
                {
                    existing.ApplyChatRow(c);
                    session = existing;
                }
                else
                {
                    session = new ChatP2pSession(c, user, auth, repo, uiSync, Settings, LocalScan);
                    _chatSessions[c.Id] = session;
                }

                needStart = !_sessionsStarted.Contains(c.Id);
            }

            if (!needStart)
                continue;
            try
            {
                await session.StartAsync(cancellationToken).ConfigureAwait(false);
                lock (_sessionLock)
                    _sessionsStarted.Add(c.Id);
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
                var buf = msg.Payload.ToArray();
                if (buf.Length == 0 || buf[0] != ChatInviteCodec.FrameChatInvite)
                    continue;
                // Не проталкивать cancellationToken в TryAccept: при остановке слушателя иначе можно прервать AddChat по пути.
                await IncomingChatInviteHandler.TryAcceptAsync(buf, _auth, _chats,
                    async (payload, dest, _) => await udp.SendAsync(payload, dest, CancellationToken.None)
                        .ConfigureAwait(false),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
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
                await u.StopAsync(cancellationToken).ConfigureAwait(false);
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
        var persisted = await _store.LoadAsync().ConfigureAwait(false);
        Settings.MaxSearchHops = persisted.MaxSearchHops;
        Settings.SendFailureSearchAttempts = persisted.SendFailureSearchAttempts;
        Settings.SendFailureRetryDelay = persisted.SendFailureRetryDelay;
        Settings.SearchWaitTimeout = persisted.SearchWaitTimeout;
        Settings.LinkTechnology = persisted.LinkTechnology;

        await LocalScan.StartAsync(user, cancellationToken).ConfigureAwait(false);

        if (!_discoveryHooked)
        {
            _presencePingWorkCts = new CancellationTokenSource();
            LocalScan.DiscoveryPingReceived += OnDiscoveryPingReceived;
            _discoveryHooked = true;
        }

        await EnsureInviteListenerRunningAsync(user, cancellationToken).ConfigureAwait(false);
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
        if (user == null || peer.TransportKind != TransportKind.Udp)
            return;

        var shortId = CompressedNetworkId.FromGuid(peer.NetworkId).ToShortString();
        var chat = await _chats.FindChatByPeerNetworkIdAsync(user.Id, shortId).ConfigureAwait(false);
        if (chat == null)
            return;

        var seenIp = UdpTransportAddress.ToIPEndPoint(peer.SourceAddress).Address.ToString();

        if (!string.IsNullOrEmpty(chat.RelayRouteBlob))
        {
            var mergedRelay = PeerHostList.MergeAppend(chat.PeerHost, seenIp);
            if (string.Equals(mergedRelay, chat.PeerHost, StringComparison.Ordinal))
                return;
            await _chats.UpdateChatP2pRouteAsync(chat.Id, mergedRelay, chat.PeerPort, chat.RelayRouteBlob)
                .ConfigureAwait(false);
            _chats.NotifyChatListChanged();
            await RefreshSessionChatRowAsync(chat.Id, cancellationToken).ConfigureAwait(false);
            return;
        }

        ChatP2pSession? session;
        var started = false;
        lock (_sessionLock)
        {
            if (_chatSessions.TryGetValue(chat.Id, out session))
                started = _sessionsStarted.Contains(chat.Id);
        }

        if (started && session != null)
        {
            var primary = PeerHostList.PrimaryHost(chat.PeerHost);
            if (string.Equals(primary, seenIp, StringComparison.Ordinal))
                return;
            await session.ApplyPeerEndpointAsync(seenIp, chat.PeerPort, cancellationToken).ConfigureAwait(false);
            return;
        }

        var mergedHost = PeerHostList.MergeAppend(chat.PeerHost, seenIp);
        if (string.Equals(mergedHost, chat.PeerHost, StringComparison.Ordinal))
            return;

        await _chats.UpdateChatP2pRouteAsync(chat.Id, mergedHost, chat.PeerPort, null).ConfigureAwait(false);
        _chats.NotifyChatListChanged();
        await RefreshSessionChatRowAsync(chat.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshSessionChatRowAsync(int chatId, CancellationToken cancellationToken)
    {
        var fresh = await _chats.GetChatAsync(chatId).ConfigureAwait(false);
        if (fresh == null)
            return;
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sessionLock)
        {
            if (_chatSessions.TryGetValue(chatId, out var s))
                s.ApplyChatRow(fresh);
        }
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

        List<ChatP2pSession> sessions;
        lock (_sessionLock)
        {
            sessions = new List<ChatP2pSession>(_chatSessions.Values);
            _chatSessions.Clear();
            _sessionsStarted.Clear();
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
