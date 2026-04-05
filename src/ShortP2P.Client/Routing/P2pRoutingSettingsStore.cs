using ShortP2P.Client;

namespace ShortP2P.Client.Routing;

/// <summary>Хранит настройки маршрутизации в <see cref="ISessionStorage"/>.</summary>
public sealed class P2pRoutingSettingsStore(ISessionStorage storage)
{
    private const string KMaxHops = "p2p_route_max_hops";
    private const string KAttempts = "p2p_send_search_attempts";
    private const string KDelayMs = "p2p_send_retry_delay_ms";
    private const string KSearchTimeoutMs = "p2p_search_timeout_ms";

    private readonly ISessionStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));

    public async Task<P2pRoutingSettings> LoadAsync()
    {
        var s = new P2pRoutingSettings();
        if (int.TryParse(await _storage.GetAsync(KMaxHops).ConfigureAwait(false), out var mh) && mh is >= 1 and <= 3)
            s.MaxSearchHops = mh;
        if (int.TryParse(await _storage.GetAsync(KAttempts).ConfigureAwait(false), out var at) && at >= 1)
            s.SendFailureSearchAttempts = at;
        if (int.TryParse(await _storage.GetAsync(KDelayMs).ConfigureAwait(false), out var dm) && dm >= 0)
            s.SendFailureRetryDelay = TimeSpan.FromMilliseconds(dm);
        if (int.TryParse(await _storage.GetAsync(KSearchTimeoutMs).ConfigureAwait(false), out var st) && st >= 500)
            s.SearchWaitTimeout = TimeSpan.FromMilliseconds(st);
        return s;
    }

    public async Task SaveAsync(P2pRoutingSettings settings)
    {
        await _storage.SetAsync(KMaxHops, settings.MaxSearchHops.ToString()).ConfigureAwait(false);
        await _storage.SetAsync(KAttempts, settings.SendFailureSearchAttempts.ToString()).ConfigureAwait(false);
        await _storage.SetAsync(KDelayMs, ((int)settings.SendFailureRetryDelay.TotalMilliseconds).ToString())
            .ConfigureAwait(false);
        await _storage.SetAsync(KSearchTimeoutMs, ((int)settings.SearchWaitTimeout.TotalMilliseconds).ToString())
            .ConfigureAwait(false);
    }
}
