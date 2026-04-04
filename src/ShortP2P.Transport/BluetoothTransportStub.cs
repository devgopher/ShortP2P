using System.Threading.Channels;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport;

/// <summary>
///     Заготовка Bluetooth-транспорта. На .NET нет единого кроссплатформенного BLE API из коробки;
///     сюда подключают платформенный стек (WinRT, Android/iOS bindings и т.д.).
/// </summary>
public sealed class BluetoothTransportStub : ITransport
{
    private readonly Channel<TransportReceiveMessage> _channel = Channel.CreateUnbounded<TransportReceiveMessage>();

    public TransportKind Kind => TransportKind.Bluetooth;

    public ChannelReader<TransportReceiveMessage> Inbound => _channel.Reader;

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        _ = payload;
        _ = destination;
        _ = cancellationToken;
        return ValueTask.FromException(new NotSupportedException(
            "Bluetooth transport is not implemented: integrate a platform BLE API (Classic or LE) and wire it to ITransport."));
    }

    public ValueTask DisposeAsync()
    {
        return StopAsync();
    }
}