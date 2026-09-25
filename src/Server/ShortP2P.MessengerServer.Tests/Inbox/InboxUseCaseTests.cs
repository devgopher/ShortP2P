using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.Tests.Fakes;
using ShortP2P.MessengerServer.UseCases;
using ShortP2P.MessengerServer.UseCases.Chats;
using ShortP2P.MessengerServer.UseCases.Inbox;
using ShortP2P.MessengerServer.UseCases.Messages;

namespace ShortP2P.MessengerServer.Tests.Inbox;

public class PollInboxEventsUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsPendingMessagesAndRequestsImmediately()
    {
        var h = new TestHarness();
        await h.Stores.StatusesRepo.UpsertAsync(new ClientStatuses
        {
            NetworkId = "bob",
            DeviceId = TestIds.DeviceB,
            Status = ClientOnlineStatus.Online,
            CreatedAtUtc = TestHarness.T0
        });

        await h.SendMessage().ExecuteAsync(new SendMessageCommand(
            TestHarness.NewMessage("m1", "alice", "bob")));
        await h.CreateChatRequest().ExecuteAsync(
            new CreateChatRequestCommand("alice", "pk", "bob"));

        var result = await h.PollInbox().ExecuteAsync(
            new PollInboxEventsQuery("bob", TestIds.DeviceB, TimeoutSeconds: 1));

        Assert.Single(result.Messages);
        Assert.Single(result.ChatRequests);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEmpty_TimesOutWithoutError()
    {
        var h = new TestHarness();
        var result = await h.PollInbox().ExecuteAsync(
            new PollInboxEventsQuery("bob", TestIds.DeviceB, TimeoutSeconds: 1));

        Assert.Empty(result.Messages);
        Assert.Empty(result.ChatRequests);
        Assert.Empty(result.Forwards);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTimeoutOutOfRange_ThrowsValidation()
    {
        var h = new TestHarness();
        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.PollInbox().ExecuteAsync(new PollInboxEventsQuery("bob", TestIds.DeviceB, 999)));
        Assert.Equal("Validation", ex.Code);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDeviceIdInvalid_ThrowsValidation()
    {
        var h = new TestHarness();
        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.PollInbox().ExecuteAsync(new PollInboxEventsQuery("bob", "bad", 1)));
        Assert.Equal("Validation", ex.Code);
    }
}

public class DeviceFanoutServiceTests
{
    [Fact]
    public async Task EnsureInboxForDeviceAsync_CopiesPendingMessagesAndRequests()
    {
        var h = new TestHarness();
        await h.SendMessage().ExecuteAsync(new SendMessageCommand(
            TestHarness.NewMessage("m1", "alice", "bob")));
        await h.CreateChatRequest().ExecuteAsync(
            new CreateChatRequestCommand("alice", "pk", "bob"));

        await h.Fanout.EnsureInboxForDeviceAsync("bob", TestIds.DeviceB);

        Assert.True(h.Stores.MessageInboxes.ContainsKey(("m1", TestIds.DeviceB)));
        Assert.Single(h.Stores.ChatRequestInboxes.Keys.Where(k => k.DeviceId == TestIds.DeviceB));
    }

    [Fact]
    public async Task EnsureInboxForDeviceAsync_SkipsItemsOlderThanRetention()
    {
        var h = new TestHarness();
        h.InboxOptions.MessageRetention = TimeSpan.FromMinutes(1);
        var old = TestHarness.T0.AddHours(-2);
        h.Stores.Messages["m-old"] = TestHarness.NewMessage("m-old", "alice", "bob", old);
        await h.Stores.MessageCache.AddAsync(TestHarness.NewMessage("m-old", "alice", "bob", old));
        h.Stores.ChatRequests["r-old"] = new ChatRequest
        {
            RequestId = "r-old",
            RequesterNetworkId = "alice",
            TargetNetworkId = "bob",
            PublicKey = "pk",
            CreatedAtUtc = old
        };

        await h.Fanout.EnsureInboxForDeviceAsync("bob", TestIds.DeviceB);

        Assert.False(h.Stores.MessageInboxes.ContainsKey(("m-old", TestIds.DeviceB)));
        Assert.False(h.Stores.ChatRequestInboxes.ContainsKey(("r-old", TestIds.DeviceB)));
    }
}
