namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Host FreePowers snapshot (anonymous ServerTech).</summary>
public sealed class ServerFreePowersResponse
{
    /// <summary>Free capacity percent in [0, 100].</summary>
    public required double FreePowers { get; init; }

    public required DateTime MeasuredAtUtc { get; init; }
}
