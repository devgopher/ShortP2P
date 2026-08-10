using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ShortP2P.MessengerServer.Contracts;

namespace ShortP2P.MessengerServer.Http.Extensions;

public static class ServerApiClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IMessengerServerApi"/> from <see cref="ServerApiClientSettings"/>
    /// (section <c>ServerApiClientSettings</c>).
    /// </summary>
    public static IHttpClientBuilder AddServerApiClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ServerApiClientSettings>(
            configuration.GetSection(ServerApiClientSettings.Section));

        return services.AddServerApiClientCore();
    }

    /// <summary>
    /// Registers <see cref="IMessengerServerApi"/> with programmatic <see cref="ServerApiClientSettings"/>.
    /// </summary>
    public static IHttpClientBuilder AddServerApiClient(
        this IServiceCollection services,
        Action<ServerApiClientSettings>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
            services.Configure(configure);

        return services.AddServerApiClientCore();
    }

    private static IHttpClientBuilder AddServerApiClientCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IMessengerServerSession, MessengerServerSession>();
        services.TryAddTransient<MessengerServerBearerHandler>();

        return services
            .AddHttpClient<IMessengerServerApi, MessengerServerApiClient>((sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<ServerApiClientSettings>>().Value;
                if (string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    throw new InvalidOperationException(
                        $"Set {ServerApiClientSettings.Section}:BaseUrl (e.g. https://localhost:7196).");
                }

                if (!Uri.TryCreate(settings.BaseUrl.Trim(), UriKind.Absolute, out var baseAddress))
                {
                    throw new InvalidOperationException(
                        $"{ServerApiClientSettings.Section}:BaseUrl is not a valid absolute URI: '{settings.BaseUrl}'.");
                }

                client.BaseAddress = baseAddress;
                client.Timeout = settings.Timeout;
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .AddHttpMessageHandler<MessengerServerBearerHandler>();
    }
}
