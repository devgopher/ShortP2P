namespace ShortP2P.MessengerServer.Domain;

/// <summary>Persisted host power metrics (LiteDB singleton document).</summary>
public sealed class ServerHostPowers
{
    public const string SingletonId = "host";

    public const double DefaultTotalPower = 10.0;

    public const double DefaultFreePowers = 10.0;

    public required string Id { get; init; }

    /// <summary>Hardware score in [1, 100].</summary>
    public required double TotalPower { get; init; }

    /// <summary>Free capacity percent in [0, 100].</summary>
    public required double FreePowers { get; init; }

    public required DateTime TotalPowerMeasuredAtUtc { get; init; }

    public required DateTime FreePowersMeasuredAtUtc { get; init; }

    public static ServerHostPowers CreateDefaults(DateTime utcNow) => new()
    {
        Id = SingletonId,
        TotalPower = DefaultTotalPower,
        FreePowers = DefaultFreePowers,
        TotalPowerMeasuredAtUtc = utcNow,
        FreePowersMeasuredAtUtc = utcNow
    };
}
