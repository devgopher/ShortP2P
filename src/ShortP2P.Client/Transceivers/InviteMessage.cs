using ShortP2P.Auth.Data;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Transceivers;

/// <summary>
///     Распарсенный invite (frame 0x30) от пира + сырой payload и адрес отправителя.
/// </summary>
public sealed class InviteMessage(
    CompressedNetworkId initiatorNetworkId,
    string nickname,
    string rsaPublicKeyJson,
    string dataHost,
    int dataPort,
    ReadOnlyMemory<byte> rawPayload,
    TransportAddress remoteAddress)
{
    public CompressedNetworkId InitiatorNetworkId { get; } = initiatorNetworkId;
    public string Nickname { get; } = nickname;
    public string RsaPublicKeyJson { get; } = rsaPublicKeyJson;
    public string DataHost { get; } = dataHost;
    public int DataPort { get; } = dataPort;
    public ReadOnlyMemory<byte> RawPayload { get; } = rawPayload;
    public TransportAddress RemoteAddress { get; } = remoteAddress;
}
