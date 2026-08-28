using ShortP2P.Auth.Data;
using SQLite;

namespace ShortP2P.Client.Data;

public sealed class AppDatabase(string databasePath)
{
    private readonly string _databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
    private SQLiteAsyncConnection? _connection;

    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_connection != null)
            return _connection;

        var dir = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connection = new SQLiteAsyncConnection(_databasePath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
        await _connection.CreateTableAsync<UserEntity>();
        await _connection.CreateTableAsync<ChatEntity>();
        await _connection.CreateTableAsync<ChatMessageEntity>();
        await _connection.CreateTableAsync<BleDiscoveredPeerEntity>();
        await _connection.CreateTableAsync<MessengerServerEntity>();
        try
        {
            await _connection.ExecuteAsync("ALTER TABLE chats ADD COLUMN RelayRouteBlob TEXT NULL");
        }
        catch
        {
            // column already exists
        }

        try
        {
            await _connection.ExecuteAsync("ALTER TABLE chats ADD COLUMN PeerEndpointsJson TEXT NULL");
        }
        catch
        {
            // column already exists
        }

        try
        {
            await _connection.ExecuteAsync("ALTER TABLE chats ADD COLUMN PeerKeySourceKind TEXT NULL");
        }
        catch
        {
            // column already exists
        }

        try
        {
            await _connection.ExecuteAsync("ALTER TABLE chats ADD COLUMN PeerKeySourceDetail TEXT NULL");
        }
        catch
        {
            // column already exists
        }

        try
        {
            await _connection.ExecuteAsync(
                "ALTER TABLE messages ADD COLUMN DeliveryStatus INTEGER NOT NULL DEFAULT 2");
        }
        catch
        {
            // column already exists
        }

        try
        {
            await _connection.ExecuteAsync("UPDATE messages SET DeliveryStatus = 0 WHERE Outgoing = 0");
        }
        catch
        {
            // ignore
        }

        try
        {
            await _connection.ExecuteAsync(
                "ALTER TABLE messages ADD COLUMN PayloadKind INTEGER NOT NULL DEFAULT 0");
        }
        catch
        {
            // column already exists
        }

        try
        {
            await _connection.ExecuteAsync("ALTER TABLE messages ADD COLUMN MimeType TEXT NOT NULL DEFAULT ''");
        }
        catch
        {
            // column already exists
        }

        try
        {
            await _connection.ExecuteAsync("ALTER TABLE messages ADD COLUMN ImageBlob BLOB NULL");
        }
        catch
        {
            // column already exists
        }

        try
        {
            await _connection.ExecuteAsync("ALTER TABLE messages ADD COLUMN TransferId TEXT NOT NULL DEFAULT ''");
        }
        catch
        {
            // column already exists
        }

        try
        {
            await _connection.ExecuteAsync("ALTER TABLE messages ADD COLUMN TransferToken TEXT NOT NULL DEFAULT ''");
        }
        catch
        {
            // column already exists
        }

        try
        {
            await _connection.ExecuteAsync(
                "ALTER TABLE messages ADD COLUMN TransferPayloadKind TEXT NOT NULL DEFAULT ''");
        }
        catch
        {
            // column already exists
        }

        try
        {
            await _connection.ExecuteAsync("ALTER TABLE messages ADD COLUMN TransferFileName TEXT NOT NULL DEFAULT ''");
        }
        catch
        {
            // column already exists
        }

        try
        {
            await _connection.ExecuteAsync(
                "ALTER TABLE messages ADD COLUMN TransferSizeBytes INTEGER NOT NULL DEFAULT 0");
        }
        catch
        {
            // column already exists
        }

        try
        {
            await _connection.ExecuteAsync("ALTER TABLE messages ADD COLUMN TransferHost TEXT NOT NULL DEFAULT ''");
        }
        catch
        {
            // column already exists
        }

        try
        {
            await _connection.ExecuteAsync("ALTER TABLE messages ADD COLUMN TransferPort INTEGER NOT NULL DEFAULT 0");
        }
        catch
        {
            // column already exists
        }

        try
        {
            await _connection.ExecuteAsync(
                "ALTER TABLE messages ADD COLUMN TransferExpiresUtcTicks INTEGER NOT NULL DEFAULT 0");
        }
        catch
        {
            // column already exists
        }

        try
        {
            await _connection.ExecuteAsync("ALTER TABLE messages ADD COLUMN TransferState INTEGER NOT NULL DEFAULT 0");
        }
        catch
        {
            // column already exists
        }

        await MigrateBleDiscoveredPeersToFullNetworkIdAsync(_connection).ConfigureAwait(false);

        return _connection;
    }

    private static async Task MigrateBleDiscoveredPeersToFullNetworkIdAsync(SQLiteAsyncConnection connection)
    {
        const string flagKey = "network_id_12_bytes_v3";
        try
        {
            await connection.ExecuteAsync(
                    "CREATE TABLE IF NOT EXISTS app_schema_flags (key TEXT PRIMARY KEY, value TEXT NOT NULL)")
                .ConfigureAwait(false);
            var applied = await connection.ExecuteScalarAsync<string>(
                "SELECT value FROM app_schema_flags WHERE key = ?", flagKey).ConfigureAwait(false);
            if (string.Equals(applied, "1", StringComparison.Ordinal))
                return;

            await connection.ExecuteAsync("DELETE FROM ble_discovered_peers").ConfigureAwait(false);
            await connection.ExecuteAsync(
                    "INSERT OR REPLACE INTO app_schema_flags (key, value) VALUES (?, ?)", flagKey, "1")
                .ConfigureAwait(false);
        }
        catch
        {
            // ignore migration issues on very old DB files
        }
    }
}