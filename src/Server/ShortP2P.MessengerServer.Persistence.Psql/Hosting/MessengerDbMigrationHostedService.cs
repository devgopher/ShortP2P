using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShortP2P.MessengerServer.Persistence.Psql;

namespace ShortP2P.MessengerServer.Persistence.Psql.Hosting;

/// <summary>Applies EF Core migrations for <see cref="MessengerDbContext"/> on host start.</summary>
public sealed class MessengerDbMigrationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<MessengerDbMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Messenger PostgreSQL migrations applied");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
