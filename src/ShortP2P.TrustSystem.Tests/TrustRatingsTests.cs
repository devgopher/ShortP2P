using ShortP2P.TrustSystem;

namespace ShortP2P.TrustSystem.Tests;

public class TrustRatingsTests
{
    [Fact]
    public void ArithmeticMean_AveragesAllSamples()
    {
        Assert.Equal(0.6f, TrustRatings.ArithmeticMean([0.5f, 0.7f]), precision: 4);
    }

    [Fact]
    public void AverageByEndpoint_GroupsHostPort()
    {
        var means = TrustRatings.AverageByEndpoint(
        [
            new RatedServer("10.0.0.2", 443, 0.8f),
            new RatedServer("10.0.0.2", 443, 0.4f),
            new RatedServer("10.0.0.9", 51111, 0.9f)
        ]);

        Assert.Equal(2, means.Count);
        Assert.Equal(0.6f, means[ServerEndpoint.Parse("10.0.0.2", 443)], 4);
        Assert.Equal(0.9f, means[ServerEndpoint.Parse("10.0.0.9", 51111)], 4);
    }
}
