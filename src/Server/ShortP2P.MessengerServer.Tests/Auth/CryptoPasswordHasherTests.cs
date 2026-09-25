using ShortP2P.MessengerServer.Auth;

namespace ShortP2P.MessengerServer.Tests.Auth;

public class CryptoPasswordHasherTests
{
    [Fact]
    public void HashAndVerify_RoundTrips()
    {
        var hasher = new CryptoPasswordHasher();
        var result = hasher.Hash("hunter2");

        Assert.False(string.IsNullOrWhiteSpace(result.Salt));
        Assert.False(string.IsNullOrWhiteSpace(result.Hash));
        Assert.True(hasher.Verify("hunter2", result.Salt, result.Hash));
        Assert.False(hasher.Verify("wrong", result.Salt, result.Hash));
    }
}