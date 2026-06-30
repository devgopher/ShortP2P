using ShortP2P.Auth.Data;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Ble;

/// <summary>
///     Локальное хранилище BLE-пиров, увиденных сканированием и/или presence-пингом.
/// </summary>
public interface IBleDiscoveredPeerStore
{
    ValueTask RecordScanSeenAsync(TransportAddress bluetoothMac, BleAdScanResult scanResult = default,
        CancellationToken cancellationToken = default);

    ValueTask RecordPingAsync(TransportAddress bluetoothMac, CompressedNetworkId peerNetworkId, string nickname,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     NetworkId с data-порта BLE: сохраняет MAC и удаляет прочие MAC с тем же network id.
    /// </summary>
    ValueTask RecordDataPortNetworkIdAsync(TransportAddress bluetoothMac, CompressedNetworkId peerNetworkId,
        CancellationToken cancellationToken = default);
}