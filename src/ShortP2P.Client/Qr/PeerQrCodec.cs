using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShortP2P.Crypto;
using ShortP2P.Transport;

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

        if (string.IsNullOrWhiteSpace(p.N) || string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.K))
        {
            error = "QR is missing nickname, id, or public key.";
            return false;
        }

        TryMergeQrHosts(p, out var mergedHosts);
        TryMergeQrMacs(p, out var mergedMacs);

        if (mergedHosts.Count == 0 && mergedMacs.Count == 0)
        {
            error = "QR is missing a valid host IP (h / ha) or Bluetooth MAC (b / ba).";
            return false;
        }

        p.H = mergedHosts.Count > 0 ? mergedHosts[0] : "";
        p.Ha = mergedHosts.Count > 1 ? mergedHosts.Skip(1).ToList() : null;
        p.B = mergedMacs.Count > 0 ? mergedMacs[0] : null;
        p.Ba = mergedMacs.Count > 1 ? mergedMacs.Skip(1).ToList() : null;

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

    private static void TryMergeQrHosts(PeerQrPayload p, out List<string> merged)
    {
        merged = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        TryAddQrHost(merged, seen, p.H);
        if (p.Ha != null)
        {
            foreach (var x in p.Ha)
                TryAddQrHost(merged, seen, x);
        }
    }

    private static void TryMergeQrMacs(PeerQrPayload p, out List<string> merged)
    {
        merged = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        TryAddQrMac(merged, seen, p.B);
        if (p.Ba != null)
        {
            foreach (var x in p.Ba)
                TryAddQrMac(merged, seen, x);
        }
    }

    private static void TryAddQrHost(List<string> merged, HashSet<string> seen, string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return;
        var t = s.Trim();
        if (!IPAddress.TryParse(t, out _))
            return;
        if (!seen.Add(t))
            return;
        merged.Add(t);
    }

    private static void TryAddQrMac(List<string> merged, HashSet<string> seen, string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return;
        if (!BluetoothTransportAddress.TryParseMac(s, out var mac))
            return;
        var canon = BluetoothTransportAddress.ToMacString(mac);
        if (!seen.Add(canon))
            return;
        merged.Add(canon);
    }
}
