using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ShortP2P.Discovery.RouteTables;

/// <summary>
///     Периодическое удаление <see cref="PeerIdentityAddress" />, у которых истёк срок по <see cref="PeerIdentityAddress.LastSeen" />.
/// </summary>
public static class RoutePeerRoutesExpiryCleanup
{
    /// <summary>
    ///     Удаляет строки, у которых <see cref="PeerIdentityAddress.LastSeen" /> старше <paramref name="staleAfter" /> от текущего UTC.
    /// </summary>
    public static async Task RunOnceAsync(RouteDbContext db, TimeSpan staleAfter, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - staleAfter;
        await db.Routes
            .SelectMany(r => r.PeerRoutes!)
            .Where(p => p.LastSeen < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    ///     Цикл: очистка, затем ожидание <see cref="RoutePeerRoutesExpiryOptions.CleanupPeriod" />. Первая очистка сразу после старта.
    /// </summary>
    public static async Task RunPeriodicAsync(
        IServiceScopeFactory scopeFactory,
        RoutePeerRoutesExpiryOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<RouteDbContext>();
                    await RunOnceAsync(db, options.StaleAfter, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Route peer address expiry cleanup failed");
                }

                await Task.Delay(options.CleanupPeriod, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    ///     Запуск <see cref="RunPeriodicAsync" /> в непривязанной задаче (WinForms и другие процессы без generic host).
    /// </summary>
    public static void StartDetached(
        IServiceProvider rootProvider,
        RoutePeerRoutesExpiryOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var scopeFactory = rootProvider.GetRequiredService<IServiceScopeFactory>();
        _ = RunPeriodicAsync(scopeFactory, options, logger, cancellationToken);
    }
}
