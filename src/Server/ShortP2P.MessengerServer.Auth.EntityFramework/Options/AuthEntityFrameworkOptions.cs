namespace ShortP2P.MessengerServer.Auth.EntityFramework.Options;

/// <summary>EF Core auth store settings (relational, provider-agnostic).</summary>
public sealed class AuthEntityFrameworkOptions
{
    /// <summary>Nested under <c>Auth</c>: <c>Auth:EntityFramework</c>.</summary>
    public const string Section = "Auth:EntityFramework";

    /// <summary>Sqlite (default) or Npgsql.</summary>
    public string Provider { get; set; } = "Sqlite";

    public string ConnectionString { get; set; } = "Data Source=messenger-auth.db";

    public bool ApplyMigrationsOnStartup { get; set; } = true;
}
