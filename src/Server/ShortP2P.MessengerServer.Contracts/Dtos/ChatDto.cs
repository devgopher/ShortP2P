namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Registered chat between subscribers.</summary>
public sealed class ChatDto
{
    public required string ChatId { get; init; }

    /// <summary>Participant short network ids.</summary>
    public required IReadOnlyList<string> NetworkIds { get; init; }

    /// <summary>Creation time UTC.</summary>
    public required DateTime CreatedAtUtc { get; init; }
}
