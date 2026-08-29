using ShortP2P.Client.Data;
using SQLite;

namespace ShortP2P.Client.Services.MessengerServers;

public interface IMessengerServerRepository
{
    Task<IReadOnlyList<MessengerServerEntity>> ListByUserAsync(int userId, CancellationToken cancellationToken = default);

    Task<MessengerServerEntity?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<MessengerServerEntity?> FindByUserAndBaseUrlAsync(int userId, string baseUrl, CancellationToken cancellationToken = default);

    Task<int> CountByUserAsync(int userId, CancellationToken cancellationToken = default);

    Task<MessengerServerEntity> InsertAsync(MessengerServerEntity entity, CancellationToken cancellationToken = default);

    Task UpdateAsync(MessengerServerEntity entity, CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class SqliteMessengerServerRepository(AppDatabase db) : IMessengerServerRepository
{
    public async Task<IReadOnlyList<MessengerServerEntity>> ListByUserAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var conn = await db.GetConnectionAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return await conn.Table<MessengerServerEntity>()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedUtcTicks)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<MessengerServerEntity?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var conn = await db.GetConnectionAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return await conn.FindAsync<MessengerServerEntity>(id).ConfigureAwait(false);
    }

    public async Task<MessengerServerEntity?> FindByUserAndBaseUrlAsync(
        int userId,
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeBaseUrl(baseUrl);
        var conn = await db.GetConnectionAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var all = await conn.Table<MessengerServerEntity>()
            .Where(s => s.UserId == userId)
            .ToListAsync()
            .ConfigureAwait(false);
        return all.FirstOrDefault(s =>
            string.Equals(NormalizeBaseUrl(s.BaseUrl), normalized, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<int> CountByUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var conn = await db.GetConnectionAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return await conn.Table<MessengerServerEntity>()
            .Where(s => s.UserId == userId)
            .CountAsync()
            .ConfigureAwait(false);
    }

    public async Task<MessengerServerEntity> InsertAsync(
        MessengerServerEntity entity,
        CancellationToken cancellationToken = default)
    {
        var conn = await db.GetConnectionAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        entity.BaseUrl = NormalizeBaseUrl(entity.BaseUrl);
        await conn.InsertAsync(entity).ConfigureAwait(false);
        return entity;
    }

    public async Task UpdateAsync(MessengerServerEntity entity, CancellationToken cancellationToken = default)
    {
        var conn = await db.GetConnectionAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        entity.BaseUrl = NormalizeBaseUrl(entity.BaseUrl);
        entity.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
        await conn.UpdateAsync(entity).ConfigureAwait(false);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var conn = await db.GetConnectionAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await conn.DeleteAsync<MessengerServerEntity>(id).ConfigureAwait(false);
    }

    public static string NormalizeBaseUrl(string baseUrl)
    {
        Require.NotNullOrWhiteSpace(baseUrl);
        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new global::System.ArgumentException("BaseUrl must be an absolute http(s) URL.", nameof(baseUrl));
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}
