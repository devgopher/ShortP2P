namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Host TotalPower snapshot (anonymous ServerTech).</summary>
public sealed class ServerPowerResponse
{
    /// <summary>Hardware power score in [1, 100].</summary>
    public required double TotalPower { get; init; }

    public required DateTime MeasuredAtUtc { get; init; }
}
