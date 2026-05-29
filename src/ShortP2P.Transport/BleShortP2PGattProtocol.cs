using System.Buffers.Binary;
using ShortP2P.Auth.Data;

namespace ShortP2P.Transport;

/// <summary>
///     Общий BLE GATT-протокол ShortP2P (custom service, без OTS). UUID совпадают на Windows и Android.
/// </summary>
public static class BleShortP2PGattProtocol
{
    public static readonly Guid ServiceUuid = Guid.Parse("9FE8E58B-AF85-4D91-B245-2B40EA0439C7");
    public static readonly Guid PeerRxCharacteristicUuid = Guid.Parse("8DFE6F10-6CB7-4E73-A918-DC47AC34D9E9");
    public static readonly Guid PeerTxCharacteristicUuid = Guid.Parse("7CF03A12-8B5E-4D91-B245-2B40EA0439C8");

    /// <summary>Кадр объявления NetworkId по GATT RX (только для сопряжённых пиров, не в рекламе).</summary>
    public const byte FrameNetworkIdAnnounce = 0x32;

    public const int NetworkIdAnnouncePacketLength = 1 + CompressedNetworkId.WireLength;

    public const uint ApplicationChunkMagic = 0x53503243;
    public const int ApplicationChunkHeaderLength = 16;

    public const int GattServiceDataNetworkIdLength = CompressedNetworkId.WireLength;

    public const ushort ManufacturerCompanyId = 0xE58B;

    private static ReadOnlySpan<byte> ManufacturerMagic => "SP2N"u8;

    public const byte ManufacturerPayloadTypeNetworkId = 0x02;

    public const int ManufacturerNetworkIdPayloadLength = 1 + GattServiceDataNetworkIdLength;

    public const int ManufacturerLegacyNetworkIdPayloadLength = 4 + 16;

    public static byte[] BuildManufacturerNetworkIdPayload(CompressedNetworkId networkId)
    {
        if (networkId.IsEmpty)
            throw new ArgumentException("NetworkId must not be empty.", nameof(networkId));
        var buf = new byte[ManufacturerNetworkIdPayloadLength];
        buf[0] = ManufacturerPayloadTypeNetworkId;
        if (!networkId.TryWriteBytes(buf.AsSpan(1)))
            throw new InvalidOperationException("Failed to serialize NetworkId.");
        return buf;
    }

    public static bool TryParseManufacturerNetworkIdPayload(ushort companyId, ReadOnlySpan<byte> data,
        out CompressedNetworkId networkId)
    {
        networkId = CompressedNetworkId.Empty;
        if (companyId != ManufacturerCompanyId || data.Length < ManufacturerNetworkIdPayloadLength)
            return false;
        if (data[0] != ManufacturerPayloadTypeNetworkId)
            return false;
        networkId = CompressedNetworkId.FromWireBytes(data.Slice(1, GattServiceDataNetworkIdLength));
        return !networkId.IsEmpty;
    }

    public static byte[] BuildManufacturerLegacyNetworkIdPayload(CompressedNetworkId networkId)
    {
        if (networkId.IsEmpty)
            throw new ArgumentException("NetworkId must not be empty.", nameof(networkId));
        var buf = new byte[ManufacturerLegacyNetworkIdPayloadLength];
        ManufacturerMagic.CopyTo(buf);
        Span<byte> wire = stackalloc byte[GattServiceDataNetworkIdLength];
        networkId.TryWriteBytes(wire);
        wire.CopyTo(buf.AsSpan(4, GattServiceDataNetworkIdLength));
        return buf;
    }

    public static bool TryParseManufacturerLegacyNetworkId(ushort companyId, ReadOnlySpan<byte> data,
        out CompressedNetworkId networkId)
    {
        networkId = CompressedNetworkId.Empty;
        if (companyId != ManufacturerCompanyId || data.Length < ManufacturerLegacyNetworkIdPayloadLength)
            return false;
        if (!data.Slice(0, 4).SequenceEqual(ManufacturerMagic))
            return false;
        networkId = CompressedNetworkId.FromWireBytes(data.Slice(4, 12));
        return !networkId.IsEmpty;
    }

    public static bool TryParseGattServiceDataNetworkIdPayload(ReadOnlySpan<byte> serviceData,
        out CompressedNetworkId networkId)
    {
        networkId = CompressedNetworkId.Empty;
        if (serviceData.Length == ManufacturerNetworkIdPayloadLength
            && serviceData[0] == ManufacturerPayloadTypeNetworkId)
        {
            networkId = CompressedNetworkId.FromWireBytes(serviceData.Slice(1, GattServiceDataNetworkIdLength));
            return !networkId.IsEmpty;
        }

        return TryParseGattServiceDataNetworkId(serviceData, out networkId);
    }

    public static byte[] BuildGattServiceDataNetworkId(CompressedNetworkId networkId)
    {
        if (networkId.IsEmpty)
            throw new ArgumentException("NetworkId must not be empty.", nameof(networkId));
        var buf = new byte[GattServiceDataNetworkIdLength];
        if (!networkId.TryWriteBytes(buf))
            throw new InvalidOperationException("Failed to serialize NetworkId.");
        return buf;
    }

    public static bool TryParseAdvertisementServiceDataSection(ReadOnlySpan<byte> sectionPayload,
        ReadOnlySpan<byte> serviceUuidBytes, bool serviceUuidAdvertised, out CompressedNetworkId networkId)
    {
        networkId = CompressedNetworkId.Empty;
        if (TryParseGattServiceDataNetworkIdPayload(sectionPayload, out networkId))
            return true;

        if (sectionPayload.Length == GattServiceDataNetworkIdLength)
        {
            if (!serviceUuidAdvertised)
                return false;
            return TryParseGattServiceDataNetworkId(sectionPayload, out networkId);
        }

        if (sectionPayload.Length < 16 + GattServiceDataNetworkIdLength
            || !sectionPayload.Slice(0, 16).SequenceEqual(serviceUuidBytes))
            return false;
        return TryParseGattServiceDataNetworkIdPayload(sectionPayload.Slice(16), out networkId);
    }

    public static bool TryParseGattServiceDataNetworkId(ReadOnlySpan<byte> serviceData, out CompressedNetworkId networkId)
    {
        networkId = CompressedNetworkId.Empty;
        if (serviceData.Length < GattServiceDataNetworkIdLength)
            return false;
        networkId = CompressedNetworkId.FromWireBytes(serviceData.Slice(0, GattServiceDataNetworkIdLength));
        return !networkId.IsEmpty;
    }

    public static byte[] BuildApplicationChunk(uint fullStreamCrc32, int chunkIndex, int totalChunks,
        ReadOnlySpan<byte> payloadSlice)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalChunks);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkIndex, totalChunks);

        var buf = new byte[ApplicationChunkHeaderLength + payloadSlice.Length];
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0, 4), ApplicationChunkMagic);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(4, 4), fullStreamCrc32);
        BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(8, 4), chunkIndex);
        BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(12, 4), totalChunks);
        payloadSlice.CopyTo(buf.AsSpan(ApplicationChunkHeaderLength));
        return buf;
    }

    public static byte[] BuildNetworkIdAnnouncePacket(CompressedNetworkId networkId)
    {
        if (networkId.IsEmpty)
            throw new ArgumentException("NetworkId must not be empty.", nameof(networkId));
        var buf = new byte[NetworkIdAnnouncePacketLength];
        buf[0] = FrameNetworkIdAnnounce;
        if (!networkId.TryWriteBytes(buf.AsSpan(1)))
            throw new InvalidOperationException("Failed to serialize NetworkId.");
        return buf;
    }

    public static bool TryParseNetworkIdAnnouncePacket(ReadOnlySpan<byte> buffer, out CompressedNetworkId networkId)
    {
        networkId = CompressedNetworkId.Empty;
        if (buffer.Length < NetworkIdAnnouncePacketLength || buffer[0] != FrameNetworkIdAnnounce)
            return false;
        networkId = CompressedNetworkId.FromWireBytes(buffer.Slice(1, CompressedNetworkId.WireLength));
        return !networkId.IsEmpty;
    }

    public static bool TryParseApplicationChunk(ReadOnlySpan<byte> buffer, out uint fullStreamCrc32,
        out int chunkIndex, out int totalChunks, out ReadOnlySpan<byte> payload)
    {
        fullStreamCrc32 = 0;
        chunkIndex = 0;
        totalChunks = 0;
        payload = ReadOnlySpan<byte>.Empty;
        if (buffer.Length < ApplicationChunkHeaderLength)
            return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(buffer) != ApplicationChunkMagic)
            return false;
        fullStreamCrc32 = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(4, 4));
        chunkIndex = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(8, 4));
        totalChunks = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(12, 4));
        if (totalChunks <= 0 || chunkIndex < 0 || chunkIndex >= totalChunks)
            return false;
        payload = buffer[ApplicationChunkHeaderLength..];
        return true;
    }
}
