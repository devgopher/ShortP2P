using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.Tests.Fakes;
using ShortP2P.MessengerServer.UseCases;
using ShortP2P.MessengerServer.UseCases.Auth;

namespace ShortP2P.MessengerServer.Tests.Auth;

public class LoginClientUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_WhenCredentialsValid_ReturnsTokenAndMarksOnline()
    {
        var h = new TestHarness();
        await h.RegisterClient().ExecuteAsync(new RegisterClientCommand("Alice", "net-a", "secret", TestIds.DeviceA));

        var result = await h.LoginClient().ExecuteAsync(
            new LoginClientCommand("net-a", "secret", TestIds.DeviceB));

        Assert.Equal("tok:net-a:" + TestIds.DeviceB, result.Token);
        Assert.Equal(TestHarness.T0.AddHours(1), result.ExpiresAtUtc);

        var status = h.Stores.Statuses[( "net-a", TestIds.DeviceB )];
        Assert.Equal(ClientOnlineStatus.Online, status.Status);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPasswordWrong_ThrowsUnauthorized()
    {
        var h = new TestHarness();
        await h.RegisterClient().ExecuteAsync(new RegisterClientCommand("Alice", "net-a", "secret", TestIds.DeviceA));

        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.LoginClient().ExecuteAsync(new LoginClientCommand("net-a", "wrong", TestIds.DeviceA)));
        Assert.Equal("Unauthorized", ex.Code);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAccountMissing_ThrowsUnauthorized()
    {
        var h = new TestHarness();
        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.LoginClient().ExecuteAsync(new LoginClientCommand("missing", "secret", TestIds.DeviceA)));
        Assert.Equal("Unauthorized", ex.Code);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDeviceIdInvalid_ThrowsValidation()
    {
        var h = new TestHarness();
        await h.RegisterClient().ExecuteAsync(new RegisterClientCommand("Alice", "net-a", "secret", TestIds.DeviceA));

        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.LoginClient().ExecuteAsync(new LoginClientCommand("net-a", "secret", "bad")));
        Assert.Equal("Validation", ex.Code);
    }
}
