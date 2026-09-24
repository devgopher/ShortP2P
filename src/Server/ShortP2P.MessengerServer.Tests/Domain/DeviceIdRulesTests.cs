using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.Tests.Domain;

public class DeviceIdRulesTests
{
    [Theory]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg", false)]
    [InlineData("short", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_ChecksLowercaseHex64(string? value, bool expected) =>
        Assert.Equal(expected, DeviceIdRules.IsValid(value));

    [Fact]
    public void RequireValid_TrimsAndReturns()
    {
        var id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        Assert.Equal(id, DeviceIdRules.RequireValid($"  {id}  "));
    }

    [Fact]
    public void RequireValid_WhenInvalid_Throws()
    {
        Assert.Throws<ArgumentException>(() => DeviceIdRules.RequireValid("bad"));
    }
}

public class ServerHostPowersTests
{
    [Fact]
    public void CreateDefaults_UsesSingletonIdAndDefaultScores()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var powers = ServerHostPowers.CreateDefaults(now);

        Assert.Equal(ServerHostPowers.SingletonId, powers.Id);
        Assert.Equal(ServerHostPowers.DefaultTotalPower, powers.TotalPower);
        Assert.Equal(ServerHostPowers.DefaultFreePowers, powers.FreePowers);
        Assert.Equal(now, powers.TotalPowerMeasuredAtUtc);
        Assert.Equal(now, powers.FreePowersMeasuredAtUtc);
    }
}
