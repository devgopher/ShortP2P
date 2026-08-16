namespace ShortP2P.MessengerServer.UseCases.Abstractions;

public interface IAuthTokenService
{
    AuthToken IssueToken(string networkId, string deviceId);
}
