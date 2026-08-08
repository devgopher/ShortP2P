using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortP2P.MessengerServer.Persistence.Psql.Hosting;
using ShortP2P.MessengerServer.Persistence.Psql.Repositories;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Persistence.Psql;

public static class MessengerPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers PostgreSQL <see cref="MessengerDbContext"/>, repositories and automatic migration on startup.
    /// </summary>
    public static IServiceCollection AddMessengerPostgres(
        this IServiceCollection services,
        string connectionString,
        bool applyMigrationsOnStartup = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<MessengerDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IClientAccountRepository, PostgresClientAccountRepository>();
        services.AddScoped<IChatRepository, PostgresChatRepository>();
        services.AddScoped<IChatRequestRepository, PostgresChatRequestRepository>();
        services.AddScoped<ICryptoKeysRepository, PostgresCryptoKeysRepository>();
        services.AddScoped<IClientStatusRepository, PostgresClientStatusRepository>();
        services.AddScoped<IMessageRepository, PostgresMessageRepository>();
        services.AddScoped<IDeliveryTicketRepository, PostgresDeliveryTicketRepository>();

        if (applyMigrationsOnStartup)
            services.AddHostedService<MessengerDbMigrationHostedService>();

        return services;
    }
}
