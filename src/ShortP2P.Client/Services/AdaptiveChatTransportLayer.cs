using ShortP2P.Client.Routing;
using ShortP2P.Transport.Abstractions;
using System.Threading.Channels;

namespace ShortP2P.Client.Services;

/// <summary>
///     Транспортный слой чата: приём с локального UDP и отправка на адрес пира из чата.
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
        var inboundTransports = inboundTransportsProvider();
        if (inboundTransports.Count == 0)
            throw new InvalidOperationException("No inbound transports are started.");
        foreach (var transport in inboundTransports)
            _directReceiveTasks.Add(Task.Run(() => DirectReceiveLoopAsync(transport, _cts.Token), _cts.Token));

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

        foreach (var peerAddress in peerAddresses)
        {
            var transport = transportResolver(peerAddress ??
                                              throw new InvalidOperationException(
                                                  $"Transport is not started for {peerAddress.Kind}."));

            if (transport == null) continue;
            try
            {
                await transport.SendAsync(packet, peerAddress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                continue;
            }

            break;
        }
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
