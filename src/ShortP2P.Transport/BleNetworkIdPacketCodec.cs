using ShortP2P.Auth.Data;

namespace ShortP2P.Transport;

/// <summary>
///     Кадр объявления NetworkId по BLE data-порту: префикс <see cref="Prefix" /> + 12 байт wire NetworkId.
///     Совместимость: однобайтовый кадр <see cref="BleShortP2PGattProtocol.FrameNetworkIdAnnounce" /> (0x32).
/// </summary>
public static class BleNetworkIdPacketCodec
{
    public static ReadOnlySpan<byte> Prefix => [0x33, 0x55];

    public const int PrefixLength = 2;

    public const int PacketLength = PrefixLength + CompressedNetworkId.WireLength;

    public static bool IsPrefixedPacket(ReadOnlySpan<byte> data)
    {
        return data.Length >= PrefixLength && data[0] == Prefix[0] && data[1] == Prefix[1];
    }

    public static byte[] BuildPacket(CompressedNetworkId networkId)
    {
        if (networkId.IsEmpty)
            throw new ArgumentException("NetworkId must not be empty.", nameof(networkId));
        var buf = new byte[PacketLength];
        Prefix.CopyTo(buf);
        if (!networkId.TryWriteBytes(buf.AsSpan(PrefixLength)))
            throw new InvalidOperationException("Failed to serialize NetworkId.");
        return buf;
    }

    public static bool TryParsePacket(ReadOnlySpan<byte> data, out CompressedNetworkId networkId)
    {
        networkId = CompressedNetworkId.Empty;
        if (IsPrefixedPacket(data) && data.Length >= PacketLength)
        {
            networkId = CompressedNetworkId.FromWireBytes(data.Slice(PrefixLength, CompressedNetworkId.WireLength));
            return !networkId.IsEmpty;
        }

        return BleShortP2PGattProtocol.TryParseNetworkIdAnnouncePacket(data, out networkId);
    }
}
