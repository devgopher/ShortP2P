namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Standard API error body.</summary>
public sealed class ApiError
{
    public required string Code { get; init; }

    public required string Message { get; init; }
}
