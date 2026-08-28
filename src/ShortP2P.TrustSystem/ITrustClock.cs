namespace ShortP2P.TrustSystem;

public interface ITrustClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemTrustClock : ITrustClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
