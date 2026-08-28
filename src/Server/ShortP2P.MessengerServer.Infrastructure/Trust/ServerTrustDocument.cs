using ShortP2P.TrustSystem;

namespace ShortP2P.MessengerServer.Infrastructure.Trust;

public sealed class ServerTrustDocument
{
    public string Id { get; set; } = "";

    public string Host { get; set; } = "";

    public int Port { get; set; }

    public float Rating { get; set; }

    public int IntegrityPenaltiesApplied { get; set; }

    public int UnavailableBucketsSeen { get; set; }

    public DateTime? LastComplaintUtc { get; set; }

    public DateTime? RecoveryAnchorUtc { get; set; }

    public float? RatingAtRecoveryStart { get; set; }

    public List<ServerClaimDocument> Claims { get; set; } = [];

    public static ServerTrustDocument FromDomain(ServerTrustState state)
    {
        var endpoint = ServerEndpoint.Parse(state.Host, state.Port);
        return new ServerTrustDocument
        {
            Id = endpoint.Key,
            Host = endpoint.Host,
            Port = endpoint.Port,
            Rating = state.Rating,
            IntegrityPenaltiesApplied = state.IntegrityPenaltiesApplied,
            UnavailableBucketsSeen = state.UnavailableBucketsSeen,
            LastComplaintUtc = state.LastComplaintUtc,
            RecoveryAnchorUtc = state.RecoveryAnchorUtc,
            RatingAtRecoveryStart = state.RatingAtRecoveryStart,
            Claims = state.Claims.Select(c => new ServerClaimDocument
            {
                ComplainantId = c.ComplainantId,
                Reason = c.Reason.ToString(),
                Utc = c.Utc
            }).ToList()
        };
    }

    public ServerTrustState ToDomain()
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
                Reason = Enum.TryParse<ServerClaimReason>(c.Reason, ignoreCase: true, out var reason)
                    ? reason
                    : ServerClaimReason.UNAVAILABLE,
                Utc = c.Utc
            }).ToList()
        };
    }
}

public sealed class ServerClaimDocument
{
    public string ComplainantId { get; set; } = "";

    public string Reason { get; set; } = "";

    public DateTime Utc { get; set; }
}
