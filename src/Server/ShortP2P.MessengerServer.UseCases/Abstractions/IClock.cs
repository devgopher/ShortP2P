namespace ShortP2P.MessengerServer.UseCases.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
}
