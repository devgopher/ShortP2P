using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Infrastructure.HostPowers;

public static class HostPowersServiceCollectionExtensions
{
    public static IServiceCollection AddHostPowersLiteDb(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<HostPowersLiteDbOptions>(
            configuration.GetSection(HostPowersLiteDbOptions.Section));

        var options = new HostPowersLiteDbOptions();
        configuration.GetSection(HostPowersLiteDbOptions.Section).Bind(options);
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("HostPowers:LiteDb:ConnectionString is required.");

        services.AddSingleton<IServerHostPowersRepository, LiteDbServerHostPowersRepository>();
        return services;
    }
}
