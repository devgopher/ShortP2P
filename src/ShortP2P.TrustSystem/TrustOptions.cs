namespace ShortP2P.TrustSystem;

public sealed class TrustOptions
{
    public const string Section = "Trust";

    /// <summary>This host's advertised IP/hostname; claims against it are rejected.</summary>
    public string? SelfHost { get; set; }

    /// <summary>This host's advertised port; 0 means self-check uses only <see cref="SelfHost"/> match plus <see cref="SelfPort"/>.</summary>
    public int SelfPort { get; set; }

    public float DefaultRating { get; set; } = 0.8f;

    /// <summary>Share of local subscribers that triggers a penalty bucket (5%).</summary>
    public float ComplaintShareThreshold { get; set; } = 0.05f;

    public float IntegrityFirstPenalty { get; set; } = 0.1f;

    /// <summary>Multiplier applied to rating on each subsequent integrity strike.</summary>
    public float IntegrityExponentialFactor { get; set; } = 0.5f;

    /// <summary>Ratings below this after a penalty snap to 0.</summary>
    public float CollapseBelow { get; set; } = 0.05f;

    public float UnavailablePenalty { get; set; } = 0.05f;

    public TimeSpan UnavailableWindow { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan QuietBeforeRecovery { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan RecoveryDuration { get; set; } = TimeSpan.FromHours(6);

    public float RecoveryTarget { get; set; } = 0.8f;

    /// <summary>Minimum rating included in <c>AskServers</c> (servers below this are omitted).</summary>
    public float MinPublishRating { get; set; } = 0.3f;
}
