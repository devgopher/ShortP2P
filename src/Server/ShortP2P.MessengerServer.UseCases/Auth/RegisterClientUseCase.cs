using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Auth;

public sealed class RegisterClientUseCase(
    IClientAccountRepository accounts,
    IClientStatusRepository statuses,
    IPasswordHasher passwordHasher,
    IClock clock)
{
    public async Task ExecuteAsync(RegisterClientCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Nick) ||
            string.IsNullOrWhiteSpace(command.NetworkId) ||
            string.IsNullOrWhiteSpace(command.Password))
        {
            throw UseCaseException.Validation("Nick, networkId and password are required.");
        }

        var nick = command.Nick.Trim();
        var networkId = command.NetworkId.Trim();

        if (await accounts.FindByNetworkIdAsync(networkId, cancellationToken).ConfigureAwait(false) != null)
            throw UseCaseException.Conflict("NetworkId is already registered.");

        if (await accounts.FindByNickAsync(nick, cancellationToken).ConfigureAwait(false) != null)
            throw UseCaseException.Conflict("Nick is already registered.");

        var hashed = passwordHasher.Hash(command.Password);
        var now = clock.UtcNow;

        await accounts.AddAsync(
            new ClientAccount
            {
                Nick = nick,
                NetworkId = networkId,
                PasswordSalt = hashed.Salt,
                PasswordHash = hashed.Hash,
                CreatedAtUtc = now
            },
            cancellationToken).ConfigureAwait(false);

        await statuses.UpsertAsync(
            new ClientStatuses
            {
                NetworkId = networkId,
                Status = ClientOnlineStatus.Offline,
                CreatedAtUtc = now
            },
            cancellationToken).ConfigureAwait(false);
    }
}
