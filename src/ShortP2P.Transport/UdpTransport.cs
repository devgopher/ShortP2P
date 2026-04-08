using System.Net.Sockets;
using System.Threading.Channels;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport;

/// <summary>
///     UDP-транспорт (кроссплатформенный). Один датаграмма = один блок для верхнего слоя.
/// </summary>
public sealed class UdpTransport(int listenPort, bool enableBroadcast = false) : ITransport
{
    private readonly Channel<TransportReceiveMessage> _channel = Channel.CreateUnbounded<TransportReceiveMessage>();
    private readonly UdpClient _udp = CreateClient(listenPort, enableBroadcast);

    private static UdpClient CreateClient(int listenPort, bool enableBroadcast)
    {
        var c = new UdpClient(listenPort);
        if (enableBroadcast)
            c.EnableBroadcast = true;
        return c;
    }
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    public TransportKind Kind => TransportKind.Udp;

    public ChannelReader<TransportReceiveMessage> Inbound => _channel.Reader;

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (_cts != null) return ValueTask.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;
        _receiveTask = Task.Run(() => ReceiveLoopAsync(token), token);
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts == null) return;
        
        await _cts.CancelAsync();

        _udp.Close();

        if (_receiveTask != null)
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected
            }
            catch (ObjectDisposedException)
            {
                // expected
            }

        _receiveTask = null;
        _cts.Dispose();
        _cts = null;
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        if (destination.Kind != TransportKind.Udp)
            throw new ArgumentException("Destination must be UDP.", nameof(destination));

        var ep = UdpTransportAddress.ToIPEndPoint(destination);
        await _udp.SendAsync(payload.ToArray(), ep, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        return StopAsync();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
            try
            {
                var result = await _udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                var addr = UdpTransportAddress.FromIPEndPoint(result.RemoteEndPoint);
                await _channel.Writer.WriteAsync(new TransportReceiveMessage(result.Buffer, addr), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

        _channel.Writer.TryComplete();
    }
}