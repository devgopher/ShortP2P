using ShortP2P.MessengerServer.Api.Auth;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.DependencyInjection;

public static class ServerCertificateServiceCollectionExtensions
{
    public static IServiceCollection AddServerCertificateReader(this IServiceCollection services)
    {
        services.AddSingleton<IServerCertificateReader, KestrelServerCertificateReader>();
        return services;
    }
}
