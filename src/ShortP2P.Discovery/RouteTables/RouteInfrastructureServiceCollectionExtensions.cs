using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShortP2P.Discovery.Strategies;

namespace ShortP2P.Discovery.RouteTables;

public static class RouteInfrastructureServiceCollectionExtensions
{
    /// <param name="sqliteDatabasePath"></param>
    /// <param name="registerHostedCleanup">
    ///     <see langword="true" /> — зарегистрировать фоновый сервис (см. <see cref="IHostedService" />) для MAUI / generic host.
    ///     <see langword="false" /> — только контекст и опции; затем вызовите <see cref="StartRoutePeerRoutesExpiryCleanupDetached" />.
    /// </param>
    /// <param name="services"></param>
    public static IServiceCollection AddRouteDbContextWithPeerExpiryCleanup(
        this IServiceCollection services,
        string sqliteDatabasePath,
        bool registerHostedCleanup = true,
        Action<RoutePeerRoutesExpiryOptions>? configureExpiry = null)
    {
        var dbPath = $"Data Source={sqliteDatabasePath}";
        services.AddDbContext<RouteDbContext>(o => o.UseSqlite(dbPath));
        services.AddDbContextFactory<RouteDbContext>(o => o.UseSqlite(dbPath), ServiceLifetime.Singleton);
        services.AddSingleton<IRouteTableSnapshotSource, EfRouteTableSnapshotSource>();
        services.AddSingleton<IDiscoveryStrategy, GossipStrategy>();
        services.AddSingleton(_ =>
        {
            var options = new RoutePeerRoutesExpiryOptions();
            configureExpiry?.Invoke(options);
            return options;
        });

        if (registerHostedCleanup)
        {
            services.AddHostedService<RoutePeerRoutesExpiryCleanupHostedService>();
            services.AddHostedService<DiscoveryRoutesUpdateHostedService>();
        }

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
        var routeSyncLogger = loggerFactory.CreateLogger("ShortP2P.RouteSync");
        
        var cts = new CancellationTokenSource();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();
        RoutePeerRoutesExpiryCleanup.StartDetached(rootProvider, options, logger, cts.Token);
        _ = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    using var scope = rootProvider.CreateScope();
                    var strategies = scope.ServiceProvider.GetServices<IDiscoveryStrategy>();
                    foreach (var strategy in strategies)
                        await strategy.UpdateRoutesAsync(cancellationToken: cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    routeSyncLogger.LogError(ex, "Detached route sync tick failed");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), cts.Token).ConfigureAwait(false);
            }
        }, cts.Token);
    }
}
