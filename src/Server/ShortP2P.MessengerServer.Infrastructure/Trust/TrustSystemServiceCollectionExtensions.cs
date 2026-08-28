using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortP2P.MessengerServer.UseCases.Abstractions;
using ShortP2P.TrustSystem;

namespace ShortP2P.MessengerServer.Infrastructure.Trust;

public static class TrustSystemServiceCollectionExtensions
{
    public static IServiceCollection AddTrustSystemLiteDb(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<TrustOptions>(configuration.GetSection(TrustOptions.Section));
        services.Configure<TrustLiteDbOptions>(configuration.GetSection(TrustLiteDbOptions.Section));

        var lite = new TrustLiteDbOptions();
        configuration.GetSection(TrustLiteDbOptions.Section).Bind(lite);
        if (string.IsNullOrWhiteSpace(lite.ConnectionString))
            throw new InvalidOperationException("Trust:LiteDb:ConnectionString is required.");

        services.AddSingleton<ITrustClock>(sp => new MessengerTrustClock(sp.GetRequiredService<IClock>()));
        services.AddSingleton<ITrustStore, LiteDbTrustStore>();
        services.AddSingleton(sp =>
        {
            var options = new TrustOptions();
            configuration.GetSection(TrustOptions.Section).Bind(options);
            return new TrustEngine(
                sp.GetRequiredService<ITrustStore>(),
                sp.GetRequiredService<ITrustClock>(),
                options);
        });
        return services;
    }
}
