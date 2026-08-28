using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShortP2P.MessengerServer.UseCases.Abstractions;
using ShortP2P.MessengerServer.UseCases.Trust;
using ShortP2P.TrustSystem;

namespace ShortP2P.MessengerServer.UseCases.Hosting;

/// <summary>Applies quiet-period rating recovery toward 0.8.</summary>
public sealed class TrustRecoveryHostedService(
    IServiceScopeFactory scopeFactory,
    TrustEngine engine,
    ILogger<TrustRecoveryHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var accounts = scope.ServiceProvider.GetRequiredService<IClientAccountRepository>();
                var subscribers = await AskRatingUseCase.CountSubscribersAsync(accounts, stoppingToken)
                    .ConfigureAwait(false);
                await engine.RefreshAllAsync(subscribers, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Trust rating recovery tick failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
