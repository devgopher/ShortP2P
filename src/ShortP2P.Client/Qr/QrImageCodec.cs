using System.Diagnostics.CodeAnalysis;
using QRCoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;
using ZXing.Common;

namespace ShortP2P.Client.Qr;

/// <summary>PNG encode / image decode for QR payloads (peer, messenger server, …).</summary>
public static class QrImageCodec
{
    public static byte[] EncodePng(string payload, int pixelsPerModule = 8)
    {
        Require.NotNullOrWhiteSpace(payload);
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }

    /// <summary>Reads the first QR code from an image (PNG/JPEG/WebP, etc.).</summary>
    public static bool TryReadText(ReadOnlySpan<byte> imageBytes, [NotNullWhen(true)] out string? text,
        out string? error)
    {
        text = null;
        error = null;
        try
        {
            using var image = Image.Load<Rgba32>(imageBytes);
            var reader = new ZXing.ImageSharp.BarcodeReader<Rgba32>
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
                    TryHarder = true
                }
            };
            var r = reader.Decode(image);
            var raw = r?.Text;
            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "No QR code found in the image.";
                return false;
            }

            text = raw.Trim();
            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not read image: {ex.Message}";
            return false;
        }
    }
}
