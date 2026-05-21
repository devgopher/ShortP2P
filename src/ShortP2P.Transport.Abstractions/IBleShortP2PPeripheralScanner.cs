namespace ShortP2P.Transport.Abstractions;

/// <summary>
///     Пассивное/активное сканирование BLE-рекламы с сервисом ShortP2P (см. <c>BleShortP2PGattProtocol.ServiceUuid</c>).
/// </summary>
public interface IBleShortP2PPeripheralScanner
{
    /// <summary>
    ///     В течение <paramref name="duration" /> вызывает <paramref name="onDeviceDiscovered" /> для каждого
    ///     уникального MAC (Bluetooth <see cref="TransportAddress" />).
    ///     <paramref name="onDeviceDiscovered" /> получает NetworkId из рекламы, если удалось распарсить.
    /// </summary>
    Task ScanAsync(TimeSpan duration, Action<TransportAddress, Guid?> onDeviceDiscovered,
        CancellationToken cancellationToken = default);
}
