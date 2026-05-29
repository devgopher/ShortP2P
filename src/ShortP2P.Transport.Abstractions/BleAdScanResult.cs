using ShortP2P.Auth.Data;

namespace ShortP2P.Transport.Abstractions;

/// <summary>
///     Идентичность BLE-пира: канонический NetworkId (12 байт), обычно из GATT-кадра 0x32, не из рекламы.
/// </summary>
public readonly struct BleAdScanResult
{
    public CompressedNetworkId? NetworkId { get; init; }

    public bool HasNetworkId => NetworkId is { } id && !id.IsEmpty;

    public bool HasIdentity => HasNetworkId;
}
