using ShortP2P.Auth.Data;

namespace ShortP2P.Auth;

public interface IUserAuthRepository
{
    Task<UserEntity?> FindByNicknameAsync(string nickname, CancellationToken cancellationToken = default);

    Task<UserEntity?> FindByIdAsync(int id, CancellationToken cancellationToken = default);

    Task InsertUserAsync(UserEntity user, CancellationToken cancellationToken = default);
}