using LiteDB;
using Microsoft.Extensions.Options;
using ShortP2P.TrustSystem;

namespace ShortP2P.MessengerServer.Infrastructure.Trust;

public sealed class LiteDbTrustStore : ITrustStore, IDisposable
{
    private readonly ILiteDatabase _db;
    private readonly ILiteCollection<ServerTrustDocument> _collection;
    private readonly object _gate = new();

    public LiteDbTrustStore(IOptions<TrustLiteDbOptions> options)
    {
        var cs = options.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("Trust:LiteDb:ConnectionString is required.");

        _db = new LiteDatabase(cs);
        _collection = _db.GetCollection<ServerTrustDocument>("server_trust");
        _collection.EnsureIndex(x => x.Id, unique: true);
    }

    public Task<ServerTrustState?> GetAsync(ServerEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var doc = _collection.FindById(endpoint.Key);
            return Task.FromResult(doc?.ToDomain());
        }
    }

    public Task UpsertAsync(ServerTrustState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _collection.Upsert(ServerTrustDocument.FromDomain(state));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ServerTrustState>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<ServerTrustState> list = _collection.FindAll()
                .Select(d => d.ToDomain())
                .OrderBy(s => s.Host, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Port)
                .ToList();
            return Task.FromResult(list);
        }
    }

    public void Dispose() => _db.Dispose();
}
