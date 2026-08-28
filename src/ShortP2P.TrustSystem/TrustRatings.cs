namespace ShortP2P.TrustSystem;

public static class TrustRatings
{
    /// <summary>Lower trust floor for publishing, polling, and auto-adding servers.</summary>
    public const float Floor = 0.3f;

    public const float Default = 0.8f;

    public static float ArithmeticMean(IReadOnlyList<float> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
            throw new ArgumentException("At least one rating is required.", nameof(values));

        double sum = 0;
        for (var i = 0; i < values.Count; i++)
            sum += values[i];
        return (float)(sum / values.Count);
    }

    public static IReadOnlyDictionary<ServerEndpoint, float> AverageByEndpoint(
        IEnumerable<RatedServer> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var buckets = new Dictionary<ServerEndpoint, List<float>>();
        foreach (var sample in samples)
        {
            if (!ServerEndpoint.TryParse(sample.ServerIp, sample.ServerPort, out var endpoint, out _))
                continue;
            if (!buckets.TryGetValue(endpoint, out var list))
            {
                list = [];
                buckets[endpoint] = list;
            }

            list.Add(sample.Rating);
        }

        return buckets.ToDictionary(kv => kv.Key, kv => ArithmeticMean(kv.Value));
    }
}
