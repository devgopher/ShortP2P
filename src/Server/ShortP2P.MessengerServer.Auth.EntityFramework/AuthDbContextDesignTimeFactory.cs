using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ShortP2P.MessengerServer.Auth.EntityFramework;

/// <summary>Design-time factory (Sqlite by default). Override via env <c>AUTH_EF_DB</c>.</summary>
public sealed class AuthDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("AUTH_EF_DB")
            ?? "Data Source=messenger-auth.db";

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new AuthDbContext(options);
    }
}
