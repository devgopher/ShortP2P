using ShortP2P.MessengerServer.Infrastructure;
using ShortP2P.MessengerServer.Infrastructure.Caching;
using ShortP2P.MessengerServer.UseCases.Abstractions;
using ShortP2P.MessengerServer.UseCases.Hosting;

namespace ShortP2P.MessengerServer.Api.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers shared infrastructure (clock + <see cref="MessengerCacheOptions"/> from config).
    /// Continue with <see cref="InfrastructureBuilder.WithInMemoryCache"/>.
    /// </summary>
    public static InfrastructureBuilder AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IClock, SystemClock>();

        var cacheOptions = new MessengerCacheOptions();
        configuration.GetSection("MessengerCache").Bind(cacheOptions);
        services.AddSingleton(cacheOptions);

        return new InfrastructureBuilder(services, configuration, cacheOptions);
    }
}

public sealed class InfrastructureBuilder
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;
    private readonly MessengerCacheOptions _cacheOptions;
    private bool _cacheRegistered;

    internal InfrastructureBuilder(
        IServiceCollection services,
        IConfiguration configuration,
        MessengerCacheOptions cacheOptions)
    {
        _services = services;
        _configuration = configuration;
        _cacheOptions = cacheOptions;
    }

    /// <summary>
    /// Registers in-memory message/ticket caches bound from
    /// <see cref="InMemoryMessengerCacheOptions.Section"/>.
    /// </summary>
    public InfrastructureBuilder WithInMemoryCache()
    {
        if (_cacheRegistered)
            return this;

        var section = _configuration.GetSection(InMemoryMessengerCacheOptions.Section);
        _services.AddInMemoryMessengerCaches(o => section.Bind(o));
        _cacheOptions.CacheEnabled = true;
        _cacheRegistered = true;
        return this;
    }

    /// <summary>
    /// Registers the TTL promotion hosted service (cache → durable repository).
    /// Safe to call when repository is disabled — the service no-ops.
    /// </summary>
    public InfrastructureBuilder WithCachePromotion()
    {
       // _services.AddHostedService<ExpiredCachePromotionHostedService>();
        return this;
    }
}
