namespace ShortP2P.Discovery;

/// <summary>
///     Параметры UDP discovery (broadcast на выделенный порт, отдельно от порта данных мессенджера).
/// </summary>
public sealed class UdpPeerDiscoveryOptions
{
    /// <summary>Порт по умолчанию: beacon <see cref="UdpPeerDiscoveryService" />, gossip и запрос маршрутной таблицы (wire).</summary>
    public const int DefaultDiscoveryUdpPort = 17890;

    /// <summary>Порт, на котором слушаются и рассылаются beacon-пакеты и wire-запросы discovery.</summary>
    public int DiscoveryPort { get; set; } = DefaultDiscoveryUdpPort;

    public TimeSpan AnnounceInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan PeerStaleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Максимальная длина nickname в UTF-8 (байты).</summary>
    public int MaxNicknameUtf8Bytes { get; set; } = 64;

    /// <summary>Период проверки «пропавших» соседей.</summary>
    public TimeSpan StaleCheckInterval { get; set; } = TimeSpan.FromSeconds(1);
}