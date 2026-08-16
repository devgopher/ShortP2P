using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;
using ShortP2P.MessengerServer.UseCases.Inbox;

namespace ShortP2P.MessengerServer.UseCases.Auth;

public sealed record LoginClientCommand(string NetworkId, string Password, string DeviceId);

public sealed class LoginClientUseCase(
    IClientAccountRepository accounts,
    IClientStatusRepository statuses,
    IPasswordHasher passwordHasher,
    IAuthTokenService tokenService,
    DeviceFanoutService fanout,
    IClock clock)
{
    public async Task<LoginClientResult> ExecuteAsync(
        LoginClientCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.NetworkId) || string.IsNullOrWhiteSpace(command.Password))
            throw UseCaseException.Validation("NetworkId and password are required.");

        if (!DeviceIdRules.IsValid(command.DeviceId?.Trim()))
            throw UseCaseException.Validation("DeviceId must be 64 lowercase hex characters (SHA-256).");

        var networkId = command.NetworkId.Trim();
        var deviceId = command.DeviceId.Trim();
        var account = await accounts.FindByNetworkIdAsync(networkId, cancellationToken).ConfigureAwait(false);
        if (account is null || !passwordHasher.Verify(command.Password, account.PasswordSalt, account.PasswordHash))
            throw UseCaseException.Unauthorized("Invalid networkId or password.");

        var now = clock.UtcNow;
        await statuses.UpsertAsync(
            new ClientStatuses
            {
                NetworkId = networkId,
                DeviceId = deviceId,
                Status = ClientOnlineStatus.Online,
                CreatedAtUtc = now
            },
            cancellationToken).ConfigureAwait(false);

        await fanout.EnsureInboxForDeviceAsync(networkId, deviceId, cancellationToken).ConfigureAwait(false);

        var token = tokenService.IssueToken(networkId, deviceId);
        return new LoginClientResult(token.Token, token.ExpiresAtUtc);
    }
}
