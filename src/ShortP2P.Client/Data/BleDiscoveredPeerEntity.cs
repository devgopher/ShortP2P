using SQLite;

namespace ShortP2P.Client.Data;

[Table("ble_discovered_peers")]
public sealed class BleDiscoveredPeerEntity
{
    /// <summary>Нормализованный MAC (<c>AA:BB:...</c>).</summary>
    [PrimaryKey]
    public string MacNormalized { get; set; } = "";

    public long LastSeenScanUtcTicks { get; set; }

    public long LastPingUtcTicks { get; set; }

    public string? PeerNetworkIdShort { get; set; }

    public string? PeerNickname { get; set; }
}