using System.Collections.Concurrent;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.Persistence.InMemory;

/// <summary>Shared in-memory store used when PostgreSQL persistence is disabled.</summary>
public sealed class InMemoryMessengerStore
{
    private readonly object _chatRequestsGate = new();
    private readonly List<(long Id, ChatRequest Request)> _chatRequests = [];
    private long _chatRequestSeq;

    public ConcurrentDictionary<string, ClientAccount> AccountsByNetworkId { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, string> NetworkIdByNick { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, ClientStatuses> Statuses { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, Chat> Chats { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<(string Src, string Tgt), CryptoKeys> CryptoKeys { get; } = new();
    public ConcurrentDictionary<string, Message> Messages { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, (DeliveryTicket Ticket, string SrcNetworkId)> DeliveryTickets { get; } =
        new(StringComparer.Ordinal);

    public long NextChatRequestId() => Interlocked.Increment(ref _chatRequestSeq);

    public void AddChatRequest(ChatRequest request)
    {
        var id = NextChatRequestId();
        lock (_chatRequestsGate)
            _chatRequests.Add((id, request));
    }

    public IReadOnlyList<ChatRequest> ListChatRequestsByTarget(string targetNetworkId)
    {
        lock (_chatRequestsGate)
        {
            return _chatRequests
                .Where(x => string.Equals(x.Request.TargetNetworkId, targetNetworkId, StringComparison.Ordinal))
                .Select(x => x.Request)
                .ToArray();
        }
    }
}
