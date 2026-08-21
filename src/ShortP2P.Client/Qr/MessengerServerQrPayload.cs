using System.Text.Json.Serialization;

namespace ShortP2P.Client.Qr;

/// <summary>Compact JSON payload in a messenger-server share QR (IP/host + port).</summary>
public sealed class MessengerServerQrPayload
{
    public const string TypeMessengerServer = "ms";

    [JsonPropertyName("v")] public int V { get; set; } = 1;

    /// <summary>Type discriminator; must be <see cref="TypeMessengerServer"/>.</summary>
    [JsonPropertyName("t")]
    public string T { get; set; } = "";

    [JsonPropertyName("h")] public string H { get; set; } = "";

    [JsonPropertyName("p")] public int P { get; set; }

    /// <summary>http or https. Omitted means https.</summary>
    [JsonPropertyName("s")]
    public string? S { get; set; }
}
