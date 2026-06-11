using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery;

/// <summary>
///     Wi-Fi Direct device id, на которые нужно слать presence-пинг в каждом раунде.
/// </summary>
public interface IWifiDirectPresencePingTargetsProvider
{
    ValueTask<IReadOnlyList<TransportAddress>> GetWifiDirectPingTargetsAsync(
        CancellationToken cancellationToken = default);
}
