using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.Auth;

/// <summary>PBKDF2 password hasher (salt + hash) via <see cref="ShortP2P.Crypto.PasswordHasher"/>.</summary>
public sealed class CryptoPasswordHasher : IPasswordHasher
{
    public PasswordHashResult Hash(string password)
    {
        var (salt, hash) = ShortP2P.Crypto.PasswordHasher.Hash(password);
        return new PasswordHashResult(salt, hash);
    }

    public bool Verify(string password, string salt, string hash) =>
        ShortP2P.Crypto.PasswordHasher.Verify(password, salt, hash);
}
