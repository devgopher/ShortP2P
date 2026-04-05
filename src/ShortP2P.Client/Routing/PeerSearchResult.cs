using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Routing;

public sealed class PeerSearchResult
{
    public required string PeerHost { get; init; }

    public int PeerPort { get; init; }

    public required string RsaPublicJson { get; init; }

    public TransportAddress? FirstRelayHop { get; init; }

    public IReadOnlyList<TransportAddress> RelayStrip { get; init; } = Array.Empty<TransportAddress>();
}
