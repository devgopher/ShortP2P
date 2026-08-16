using System.Collections.Concurrent;
using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.Api.Persistence.InMemory;

/// <summary>Shared in-memory store used when PostgreSQL messenger persistence is disabled.</summary>
public sealed class InMemoryMessengerStore
{
    public ConcurrentDictionary<(string NetworkId, string DeviceId), ClientStatuses> Statuses { get; } = new();

    public ConcurrentDictionary<string, Chat> Chats { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<(string Src, string Tgt), CryptoKeys> CryptoKeys { get; } = new();

    public ConcurrentDictionary<string, Message> Messages { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<(string MessageId, string DeviceId), MessageInboxEntry> MessageInboxes { get; } = new();

    public ConcurrentDictionary<string, ChatRequest> ChatRequests { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<(string RequestId, string DeviceId), ChatRequestInboxEntry> ChatRequestInboxes { get; } =
        new();

    public ConcurrentDictionary<string, (DeliveryTicket Ticket, string SrcNetworkId)> DeliveryTickets { get; } =
        new(StringComparer.Ordinal);
}
