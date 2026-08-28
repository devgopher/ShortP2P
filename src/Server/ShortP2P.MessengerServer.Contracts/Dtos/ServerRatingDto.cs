namespace ShortP2P.MessengerServer.Contracts.Dtos;

public sealed class ServerRatingDto
{
    public required string ServerIp { get; init; }

    public required int ServerPort { get; init; }

    public required float Rating { get; init; }
}
