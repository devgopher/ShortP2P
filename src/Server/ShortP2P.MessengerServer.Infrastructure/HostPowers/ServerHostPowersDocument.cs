using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.Infrastructure.HostPowers;

internal sealed class ServerHostPowersDocument
{
    public string Id { get; set; } = ServerHostPowers.SingletonId;
    public double TotalPower { get; set; } = ServerHostPowers.DefaultTotalPower;
    public double FreePowers { get; set; } = ServerHostPowers.DefaultFreePowers;
    public DateTime TotalPowerMeasuredAtUtc { get; set; }
    public DateTime FreePowersMeasuredAtUtc { get; set; }

    public ServerHostPowers ToDomain() => new()
    {
        Id = Id,
        TotalPower = TotalPower,
        FreePowers = FreePowers,
        TotalPowerMeasuredAtUtc = DateTime.SpecifyKind(TotalPowerMeasuredAtUtc, DateTimeKind.Utc),
        FreePowersMeasuredAtUtc = DateTime.SpecifyKind(FreePowersMeasuredAtUtc, DateTimeKind.Utc)
    };

    public static ServerHostPowersDocument FromDomain(ServerHostPowers powers) => new()
    {
        Id = powers.Id,
        TotalPower = powers.TotalPower,
        FreePowers = powers.FreePowers,
        TotalPowerMeasuredAtUtc = powers.TotalPowerMeasuredAtUtc,
        FreePowersMeasuredAtUtc = powers.FreePowersMeasuredAtUtc
    };
}
