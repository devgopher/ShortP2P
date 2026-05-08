using SQLite;

namespace ShortP2P.Client.Data;

[Table("messages")]
public class ChatMessageEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ChatId { get; set; }

    public bool Outgoing { get; set; }

    public string Text { get; set; } = "";

    public long SentUtcTicks { get; set; }

    /// <summary>Значение <see cref="MessageDeliveryStatus"/>; для входящих — NotApplicable.</summary>
    public int DeliveryStatus { get; set; }

    /// <summary><see cref="ChatPayloadKind"/>.</summary>
    public int PayloadKind { get; set; }

    public string MimeType { get; set; } = "";

    public byte[]? ImageBlob { get; set; }

    public string TransferId { get; set; } = "";

    public string TransferToken { get; set; } = "";

    public string TransferPayloadKind { get; set; } = "";

    public string TransferFileName { get; set; } = "";

    public long TransferSizeBytes { get; set; }

    public string TransferHost { get; set; } = "";

    public int TransferPort { get; set; }

    public long TransferExpiresUtcTicks { get; set; }

    /// <summary>Значение <see cref="ChatTransferState"/>.</summary>
    public int TransferState { get; set; }
}
