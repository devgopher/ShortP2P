using SQLite;

namespace ShortP2P.Client.Data;

/// <summary>
/// Cross-server dedup: the same MessageId can arrive from multiple messenger servers.
/// We ingest once, but still submit delivery receipts to every server.
/// </summary>
[Table("seen_server_messages")]
public class SeenServerMessageEntity
{
    [PrimaryKey]
    public string MessageId { get; set; } = "";

    public long SeenUtcTicks { get; set; }
}