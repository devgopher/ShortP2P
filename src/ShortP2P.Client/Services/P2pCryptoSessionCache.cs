using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using ShortP2P.Crypto;

namespace ShortP2P.Client.Services;

/// <summary>Потокобезопасный кэш крипто-сессий P2P (одна запись на chatId).</summary>
public sealed class P2pCryptoSessionCache
{
    private readonly ConcurrentDictionary<int, P2PSession> _entries = new();

    public P2PSession GetSession(int chatId, Func<P2PSession> createSession)
    {
        return _entries.GetOrAdd(chatId, _ => createSession());
    }

    public bool TryGetSession(int chatId, [NotNullWhen(true)] out P2PSession? session)
    {
        return _entries.TryGetValue(chatId, out session);
    }

    public bool TryRemove(int chatId, out P2PSession? session)
    {
        return _entries.TryRemove(chatId, out session);
    }
}