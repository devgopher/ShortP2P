using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Tests.Fakes;

internal sealed class FakeAuthTokenService(IClock clock) : IAuthTokenService
{
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(1);

    public AuthToken IssueToken(string networkId, string deviceId) =>
        new($"tok:{networkId}:{deviceId}", clock.UtcNow.Add(Lifetime));
}
