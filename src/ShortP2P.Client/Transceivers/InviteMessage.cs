using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Transceivers;

/// <summary>
///     Распарсенный invite (frame 0x30) от пира + сырой payload и адрес отправителя.
/// </summary>
public sealed class InviteMessage(
    Guid initiatorNetworkId,
    string nickname,
    string rsaPublicKeyJson,
    string dataHost,
    int dataPort,
    ReadOnlyMemory<byte> rawPayload,
    TransportAddress remoteAddress)
{
    public Guid InitiatorNetworkId { get; } = initiatorNetworkId;
    public string Nickname { get; } = nickname;
    public string RsaPublicKeyJson { get; } = rsaPublicKeyJson;
    public string DataHost { get; } = dataHost;
    public int DataPort { get; } = dataPort;
    public ReadOnlyMemory<byte> RawPayload { get; } = rawPayload;
    public TransportAddress RemoteAddress { get; } = remoteAddress;
}
