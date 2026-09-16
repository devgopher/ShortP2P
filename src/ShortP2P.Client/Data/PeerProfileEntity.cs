using ShortP2P.Auth.Data;
using SQLite;

namespace ShortP2P.Client.Data;

[Table("peer_profiles")]
public sealed class PeerProfileEntity
{
    [PrimaryKey]
    public string NetworkIdShort { get; set; } = "";

    public string Nickname { get; set; } = "";

    public string AboutMe { get; set; } = "";

    public byte[]? Avatar { get; set; }

    public long UpdatedUtcTicks { get; set; }
}
