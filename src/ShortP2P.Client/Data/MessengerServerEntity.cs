using SQLite;

namespace ShortP2P.Client.Data;

/// <summary>Messenger HTTPS server known to this client (max 32).</summary>
[Table("messenger_servers")]
public sealed class MessengerServerEntity
{
    [PrimaryKey] [AutoIncrement] public int Id { get; set; }

    [Indexed] public int UserId { get; set; }

    /// <summary>Absolute base URL, e.g. https://host:7196</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Pinned SHA-256 fingerprint from GET /server/certificate (hex or base64 as returned).</summary>
    public string FingerprintSha256 { get; set; } = "";

    public bool Trusted { get; set; } = true;

    public bool Active { get; set; } = true;

    /// <summary>True after successful Register (or restored known account).</summary>
    public bool IsRegistered { get; set; }

    /// <summary>Password used for this server account (auto-generated on first register).</summary>
    public string AccountPassword { get; set; } = "";

    /// <summary>Network id.</summary>
    public string NetworkId { get; set; } = "";

    public string Nick { get; set; } = "";

    public long CreatedUtcTicks { get; set; }

    public long UpdatedUtcTicks { get; set; }
}
