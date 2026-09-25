using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.Tests.Fakes;
using ShortP2P.MessengerServer.UseCases.Abstractions;
using ShortP2P.MessengerServer.UseCases.Forwards;
using ShortP2P.MessengerServer.UseCases.Hosting;

namespace ShortP2P.MessengerServer.Tests.Hosting;

public class InMemoryForwardHubTests
{
    [Fact]
    public void TryAdd_TakeForTarget_ConsumesEntries()
    {
        var hub = new InMemoryForwardHub();
        var envelope = new ForwardEnvelope(
            "f1", "alice", "bob", ForwardKind.PeerProfileRequest, "YQ==", TestHarness.T0);

        Assert.True(hub.TryAdd(envelope));
        Assert.False(hub.TryAdd(envelope));

        var taken = hub.TakeForTarget("bob", TestHarness.T0);
        Assert.Single(taken);
        Assert.Empty(hub.TakeForTarget("bob", TestHarness.T0));
    }

    [Fact]
    public void TakeForTarget_SkipsExpiredEntries()
    {
        var hub = new InMemoryForwardHub();
        hub.TryAdd(new ForwardEnvelope(
            "f1", "alice", "bob", ForwardKind.PeerProfileRequest, "YQ==", TestHarness.T0));

        var later = TestHarness.T0.Add(ForwardPayloadLimits.ForwardTtl).AddSeconds(1);
        Assert.Empty(hub.TakeForTarget("bob", later));
    }

    [Fact]
    public void PurgeExpired_RemovesOldEntries()
    {
        var hub = new InMemoryForwardHub();
        hub.TryAdd(new ForwardEnvelope(
            "f1", "alice", "bob", ForwardKind.PeerProfileRequest, "YQ==", TestHarness.T0));

        var later = TestHarness.T0.Add(ForwardPayloadLimits.ForwardTtl).AddSeconds(1);
        hub.PurgeExpired(later);
        Assert.Empty(hub.TakeForTarget("bob", later));
    }
}

public class InboxWaitServiceTests
{
    [Fact]
    public async Task Notify_WakesWaitingPoll()
    {
        var wait = new InboxWaitService();
        var waiting = wait.WaitAsync("bob", TestIds.DeviceB, TimeSpan.FromSeconds(5));

        await Task.Delay(50);
        wait.Notify("bob");

        await waiting; // should complete without timeout exception
    }

    [Fact]
    public async Task WaitAsync_WhenTimeout_CompletesNormally()
    {
        var wait = new InboxWaitService();
        await wait.WaitAsync("bob", TestIds.DeviceB, TimeSpan.FromMilliseconds(30));
    }
}
