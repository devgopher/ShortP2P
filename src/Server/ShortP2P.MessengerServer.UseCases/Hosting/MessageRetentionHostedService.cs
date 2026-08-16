using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Hosting;

/// <summary>Purges messages and chat requests older than <see cref="MessengerInboxOptions.MessageRetention"/>.</summary>
public sealed class MessageRetentionHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<MessengerInboxOptions> inboxOptions,
    IClock clock,
    ILogger<MessageRetentionHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Message retention purge failed.");
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

    private async Task PurgeOnceAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow - inboxOptions.Value.MessageRetention;
        await using var scope = scopeFactory.CreateAsyncScope();
        var messages = scope.ServiceProvider.GetService<IMessageRepository>();
        var chatRequests = scope.ServiceProvider.GetService<IChatRequestRepository>();

        if (messages is not null)
            await messages.RemoveOlderThanAsync(cutoff, cancellationToken).ConfigureAwait(false);

        if (chatRequests is not null)
            await chatRequests.RemoveOlderThanAsync(cutoff, cancellationToken).ConfigureAwait(false);

        logger.LogDebug("Retention purge completed for cutoff {CutoffUtc:o}.", cutoff);
    }
}
