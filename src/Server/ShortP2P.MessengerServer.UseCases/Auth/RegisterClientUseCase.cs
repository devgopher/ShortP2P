using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Auth;

public sealed record RegisterClientCommand(string Nick, string NetworkId, string Password, string DeviceId);

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

        if (!DeviceIdRules.IsValid(command.DeviceId?.Trim()))
            throw UseCaseException.Validation("DeviceId must be 64 lowercase hex characters (SHA-256).");

        var nick = command.Nick.Trim();
        var networkId = command.NetworkId.Trim();
        var deviceId = command.DeviceId.Trim();

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
                DeviceId = deviceId,
                Status = ClientOnlineStatus.Offline,
                CreatedAtUtc = now
            },
            cancellationToken).ConfigureAwait(false);
    }
}
