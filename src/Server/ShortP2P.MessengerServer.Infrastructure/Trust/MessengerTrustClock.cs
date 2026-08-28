using ShortP2P.MessengerServer.UseCases.Abstractions;
using ShortP2P.TrustSystem;

namespace ShortP2P.MessengerServer.Infrastructure.Trust;

internal sealed class MessengerTrustClock(IClock clock) : ITrustClock
{
    public DateTime UtcNow => clock.UtcNow;
}
