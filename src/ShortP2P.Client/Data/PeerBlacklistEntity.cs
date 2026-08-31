using SQLite;

namespace ShortP2P.Client.Data;

[Table("peer_blacklist")]
public sealed class PeerBlacklistEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int UserId { get; set; }

    public string NetworkId { get; set; } = "";

    public string Nickname { get; set; } = "";

    public long AddedUtcTicks { get; set; }
}
