using System.Threading.Channels;

namespace ShortP2P.Discovery;

/// <summary>
///     Сервис поиска абонентов рядом (реализация по умолчанию для LAN — <see cref="UdpPeerDiscoveryService" />).
/// </summary>
public interface IPeerDiscoveryService : IAsyncDisposable
{
    PeerIdentity LocalPeer { get; }

    ChannelReader<DiscoveryNotification> Notifications { get; }

    IReadOnlyCollection<DiscoveredPeer> GetPeersSnapshot();

    ValueTask StartAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}