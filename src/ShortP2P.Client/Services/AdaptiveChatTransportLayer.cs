using ShortP2P.Client.Routing;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;
using System.Threading.Channels;

namespace ShortP2P.Client.Services;

/// <summary>
///     Транспортный слой чата: приём с локального UDP и отправка на адрес пира из чата.
/// </summary>
public sealed class AdaptiveChatTransportLayer(
    Func<TransportAddress?> directPeerAddressProvider,
    Func<UdpTransport?> udpProvider,
    Func<TransportAddress, bool>? shouldAcceptFrom = null)
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
        var udp = udpProvider() ?? throw new InvalidOperationException("UDP transport is not started.");
        _directReceiveTask = Task.Run(() => DirectReceiveLoopAsync(udp, _cts.Token), _cts.Token);

        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (_cts == null)
            return;

        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        if (_directReceiveTask != null)
        {
            try
            {
                await _directReceiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }

        _directReceiveTask = null;
        _cts.Dispose();
        _cts = null;
        _inbound.Writer.TryComplete();
    }

    public async ValueTask SendPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
    {
        var udp = udpProvider() ?? throw new InvalidOperationException("UDP transport is not started.");
        var peerAddress = directPeerAddressProvider() ?? throw new InvalidOperationException("Peer address is not set.");
        await udp.SendAsync(packet, peerAddress, cancellationToken).ConfigureAwait(false);
    }

    private async Task DirectReceiveLoopAsync(UdpTransport udp, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var msg in udp.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var payload = msg.Payload;
                if (shouldAcceptFrom != null &&
                    (payload.IsEmpty || payload.Span[0] != ChatInviteCodec.FrameChatInvite) &&
                    !shouldAcceptFrom(msg.RemoteAddress))
                    continue;
                await _inbound.Writer.WriteAsync(msg, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
    }
}
