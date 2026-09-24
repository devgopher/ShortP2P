using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.Tests.Fakes;
using ShortP2P.MessengerServer.UseCases.Auth;

namespace ShortP2P.MessengerServer.Tests.Presence;

public class GetClientPresencesUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_MarksOnlineWhenWithinTimeout()
    {
        var h = new TestHarness();
        await h.RegisterClient().ExecuteAsync(new RegisterClientCommand("Alice", "alice", "pw", TestIds.DeviceA));
        await h.RegisterClient().ExecuteAsync(new RegisterClientCommand("Bob", "bob", "pw", TestIds.DeviceB));

        await h.Stores.StatusesRepo.UpsertAsync(new ClientStatuses
        {
            NetworkId = "alice",
            DeviceId = TestIds.DeviceA,
            Status = ClientOnlineStatus.Online,
            CreatedAtUtc = TestHarness.T0
        });
        await h.Stores.StatusesRepo.UpsertAsync(new ClientStatuses
        {
            NetworkId = "bob",
            DeviceId = TestIds.DeviceB,
            Status = ClientOnlineStatus.Online,
            CreatedAtUtc = TestHarness.T0.AddMinutes(-10)
        });

        h.Clock.UtcNow = TestHarness.T0.AddSeconds(20);
        var list = await h.GetClientPresences().ExecuteAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal(ClientOnlineStatus.Online, list.Single(p => p.NetworkId == "alice").Status);
        Assert.Equal(ClientOnlineStatus.Offline, list.Single(p => p.NetworkId == "bob").Status);
    }

    [Fact]
    public async Task ExecuteAsync_OrdersByNickThenNetworkId()
    {
        var h = new TestHarness();
        await h.RegisterClient().ExecuteAsync(new RegisterClientCommand("Zoe", "z", "pw", TestIds.DeviceA));
        await h.RegisterClient().ExecuteAsync(new RegisterClientCommand("Amy", "a", "pw", TestIds.DeviceB));

        var list = await h.GetClientPresences().ExecuteAsync();
        Assert.Equal(["a", "z"], list.Select(p => p.NetworkId).ToArray());
    }
}
