using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Tests.Fakes;

internal sealed class FakePasswordHasher : IPasswordHasher
{
    public PasswordHashResult Hash(string password) =>
        new($"salt:{password}", $"hash:{password}");

    public bool Verify(string password, string salt, string hash) =>
        salt == $"salt:{password}" && hash == $"hash:{password}";
}
