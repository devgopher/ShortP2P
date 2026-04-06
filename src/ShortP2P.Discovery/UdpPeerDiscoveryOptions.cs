namespace ShortP2P.Discovery;

/// <summary>
///     Параметры UDP discovery (broadcast на выделенный порт, отдельно от порта данных мессенджера).
/// </summary>
public sealed class UdpPeerDiscoveryOptions
{
    /// <summary>Порт, на котором слушаются и рассылаются beacon-пакеты.</summary>
    public int DiscoveryPort { get; set; } = 17890;

    public TimeSpan AnnounceInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan PeerStaleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Максимальная длина nickname в UTF-8 (байты).</summary>
    public int MaxNicknameUtf8Bytes { get; set; } = 64;

    /// <summary>Период проверки «пропавших» соседей.</summary>
    public TimeSpan StaleCheckInterval { get; set; } = TimeSpan.FromSeconds(1);
}