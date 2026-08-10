using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortP2P.MessengerServer.Auth.DependencyInjection;
using ShortP2P.MessengerServer.Auth.LiteDB.Options;
using ShortP2P.MessengerServer.Auth.LiteDB.Repositories;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Auth.LiteDB.DependencyInjection;

public static class AuthLiteDbExtensions
{
    /// <summary>Registers LiteDB <see cref="IClientAccountRepository"/>.</summary>
    public static AuthBuilder WithLiteDb(this AuthBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Configure<AuthLiteDbOptions>(
            builder.Configuration.GetSection(AuthLiteDbOptions.Section));

        var options = new AuthLiteDbOptions();
        builder.Configuration.GetSection(AuthLiteDbOptions.Section).Bind(options);
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("Auth:LiteDb:ConnectionString is required.");

        builder.Services.AddSingleton<IClientAccountRepository, LiteDbClientAccountRepository>();
        return builder;
    }
}
