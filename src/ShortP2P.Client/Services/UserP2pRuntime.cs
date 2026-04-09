using System.Collections.Generic;
using ShortP2P.Client.Data;
using ShortP2P.Client.LocalNetwork;
using ShortP2P.Client.Routing;
using ShortP2P.Discovery;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Services;

/// <summary>Discovery + настройки маршрутизации + <see cref="SharedUserUdpGateway"/> для сессии пользователя.</summary>
public sealed class UserP2pRuntime : IAsyncDisposable
{
    private readonly ChatRepository _chats;
    private readonly P2pRoutingSettingsStore _store;
    private UdpPeerDiscoveryService? _discovery;

    private readonly object _sessionLock = new();
    private readonly Dictionary<int, ChatP2pSession> _chatSessions = new();
    private readonly HashSet<int> _sessionsStarted = new();

    public P2pRoutingSettings Settings { get; } = new();

    public SharedUserUdpGateway Gateway { get; }

    /// <summary>Сканирование LAN по discovery-пингам (UDP 565, приём того же кадра по Bluetooth при наличии транспорта).</summary>
    public LocalNetworkScanner LocalScan { get; }

    public event EventHandler<PeerPresenceChangedEventArgs>? PeerPresenceChanged
    {
        add => Gateway.PeerPresenceChanged += value;
        remove => Gateway.PeerPresenceChanged -= value;
    }

    public UserP2pRuntime(AuthService auth, ChatRepository chats, P2pRoutingSettingsStore store,
        ITransport? bluetoothTransport = null)
    {
        _chats = chats;
        _store = store;
        Gateway = new SharedUserUdpGateway(auth, chats, Settings, bluetoothTransport);
        LocalScan = new LocalNetworkScanner(Gateway);
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

            var s = new ChatP2pSession(chat, user, auth, repo, uiSync, Gateway, Settings);
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

    /// <summary>Запускает фоновые P2P-сессии для всех чатов из БД (ещё не стартовавшие).</summary>
    public async Task EnsureAllChatSessionsStartedAsync(UserEntity user, AuthService auth, ChatRepository repo,
        SynchronizationContext? uiSync, CancellationToken cancellationToken = default)
    {
        await Gateway.EnsureStartedAsync(user, cancellationToken).ConfigureAwait(false);
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
                    session = new ChatP2pSession(c, user, auth, repo, uiSync, Gateway, Settings);
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

    public async Task EnsureStartedAsync(UserEntity user, CancellationToken cancellationToken = default)
    {
        var persisted = await _store.LoadAsync().ConfigureAwait(false);
        Settings.MaxSearchHops = persisted.MaxSearchHops;
        Settings.SendFailureSearchAttempts = persisted.SendFailureSearchAttempts;
        Settings.SendFailureRetryDelay = persisted.SendFailureRetryDelay;
        Settings.SearchWaitTimeout = persisted.SearchWaitTimeout;

        await Gateway.EnsureStartedAsync(user, cancellationToken).ConfigureAwait(false);
        await LocalScan.StartAsync(user, cancellationToken).ConfigureAwait(false);

        if (_discovery != null)
        {
            Gateway.SetDiscovery(_discovery);
            return;
        }

        var nid = CompressedNetworkId.FromShortString(user.NetworkIdShort);
        var peer = new PeerIdentity(user.Nickname, nid, user.DataUdpPort);
        _discovery = new UdpPeerDiscoveryService(peer);
        await _discovery.StartAsync(cancellationToken).ConfigureAwait(false);
        Gateway.SetDiscovery(_discovery);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
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
        Gateway.SetDiscovery(null);
        Gateway.ClearChatSinks();
        if (_discovery != null)
        {
            try
            {
                await _discovery.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }

            _discovery = null;
        }

        await Gateway.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    public bool IsPeerOnline(string peerNetworkIdShort)
    {
        var id = CompressedNetworkId.FromShortString(peerNetworkIdShort).Value;
        return Gateway.IsPeerOnline(id);
    }
}
