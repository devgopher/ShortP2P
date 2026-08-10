namespace ShortP2P.MessengerServer.Api.Options;

/// <summary>PostgreSQL persistence settings. When <see cref="Enabled"/> is false, in-memory repositories are used instead.</summary>
public sealed class PersistenceOptions
{
    /// <summary>Configuration section name in appsettings.</summary>
    public const string Section = "Persistence";

    /// <summary>When false, Postgres is not registered and durable message store is off.</summary>
    public bool Enabled { get; set; } = true;

    public string ConnectionString { get; set; } =
        "Host=localhost;Port=5432;Database=shortp2p_messenger;Username=postgres;Password=postgres";

    public bool ApplyMigrationsOnStartup { get; set; } = true;
}
