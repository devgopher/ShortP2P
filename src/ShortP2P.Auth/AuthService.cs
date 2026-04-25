using ShortP2P.Auth.Data;
using ShortP2P.Crypto;

namespace ShortP2P.Auth;

public sealed class AuthService
{
    private const string SessionUserIdKey = "shortp2p_session_user_id";

    private readonly IUserAuthRepository _users;
    private readonly ISessionStorage _sessionStorage;

    public AuthService(IUserAuthRepository users, ISessionStorage sessionStorage)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _sessionStorage = sessionStorage ?? throw new ArgumentNullException(nameof(sessionStorage));
    }

    public UserEntity? CurrentUser { get; private set; }

    public async Task<(bool ok, string? error)> RegisterAsync(string nickname, string password)
    {
        if (string.IsNullOrWhiteSpace(nickname) || string.IsNullOrWhiteSpace(password))
            return (false, "Nickname and password are required.");

        var existing = await _users.FindByNicknameAsync(nickname.Trim()).ConfigureAwait(false);
        if (existing != null)
            return (false, "This nickname is already registered.");

        var networkId = CompressedNetworkId.New();
        var (salt, hash) = PasswordHasher.Hash(password);
        var keys = P2PCrypto.GenerateKeyPair();

        var user = new UserEntity
        {
            Nickname = nickname.Trim(),
            NetworkIdShort = networkId.ToShortString(),
            PasswordSaltBase64 = salt,
            PasswordHashBase64 = hash,
            RsaPrivateJson = RsaKeySerializer.SerializePrivate(keys.PrivateKey),
            RsaPublicJson = RsaKeySerializer.SerializePublic(keys.PublicKey),
            DataUdpPort = 50100,
            CreatedUtcTicks = DateTime.UtcNow.Ticks,
        };

        await _users.InsertUserAsync(user).ConfigureAwait(false);
        await PersistSessionAsync(user.Id).ConfigureAwait(false);
        CurrentUser = user;
        return (true, null);
    }

    public async Task<(bool ok, string? error)> LoginAsync(string nickname, string password)
    {
        if (string.IsNullOrWhiteSpace(nickname) || string.IsNullOrWhiteSpace(password))
            return (false, "Nickname and password are required.");

        var user = await _users.FindByNicknameAsync(nickname.Trim()).ConfigureAwait(false);
        if (user == null || !PasswordHasher.Verify(password, user.PasswordSaltBase64, user.PasswordHashBase64))
            return (false, "Invalid nickname or password.");

        await PersistSessionAsync(user.Id).ConfigureAwait(false);
        CurrentUser = user;
        return (true, null);
    }

    public async Task LogoutAsync()
    {
        CurrentUser = null;
        _sessionStorage.Remove(SessionUserIdKey);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task<bool> TryRestoreSessionAsync()
    {
        var idStr = await _sessionStorage.GetAsync(SessionUserIdKey).ConfigureAwait(false);
        if (string.IsNullOrEmpty(idStr) || !int.TryParse(idStr, out var id))
            return false;

        var user = await _users.FindByIdAsync(id).ConfigureAwait(false);
        if (user == null)
            return false;
        CurrentUser = user;
        return true;
    }

    private Task PersistSessionAsync(int userId) =>
        _sessionStorage.SetAsync(SessionUserIdKey, userId.ToString());

    public RsaPrivateKey GetCurrentPrivateKey()
    {
        return CurrentUser == null
            ? throw new InvalidOperationException("Not logged in.")
            : RsaKeySerializer.DeserializePrivate(CurrentUser.RsaPrivateJson);
    }

    public RsaPublicKey GetCurrentPublicKey()
    {
        return CurrentUser == null
            ? throw new InvalidOperationException("Not logged in.")
            : RsaKeySerializer.DeserializePublic(CurrentUser.RsaPublicJson);
    }
}
