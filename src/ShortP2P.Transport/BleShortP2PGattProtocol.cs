using System.Buffers.Binary;

namespace ShortP2P.Transport;

/// <summary>
///     Общий BLE GATT-протокол ShortP2P (custom service, без OTS). UUID совпадают на Windows и Android.
/// </summary>
/// <remarks>
///     <para><b>Дуплекс: две характеристики на GATT Server (peripheral)</b></para>
///     <list type="bullet">
///         <item>
///             <see cref="PeerRxCharacteristicUuid" /> — <b>приём</b> на сервере: Central пишет (Write /
///             Write Without Response). Локальный <c>ITransport.Inbound</c>.
///         </item>
///         <item>
///             <see cref="PeerTxCharacteristicUuid" /> — <b>передача</b> с сервера: Notify/Indicate на
///             подписанный Central. Удалённый Central подписывается (CCCD) и получает данные в
///             <c>ValueChanged</c> / <c>OnCharacteristicChanged</c>.
///         </item>
///     </list>
///     <para>
///         <b>Исходящий Send (локальный Central):</b> запись в <see cref="PeerRxCharacteristicUuid" /> пира.
///         <b>Входящий приём:</b> Write на локальный RX и/или Notify с <see cref="PeerTxCharacteristicUuid" /> пира
///         (после подписки при подключении).
///     </para>
///     <para>
///         Симметричный чат: у каждого узла поднят Server (RX+TX) и клиент к пиру (write в RX пира,
///         subscribe на TX пира).
///     </para>
/// </remarks>
public static class BleShortP2PGattProtocol
{
    /// <summary>Custom ShortP2P service (128-bit UUID).</summary>
    public static readonly Guid ServiceUuid = Guid.Parse("9FE8E58B-AF85-4D91-B245-2B40EA0439C7");

    /// <summary>Приём на peripheral: Central → Write → сервер.</summary>
    public static readonly Guid PeerRxCharacteristicUuid = Guid.Parse("8DFE6F10-6CB7-4E73-A918-DC47AC34D9E9");
    
    /// <summary>Передача с peripheral: сервер → Notify → Central (нужна подписка CCCD).</summary>
    public static readonly Guid PeerTxCharacteristicUuid = Guid.Parse("7CF03A12-8B5E-4D91-B245-2B40EA0439C8");

    /// <summary>Magic «SP2C» для опционального прикладного чанка поверх GATT.</summary>
    public const uint ApplicationChunkMagic = 0x53503243;

    public const int ApplicationChunkHeaderLength = 16;

    /// <summary>Длина NetworkId в GATT Service Data (Guid wire, 16 байт).</summary>
    public const int GattServiceDataNetworkIdLength = 16;

    /// <summary>
    ///     Company ID для Manufacturer Data (производная от префикса <see cref="ServiceUuid" />).
    ///     Укладывается в legacy scan response вместе с 16-байтным NetworkId.
    /// </summary>
    public const ushort ManufacturerCompanyId = 0xE58B;

    private static ReadOnlySpan<byte> ManufacturerMagic => "SP2N"u8;

    public const int ManufacturerNetworkIdPayloadLength = 4 + GattServiceDataNetworkIdLength;

    /// <summary>Manufacturer Data: magic «SP2N» + NetworkId (16 байт wire Guid).</summary>
    public static byte[] BuildManufacturerNetworkIdPayload(Guid networkId)
    {
        if (networkId == Guid.Empty)
            throw new ArgumentException("NetworkId must not be empty.", nameof(networkId));
        var buf = new byte[ManufacturerNetworkIdPayloadLength];
        ManufacturerMagic.CopyTo(buf);
        if (!networkId.TryWriteBytes(buf.AsSpan(4)))
            throw new InvalidOperationException("Failed to serialize NetworkId.");
        return buf;
    }

    public static bool TryParseManufacturerNetworkId(ushort companyId, ReadOnlySpan<byte> data, out Guid networkId)
    {
        networkId = Guid.Empty;
        if (companyId != ManufacturerCompanyId || data.Length < ManufacturerNetworkIdPayloadLength)
            return false;
        if (!data.Slice(0, 4).SequenceEqual(ManufacturerMagic))
            return false;
        networkId = new Guid(data.Slice(4, GattServiceDataNetworkIdLength));
        return networkId != Guid.Empty;
    }

    /// <summary>
    ///     Service Data AD 0x21: либо UUID(16)+NetworkId(16), либо только NetworkId(16), если UUID уже в
    ///     <c>ServiceUuids</c> рекламы.
    /// </summary>
    public static bool TryParseAdvertisementServiceDataSection(ReadOnlySpan<byte> sectionPayload,
        ReadOnlySpan<byte> serviceUuidBytes, bool serviceUuidAdvertised, out Guid networkId)
    {
        networkId = Guid.Empty;
        if (sectionPayload.Length == GattServiceDataNetworkIdLength)
        {
            if (!serviceUuidAdvertised)
                return false;
            return TryParseGattServiceDataNetworkId(sectionPayload, out networkId);
        }

        if (sectionPayload.Length < 16 + GattServiceDataNetworkIdLength
            || !sectionPayload.Slice(0, 16).SequenceEqual(serviceUuidBytes))
            return false;
        return TryParseGattServiceDataNetworkId(sectionPayload.Slice(16), out networkId);
    }

    /// <summary>Payload для <see cref="GattServiceProviderAdvertisingParameters.ServiceData" /> (UUID сервиса в AD отдельно).</summary>
    public static byte[] BuildGattServiceDataNetworkId(Guid networkId)
    {
        if (networkId == Guid.Empty)
            throw new ArgumentException("NetworkId must not be empty.", nameof(networkId));
        var buf = new byte[GattServiceDataNetworkIdLength];
        if (!networkId.TryWriteBytes(buf))
            throw new InvalidOperationException("Failed to serialize NetworkId.");
        return buf;
    }

    public static bool TryParseGattServiceDataNetworkId(ReadOnlySpan<byte> serviceData, out Guid networkId)
    {
        networkId = Guid.Empty;
        if (serviceData.Length < GattServiceDataNetworkIdLength)
            return false;
        networkId = new Guid(serviceData.Slice(0, GattServiceDataNetworkIdLength));
        return networkId != Guid.Empty;
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
