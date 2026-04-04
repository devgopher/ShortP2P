using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Messenger;

/// <summary>
///     Полное расшифрованное сообщение от пира.
/// </summary>
public sealed class IncomingBinaryMessage(ReadOnlyMemory<byte> payload, TransportAddress sender)
{
    public ReadOnlyMemory<byte> Payload { get; } = payload;

    public TransportAddress Sender { get; } = sender;
}