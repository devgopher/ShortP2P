using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.Tests.Fakes;
using ShortP2P.MessengerServer.UseCases;
using ShortP2P.MessengerServer.UseCases.Auth;

namespace ShortP2P.MessengerServer.Tests.Auth;

public class RegisterClientUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_WhenValid_CreatesAccountAndOfflineStatus()
    {
        var h = new TestHarness();
        await h.RegisterClient().ExecuteAsync(new RegisterClientCommand("Alice", "net-a", "secret", TestIds.DeviceA));

        var account = Assert.Single(h.Stores.Accounts.Values);
        Assert.Equal("Alice", account.Nick);
        Assert.Equal("net-a", account.NetworkId);
        Assert.Equal(TestHarness.T0, account.CreatedAtUtc);

        var status = Assert.Single(h.Stores.Statuses.Values);
        Assert.Equal(ClientOnlineStatus.Offline, status.Status);
        Assert.Equal(TestIds.DeviceA, status.DeviceId);
    }

    [Theory]
    [InlineData("", "net", "pw", TestIds.DeviceA)]
    [InlineData("nick", "", "pw", TestIds.DeviceA)]
    [InlineData("nick", "net", "", TestIds.DeviceA)]
    [InlineData("nick", "net", "pw", "short")]
    public async Task ExecuteAsync_WhenInvalidInput_ThrowsValidation(
        string nick, string networkId, string password, string deviceId)
    {
        var h = new TestHarness();
        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.RegisterClient().ExecuteAsync(new RegisterClientCommand(nick, networkId, password, deviceId)));
        Assert.Equal("Validation", ex.Code);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNetworkIdTaken_ThrowsConflict()
    {
        var h = new TestHarness();
        await h.RegisterClient().ExecuteAsync(new RegisterClientCommand("Alice", "net-a", "secret", TestIds.DeviceA));

        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.RegisterClient().ExecuteAsync(new RegisterClientCommand("Bob", "net-a", "other", TestIds.DeviceB)));
        Assert.Equal("Conflict", ex.Code);
        Assert.Contains("NetworkId", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNickTaken_ThrowsConflict()
    {
        var h = new TestHarness();
        await h.RegisterClient().ExecuteAsync(new RegisterClientCommand("Alice", "net-a", "secret", TestIds.DeviceA));

        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.RegisterClient().ExecuteAsync(new RegisterClientCommand("Alice", "net-b", "other", TestIds.DeviceB)));
        Assert.Equal("Conflict", ex.Code);
        Assert.Contains("Nick", ex.Message);
    }
}
