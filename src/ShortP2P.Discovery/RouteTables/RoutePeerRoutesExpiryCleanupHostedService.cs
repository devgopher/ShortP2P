using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ShortP2P.Discovery.RouteTables;

internal sealed class RoutePeerRoutesExpiryCleanupHostedService : BackgroundService
{
    private readonly ILogger<RoutePeerRoutesExpiryCleanupHostedService> _logger;
    private readonly RoutePeerRoutesExpiryOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;

    public RoutePeerRoutesExpiryCleanupHostedService(
        IServiceScopeFactory scopeFactory,
        RoutePeerRoutesExpiryOptions options,
        ILogger<RoutePeerRoutesExpiryCleanupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return RoutePeerRoutesExpiryCleanup.RunPeriodicAsync(_scopeFactory, _options, _logger, stoppingToken);
    }
}