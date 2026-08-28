using ShortP2P.TrustSystem;

namespace ShortP2P.MessengerServer.Contracts.Dtos;

public sealed class ClaimServerRequest
{
    public required string ServerIp { get; init; }

    public required int ServerPort { get; init; }

    public required ServerClaimReason Reason { get; init; }
}
