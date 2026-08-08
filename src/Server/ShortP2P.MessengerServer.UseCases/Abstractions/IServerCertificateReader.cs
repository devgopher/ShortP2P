namespace ShortP2P.MessengerServer.UseCases.Abstractions;

public interface IServerCertificateReader
{
    Task<ServerCertificateInfo> GetAsync(CancellationToken cancellationToken = default);
}
