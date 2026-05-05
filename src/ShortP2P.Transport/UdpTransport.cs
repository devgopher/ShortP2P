using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport;

/// <summary>
///     UDP-транспорт (кроссплатформенный). Один датаграмма = один блок для верхнего слоя.
///     Привязка к заданному <see cref="IPAddress" /> и порту — для приёма со всех интерфейсов используйте
///     <see cref="IPAddress.Any" /> (в т.ч. DNAT на роутере на этот хост).
///     Исходящие и входящие операции с сокетом сериализуются отдельными <see cref="SemaphoreSlim" /> (1,1).
///     Прямое создание — <see cref="CreateUdpTransport" />; общий сокет на процесс — <see cref="IUdpTransportFactory" />.
/// </summary>
public sealed class UdpTransport : ITransport
{
    private readonly IPAddress _bindAddress;
    private readonly int _listenPort;
    private readonly bool _enableBroadcast;
    private readonly Channel<TransportReceiveMessage> _channel = Channel.CreateUnbounded<TransportReceiveMessage>();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly SemaphoreSlim _receiveGate = new(1, 1);
    private UdpClient _udp;

    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    private UdpTransport(IPAddress bindAddress, int listenPort, bool enableBroadcast)
    {
        _bindAddress = bindAddress;
        _listenPort = listenPort;
        _enableBroadcast = enableBroadcast;
        _udp = CreateClient(bindAddress, listenPort, enableBroadcast);
    }

    /// <summary>Создаёт UDP-транспорт с привязкой к локальному адресу и порту.</summary>
    /// <param name="ip">Адрес привязки (часто <see cref="IPAddress.Any" />).</param>
    /// <param name="port">Локальный порт прослушивания.</param>
    /// <param name="enableBroadcast">Разрешить широковещательные исходящие датаграммы.</param>
    public static UdpTransport CreateUdpTransport(IPAddress ip, int port, bool enableBroadcast = false)
    {
        ArgumentNullException.ThrowIfNull(ip);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        return new UdpTransport(ip, port, enableBroadcast);
    }

    private static UdpClient CreateClient(IPAddress bindAddress, int listenPort, bool enableBroadcast)
    {
        var c = new UdpClient();
        c.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        c.Client.Bind(new IPEndPoint(bindAddress, listenPort));
        if (enableBroadcast)
            c.EnableBroadcast = true;

        return c;
    }

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

        await _cts.CancelAsync().ConfigureAwait(false);

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
        _receiveGate.Dispose();
        _sendGate.Dispose();
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        if (destination.Kind != TransportKind.Udp)
            throw new ArgumentException("Destination must be UDP.", nameof(destination));

        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var ep = UdpTransportAddress.ToIPEndPoint(destination);

            try
            {
                await _udp.SendAsync(payload.ToArray(), ep, cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                _udp = CreateClient(_bindAddress, _listenPort, _enableBroadcast);
                await _udp.SendAsync(payload.ToArray(), ep, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _sendGate.Release();
        }
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
                await _receiveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var result = await _udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                    var addr = UdpTransportAddress.FromIPEndPoint(result.RemoteEndPoint);
                    await _channel.Writer
                        .WriteAsync(new TransportReceiveMessage(result.Buffer, addr), cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _receiveGate.Release();
                }
            }
            catch (SocketException ex)
            {
                _udp = CreateClient(_bindAddress, _listenPort, _enableBroadcast);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                _udp = CreateClient(_bindAddress, _listenPort, _enableBroadcast);
            }

        _channel.Writer.TryComplete();
    }
}
