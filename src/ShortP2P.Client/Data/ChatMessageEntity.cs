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
}
