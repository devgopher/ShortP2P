using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ShortP2P.Discovery.RouteTables;

public static class RouteInfrastructureServiceCollectionExtensions
{
    /// <param name="registerHostedCleanup">
    ///     <see langword="true" /> — зарегистрировать фоновый сервис (см. <see cref="IHostedService" />) для MAUI / generic host.
    ///     <see langword="false" /> — только контекст и опции; затем вызовите <see cref="StartRoutePeerRoutesExpiryCleanupDetached" />.
    /// </param>
    public static IServiceCollection AddRouteDbContextWithPeerExpiryCleanup(
        this IServiceCollection services,
        string sqliteDatabasePath,
        bool registerHostedCleanup = true,
        Action<RoutePeerRoutesExpiryOptions>? configureExpiry = null)
    {
        services.AddDbContext<RouteDbContext>(o => o.UseSqlite($"Data Source={sqliteDatabasePath}"));
        services.AddSingleton(_ =>
        {
            var options = new RoutePeerRoutesExpiryOptions();
            configureExpiry?.Invoke(options);
            return options;
        });

        if (registerHostedCleanup)
            services.AddHostedService<RoutePeerRoutesExpiryCleanupHostedService>();

        return services;
    }

    /// <summary>
    ///     Старт цикла очистки для приложений без host (например WinForms).
    /// </summary>
    public static void StartRoutePeerRoutesExpiryCleanupDetached(this IServiceProvider rootProvider)
    {
        var options = rootProvider.GetRequiredService<RoutePeerRoutesExpiryOptions>();
        var loggerFactory = rootProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("ShortP2P.RoutePeerRoutesExpiry");
        
        var cts = new CancellationTokenSource();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();
        RoutePeerRoutesExpiryCleanup.StartDetached(rootProvider, options, logger, cts.Token);
    }
}
