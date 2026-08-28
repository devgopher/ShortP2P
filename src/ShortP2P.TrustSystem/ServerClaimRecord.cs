namespace ShortP2P.TrustSystem;

public sealed class ServerClaimRecord
{
    public required string ComplainantId { get; set; }

    public ServerClaimReason Reason { get; set; }

    public DateTime Utc { get; set; }
}
