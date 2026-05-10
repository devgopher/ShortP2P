using System.Buffers.Binary;

namespace ShortP2P.Transport;

/// <summary>
///     Общий BLE GATT-протокол ShortP2P (custom service, без OTS). UUID совпадают с
///     <c>WindowsBluetoothTransport</c> и <c>AndroidBluetoothTransport</c>.
/// </summary>
/// <remarks>
///     <para><b>Роли BLE и имена в продукте</b></para>
///     <list type="bullet">
///         <item>
///             <b>Peripheral (GATT Server)</b> — узел, который публикует сервис ShortP2P и локальную
///             характеристику «RX»: удалённый пир подключается как <b>Central</b> и <b>пишет</b> в эту
///             характеристику, чтобы передать вам полезную нагрузку. Локально приём = обработка Write.
///         </item>
///         <item>
///             <b>Central (GATT Client)</b> — узел, который подключается к пиру, находит сервис/характеристику
///             и выполняет <b>Write (без ответа, по возможности)</b> для исходящих данных.
///         </item>
///     </list>
///     <para>
///         Текущая реализация в приложении <b>симметрична</b>: у каждого пира поднят GATT Server
///         (peripheral) и одновременно используется роль Central для записи в «RX» пира.
///         Для сценария «только клиент без сканирования» на Windows можно отключить discoverable-рекламу
///         (см. параметры <c>WindowsBluetoothTransport</c> в проекте Bluetooth.Windows), оставляя сервис
///         connectable; либо отдельный асимметричный профиль с двумя характеристиками
///         (запись к серверу + notify/indicate от сервера) — см. README транспорта.
///     </para>
///     <para><b>Крупные объёмы (без OTS)</b></para>
///     <para>
///         Приоритет: фрагментация на уровне мессенджера/сессии (уже есть chunking шифротекста) +
///         последовательные GATT Write. Опционально на уровне приложения — кадры с CRC (см.
///         <see cref="TryParseApplicationChunk" /> / <see cref="BuildApplicationChunk" />); уведомления
///         с сервера и custom L2CAP CoC / Classic RFCOMM оставлены на будущее (другой объём работ и API
///         по платформам).
///     </para>
/// </remarks>
public static class BleShortP2PGattProtocol
{
    /// <summary>Custom ShortP2P service (128-bit UUID).</summary>
    public static readonly Guid ServiceUuid = Guid.Parse("9FE8E58B-AF85-4D91-B245-2B40EA0439C7");

    /// <summary>
    ///     Характеристика, в которую Central пишет данные для приёма на стороне peripheral.
    ///     (В коде WinRT названа BleRx — «RX с точки зрения удалённого отправителя».)
    /// </summary>
    public static readonly Guid PeerRxCharacteristicUuid = Guid.Parse("8DFE6F10-6CB7-4E73-A918-DC47AC34D9E9");

    /// <summary>Magic «SP2C» для опционального прикладного чанка поверх GATT (не обязателен для текущего чата).</summary>
    public const uint ApplicationChunkMagic = 0x53503243;

    public const int ApplicationChunkHeaderLength = 16;

    /// <summary>
    ///     Опциональный заголовок прикладного чанка: magic, полный размер потока, индекс чанка, число чанков, CRC32 полного потока.
    ///     Полезная нагрузка идёт следом; последний чанк может быть короче.
    /// </summary>
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
