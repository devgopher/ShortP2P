namespace ShortP2P.Client.Services.MessengerServers;

/// <summary>Live anonymous ServerTech snapshot for UI (null = unavailable).</summary>
public sealed class MessengerServerTechMetrics
{
    public required int ServerId { get; init; }

    /// <summary>TotalPower score in [1, 100].</summary>
    public double? TotalPower { get; init; }

    /// <summary>FreePowers percent in [0, 100].</summary>
    public double? FreePowers { get; init; }

    /// <summary>Client-measured <c>/server-tech/ping</c> RTT in milliseconds.</summary>
    public long? PingRttMs { get; init; }
}
