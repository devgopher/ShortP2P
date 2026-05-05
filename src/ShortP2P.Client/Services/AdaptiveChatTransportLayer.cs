using ShortP2P.Client.Routing;
using ShortP2P.Transport.Abstractions;
using System.Threading.Channels;

namespace ShortP2P.Client.Services;

/// <summary>
///     Транспортный слой чата: приём UDP (все интерфейсы, в т.ч. после DNAT с WAN) и отправка на адреса пира из чата
///     (LAN или публичный IPv4 при пробросе портов).
/// </summary>
public sealed class AdaptiveChatTransportLayer(
    Func<TransportAddress[]?> directPeerAddressProvider,
    Func<TransportAddress, ITransport?> transportResolver,
    Func<IReadOnlyList<ITransport>> inboundTransportsProvider,
    Func<TransportKind, bool>? isTransportEnabled = null,
    Func<TransportAddress, bool>? shouldAcceptFrom = null)
{
    private readonly Channel<TransportReceiveMessage> _inbound = Channel.CreateUnbounded<TransportReceiveMessage>();
    private CancellationTokenSource? _cts;
    private readonly List<Task> _directReceiveTasks = [];

    public ChannelReader<TransportReceiveMessage> Inbound => _inbound.Reader;

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (_cts != null)
            return ValueTask.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var localCts = _cts;
        var inboundTransports = inboundTransportsProvider();
        if (inboundTransports.Count == 0)
            throw new InvalidOperationException("No inbound transports are started.");
        foreach (var transport in inboundTransports)
            _directReceiveTasks.Add(Task.Run(() => DirectReceiveLoopAsync(transport, localCts.Token), localCts.Token));

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

        if (_directReceiveTasks.Count > 0)
        {
            foreach (var task in _directReceiveTasks)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // ignore
                }
            }
        }

        _directReceiveTasks.Clear();
        _cts.Dispose();
        _cts = null;
        _inbound.Writer.TryComplete();
    }

    public async ValueTask SendPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
    {
        var peerAddresses = directPeerAddressProvider() ?? throw new InvalidOperationException("Peer address is not set.");
        Exception? lastError = null;
        var attempted = 0;

        foreach (var peerAddress in peerAddresses)
        {
            var transport = transportResolver(peerAddress);

            if (transport == null) continue;
            attempted++;
            try
            {
                await transport.SendAsync(packet, peerAddress, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                continue;
            }
        }

        if (attempted == 0)
            throw new InvalidOperationException("No transport available for current peer addresses.");

        throw new IOException("Failed to send packet to any peer address.", lastError);
    }

    private async Task DirectReceiveLoopAsync(ITransport transport, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var msg in transport.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (isTransportEnabled != null && !isTransportEnabled(msg.RemoteAddress.Kind))
                    continue;
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
