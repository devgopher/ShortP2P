using ShortP2P.MessengerServer.Tests.Fakes;
using ShortP2P.MessengerServer.UseCases;
using ShortP2P.MessengerServer.UseCases.Auth;
using ShortP2P.MessengerServer.UseCases.Trust;
using ShortP2P.TrustSystem;

namespace ShortP2P.MessengerServer.Tests.Trust;

public class TrustUseCaseTests
{
    private static readonly DateTime T0 = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AskRating_PassesSubscriberCountFromAccounts()
    {
        var h = new TestHarness();
        await h.RegisterClient().ExecuteAsync(new RegisterClientCommand("A", "a", "pw", TestIds.DeviceA));
        await h.RegisterClient().ExecuteAsync(new RegisterClientCommand("B", "b", "pw", TestIds.DeviceB));

        var engine = CreateEngine();
        var useCase = new AskRatingUseCase(engine, h.Stores.AccountsRepo);

        var list = await useCase.ExecuteAsync("10.0.0.2", 443);
        Assert.Single(list);
        Assert.Equal(0.8f, list[0].Rating);
    }

    [Fact]
    public async Task AskServers_ReturnsKnownServersAboveThreshold()
    {
        var engine = CreateEngine(out var store);
        await store.UpsertAsync(new ServerTrustState
        {
            Host = "10.0.0.8",
            Port = 443,
            Rating = 0.8f,
            LastComplaintUtc = T0
        });

        var h = new TestHarness();
        var list = await new AskServersUseCase(engine, h.Stores.AccountsRepo).ExecuteAsync();
        Assert.Contains(list, s => s.ServerIp == "10.0.0.8");
    }

    [Fact]
    public async Task ClaimServer_WhenCallerMissing_ThrowsUnauthorized()
    {
        var engine = CreateEngine();
        var h = new TestHarness();
        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            new ClaimServerUseCase(engine, h.Stores.AccountsRepo).ExecuteAsync(
                "", "10.0.0.2", 443, ServerClaimReason.WRONGCERT));
        Assert.Equal("Unauthorized", ex.Code);
    }

    [Fact]
    public async Task ClaimServer_MapsTrustExceptionToValidation()
    {
        var engine = CreateEngine(out _, selfHost: "10.0.0.1", selfPort: 51111);
        var h = new TestHarness();

        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            new ClaimServerUseCase(engine, h.Stores.AccountsRepo).ExecuteAsync(
                "alice", "10.0.0.1", 51111, ServerClaimReason.UNAVAILABLE));
        Assert.Equal("Validation", ex.Code);
    }

    [Fact]
    public async Task CountSubscribers_CountsDistinctNetworkIds()
    {
        var h = new TestHarness();
        await h.RegisterClient().ExecuteAsync(new RegisterClientCommand("A", "a", "pw", TestIds.DeviceA));
        await h.RegisterClient().ExecuteAsync(new RegisterClientCommand("B", "b", "pw", TestIds.DeviceB));

        var count = await AskRatingUseCase.CountSubscribersAsync(h.Stores.AccountsRepo, CancellationToken.None);
        Assert.Equal(2, count);
    }

    private static TrustEngine CreateEngine() => CreateEngine(out _);

    private static TrustEngine CreateEngine(
        out InMemoryTrustStore store,
        string? selfHost = "10.0.0.1",
        int selfPort = 51111)
    {
        store = new InMemoryTrustStore();
        var clock = new FakeTrustClock(T0);
        var options = new TrustOptions { SelfHost = selfHost, SelfPort = selfPort };
        return new TrustEngine(store, clock, options);
    }

    private sealed class FakeTrustClock : ITrustClock
    {
        public FakeTrustClock(DateTime utcNow) => UtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        public DateTime UtcNow { get; set; }
    }
}
