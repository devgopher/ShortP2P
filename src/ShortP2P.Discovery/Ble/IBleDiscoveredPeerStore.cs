using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Ble;

/// <summary>
///     Локальное хранилище BLE-пиров, увиденных сканированием и/или presence-пингом.
/// </summary>
public interface IBleDiscoveredPeerStore
{
    ValueTask RecordScanSeenAsync(TransportAddress bluetoothMac, BleAdScanResult scanResult = default,
        CancellationToken cancellationToken = default);

    ValueTask RecordPingAsync(TransportAddress bluetoothMac, Guid peerNetworkId, string nickname,
        CancellationToken cancellationToken = default);
}
