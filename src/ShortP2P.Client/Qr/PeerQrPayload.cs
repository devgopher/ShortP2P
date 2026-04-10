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

    /// <summary>Дополнительные IPv4/IPv6 (первый по-прежнему в <see cref="H"/>). Старые клиенты поле игнорируют.</summary>
    [JsonPropertyName("ha")]
    public List<string>? Ha { get; set; }

    [JsonPropertyName("p")]
    public int P { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>RSA public key JSON (same format as <see cref="Services.RsaKeySerializer"/>).</summary>
    [JsonPropertyName("k")]
    public string K { get; set; } = "";

    /// <summary>Все хосты для поля чата (<see cref="Data.ChatEntity.PeerHost"/>): через запятую.</summary>
    public string GetCommaSeparatedHosts()
    {
        var list = new List<string> { H.Trim() };
        if (Ha != null)
        {
            foreach (var x in Ha)
            {
                var t = x.Trim();
                if (t.Length > 0 && !list.Contains(t, StringComparer.OrdinalIgnoreCase))
                    list.Add(t);
            }
        }

        return string.Join(", ", list);
    }
}
