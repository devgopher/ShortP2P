using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Auth;

public sealed class LoginClientUseCase(
    IClientAccountRepository accounts,
    IPasswordHasher passwordHasher,
    IAuthTokenService tokenService)
{
    public async Task<LoginClientResult> ExecuteAsync(
        LoginClientCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.NetworkId) || string.IsNullOrWhiteSpace(command.Password))
            throw UseCaseException.Validation("NetworkId and password are required.");

        var networkId = command.NetworkId.Trim();
        var account = await accounts.FindByNetworkIdAsync(networkId, cancellationToken).ConfigureAwait(false);
        if (account is null || !passwordHasher.Verify(command.Password, account.PasswordSalt, account.PasswordHash))
            throw UseCaseException.Unauthorized("Invalid networkId or password.");

        var token = tokenService.IssueToken(account.NetworkId);
        return new LoginClientResult(token.Token, token.ExpiresAtUtc);
    }
}
