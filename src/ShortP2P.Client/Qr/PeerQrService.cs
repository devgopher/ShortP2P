using System.Diagnostics.CodeAnalysis;
using QRCoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ShortP2P.Client.Data;
using ZXing;
using ZXing.Common;

namespace ShortP2P.Client.Qr;

public static class PeerQrService
{
    /// <summary>Builds the v1 payload for the logged-in user. Host is auto-detected unless <paramref name="hostOverride"/> is set.</summary>
    public static PeerQrPayload BuildPayload(UserEntity user, string rsaPublicKeyJson, string? hostOverride = null)
    {
        var host = hostOverride?.Trim();
        if (string.IsNullOrEmpty(host))
            host = LocalIPv4Resolver.TryGetPreferredUnicastIpv4() ?? "127.0.0.1";

        return new PeerQrPayload
        {
            V = 1,
            N = user.Nickname.Trim(),
            H = host,
            P = user.DataUdpPort,
            Id = user.NetworkIdShort.Trim(),
            K = rsaPublicKeyJson.Trim(),
        };
    }

    public static byte[] EncodeQrPng(PeerQrPayload payload, int pixelsPerModule = 8)
    {
        var json = PeerQrCodec.Serialize(payload);
        return EncodeQrPngFromJson(json, pixelsPerModule);
    }

    public static byte[] EncodeQrPngFromJson(string json, int pixelsPerModule = 8)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(json, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }

    /// <summary>Reads the first QR code from an image (PNG/JPEG/WebP, etc.).</summary>
    public static bool TryDecodeImage(ReadOnlySpan<byte> imageBytes, [NotNullWhen(true)] out PeerQrPayload? payload,
        out string? error)
    {
        payload = null;
        error = null;
        string? text;
        try
        {
            using var image = Image.Load<Rgba32>(imageBytes);
            var reader = new ZXing.ImageSharp.BarcodeReader<Rgba32>
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
                    TryHarder = true,
                },
            };
            var r = reader.Decode(image);
            text = r?.Text;
        }
        catch (Exception ex)
        {
            error = $"Could not read image: {ex.Message}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "No QR code found in the image.";
            return false;
        }

        return PeerQrCodec.TryDeserialize(text.Trim(), out payload, out error);
    }
}
