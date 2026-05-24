namespace ShortP2P.Transport.Abstractions;

/// <summary>
///     Идентичность пира из BLE-рекламы: 8-байтный hint (v2) и/или полный NetworkId (legacy v1).
/// </summary>
public readonly struct BleAdScanResult
{
    public const int NetworkIdHintLength = 8;

    /// <summary>Первые 8 байт wire-Guid (v2 manufacturer или производная от legacy v1).</summary>
    public ReadOnlyMemory<byte> NetworkIdHint { get; init; }

    /// <summary>Legacy: полный NetworkId был в рекламе (SP2N + 16 байт).</summary>
    public Guid? LegacyFullNetworkId { get; init; }

    public bool HasHint => NetworkIdHint.Length == NetworkIdHintLength;

    public bool HasIdentity => HasHint || LegacyFullNetworkId is { } id && id != Guid.Empty;
}
