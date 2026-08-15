namespace ShortP2P.Client.Services.MessengerServers;

/// <summary>Live ranking metrics for a messenger server (in-memory, per client session).</summary>
public sealed class MessengerServerRankStats
{
    /// <summary>Consecutive failed HTTP calls of any kind; reset on success.</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>Last successful KeepAlive round-trip, milliseconds. Null if never measured.</summary>
    public long? LastKeepAliveRttMs { get; set; }

    public DateTime? LastSuccessUtc { get; set; }

    public DateTime? LastFailureUtc { get; set; }

    /// <summary>True when the last tracked request succeeded (or no failures yet).</summary>
    public bool IsAvailable => ConsecutiveFailures == 0;
}

/// <summary>
/// Rank order: available first (fewer consecutive failures), then lower KeepAlive RTT.
/// </summary>
public static class MessengerServerRankComparer
{
    public static int Compare(MessengerServerRankStats? a, MessengerServerRankStats? b)
    {
        a ??= new MessengerServerRankStats();
        b ??= new MessengerServerRankStats();

        var failCmp = a.ConsecutiveFailures.CompareTo(b.ConsecutiveFailures);
        if (failCmp != 0)
            return failCmp;

        var rttA = a.LastKeepAliveRttMs ?? long.MaxValue;
        var rttB = b.LastKeepAliveRttMs ?? long.MaxValue;
        return rttA.CompareTo(rttB);
    }
}
