using ShortP2P.Auth;
using ShortP2P.Auth.Data;

namespace ShortP2P.Client.Data;

public sealed class SqliteUserAuthRepository(AppDatabase db) : IUserAuthRepository
{
    private readonly AppDatabase _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<UserEntity?> FindByNicknameAsync(string nickname, CancellationToken cancellationToken = default)
    {
        var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
        return await conn.Table<UserEntity>().Where(u => u.Nickname == nickname).FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    public async Task<UserEntity?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
        return await conn.FindAsync<UserEntity>(id).ConfigureAwait(false);
    }

    public async Task InsertUserAsync(UserEntity user, CancellationToken cancellationToken = default)
    {
        var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
        await conn.InsertAsync(user).ConfigureAwait(false);
    }
}
