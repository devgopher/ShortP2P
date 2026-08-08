namespace ShortP2P.MessengerServer.Domain;

/// <summary>Registered chat between subscribers.</summary>
public sealed class Chat
{
    public required string ChatId { get; init; }

    /// <summary>Participant short network ids.</summary>
    public required IReadOnlyList<string> NetworkIds { get; init; }

    /// <summary>Creation time UTC.</summary>
    public required DateTime CreatedAtUtc { get; init; }
}
