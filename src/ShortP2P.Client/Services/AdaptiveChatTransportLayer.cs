using ShortP2P.Client.Routing;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Services;

/// <summary>
///     Транспортный слой чата, не привязанный к конкретному транспорту:
///     выбирает доступный транспорт и адрес доставки на основании последних ping пакетов.
/// </summary>
public sealed class AdaptiveChatTransportLayer(
    SharedUserUdpGateway? sharedGateway,
    Func<ChatRelayRoute> currentRouteProvider,
    Func<TransportAddress?> directPeerAddressProvider,
    Func<UdpTransport?> udpProvider,
    Guid peerNetworkId)
{
    public async ValueTask SendPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
    {
        if (sharedGateway != null)
        {
            if (sharedGateway.TryGetPeerLastSeenAddress(peerNetworkId, out var pingAddress) &&
                sharedGateway.IsTransportAvailable(pingAddress.Kind))
            {
                await sharedGateway.SendRawToAsync(packet, pingAddress, cancellationToken).ConfigureAwait(false);
                return;
            }

            await sharedGateway.SendP2pPayloadAsync(packet, currentRouteProvider(), cancellationToken).ConfigureAwait(false);
            return;
        }

        var udp = udpProvider() ?? throw new InvalidOperationException("UDP transport is not started.");
        var peerAddress = directPeerAddressProvider() ?? throw new InvalidOperationException("Peer address is not set.");
        await udp.SendAsync(packet, peerAddress, cancellationToken).ConfigureAwait(false);
    }
}
