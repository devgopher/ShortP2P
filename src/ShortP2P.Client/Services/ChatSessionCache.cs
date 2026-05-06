using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace ShortP2P.Client.Services;

/// <summary>Потокобезопасный кэш чат-сессий (одна запись на chatId).</summary>
public sealed class ChatSessionCache
{
    private sealed class SessionCacheEntry(ChatP2pSession session, bool isStarted = false)
    {
        public ChatP2pSession Session { get; } = session;
        public int Started = isStarted ? 1 : 0;
    }

    private readonly ConcurrentDictionary<int, SessionCacheEntry> _entries = new();

    public ChatP2pSession GetSession(int chatId, Func<ChatP2pSession> createSession, Action<ChatP2pSession> applyChat)
    {
        var entry = _entries.GetOrAdd(chatId, _ => new SessionCacheEntry(createSession()));
        applyChat(entry.Session);
        return entry.Session;
    }

    public bool TryGetSession(int chatId, [NotNullWhen(true)] out ChatP2pSession? session)
    {
        if (_entries.TryGetValue(chatId, out var entry))
        {
            session = entry.Session;
            return true;
        }

        session = null;
        return false;
    }

    public bool IsStarted(int chatId)
    {
        return _entries.TryGetValue(chatId, out var entry) && Volatile.Read(ref entry.Started) == 1;
    }

    public void MarkStarted(int chatId)
    {
        if (_entries.TryGetValue(chatId, out var entry))
            Interlocked.Exchange(ref entry.Started, 1);
    }

    public bool TryRemove(int chatId, out ChatP2pSession? session)
    {
        if (_entries.TryRemove(chatId, out var entry))
        {
            session = entry.Session;
            return true;
        }

        session = null;
        return false;
    }

    public IReadOnlyList<ChatP2pSession> DrainAll()
    {
        var list = _entries.Values.Select(x => x.Session).ToList();
        _entries.Clear();
        return list;
    }
}
