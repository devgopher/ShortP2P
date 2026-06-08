using SQLite;

namespace ShortP2P.Auth.Data;

[Table("users")]
public class UserEntity
{
    [PrimaryKey] [AutoIncrement] public int Id { get; set; }

    public string Nickname { get; set; } = "";

    public string NetworkIdShort { get; set; } = "";

    public string PasswordSaltBase64 { get; set; } = "";

    public string PasswordHashBase64 { get; set; } = "";

    public string RsaPrivateJson { get; set; } = "";

    public string RsaPublicJson { get; set; } = "";

    public int DataUdpPort { get; set; } = 17500;

    public long CreatedUtcTicks { get; set; }
}