namespace ShortP2P.MessengerServer.UseCases.Abstractions;

public interface IPasswordHasher
{
    PasswordHashResult Hash(string password);

    bool Verify(string password, string salt, string hash);
}
