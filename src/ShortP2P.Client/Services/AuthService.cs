using ShortP2P.Client.Data;
using ShortP2P.Crypto;
using ShortP2P.Discovery;

namespace ShortP2P.Client.Services;

public sealed class AuthService
{
    private const string SessionUserIdKey = "shortp2p_session_user_id";

    private readonly AppDatabase _db;
    private readonly ISessionStorage _sessionStorage;

    public AuthService(AppDatabase db, ISessionStorage sessionStorage)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _sessionStorage = sessionStorage ?? throw new ArgumentNullException(nameof(sessionStorage));
    }

    public UserEntity? CurrentUser { get; private set; }

    public async Task<(bool ok, string? error)> RegisterAsync(string nickname, string password)
    {
        if (string.IsNullOrWhiteSpace(nickname) || string.IsNullOrWhiteSpace(password))
            return (false, "Nickname and password are required.");

        var conn = await _db.GetConnectionAsync();
        var existing = await conn.Table<UserEntity>().Where(u => u.Nickname == nickname).FirstOrDefaultAsync();
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

        await conn.InsertAsync(user);
        await PersistSessionAsync(user.Id);
        CurrentUser = user;
        return (true, null);
    }

    public async Task<(bool ok, string? error)> LoginAsync(string nickname, string password)
    {
        if (string.IsNullOrWhiteSpace(nickname) || string.IsNullOrWhiteSpace(password))
            return (false, "Nickname and password are required.");

        var conn = await _db.GetConnectionAsync();
        var user = await conn.Table<UserEntity>().Where(u => u.Nickname == nickname).FirstOrDefaultAsync();
        if (user == null || !PasswordHasher.Verify(password, user.PasswordSaltBase64, user.PasswordHashBase64))
            return (false, "Invalid nickname or password.");

        await PersistSessionAsync(user.Id);
        CurrentUser = user;
        return (true, null);
    }

    public async Task LogoutAsync()
    {
        CurrentUser = null;
        _sessionStorage.Remove(SessionUserIdKey);
        await Task.CompletedTask;
    }

    public async Task<bool> TryRestoreSessionAsync()
    {
        var idStr = await _sessionStorage.GetAsync(SessionUserIdKey).ConfigureAwait(false);
        if (string.IsNullOrEmpty(idStr) || !int.TryParse(idStr, out var id))
            return false;

        var conn = await _db.GetConnectionAsync();
        var user = await conn.FindAsync<UserEntity>(id).ConfigureAwait(false);
        if (user == null)
            return false;
        CurrentUser = user;
        return true;
    }

    public async Task UpdateDataPortAsync(int port)
    {
        if (CurrentUser == null) return;
        CurrentUser.DataUdpPort = port;
        var conn = await _db.GetConnectionAsync();
        await conn.UpdateAsync(CurrentUser);
    }

    private Task PersistSessionAsync(int userId) =>
        _sessionStorage.SetAsync(SessionUserIdKey, userId.ToString());

    public RsaPrivateKey GetCurrentPrivateKey()
    {
        if (CurrentUser == null) throw new InvalidOperationException("Not logged in.");
        return RsaKeySerializer.DeserializePrivate(CurrentUser.RsaPrivateJson);
    }

    public RsaPublicKey GetCurrentPublicKey()
    {
        if (CurrentUser == null) throw new InvalidOperationException("Not logged in.");
        return RsaKeySerializer.DeserializePublic(CurrentUser.RsaPublicJson);
    }
}
