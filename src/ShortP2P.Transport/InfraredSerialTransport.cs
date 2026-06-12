using System.Buffers.Binary;
using System.IO.Ports;
using System.Threading.Channels;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport;

/// <summary>
///     ИК-порт часто доступен как виртуальный COM / последовательный канал.
///     Кадрирование: 4 байта длины (BE) + полезная нагрузка (до лимита мессенджера на уровне приложения).
/// </summary>
public sealed class InfraredSerialTransport : ITransport
{
    private readonly int _baudRate;
    private readonly Channel<TransportReceiveMessage> _channel = Channel.CreateUnbounded<TransportReceiveMessage>();
    private readonly string _portName;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private SerialPort? _serial;

    /// <param name="portName">Имя порта, например COM3 или /dev/ttyUSB0.</param>
    public InfraredSerialTransport(string portName, int baudRate = 115200)
    {
        _portName = portName ?? throw new ArgumentNullException(nameof(portName));
        _baudRate = baudRate;
    }

    public TransportKind Kind => TransportKind.Infrared;

    public ChannelReader<TransportReceiveMessage> Inbound => _channel.Reader;

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (_serial != null) return ValueTask.CompletedTask;

        _serial = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = SerialPort.InfiniteTimeout,
            WriteTimeout = SerialPort.InfiniteTimeout
        };
        _serial.Open();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _readTask = Task.Run(() => ReadLoopAsync(_serial.BaseStream, _cts.Token), _cts.Token);
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts != null)
        {
            try
            {
                await _cts.CancelAsync().ConfigureAwait(false);
            }
            catch
            {
                await _cts.CancelAsync();
            }

            _cts.Dispose();
            _cts = null;
        }

        if (_readTask != null)
        {
            try
            {
                await _readTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }

            _readTask = null;
        }

        if (_serial != null)
        {
            if (_serial.IsOpen)
                _serial.Close();
            _serial.Dispose();
            _serial = null;
        }

        _channel.Writer.TryComplete();
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        _ = destination;
        if (_serial == null || !_serial.IsOpen)
            throw new InvalidOperationException("Serial transport is not started.");

        if (payload.Length > int.MaxValue - 4)
            throw new ArgumentException("Payload is too large.", nameof(payload));

        var len = payload.Length;
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, len);

        var stream = _serial.BaseStream;
        await stream.WriteAsync(header.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        return StopAsync();
    }

    private async Task ReadLoopAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lenBuf = new byte[4];
        while (!cancellationToken.IsCancellationRequested)
        {
            await ReadExactAsync(stream, lenBuf.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
            var len = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
            if (len < 0 || len > 1024 * 1024)
            {
                _channel.Writer.TryComplete(new InvalidDataException($"Invalid framed length: {len}."));
                return;
            }

            var body = new byte[len];
            await ReadExactAsync(stream, body.AsMemory(0, len), cancellationToken).ConfigureAwait(false);

            var remote = new TransportAddress(TransportKind.Infrared, []);
            await _channel.Writer.WriteAsync(new TransportReceiveMessage(body, remote), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var remaining = buffer.Length;
        var offset = 0;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.Slice(offset, remaining), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException();
            remaining -= read;
            offset += read;
        }
    }
}