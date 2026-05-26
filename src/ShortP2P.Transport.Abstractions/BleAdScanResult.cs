using ShortP2P.Auth.Data;

namespace ShortP2P.Transport.Abstractions;

/// <summary>
///     Идентичность пира из BLE-рекламы: канонический NetworkId (12 байт).
/// </summary>
public readonly struct BleAdScanResult
{
    public CompressedNetworkId? NetworkId { get; init; }

    public bool HasNetworkId => NetworkId is { } id && !id.IsEmpty;

    public bool HasIdentity => HasNetworkId;
}
