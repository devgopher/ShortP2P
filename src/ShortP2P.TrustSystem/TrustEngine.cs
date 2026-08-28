namespace ShortP2P.TrustSystem;

/// <summary>
/// Per-server reputation of other messenger servers.
/// Ratings live in <see cref="ITrustStore"/> (cache/DB). Penalty math is deterministic and clock-driven.
/// </summary>
public sealed class TrustEngine(ITrustStore store, ITrustClock clock, TrustOptions options)
{
    private readonly ITrustStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ITrustClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly TrustOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<RatedServer>> AskRatingAsync(
        string serverIp,
        int serverPort,
        int subscriberCount,
        CancellationToken cancellationToken = default)
    {
        var endpoint = ServerEndpoint.Parse(serverIp, serverPort);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsSelf(endpoint))
                await EnsureExistsAsync(endpoint, cancellationToken).ConfigureAwait(false);

            await RefreshAllUnlockedAsync(subscriberCount, cancellationToken).ConfigureAwait(false);
            return await ListRatingsUnlockedAsync(minRating: null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Known peer servers with rating at least <see cref="TrustOptions.MinPublishRating"/> (default 0.3).</summary>
    public async Task<IReadOnlyList<RatedServer>> AskServersAsync(
        int subscriberCount,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RefreshAllUnlockedAsync(subscriberCount, cancellationToken).ConfigureAwait(false);
            return await ListRatingsUnlockedAsync(_options.MinPublishRating, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClaimServerAsync(
        string serverIp,
        int serverPort,
        ServerClaimReason reason,
        string complainantId,
        int subscriberCount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(complainantId))
            throw new TrustException("complainantId is required.");

        var endpoint = ServerEndpoint.Parse(serverIp, serverPort);
        if (IsSelf(endpoint))
            throw new TrustException("Cannot claim this server itself.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureExistsAsync(endpoint, cancellationToken).ConfigureAwait(false);
            var now = _clock.UtcNow;
            var id = complainantId.Trim();
            var existing = state.Claims.FirstOrDefault(c =>
                string.Equals(c.ComplainantId, id, StringComparison.Ordinal) && c.Reason == reason);
            if (existing != null)
                existing.Utc = now;
            else
            {
                state.Claims.Add(new ServerClaimRecord
                {
                    ComplainantId = id,
                    Reason = reason,
                    Utc = now
                });
            }

            state.LastComplaintUtc = now;
            state.RecoveryAnchorUtc = null;
            state.RatingAtRecoveryStart = null;
            ApplyPenalties(state, subscriberCount, now);
            await _store.UpsertAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RefreshAllAsync(int subscriberCount, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RefreshAllUnlockedAsync(subscriberCount, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool IsSelf(string serverIp, int serverPort) =>
        ServerEndpoint.TryParse(serverIp, serverPort, out var endpoint, out _) && IsSelf(endpoint);

    private bool IsSelf(ServerEndpoint endpoint)
    {
        if (string.IsNullOrWhiteSpace(_options.SelfHost) || _options.SelfPort is < 1 or > 65535)
            return false;
        if (!ServerEndpoint.TryParse(_options.SelfHost, _options.SelfPort, out var self, out _))
            return false;
        return endpoint.EqualsEndpoint(self);
    }

    private async Task<ServerTrustState> EnsureExistsAsync(ServerEndpoint endpoint, CancellationToken cancellationToken)
    {
        var existing = await _store.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
        if (existing != null)
            return existing;

        var created = new ServerTrustState
        {
            Host = endpoint.Host,
            Port = endpoint.Port,
            Rating = Clamp01(_options.DefaultRating)
        };
        await _store.UpsertAsync(created, cancellationToken).ConfigureAwait(false);
        return created;
    }

    private async Task RefreshAllUnlockedAsync(int subscriberCount, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var all = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var state in all)
        {
            ApplyPenalties(state, subscriberCount, now);
            ApplyRecovery(state, now);
            await _store.UpsertAsync(state, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<RatedServer>> ListRatingsUnlockedAsync(
        float? minRating,
        CancellationToken cancellationToken)
    {
        var all = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        var query = all.AsEnumerable();
        if (minRating is { } min)
            query = query.Where(s => s.Rating >= min);
        return query
            .Select(s => new RatedServer(s.Host, s.Port, s.Rating))
            .ToList();
    }

    private void ApplyPenalties(ServerTrustState state, int subscriberCount, DateTime now)
    {
        var n = Math.Max(1, subscriberCount);

        var integrityUnique = state.Claims
            .Where(c => c.Reason is ServerClaimReason.MALFUNCTIONED or ServerClaimReason.WRONGCERT)
            .Select(c => c.ComplainantId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var integrityBuckets = CountShareBuckets(integrityUnique, n);
        while (state.IntegrityPenaltiesApplied < integrityBuckets)
        {
            state.IntegrityPenaltiesApplied++;
            ApplyIntegrityStrike(state, state.IntegrityPenaltiesApplied);
        }

        var windowStart = now - _options.UnavailableWindow;
        var unavailableUnique = state.Claims
            .Where(c => c.Reason == ServerClaimReason.UNAVAILABLE && c.Utc >= windowStart)
            .Select(c => c.ComplainantId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (unavailableUnique == 0)
            state.UnavailableBucketsSeen = 0;
        else
        {
            var unavailableBuckets = CountShareBuckets(unavailableUnique, n);
            var extra = unavailableBuckets - state.UnavailableBucketsSeen;
            if (extra > 0)
            {
                state.Rating = Math.Max(0f, state.Rating - _options.UnavailablePenalty * extra);
                SnapIfCollapsed(state);
                state.UnavailableBucketsSeen = unavailableBuckets;
            }
        }
    }

    private void ApplyIntegrityStrike(ServerTrustState state, int strikeNumber)
    {
        if (strikeNumber <= 1)
            state.Rating = Math.Max(0f, state.Rating - _options.IntegrityFirstPenalty);
        else
            state.Rating *= _options.IntegrityExponentialFactor;
        SnapIfCollapsed(state);
    }

    private void SnapIfCollapsed(ServerTrustState state)
    {
        if (state.Rating < _options.CollapseBelow)
            state.Rating = 0f;
        state.Rating = Clamp01(state.Rating);
    }

    private void ApplyRecovery(ServerTrustState state, DateTime now)
    {
        if (state.LastComplaintUtc is null)
        {
            if (state.Rating < _options.RecoveryTarget)
                RecoverTowardTarget(state, now, assumeQuietSince: now - _options.QuietBeforeRecovery);
            return;
        }

        var quietFor = now - state.LastComplaintUtc.Value;
        if (quietFor < _options.QuietBeforeRecovery)
        {
            state.RecoveryAnchorUtc = null;
            state.RatingAtRecoveryStart = null;
            return;
        }

        RecoverTowardTarget(state, now, state.LastComplaintUtc.Value + _options.QuietBeforeRecovery);
    }

    private void RecoverTowardTarget(ServerTrustState state, DateTime now, DateTime assumeQuietSince)
    {
        if (state.Rating >= _options.RecoveryTarget)
        {
            state.Rating = _options.RecoveryTarget;
            state.RecoveryAnchorUtc = null;
            state.RatingAtRecoveryStart = null;
            return;
        }

        if (state.RecoveryAnchorUtc is null)
        {
            state.RecoveryAnchorUtc = assumeQuietSince;
            state.RatingAtRecoveryStart = state.Rating;
        }

        var elapsed = now - state.RecoveryAnchorUtc.Value;
        if (elapsed < TimeSpan.Zero)
            return;

        var durationTicks = Math.Max(1, _options.RecoveryDuration.Ticks);
        var t = Math.Clamp(elapsed.Ticks / (double)durationTicks, 0d, 1d);
        var start = state.RatingAtRecoveryStart ?? state.Rating;
        state.Rating = Clamp01((float)(start + (_options.RecoveryTarget - start) * t));
        if (t >= 1d)
        {
            state.Rating = _options.RecoveryTarget;
            state.RecoveryAnchorUtc = null;
            state.RatingAtRecoveryStart = null;
        }
    }

    private int CountShareBuckets(int uniqueComplainants, int subscriberCount)
    {
        if (uniqueComplainants <= 0)
            return 0;
        var threshold = _options.ComplaintShareThreshold * subscriberCount;
        if (threshold <= 0)
            return uniqueComplainants;

        var buckets = 0;
        while (uniqueComplainants > threshold * (buckets + 1))
            buckets++;
        return buckets;
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
}
