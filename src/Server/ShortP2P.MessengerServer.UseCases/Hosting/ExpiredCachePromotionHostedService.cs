using Microsoft.Extensions.Hosting;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Hosting;

/// <summary>
/// Periodically promotes messages and delivery tickets that exceeded cache TTL
/// into the durable repositories and removes them from cache.
/// Runs only when both cache and repository are enabled in settings.
/// </summary>
public sealed class ExpiredCachePromotionHostedService(
    IMessageCache messageCache,
    IMessageRepository messages,
    IDeliveryTicketCache ticketCache,
    IDeliveryTicketRepository tickets,
    IClock clock,
    MessengerCacheOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var poll = options.PollInterval <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(10)
                : options.PollInterval;

            try
            {
                await PromoteExpiredAsync(stoppingToken).ConfigureAwait(false);
            }
            catch
            {
                // Keep the loop alive; host logging can wrap this service later.
            }

            try
            {
                await Task.Delay(poll, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PromoteExpiredAsync(CancellationToken cancellationToken)
    {
        if (!options.CacheEnabled || !options.RepositoryEnabled)
            return;

        var ttl = options.TimeToLive <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(60)
            : options.TimeToLive;
        var olderThan = clock.UtcNow - ttl;

        var expiredMessages = await StorageAccess
            .TryListAsync(() => messageCache.TakeExpiredAsync(olderThan, cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in expiredMessages)
        {
            var written = await StorageAccess
                .TryWriteAsync(
                    async () =>
                    {
                        var existing = await messages.FindByIdAsync(message.MessageId, cancellationToken)
                            .ConfigureAwait(false);
                        if (existing is null)
                            await messages.AddAsync(message, cancellationToken).ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (!written)
            {
                await StorageAccess
                    .TryWriteToCacheAsync(
                        cacheEnabled: true,
                        () => messageCache.IsWriteAvailable,
                        () => messageCache.AddAsync(message, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var expiredTickets = await StorageAccess
            .TryListAsync(() => ticketCache.TakeExpiredAsync(olderThan, cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        foreach (var expired in expiredTickets)
        {
            var written = await StorageAccess
                .TryWriteAsync(() => tickets.AddAsync(expired.Ticket, cancellationToken), cancellationToken)
                .ConfigureAwait(false);

            if (!written)
            {
                await StorageAccess
                    .TryWriteToCacheAsync(
                        cacheEnabled: true,
                        () => ticketCache.IsWriteAvailable,
                        () => ticketCache.AddAsync(expired, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
