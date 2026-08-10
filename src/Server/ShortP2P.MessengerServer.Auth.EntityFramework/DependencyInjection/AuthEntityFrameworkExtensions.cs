using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortP2P.MessengerServer.Auth.DependencyInjection;
using ShortP2P.MessengerServer.Auth.EntityFramework.Hosting;
using ShortP2P.MessengerServer.Auth.EntityFramework.Options;
using ShortP2P.MessengerServer.Auth.EntityFramework.Repositories;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Auth.EntityFramework.DependencyInjection;

public static class AuthEntityFrameworkExtensions
{
    /// <summary>
    /// Registers EF Core <see cref="IClientAccountRepository"/> (Sqlite or Npgsql from config).
    /// </summary>
    public static AuthBuilder WithEntityFrameworkDb(this AuthBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;
        var configuration = builder.Configuration;

        services.Configure<AuthEntityFrameworkOptions>(
            configuration.GetSection(AuthEntityFrameworkOptions.Section));

        var options = new AuthEntityFrameworkOptions();
        configuration.GetSection(AuthEntityFrameworkOptions.Section).Bind(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("Auth:EntityFramework:ConnectionString is required.");

        services.AddDbContext<AuthDbContext>(db => ConfigureProvider(db, options));
        services.AddScoped<IClientAccountRepository, EfClientAccountRepository>();

        if (options.ApplyMigrationsOnStartup)
            services.AddHostedService<AuthDbMigrationHostedService>();

        return builder;
    }

    private static void ConfigureProvider(DbContextOptionsBuilder db, AuthEntityFrameworkOptions options)
    {
        var provider = options.Provider?.Trim() ?? "Sqlite";
        if (provider.Equals("Npgsql", StringComparison.OrdinalIgnoreCase)
            || provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)
            || provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            db.UseNpgsql(options.ConnectionString);
            return;
        }

        if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)
            || provider.Equals("SQLite", StringComparison.OrdinalIgnoreCase))
        {
            db.UseSqlite(options.ConnectionString);
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported Auth:EntityFramework:Provider '{options.Provider}'. Use Sqlite or Npgsql.");
    }
}
