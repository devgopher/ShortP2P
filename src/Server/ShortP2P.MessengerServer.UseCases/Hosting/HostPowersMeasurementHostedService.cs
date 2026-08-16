using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShortP2P.MessengerServer.UseCases.ServerTech;

namespace ShortP2P.MessengerServer.UseCases.Hosting;

/// <summary>
/// Measures TotalPower at startup + hourly; FreePowers at startup + every minute.
/// </summary>
public sealed class HostPowersMeasurementHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<HostPowersMeasurementHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan TotalPowerPeriod = TimeSpan.FromHours(1);
    private static readonly TimeSpan FreePowersPeriod = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await MeasureBothSafeAsync(stoppingToken).ConfigureAwait(false);

        var nextTotal = DateTime.UtcNow + TotalPowerPeriod;
        var nextFree = DateTime.UtcNow + FreePowersPeriod;

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var delayToTotal = nextTotal - now;
            var delayToFree = nextFree - now;
            var delay = delayToTotal < delayToFree ? delayToTotal : delayToFree;
            if (delay < TimeSpan.Zero)
                delay = TimeSpan.Zero;

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            now = DateTime.UtcNow;
            if (now >= nextFree)
            {
                await MeasureFreeSafeAsync(stoppingToken).ConfigureAwait(false);
                nextFree = DateTime.UtcNow + FreePowersPeriod;
            }

            if (now >= nextTotal)
            {
                await MeasureTotalSafeAsync(stoppingToken).ConfigureAwait(false);
                nextTotal = DateTime.UtcNow + TotalPowerPeriod;
            }
        }
    }

    private async Task MeasureBothSafeAsync(CancellationToken cancellationToken)
    {
        await MeasureTotalSafeAsync(cancellationToken).ConfigureAwait(false);
        await MeasureFreeSafeAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task MeasureTotalSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<HostPowersMeasurementService>();
            await svc.MeasureTotalPowerAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TotalPower measurement cycle failed.");
        }
    }

    private async Task MeasureFreeSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<HostPowersMeasurementService>();
            await svc.MeasureFreePowersAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FreePowers measurement cycle failed.");
        }
    }
}
