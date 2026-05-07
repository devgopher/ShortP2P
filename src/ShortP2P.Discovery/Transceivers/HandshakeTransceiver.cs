using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Transceivers;

/// <summary>
///     Unicast-only приёмопередатчик handshake-кадров: 0x01 (RSA handshake, 128 байт) и 0x04
///     (session setup request, 16 байт Guid). Принимает датаграммы от <see cref="DataPortMultiplexer" />.
/// </summary>
public sealed class HandshakeTransceiver : IUnicastTransceiver<HandshakeMessage>
{
    private readonly Func<ReadOnlyMemory<byte>, TransportAddress, CancellationToken, ValueTask> _sendRaw;
    private bool _started;

    public HandshakeTransceiver(Func<ReadOnlyMemory<byte>, TransportAddress, CancellationToken, ValueTask> sendRaw)
    {
        _sendRaw = sendRaw ?? throw new ArgumentNullException(nameof(sendRaw));
    }

    public event EventHandler<HandshakeMessage>? GotData;

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        _started = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        _started = false;
        return ValueTask.CompletedTask;
    }

    public async ValueTask SendAsync(HandshakeMessage message, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(destination);
        if (BroadcastAddressFilter.IsLocalIpv4Broadcast(destination))
            throw new InvalidOperationException("Broadcast is not allowed for handshake transceiver.");

        var body = message.Body;
        var packet = new byte[1 + body.Length];
        packet[0] = (byte)message.Kind;
        if (!body.IsEmpty)
            body.CopyTo(packet.AsMemory(1));
        await _sendRaw(packet, destination, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => StopAsync();

    /// <summary>Вызывается из <see cref="DataPortMultiplexer" /> при разборе входящей датаграммы.</summary>
    internal void HandleIncoming(HandshakeKind kind, ReadOnlyMemory<byte> body, TransportAddress remoteAddress)
    {
        if (!_started)
            return;
        if (BroadcastAddressFilter.IsLocalIpv4Broadcast(remoteAddress))
            return;
        var msg = new HandshakeMessage(kind, body, remoteAddress);
        try
        {
            GotData?.Invoke(this, msg);
        }
        catch
        {
            // не ронять цикл приёма
        }
    }
}
