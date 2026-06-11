using ShortP2P.Auth.Data;

namespace ShortP2P.Transport;

/// <summary>
///     Vendor Information Element / DNS-SD TXT payload для обмена NetworkId по Wi-Fi Direct.
/// </summary>
public static class WifiDirectShortP2PProtocol
{
    public const string ServicePort = "17551";
    public const int MaxFrameLength = 64 * 1024;
    public const byte OuiType = 0x50;
    public static readonly byte[] VendorOui = [0x9F, 0xE8, 0xE5];

    private const byte Version = 1;
    private const byte Magic0 = (byte)'S';
    private const byte Magic1 = (byte)'P';
    private const int HeaderLength = 3;
    private const int MinPayloadLength = HeaderLength + CompressedNetworkId.WireLength;

    public static bool MatchesInformationElement(ReadOnlySpan<byte> oui, byte ouiType)
    {
        return ouiType == OuiType && oui.Length == VendorOui.Length && oui.SequenceEqual(VendorOui);
    }

    public static byte[] BuildNetworkIdPayload(CompressedNetworkId networkId)
    {
        if (networkId.IsEmpty)
            throw new ArgumentException("NetworkId must not be empty.", nameof(networkId));

        var buf = new byte[MinPayloadLength];
        buf[0] = Magic0;
        buf[1] = Magic1;
        buf[2] = Version;
        if (!networkId.TryWriteBytes(buf.AsSpan(HeaderLength)))
            throw new InvalidOperationException("Failed to serialize NetworkId.");
        return buf;
    }

    public static bool TryParseNetworkIdPayload(ReadOnlySpan<byte> payload, out CompressedNetworkId networkId)
    {
        networkId = CompressedNetworkId.Empty;
        if (payload.Length < MinPayloadLength)
            return false;
        if (payload[0] != Magic0 || payload[1] != Magic1 || payload[2] != Version)
            return false;

        networkId = CompressedNetworkId.FromWireBytes(payload.Slice(HeaderLength, CompressedNetworkId.WireLength));
        return !networkId.IsEmpty;
    }
}
