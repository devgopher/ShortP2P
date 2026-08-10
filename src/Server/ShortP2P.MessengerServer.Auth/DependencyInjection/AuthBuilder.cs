using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ShortP2P.MessengerServer.Auth.DependencyInjection;

/// <summary>Fluent builder returned by <see cref="AuthServiceCollectionExtensions.AddAuth"/>.</summary>
public sealed class AuthBuilder
{
    internal AuthBuilder(IServiceCollection services, IConfiguration configuration)
    {
        Services = services;
        Configuration = configuration;
    }

    public IServiceCollection Services { get; }

    public IConfiguration Configuration { get; }
}
