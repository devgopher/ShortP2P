namespace ShortP2P.TrustSystem.Tests;

internal sealed class FakeTrustClock : ITrustClock
{
    public FakeTrustClock(DateTime utcNow) => UtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

    public DateTime UtcNow { get; set; }

    public void Advance(TimeSpan span) => UtcNow = UtcNow.Add(span);
}
