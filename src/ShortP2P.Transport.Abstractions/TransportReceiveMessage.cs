namespace ShortP2P.Transport.Abstractions;

/// <summary>
///     Входящий пакет с транспорта (ещё не расшифрованный бэкендом мессенджера).
/// </summary>
public readonly struct TransportReceiveMessage(ReadOnlyMemory<byte> payload, TransportAddress remoteAddress)
{
    public ReadOnlyMemory<byte> Payload { get; } = payload;

    public TransportAddress RemoteAddress { get; } = remoteAddress;
}