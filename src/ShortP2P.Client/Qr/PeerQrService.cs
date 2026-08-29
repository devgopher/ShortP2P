using System.Diagnostics.CodeAnalysis;
using ShortP2P.Auth.Data;

namespace ShortP2P.Client.Qr;

public static class PeerQrService
{
    /// <summary>
    ///     Собирает v1 пейлоад: IPv4 в <see cref="PeerQrPayload.H" /> / <see cref="PeerQrPayload.Ha" />, network id в
    ///     <see cref="PeerQrPayload.Id" />.
    /// </summary>
    public static PeerQrPayload BuildPayload(UserEntity user, string rsaPublicKeyJson, string? hostOverride = null)
    {
        List<string> hosts;
        var single = hostOverride?.Trim();
        hosts = !string.IsNullOrEmpty(single)
            ? [single]
#if NETFRAMEWORK
            : [string.IsNullOrWhiteSpace(user.NetworkIdShort) ? "127.0.0.1" : user.NetworkIdShort.Trim()];
#else
            : InviteHostsBuilder.GetCandidatesOrdered(networkIdShort: user.NetworkIdShort).ToList();
#endif

        return new PeerQrPayload
        {
            V = 1,
            N = user.Nickname.Trim(),
            H = hosts[0],
            Ha = hosts.Count > 1 ? hosts.Skip(1).ToList() : null,
            P = user.DataUdpPort,
            Id = user.NetworkIdShort.Trim(),
            K = rsaPublicKeyJson.Trim()
        };
    }

    public static byte[] EncodeQrPng(PeerQrPayload payload, int pixelsPerModule = 8)
    {
        var json = PeerQrCodec.Serialize(payload);
        return EncodeQrPngFromJson(json, pixelsPerModule);
    }

    public static byte[] EncodeQrPngFromJson(string json, int pixelsPerModule = 8) =>
        QrImageCodec.EncodePng(json, pixelsPerModule);

    /// <summary>Reads the first QR code from an image (PNG/JPEG/WebP, etc.).</summary>
    public static bool TryDecodeImage(ReadOnlySpan<byte> imageBytes, [NotNullWhen(true)] out PeerQrPayload? payload,
        out string? error)
    {
        payload = null;
        if (!QrImageCodec.TryReadText(imageBytes, out var text, out error))
            return false;
        return PeerQrCodec.TryDeserialize(text, out payload, out error);
    }
}
