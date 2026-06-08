using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Transceivers;

/// <summary>
///     Unicast-only приёмопередатчик «обычных» (cipher 0x02) сообщений. На отправке добавляет 1-байтный
///     префикс <see cref="FrameCipher" /> к payload; на приёме отдаёт <see cref="TransportReceiveMessage" />
///     уже без префикса.
/// </summary>
public sealed class MessageTransceiver : IUnicastTransceiver<TransportReceiveMessage>
{
    public const byte FrameCipher = 0x02;

    private readonly Func<ReadOnlyMemory<byte>, TransportAddress, CancellationToken, ValueTask> _sendRaw;
    private bool _started;

    public MessageTransceiver(Func<ReadOnlyMemory<byte>, TransportAddress, CancellationToken, ValueTask> sendRaw)
    {
        _sendRaw = sendRaw ?? throw new ArgumentNullException(nameof(sendRaw));
    }

    public event EventHandler<TransportReceiveMessage>? GotData;

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

    public async ValueTask SendAsync(TransportReceiveMessage message, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (BroadcastAddressFilter.IsLocalIpv4Broadcast(destination))
            throw new InvalidOperationException("Broadcast is not allowed for cipher message transceiver.");

        var payload = message.Payload;
        var packet = new byte[payload.Length + 1];
        packet[0] = FrameCipher;
        if (!payload.IsEmpty)
            payload.CopyTo(packet.AsMemory(1));
        await _sendRaw(packet, destination, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        return StopAsync();
    }

    /// <summary>Вызывается из <see cref="DataPortMultiplexer" /> для cipher-датаграммы (без байта 0x02).</summary>
    internal void HandleIncoming(ReadOnlyMemory<byte> cipherPayload, TransportAddress remoteAddress)
    {
        if (!_started)
            return;
        if (BroadcastAddressFilter.IsLocalIpv4Broadcast(remoteAddress))
            return;
        var msg = new TransportReceiveMessage(cipherPayload, remoteAddress);
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