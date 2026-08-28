namespace ShortP2P.TrustSystem;

/// <summary>Why a subscriber reports another messenger server.</summary>
public enum ServerClaimReason
{
    UNAVAILABLE = 0,
    MALFUNCTIONED = 1,
    WRONGCERT = 2
}
