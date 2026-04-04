using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShortP2P.Client.Services;

namespace ShortP2P.Client.Qr;

public static class PeerQrCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(PeerQrPayload payload) => JsonSerializer.Serialize(payload, JsonOptions);

    public static bool TryDeserialize(string json, [NotNullWhen(true)] out PeerQrPayload? payload, out string? error)
    {
        payload = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "QR payload is empty.";
            return false;
        }

        PeerQrPayload? p;
        try
        {
            p = JsonSerializer.Deserialize<PeerQrPayload>(json.Trim(), JsonOptions);
        }
        catch (JsonException ex)
        {
            error = $"Invalid QR data: {ex.Message}";
            return false;
        }

        if (p == null)
        {
            error = "Invalid QR data.";
            return false;
        }

        if (p.V != 1)
        {
            error = "Unsupported QR version.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(p.N) || string.IsNullOrWhiteSpace(p.H) || string.IsNullOrWhiteSpace(p.Id) ||
            string.IsNullOrWhiteSpace(p.K))
        {
            error = "QR is missing nickname, host, id, or public key.";
            return false;
        }

        if (p.P is < 1 or > 65535)
        {
            error = "QR has an invalid UDP port.";
            return false;
        }

        try
        {
            _ = RsaKeySerializer.DeserializePublic(p.K.Trim());
        }
        catch
        {
            error = "QR contains an invalid RSA public key.";
            return false;
        }

        payload = p;
        return true;
    }
}
