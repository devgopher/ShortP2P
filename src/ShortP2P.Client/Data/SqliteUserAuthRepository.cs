using ShortP2P.Auth;
using ShortP2P.Auth.Data;

namespace ShortP2P.Client.Data;

public sealed class SqliteUserAuthRepository(AppDatabase appDatabase) : IUserAuthRepository
{
    private readonly AppDatabase _db = appDatabase ?? throw new global::System.ArgumentNullException(nameof(appDatabase));

    public async Task<UserEntity?> FindByNicknameAsync(string nickname, CancellationToken cancellationToken = default)
    {
        return await _db.ReadAsync(async conn =>
        {
            return await conn.Table<UserEntity>().Where(u => u.Nickname == nickname).FirstOrDefaultAsync()
                .ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task<UserEntity?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _db.ReadAsync(async conn =>
        {
            return await conn.FindAsync<UserEntity>(id).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task InsertUserAsync(UserEntity user, CancellationToken cancellationToken = default)
    {
        await _db.WriteAsync(async conn =>
        {
            await conn.InsertAsync(user).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task UpdateUserAsync(UserEntity user, CancellationToken cancellationToken = default)
    {
        await _db.WriteAsync(async conn =>
        {
            await conn.UpdateAsync(user).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
}