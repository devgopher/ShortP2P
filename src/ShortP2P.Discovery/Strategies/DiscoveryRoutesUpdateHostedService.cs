using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ShortP2P.Discovery.Strategies;

internal sealed class DiscoveryRoutesUpdateHostedService(
    IEnumerable<IDiscoveryStrategy> strategies,
    ILogger<DiscoveryRoutesUpdateHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan UpdatePeriod = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var strategy in strategies)
            {
                try
                {
                    await strategy.UpdateRoutesAsync(cancellationToken: stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Routes update failed for discovery strategy {StrategyName}", strategy.Name);
                }
            }

            await Task.Delay(UpdatePeriod, stoppingToken).ConfigureAwait(false);
        }
    }
}
