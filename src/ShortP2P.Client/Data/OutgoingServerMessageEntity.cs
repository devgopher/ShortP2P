using SQLite;

namespace ShortP2P.Client.Data;

/// <summary>
/// Maps a messenger-server MessageId to the local outgoing chat message,
/// so delivery receipts can flip <see cref="MessageDeliveryStatus"/> to Delivered.
/// </summary>
[Table("outgoing_server_messages")]
public class OutgoingServerMessageEntity
{
    [PrimaryKey]
    public string ServerMessageId { get; set; } = "";

    [Indexed]
    public int LocalMessageId { get; set; }

    [Indexed]
    public int ChatId { get; set; }

    public long CreatedUtcTicks { get; set; }
}
