using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ShortP2P.Discovery.RouteTables;

internal sealed class RoutePeerRoutesExpiryCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    RoutePeerRoutesExpiryOptions options,
    ILogger<RoutePeerRoutesExpiryCleanupHostedService> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return RoutePeerRoutesExpiryCleanup.RunPeriodicAsync(scopeFactory, options, logger, stoppingToken);
    }
}