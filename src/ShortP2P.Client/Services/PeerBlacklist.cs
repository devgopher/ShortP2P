using System.Collections.Concurrent;
using ShortP2P.Client.Data;

namespace ShortP2P.Client.Services;

/// <summary>
/// Per-account block list of peer network ids.
/// Incoming messages and invites still persist; UI events and sounds are suppressed at ingest.
/// </summary>
public sealed class PeerBlacklist
{
    private readonly AppDatabase _db;
    private readonly ConcurrentDictionary<string, byte> _ids = new(StringComparer.Ordinal);
    private int _loadedUserId = int.MinValue;

    public PeerBlacklist(AppDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public event EventHandler? Changed;

    public bool IsBlocked(int? userId, string? networkId)
    {
        if (userId is not int uid || uid <= 0)
            return false;
        var id = ChatRepository.CanonicalPeerNetworkId(networkId);
        if (id.Length == 0)
            return false;
        if (_loadedUserId != uid)
            return false;
        if (_ids.ContainsKey(id))
            return true;
        foreach (var key in _ids.Keys)
        {
            if (ChatRepository.PeerNetworkIdsEqual(key, id))
                return true;
        }

        return false;
    }

    public async Task EnsureLoadedAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
            return;
        if (_loadedUserId == userId)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
        var rows = await conn.Table<PeerBlacklistEntity>()
            .Where(r => r.UserId == userId)
            .ToListAsync()
            .ConfigureAwait(false);

        _ids.Clear();
        foreach (var row in rows)
        {
            var id = ChatRepository.CanonicalPeerNetworkId(row.NetworkId);
            if (id.Length > 0)
                _ids[id] = 0;
        }

        _loadedUserId = userId;
    }

    public async Task AddAsync(int userId, string networkId, string? nickname, CancellationToken cancellationToken = default)
    {
        var id = ChatRepository.CanonicalPeerNetworkId(networkId);
        if (userId <= 0 || id.Length == 0)
            return;

        await EnsureLoadedAsync(userId, cancellationToken).ConfigureAwait(false);
        if (IsBlocked(userId, id))
            return;

        var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
        await conn.InsertAsync(new PeerBlacklistEntity
        {
            UserId = userId,
            NetworkId = id,
            Nickname = nickname?.Trim() ?? "",
            AddedUtcTicks = DateTime.UtcNow.Ticks
        }).ConfigureAwait(false);

        _ids[id] = 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveAsync(int userId, string networkId, CancellationToken cancellationToken = default)
    {
        var id = ChatRepository.CanonicalPeerNetworkId(networkId);
        if (userId <= 0 || id.Length == 0)
            return;

        await EnsureLoadedAsync(userId, cancellationToken).ConfigureAwait(false);
        var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
        var rows = await conn.Table<PeerBlacklistEntity>()
            .Where(r => r.UserId == userId)
            .ToListAsync()
            .ConfigureAwait(false);
        foreach (var row in rows)
        {
            if (!ChatRepository.PeerNetworkIdsEqual(row.NetworkId, id))
                continue;
            await conn.DeleteAsync(row).ConfigureAwait(false);
        }

        foreach (var key in _ids.Keys.ToArray())
        {
            if (ChatRepository.PeerNetworkIdsEqual(key, id))
                _ids.TryRemove(key, out _);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<PeerBlacklistEntity>> ListAsync(int userId, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(userId, cancellationToken).ConfigureAwait(false);
        var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
        var rows = await conn.Table<PeerBlacklistEntity>()
            .Where(r => r.UserId == userId)
            .ToListAsync()
            .ConfigureAwait(false);
        return rows.OrderByDescending(r => r.AddedUtcTicks).ToList();
    }
}
