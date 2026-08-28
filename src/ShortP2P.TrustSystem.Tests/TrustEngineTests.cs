using ShortP2P.TrustSystem;

namespace ShortP2P.TrustSystem.Tests;

public class TrustEngineTests
{
    private static readonly DateTime T0 = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AskRating_AddsMissingServer_WithDefault08()
    {
        var engine = CreateEngine(out _);
        var list = await engine.AskRatingAsync("10.0.0.2", 443, subscriberCount: 100);

        var hit = Assert.Single(list);
        Assert.Equal("10.0.0.2", hit.ServerIp);
        Assert.Equal(443, hit.ServerPort);
        Assert.Equal(0.8f, hit.Rating);
    }

    [Fact]
    public async Task ClaimServer_RejectsSelf()
    {
        var engine = CreateEngine(out _, selfHost: "10.0.0.1", selfPort: 51111);
        var ex = await Assert.ThrowsAsync<TrustException>(() =>
            engine.ClaimServerAsync("10.0.0.1", 51111, ServerClaimReason.UNAVAILABLE, "alice", 10));
        Assert.Contains("itself", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Integrity_FirstFivePercent_Subtracts01()
    {
        var engine = CreateEngine(out _);
        await engine.AskRatingAsync("10.0.0.2", 443, 20);
        // 2 unique > 5% of 20 (1.0)
        await engine.ClaimServerAsync("10.0.0.2", 443, ServerClaimReason.WRONGCERT, "a", 20);
        await engine.ClaimServerAsync("10.0.0.2", 443, ServerClaimReason.MALFUNCTIONED, "b", 20);

        var rating = (await engine.AskRatingAsync("10.0.0.2", 443, 20)).Single().Rating;
        Assert.Equal(0.7f, rating, precision: 3);
    }

    [Fact]
    public async Task Integrity_SecondBucket_DecaysExponentially()
    {
        var engine = CreateEngine(out _);
        // 20 subs → buckets at unique >1, >2, ...
        await engine.ClaimServerAsync("10.0.0.2", 443, ServerClaimReason.WRONGCERT, "a", 20);
        await engine.ClaimServerAsync("10.0.0.2", 443, ServerClaimReason.WRONGCERT, "b", 20);
        await engine.ClaimServerAsync("10.0.0.2", 443, ServerClaimReason.WRONGCERT, "c", 20);

        var rating = (await engine.AskRatingAsync("10.0.0.2", 443, 20)).Single().Rating;
        // strike1: 0.8-0.1=0.7; strike2: 0.7*0.5=0.35
        Assert.Equal(0.35f, rating, precision: 3);
    }

    [Fact]
    public async Task Integrity_Below005_SnapsToZero()
    {
        var engine = CreateEngine(out _);
        for (var i = 0; i < 8; i++)
            await engine.ClaimServerAsync("10.0.0.2", 443, ServerClaimReason.MALFUNCTIONED, $"u{i}", 20);

        var rating = (await engine.AskRatingAsync("10.0.0.2", 443, 20)).Single().Rating;
        Assert.Equal(0f, rating);
    }

    [Fact]
    public async Task DuplicateClaim_SameSubscriberAndReason_DoesNotDoubleCount()
    {
        var engine = CreateEngine(out _);
        await engine.ClaimServerAsync("10.0.0.2", 443, ServerClaimReason.WRONGCERT, "a", 20);
        await engine.ClaimServerAsync("10.0.0.2", 443, ServerClaimReason.WRONGCERT, "a", 20);

        var rating = (await engine.AskRatingAsync("10.0.0.2", 443, 20)).Single().Rating;
        Assert.Equal(0.8f, rating, precision: 3);
    }

    [Fact]
    public async Task Unavailable_FivePercentInOneHour_Subtracts005()
    {
        var engine = CreateEngine(out var clock);
        await engine.ClaimServerAsync("10.0.0.2", 443, ServerClaimReason.UNAVAILABLE, "a", 20);
        await engine.ClaimServerAsync("10.0.0.2", 443, ServerClaimReason.UNAVAILABLE, "b", 20);

        var rating = (await engine.AskRatingAsync("10.0.0.2", 443, 20)).Single().Rating;
        Assert.Equal(0.75f, rating, precision: 3);

        clock.Advance(TimeSpan.FromHours(2));
        // window empty: no extra penalty; recovery not yet (needs 1h quiet then 6h)
        rating = (await engine.AskRatingAsync("10.0.0.2", 443, 20)).Single().Rating;
        Assert.True(rating >= 0.75f);
    }

    [Fact]
    public async Task Recovery_Reaches08_AfterQuietHourPlusSixHours()
    {
        var engine = CreateEngine(out var clock);
        await engine.ClaimServerAsync("10.0.0.2", 443, ServerClaimReason.UNAVAILABLE, "a", 20);
        await engine.ClaimServerAsync("10.0.0.2", 443, ServerClaimReason.UNAVAILABLE, "b", 20);
        Assert.Equal(0.75f, (await engine.AskRatingAsync("10.0.0.2", 443, 20)).Single().Rating, 3);

        clock.Advance(TimeSpan.FromHours(1));
        var mid = (await engine.AskRatingAsync("10.0.0.2", 443, 20)).Single().Rating;
        Assert.Equal(0.75f, mid, 3);

        clock.Advance(TimeSpan.FromHours(3)); // 3/6 of recovery
        var halfway = (await engine.AskRatingAsync("10.0.0.2", 443, 20)).Single().Rating;
        Assert.InRange(halfway, 0.77f, 0.78f);

        clock.Advance(TimeSpan.FromHours(3));
        var done = (await engine.AskRatingAsync("10.0.0.2", 443, 20)).Single().Rating;
        Assert.Equal(0.8f, done, 3);
    }

    [Fact]
    public async Task Recovery_AbortedByNewIntegrityClaim()
    {
        var engine = CreateEngine(out var clock);
        await engine.ClaimServerAsync("10.0.0.2", 443, ServerClaimReason.UNAVAILABLE, "a", 20);
        await engine.ClaimServerAsync("10.0.0.2", 443, ServerClaimReason.UNAVAILABLE, "b", 20);
        clock.Advance(TimeSpan.FromHours(4));
        var recovering = (await engine.AskRatingAsync("10.0.0.2", 443, 20)).Single().Rating;
        Assert.True(recovering > 0.75f);

        await engine.ClaimServerAsync("10.0.0.2", 443, ServerClaimReason.WRONGCERT, "c", 20);
        await engine.ClaimServerAsync("10.0.0.2", 443, ServerClaimReason.WRONGCERT, "d", 20);
        var after = (await engine.AskRatingAsync("10.0.0.2", 443, 20)).Single().Rating;
        Assert.True(after < recovering);
    }

    [Fact]
    public async Task AskServers_OmitsRatingsBelow03()
    {
        var store = new InMemoryTrustStore();
        var clock = new FakeTrustClock(T0);
        var engine = new TrustEngine(store, clock, new TrustOptions { SelfHost = "10.0.0.1", SelfPort = 51111 });

        await store.UpsertAsync(new ServerTrustState { Host = "10.0.0.8", Port = 443, Rating = 0.8f, LastComplaintUtc = T0 });
        await store.UpsertAsync(new ServerTrustState { Host = "10.0.0.3", Port = 443, Rating = 0.3f, LastComplaintUtc = T0 });
        await store.UpsertAsync(new ServerTrustState { Host = "10.0.0.2", Port = 443, Rating = 0.29f, LastComplaintUtc = T0 });
        await store.UpsertAsync(new ServerTrustState { Host = "10.0.0.0", Port = 443, Rating = 0f, LastComplaintUtc = T0 });

        var listed = await engine.AskServersAsync(subscriberCount: 10);
        Assert.Equal(2, listed.Count);
        Assert.Contains(listed, s => s.ServerIp == "10.0.0.8" && s.Rating == 0.8f);
        Assert.Contains(listed, s => s.ServerIp == "10.0.0.3" && s.Rating == 0.3f);
        Assert.DoesNotContain(listed, s => s.ServerIp == "10.0.0.2");
        Assert.DoesNotContain(listed, s => s.ServerIp == "10.0.0.0");
    }

    private static TrustEngine CreateEngine(out FakeTrustClock clock, string? selfHost = "10.0.0.1", int selfPort = 51111)
    {
        clock = new FakeTrustClock(T0);
        var options = new TrustOptions { SelfHost = selfHost, SelfPort = selfPort };
        return new TrustEngine(new InMemoryTrustStore(), clock, options);
    }
}
