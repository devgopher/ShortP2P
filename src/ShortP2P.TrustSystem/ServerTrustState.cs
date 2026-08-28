namespace ShortP2P.TrustSystem;

public sealed class ServerTrustState
{
    public required string Host { get; set; }

    public int Port { get; set; }

    public float Rating { get; set; }

    public int IntegrityPenaltiesApplied { get; set; }

    public int UnavailableBucketsSeen { get; set; }

    public DateTime? LastComplaintUtc { get; set; }

    public DateTime? RecoveryAnchorUtc { get; set; }

    public float? RatingAtRecoveryStart { get; set; }

    public List<ServerClaimRecord> Claims { get; set; } = [];

    public ServerEndpoint Endpoint => new(Host, Port);

    public ServerTrustState Clone()
    {
        return new ServerTrustState
        {
            Host = Host,
            Port = Port,
            Rating = Rating,
            IntegrityPenaltiesApplied = IntegrityPenaltiesApplied,
            UnavailableBucketsSeen = UnavailableBucketsSeen,
            LastComplaintUtc = LastComplaintUtc,
            RecoveryAnchorUtc = RecoveryAnchorUtc,
            RatingAtRecoveryStart = RatingAtRecoveryStart,
            Claims = Claims.Select(c => new ServerClaimRecord
            {
                ComplainantId = c.ComplainantId,
                Reason = c.Reason,
                Utc = c.Utc
            }).ToList()
        };
    }
}
