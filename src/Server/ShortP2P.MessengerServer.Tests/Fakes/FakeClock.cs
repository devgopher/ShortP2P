using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Tests.Fakes;

internal sealed class FakeClock : IClock
{
    public FakeClock(DateTime utcNow) => UtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

    public DateTime UtcNow { get; set; }

    public void Advance(TimeSpan span) => UtcNow = UtcNow.Add(span);
}
