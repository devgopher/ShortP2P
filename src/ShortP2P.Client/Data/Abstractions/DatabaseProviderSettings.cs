namespace ShortP2P.Client.Data.Abstractions;

/// <summary>
/// Manages database provider selection and persistence
/// </summary>
public class DatabaseProviderSettings
{
    private readonly string _settingsPath;
    private DatabaseProviderType _currentProvider = DatabaseProviderType.LiteDbAsync;

    public DatabaseProviderSettings(string appDataDirectory)
    {
        _settingsPath = Path.Combine(appDataDirectory, "db-provider.json");
        Load();
    }

    /// <summary>
    /// Current provider type
    /// </summary>
    public DatabaseProviderType CurrentProvider
    {
        get => _currentProvider;
        set
        {
            if (_currentProvider != value)
            {
                _currentProvider = value;
                Save();
            }
        }
    }

    /// <summary>
    /// Get all available provider types
    /// </summary>
    public static DatabaseProviderType[] AvailableProviders =>
        new[] { DatabaseProviderType.LiteDbAsync, DatabaseProviderType.Sqlite };

    /// <summary>
    /// User-facing label (keeps enum/type names technical).
    /// </summary>
    public static string GetDisplayName(DatabaseProviderType provider) => provider switch
    {
        DatabaseProviderType.LiteDbAsync => "LiteDB",
        _ => provider.ToString()
    };

    private void Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return;

            var json = File.ReadAllText(_settingsPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("providerType", out var providerProp))
            {
                if (Enum.TryParse<DatabaseProviderType>(providerProp.GetString(), true, out var provider))
                {
                    _currentProvider = provider;
                }
            }
        }
        catch
        {
            // If load fails, use default provider
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                providerType = _currentProvider.ToString(),
                timestamp = DateTime.UtcNow
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // If save fails, we'll just keep the current value
        }
    }
}
