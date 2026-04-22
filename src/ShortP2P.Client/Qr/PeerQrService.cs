using System.Diagnostics.CodeAnalysis;
using System.Linq;
using QRCoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ShortP2P.Client.Data;
using ZXing;
using ZXing.Common;

namespace ShortP2P.Client.Qr;

public static class PeerQrService
{
    /// <summary>Собирает v1 пейлоад: IPv4 в <see cref="PeerQrPayload.H"/> (первый — публичный при наличии) и <see cref="PeerQrPayload.Ha"/> (остальные), либо один хост из <paramref name="hostOverride"/>.</summary>
    public static PeerQrPayload BuildPayload(UserEntity user, string rsaPublicKeyJson, string? hostOverride = null)
    {
        List<string> hosts;
        var single = hostOverride?.Trim();
        if (!string.IsNullOrEmpty(single))
            hosts = [single];
        else
            hosts = LocalIPv4Resolver.GetInviteHostCandidatesOrdered(TimeSpan.FromSeconds(2));

        return new PeerQrPayload
        {
            V = 1,
            N = user.Nickname.Trim(),
            H = hosts[0],
            Ha = hosts.Count > 1 ? hosts.Skip(1).ToList() : null,
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
