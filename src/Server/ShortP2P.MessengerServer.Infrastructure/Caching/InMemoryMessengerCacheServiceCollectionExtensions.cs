using Microsoft.Extensions.DependencyInjection;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Infrastructure.Caching;

public static class InMemoryMessengerCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers shared in-memory <see cref="IMessageCache"/> and <see cref="IDeliveryTicketCache"/>
    /// with an optional memory limit (<see cref="InMemoryMessengerCacheOptions.MaxMemoryMegabytes"/>).
    /// </summary>
    public static IServiceCollection AddInMemoryMessengerCaches(
        this IServiceCollection services,
        Action<InMemoryMessengerCacheOptions>? configure = null)
    {
        var options = new InMemoryMessengerCacheOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<InMemoryCacheMemoryTracker>();
        services.AddSingleton<IMessageCache, InMemoryMessageCache>();
        services.AddSingleton<IDeliveryTicketCache, InMemoryDeliveryTicketCache>();
        return services;
    }
}
