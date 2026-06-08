namespace ShortP2P.Transport.Abstractions;

/// <summary>
///     Тип физического/логического транспорта для mesh-обмена.
/// </summary>
public enum TransportKind : byte
{
    Udp = 1,
    Bluetooth = 2,

    Infrared = 3
    // Tcp,
    // 
}