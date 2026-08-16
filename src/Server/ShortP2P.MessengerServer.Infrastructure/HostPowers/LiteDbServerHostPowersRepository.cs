using LiteDB;
using Microsoft.Extensions.Options;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Infrastructure.HostPowers;

public sealed class LiteDbServerHostPowersRepository : IServerHostPowersRepository, IDisposable
{
    private readonly ILiteDatabase? _db;
    private readonly ILiteCollection<ServerHostPowersDocument>? _collection;
    private readonly object _gate = new();
    private readonly IClock _clock;
    private readonly bool _available;

    public LiteDbServerHostPowersRepository(IOptions<HostPowersLiteDbOptions> options, IClock clock)
    {
        _clock = clock;
        try
        {
            var cs = options.Value.ConnectionString;
            if (string.IsNullOrWhiteSpace(cs))
                throw new InvalidOperationException("HostPowers:LiteDb:ConnectionString is required.");

            _db = new LiteDatabase(cs);
            _collection = _db.GetCollection<ServerHostPowersDocument>("server_host_powers");
            _available = true;
        }
        catch
        {
            _db = null;
            _collection = null;
            _available = false;
        }
    }

    public Task<ServerHostPowers> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_available || _collection is null)
            return Task.FromResult(ServerHostPowers.CreateDefaults(_clock.UtcNow));

        try
        {
            lock (_gate)
            {
                var doc = _collection.FindById(ServerHostPowers.SingletonId);
                if (doc is null)
                {
                    var defaults = ServerHostPowers.CreateDefaults(_clock.UtcNow);
                    try
                    {
                        _collection.Upsert(ServerHostPowersDocument.FromDomain(defaults));
                    }
                    catch
                    {
                        // ignore persist failure; still return defaults
                    }

                    return Task.FromResult(defaults);
                }

                var powers = doc.ToDomain();
                if (!IsSane(powers))
                    return Task.FromResult(ServerHostPowers.CreateDefaults(_clock.UtcNow));

                return Task.FromResult(powers);
            }
        }
        catch
        {
            return Task.FromResult(ServerHostPowers.CreateDefaults(_clock.UtcNow));
        }
    }

    public Task UpsertAsync(ServerHostPowers powers, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_available || _collection is null)
            return Task.CompletedTask;

        try
        {
            lock (_gate)
            {
                _collection.Upsert(ServerHostPowersDocument.FromDomain(powers));
            }
        }
        catch
        {
            // best-effort write
        }

        return Task.CompletedTask;
    }

    public void Dispose() => _db?.Dispose();

    private static bool IsSane(ServerHostPowers powers) =>
        double.IsFinite(powers.TotalPower) && powers.TotalPower is >= 1 and <= 100 &&
        double.IsFinite(powers.FreePowers) && powers.FreePowers is >= 0 and <= 100;
}
