using System.Diagnostics.CodeAnalysis;

namespace ShortP2P.Client.Qr;

public static class MessengerServerQrService
{
    public static bool TryBuildPayload(string baseUrl, [NotNullWhen(true)] out MessengerServerQrPayload? payload,
        out string? error) =>
        MessengerServerQrCodec.TryBuildFromBaseUrl(baseUrl, out payload, out error);

    public static byte[] EncodeQrPng(MessengerServerQrPayload payload, int pixelsPerModule = 8)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return QrImageCodec.EncodePng(MessengerServerQrCodec.Serialize(payload), pixelsPerModule);
    }

    public static bool TryDecodeImage(ReadOnlySpan<byte> imageBytes,
        [NotNullWhen(true)] out MessengerServerQrPayload? payload, out string? error)
    {
        payload = null;
        if (!QrImageCodec.TryReadText(imageBytes, out var text, out error))
            return false;
        return MessengerServerQrCodec.TryDeserialize(text, out payload, out error);
    }
}
