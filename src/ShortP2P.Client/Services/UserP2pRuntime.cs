using System.Collections.Generic;
using ShortP2P.Client.Data;
using ShortP2P.Client.LocalNetwork;
using ShortP2P.Client.Routing;

namespace ShortP2P.Client.Services;

/// <summary>Настройки маршрутизации и LAN discovery для сессии пользователя.</summary>
public sealed class UserP2pRuntime : IAsyncDisposable
{
    private readonly P2pRoutingSettingsStore _store;

    private readonly object _sessionLock = new();
    private readonly Dictionary<int, ChatP2pSession> _chatSessions = new();
    private readonly HashSet<int> _sessionsStarted = [];

    public P2pRoutingSettings Settings { get; } = new();

    /// <summary>Сканирование LAN по discovery-пингам (UDP 565, broadcast).</summary>
    public LocalNetworkScanner LocalScan { get; }

    public UserP2pRuntime(P2pRoutingSettingsStore store)
    {
        _store = store;
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

            var s = new ChatP2pSession(chat, user, auth, repo, uiSync, Settings);
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
                    session = new ChatP2pSession(c, user, auth, repo, uiSync, Settings);
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
        Settings.LinkTechnology = persisted.LinkTechnology;

        await LocalScan.StartAsync(user, cancellationToken).ConfigureAwait(false);
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
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
