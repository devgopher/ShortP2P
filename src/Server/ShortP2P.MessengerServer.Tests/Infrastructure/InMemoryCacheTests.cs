using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.Infrastructure.Caching;
using ShortP2P.MessengerServer.Tests.Fakes;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Tests.Infrastructure;

public class InMemoryMessageCacheTests
{
    [Fact]
    public async Task AddFindRemove_RoundTrip()
    {
        var clock = new FakeClock(TestHarness.T0);
        var tracker = new InMemoryCacheMemoryTracker(new InMemoryMessengerCacheOptions());
        var cache = new InMemoryMessageCache(tracker, clock);
        var message = TestHarness.NewMessage("m1", "a", "b");

        await cache.AddAsync(message);
        Assert.Equal("m1", (await cache.FindByIdAsync("m1"))!.MessageId);
        Assert.Single(await cache.ListByTargetNetworkIdAsync("b"));

        await cache.RemoveByIdsAsync(["m1"]);
        Assert.Null(await cache.FindByIdAsync("m1"));
    }

    [Fact]
    public async Task Add_WhenDuplicate_IsIdempotent()
    {
        var clock = new FakeClock(TestHarness.T0);
        var tracker = new InMemoryCacheMemoryTracker(new InMemoryMessengerCacheOptions());
        var cache = new InMemoryMessageCache(tracker, clock);
        var message = TestHarness.NewMessage("m1", "a", "b");

        await cache.AddAsync(message);
        await cache.AddAsync(message);
        Assert.Single(await cache.ListByTargetNetworkIdAsync("b"));
    }

    [Fact]
    public async Task Add_WhenMemoryLimitExceeded_Throws()
    {
        var clock = new FakeClock(TestHarness.T0);
        var tracker = new InMemoryCacheMemoryTracker(new InMemoryMessengerCacheOptions { MaxMemoryMegabytes = 1 });
        // Fill almost all of 1 MiB with reserved bytes.
        Assert.True(tracker.TryReserve(1024 * 1024 - 8));
        var cache = new InMemoryMessageCache(tracker, clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.AddAsync(TestHarness.NewMessage("m1", "a", "b", ciphertext: new string('x', 64))));
    }

    [Fact]
    public async Task ListExpired_ReturnsOlderEntries()
    {
        var clock = new FakeClock(TestHarness.T0);
        var tracker = new InMemoryCacheMemoryTracker(new InMemoryMessengerCacheOptions());
        var cache = new InMemoryMessageCache(tracker, clock);
        await cache.AddAsync(TestHarness.NewMessage("m1", "a", "b"));

        clock.Advance(TimeSpan.FromMinutes(2));
        var expired = await cache.ListExpiredAsync(TestHarness.T0.AddMinutes(1));
        Assert.Single(expired);
    }
}

public class InMemoryDeliveryTicketCacheTests
{
    [Fact]
    public async Task AddListRemove_RoundTrip()
    {
        var clock = new FakeClock(TestHarness.T0);
        var tracker = new InMemoryCacheMemoryTracker(new InMemoryMessengerCacheOptions());
        var cache = new InMemoryDeliveryTicketCache(tracker, clock);
        var entry = new CachedDeliveryTicket(
            new DeliveryTicket { MessageId = "m1", ReceivedAtUtc = TestHarness.T0 },
            "alice");

        await cache.AddAsync(entry);
        Assert.Single(await cache.ListForSourceNetworkIdAsync("alice"));
        await cache.RemoveByMessageIdsAsync(["m1"]);
        Assert.Empty(await cache.ListForSourceNetworkIdAsync("alice"));
    }
}

public class InMemoryMessageInboxCacheTests
{
    [Fact]
    public async Task AddExistsCountRemove_Work()
    {
        var tracker = new InMemoryCacheMemoryTracker(new InMemoryMessengerCacheOptions());
        var cache = new InMemoryMessageInboxCache(tracker);
        var entry = new MessageInboxEntry
        {
            MessageId = "m1",
            TgtNetworkId = "bob",
            DeviceId = TestIds.DeviceB
        };

        await cache.AddAsync(entry);
        Assert.True(await cache.ExistsAsync("m1", TestIds.DeviceB));
        Assert.Equal(["m1"], await cache.ListMessageIdsForDeviceAsync("bob", TestIds.DeviceB));
        Assert.Equal(1, await cache.CountForMessageAsync("m1"));

        await cache.RemoveAsync("m1", TestIds.DeviceB);
        Assert.Equal(0, await cache.CountForMessageAsync("m1"));
    }
}

public class InMemoryCacheMemoryTrackerTests
{
    [Fact]
    public void Unlimited_AlwaysAllowsWrites()
    {
        var tracker = new InMemoryCacheMemoryTracker(new InMemoryMessengerCacheOptions());
        Assert.True(tracker.IsWriteAvailable);
        Assert.True(tracker.TryReserve(10_000_000));
        Assert.True(tracker.IsWriteAvailable);
    }

    [Fact]
    public void Limited_RejectsWhenFull()
    {
        var tracker = new InMemoryCacheMemoryTracker(new InMemoryMessengerCacheOptions { MaxMemoryMegabytes = 1 });
        Assert.True(tracker.TryReserve(1024 * 1024));
        Assert.False(tracker.IsWriteAvailable);
        Assert.False(tracker.TryReserve(1));

        tracker.Release(100);
        Assert.True(tracker.IsWriteAvailable);
        Assert.True(tracker.TryReserve(50));
    }
}
