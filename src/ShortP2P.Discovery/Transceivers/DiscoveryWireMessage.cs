using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Transceivers;

/// <summary>Тип wire-пакета discovery (gossip + route table + peer profile) на UDP 17890.</summary>
public enum DiscoveryWireKind : byte
{
    GossipProbe = 0x40,
    GossipAck = 0x41,
    RouteTableRequest = 0x42,
    RouteTableReply = 0x43,
    PeerProfileRequest = 0x44,
    PeerProfileReply = 0x45
}

/// <summary>
///     Сырой discovery wire-пакет. Парсинг внутреннего содержимого (probe/ack/request/reply) делает
///     потребитель через специализированные кодеки в ShortP2P.Discovery.Gossip.
/// </summary>
public sealed class DiscoveryWireMessage(
    DiscoveryWireKind kind,
    ReadOnlyMemory<byte> rawPayload,
    TransportAddress remoteAddress)
{
    public DiscoveryWireKind Kind { get; } = kind;

    /// <summary>Полный datagram payload, начиная с frame-байта.</summary>
    public ReadOnlyMemory<byte> RawPayload { get; } = rawPayload;

    public TransportAddress RemoteAddress { get; } = remoteAddress;
}