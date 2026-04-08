using ShortP2P.Client.Routing;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;
using System.Threading.Channels;

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
    private readonly Channel<TransportReceiveMessage> _inbound = Channel.CreateUnbounded<TransportReceiveMessage>();
    private CancellationTokenSource? _cts;
    private Task? _directReceiveTask;

    public ChannelReader<TransportReceiveMessage> Inbound => _inbound.Reader;

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (_cts != null)
            return ValueTask.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (sharedGateway != null)
        {
            sharedGateway.SetChatSink(OnGatewayDatagramAsync);
        }
        else
        {
            var udp = udpProvider() ?? throw new InvalidOperationException("UDP transport is not started.");
            _directReceiveTask = Task.Run(() => DirectReceiveLoopAsync(udp, _cts.Token), _cts.Token);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (_cts == null)
            return;

        if (sharedGateway != null)
            sharedGateway.SetChatSink(null);

        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            _cts.Cancel();
        }

        if (_directReceiveTask != null)
        {
            try
            {
                await _directReceiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _directReceiveTask = null;
        _cts.Dispose();
        _cts = null;
        _inbound.Writer.TryComplete();
    }

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

    private async Task OnGatewayDatagramAsync(ReadOnlyMemory<byte> payload, TransportAddress from)
    {
        var token = _cts?.Token ?? CancellationToken.None;
        await _inbound.Writer.WriteAsync(new TransportReceiveMessage(payload, from), token).ConfigureAwait(false);
    }

    private async Task DirectReceiveLoopAsync(UdpTransport udp, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var msg in udp.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                await _inbound.Writer.WriteAsync(msg, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
