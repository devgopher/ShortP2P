using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Server;

public sealed class GetServerCertificateUseCase(IServerCertificateReader certificateReader)
{
    public Task<ServerCertificateInfo> ExecuteAsync(CancellationToken cancellationToken = default)
        => certificateReader.GetAsync(cancellationToken);
}
