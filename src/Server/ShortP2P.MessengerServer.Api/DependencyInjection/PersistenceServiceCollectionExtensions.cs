using ShortP2P.MessengerServer.Api.Options;
using ShortP2P.MessengerServer.Api.Persistence.InMemory;
using ShortP2P.MessengerServer.Persistence.Psql;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.DependencyInjection;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers messenger persistence from the <c>Persistence</c> section.
    /// Accounts are registered separately via <c>AddAuth().With*Db()</c>.
    /// When <see cref="PersistenceOptions.Enabled"/> is false, in-memory messenger repos are used
    /// and <see cref="MessengerCacheOptions.RepositoryEnabled"/> is set to false.
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<PersistenceOptions>(configuration.GetSection(PersistenceOptions.Section));

        var options = new PersistenceOptions();
        configuration.GetSection(PersistenceOptions.Section).Bind(options);

        foreach (var descriptor in services)
        {
            if (descriptor.ImplementationInstance is MessengerCacheOptions cacheOptions)
                cacheOptions.RepositoryEnabled = options.Enabled;
        }

        if (options.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException(
                    "Persistence:ConnectionString is required when Persistence:Enabled is true.");
            }

            services.AddMessengerPostgres(options.ConnectionString, options.ApplyMigrationsOnStartup);
        }
        else
        {
            services.AddSingleton<InMemoryMessengerStore>();
            services.AddSingleton<IClientStatusRepository, InMemoryClientStatusRepository>();
            services.AddSingleton<IChatRepository, InMemoryChatRepository>();
            services.AddSingleton<IChatRequestRepository, InMemoryChatRequestRepository>();
            services.AddSingleton<ICryptoKeysRepository, InMemoryCryptoKeysRepository>();
            services.AddSingleton<IMessageRepository, InMemoryMessageRepository>();
            services.AddSingleton<IDeliveryTicketRepository, InMemoryDeliveryTicketRepository>();
        }

        return services;
    }
}
