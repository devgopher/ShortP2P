using System.Net.Sockets;
using System.Net;
using System.Threading.Channels;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport;

/// <summary>
///     UDP-транспорт (кроссплатформенный). Один датаграмма = один блок для верхнего слоя.
///     Привязка к <see cref="IPAddress.Any"/> — приём со всех локальных IPv4, включая датаграммы,
///     доставленные на этот хост после DNAT с внешнего (WAN) адреса при пробросе портов на роутере.
/// </summary>
public sealed class UdpTransport(int listenPort, bool enableBroadcast = false) : ITransport
{
    private readonly Channel<TransportReceiveMessage> _channel = Channel.CreateUnbounded<TransportReceiveMessage>();
    private readonly UdpClient _udp = CreateClient(listenPort, enableBroadcast);

    private static UdpClient CreateClient(int listenPort, bool enableBroadcast)
    {
        var c = new UdpClient();
        c.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress,true);
        c.Client.Bind(new IPEndPoint(IPAddress.Any, listenPort));
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

    private readonly SemaphoreSlim _lockSemaphore = new(1, 1);
    
    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        await _lockSemaphore.WaitAsync(cancellationToken);
        await InnerSend(payload, destination, cancellationToken);
    }

    private async ValueTask InnerSend(ReadOnlyMemory<byte> payload, TransportAddress destination,
        CancellationToken cancellationToken)
    {
        
        if (destination.Kind != TransportKind.Udp)
            throw new ArgumentException("Destination must be UDP.", nameof(destination));

        var ep = UdpTransportAddress.ToIPEndPoint(destination);
        try
        {
            var sent = await _udp.SendAsync(payload.ToArray(), ep, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.NetworkUnreachable)
        {
            // лог, задержка и повтор или пересоздать сокет
        }

        _lockSemaphore.Release();
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