using ShortP2P.MessengerServer.Tests.Fakes;
using ShortP2P.MessengerServer.UseCases;
using ShortP2P.MessengerServer.UseCases.Auth;
using ShortP2P.MessengerServer.UseCases.Chats;

namespace ShortP2P.MessengerServer.Tests.Chats;

public class CreateChatRequestUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_WhenNewPair_CreatesChatRequestAndCryptoKeys()
    {
        var h = new TestHarness();
        await h.RegisterClient().ExecuteAsync(new RegisterClientCommand("Alice", "alice", "pw", TestIds.DeviceA));
        await h.RegisterClient().ExecuteAsync(new RegisterClientCommand("Bob", "bob", "pw", TestIds.DeviceB));
        await h.Stores.StatusesRepo.UpsertAsync(new ShortP2P.MessengerServer.Domain.ClientStatuses
        {
            NetworkId = "bob",
            DeviceId = TestIds.DeviceB,
            Status = ShortP2P.MessengerServer.Domain.ClientOnlineStatus.Online,
            CreatedAtUtc = TestHarness.T0
        });

        await h.CreateChatRequest().ExecuteAsync(
            new CreateChatRequestCommand("alice", "pubkey", "bob"));

        Assert.Single(h.Stores.Chats);
        Assert.Single(h.Stores.ChatRequests);
        Assert.True(h.Stores.CryptoKeys.ContainsKey(("alice", "bob")));
        Assert.True(h.Stores.ChatRequestInboxes.ContainsKey(
            (h.Stores.ChatRequests.Keys.Single(), TestIds.DeviceB)));
    }

    [Fact]
    public async Task ExecuteAsync_WhenChatExists_DoesNotDuplicateChat()
    {
        var h = new TestHarness();
        await h.CreateChatRequest().ExecuteAsync(
            new CreateChatRequestCommand("alice", "pk1", "bob"));
        await h.CreateChatRequest().ExecuteAsync(
            new CreateChatRequestCommand("alice", "pk2", "bob"));

        Assert.Single(h.Stores.Chats);
        Assert.Equal(2, h.Stores.ChatRequests.Count);
        Assert.Equal("pk2", h.Stores.CryptoKeys[("alice", "bob")].PublicKey);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSelfTarget_ThrowsValidation()
    {
        var h = new TestHarness();
        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.CreateChatRequest().ExecuteAsync(
                new CreateChatRequestCommand("alice", "pk", "alice")));
        Assert.Equal("Validation", ex.Code);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMissingFields_ThrowsValidation()
    {
        var h = new TestHarness();
        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.CreateChatRequest().ExecuteAsync(
                new CreateChatRequestCommand("", "pk", "bob")));
        Assert.Equal("Validation", ex.Code);
    }
}

public class GetChatsUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsChatsForNetwork()
    {
        var h = new TestHarness();
        await h.CreateChatRequest().ExecuteAsync(
            new CreateChatRequestCommand("alice", "pk", "bob"));
        await h.CreateChatRequest().ExecuteAsync(
            new CreateChatRequestCommand("carol", "pk", "dave"));

        var chats = await h.GetChats().ExecuteAsync(new GetChatsQuery("alice"));
        Assert.Single(chats);
        Assert.Contains("alice", chats[0].NetworkIds);
        Assert.Contains("bob", chats[0].NetworkIds);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNetworkIdMissing_ThrowsValidation()
    {
        var h = new TestHarness();
        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.GetChats().ExecuteAsync(new GetChatsQuery("")));
        Assert.Equal("Validation", ex.Code);
    }
}
