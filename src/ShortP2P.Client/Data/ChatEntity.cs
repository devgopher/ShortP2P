using SQLite;

namespace ShortP2P.Client.Data;

[Table("chats")]
public class ChatEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int UserId { get; set; }

    public string PeerNickname { get; set; } = "";

    public string PeerNetworkIdShort { get; set; } = "";

    public string PeerRsaPublicJson { get; set; } = "";

    public string PeerHost { get; set; } = "127.0.0.1";

    public int PeerPort { get; set; } = 17201;

    /// <summary>Сериализованный маршрут ретрансляции (первый хоп + цепочка), null/пусто — прямой UDP.</summary>
    public string? RelayRouteBlob { get; set; }

    public long UpdatedUtcTicks { get; set; }
}
