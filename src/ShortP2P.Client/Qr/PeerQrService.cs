using System.Diagnostics.CodeAnalysis;
using System.Linq;
using QRCoder;
using ShortP2P.Transport;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ShortP2P.Auth.Data;
using ZXing;
using ZXing.Common;

namespace ShortP2P.Client.Qr;

public static class PeerQrService
{
    /// <summary>Собирает v1 пейлоад: IPv4 в <see cref="PeerQrPayload.H"/> / <see cref="PeerQrPayload.Ha"/>, опционально MAC в <see cref="PeerQrPayload.B"/> / <see cref="PeerQrPayload.Ba"/>.</summary>
    public static PeerQrPayload BuildPayload(UserEntity user, string rsaPublicKeyJson, string? hostOverride = null,
        string? bluetoothMacPrimary = null, IReadOnlyList<string>? bluetoothMacAdditional = null)
    {
        List<string> hosts;
        var single = hostOverride?.Trim();
        hosts = !string.IsNullOrEmpty(single) ? [single] : LocalIPv4Resolver.GetInviteHostCandidatesOrdered(TimeSpan.FromSeconds(2));

        string? b = null;
        List<string>? ba = null;
        var macSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(bluetoothMacPrimary) &&
            BluetoothTransportAddress.TryParseMac(bluetoothMacPrimary, out var mac0))
        {
            b = BluetoothTransportAddress.ToMacString(mac0);
            macSeen.Add(b);
        }

        if (bluetoothMacAdditional is { Count: > 0 })
        {
            foreach (var raw in bluetoothMacAdditional)
            {
                if (string.IsNullOrWhiteSpace(raw) || !BluetoothTransportAddress.TryParseMac(raw, out var m))
                    continue;
                var canon = BluetoothTransportAddress.ToMacString(m);
                if (!macSeen.Add(canon))
                    continue;
                if (b == null)
                    b = canon;
                else
                {
                    ba ??= [];
                    ba.Add(canon);
                }
            }
        }

        return new PeerQrPayload
        {
            V = 1,
            N = user.Nickname.Trim(),
            H = hosts[0],
            Ha = hosts.Count > 1 ? hosts.Skip(1).ToList() : null,
            B = b,
            Ba = ba is { Count: > 0 } ? ba : null,
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
