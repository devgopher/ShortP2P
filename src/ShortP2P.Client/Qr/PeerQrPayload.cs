using System.Text.Json.Serialization;

namespace ShortP2P.Client.Qr;

/// <summary>Compact JSON payload embedded in a peer contact QR code (version 1).</summary>
public sealed class PeerQrPayload
{
    [JsonPropertyName("v")]
    public int V { get; set; }

    [JsonPropertyName("n")]
    public string N { get; set; } = "";

    [JsonPropertyName("h")]
    public string H { get; set; } = "";

    [JsonPropertyName("p")]
    public int P { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>RSA public key JSON (same format as <see cref="Services.RsaKeySerializer"/>).</summary>
    [JsonPropertyName("k")]
    public string K { get; set; } = "";
}
