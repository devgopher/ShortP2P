using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.Persistence.Psql.Entities;

public sealed class ChatRecord
{
    public string ChatId { get; set; } = "";
    public List<string> NetworkIds { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }

    public Chat ToDomain() => new()
    {
        ChatId = ChatId,
        NetworkIds = NetworkIds.ToArray(),
        CreatedAtUtc = CreatedAtUtc
    };

    public static ChatRecord FromDomain(Chat chat) => new()
    {
        ChatId = chat.ChatId,
        NetworkIds = chat.NetworkIds.ToList(),
        CreatedAtUtc = chat.CreatedAtUtc
    };
}

public sealed class ChatRequestRecord
{
    public long Id { get; set; }
    public string RequesterNetworkId { get; set; } = "";
    public string TargetNetworkId { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }

    public ChatRequest ToDomain() => new()
    {
        RequesterNetworkId = RequesterNetworkId,
        TargetNetworkId = TargetNetworkId,
        PublicKey = PublicKey,
        CreatedAtUtc = CreatedAtUtc
    };

    public static ChatRequestRecord FromDomain(ChatRequest request) => new()
    {
        RequesterNetworkId = request.RequesterNetworkId,
        TargetNetworkId = request.TargetNetworkId,
        PublicKey = request.PublicKey,
        CreatedAtUtc = request.CreatedAtUtc
    };
}

public sealed class CryptoKeysRecord
{
    public string SrcNetworkId { get; set; } = "";
    public string TgtNetworkId { get; set; } = "";
    public string PublicKey { get; set; } = "";

    public CryptoKeys ToDomain() => new()
    {
        SrcNetworkId = SrcNetworkId,
        TgtNetworkId = TgtNetworkId,
        PublicKey = PublicKey
    };

    public static CryptoKeysRecord FromDomain(CryptoKeys keys) => new()
    {
        SrcNetworkId = keys.SrcNetworkId,
        TgtNetworkId = keys.TgtNetworkId,
        PublicKey = keys.PublicKey
    };
}

public sealed class ClientStatusRecord
{
    public string NetworkId { get; set; } = "";
    public ClientOnlineStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ClientStatuses ToDomain() => new()
    {
        NetworkId = NetworkId,
        Status = Status,
        CreatedAtUtc = CreatedAtUtc
    };

    public static ClientStatusRecord FromDomain(ClientStatuses status) => new()
    {
        NetworkId = status.NetworkId,
        Status = status.Status,
        CreatedAtUtc = status.CreatedAtUtc
    };
}

public sealed class MessageRecord
{
    public string MessageId { get; set; } = "";
    public string SrcNetworkId { get; set; } = "";
    public string TgtNetworkId { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string EncryptedDataBase64 { get; set; } = "";

    public Message ToDomain() => new()
    {
        MessageId = MessageId,
        SrcNetworkId = SrcNetworkId,
        TgtNetworkId = TgtNetworkId,
        CreatedUtc = CreatedUtc,
        UpdatedUtc = UpdatedUtc,
        EncryptedDataBase64 = EncryptedDataBase64
    };

    public static MessageRecord FromDomain(Message message) => new()
    {
        MessageId = message.MessageId,
        SrcNetworkId = message.SrcNetworkId,
        TgtNetworkId = message.TgtNetworkId,
        CreatedUtc = message.CreatedUtc,
        UpdatedUtc = message.UpdatedUtc,
        EncryptedDataBase64 = message.EncryptedDataBase64
    };
}

public sealed class DeliveryTicketRecord
{
    public string MessageId { get; set; } = "";
    public DateTime ReceivedAtUtc { get; set; }

    public DeliveryTicket ToDomain() => new()
    {
        MessageId = MessageId,
        ReceivedAtUtc = ReceivedAtUtc
    };

    public static DeliveryTicketRecord FromDomain(DeliveryTicket ticket) => new()
    {
        MessageId = ticket.MessageId,
        ReceivedAtUtc = ticket.ReceivedAtUtc
    };
}
