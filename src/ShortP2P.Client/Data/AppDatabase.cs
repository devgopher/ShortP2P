using SQLite;
using ShortP2P.Auth.Data;

namespace ShortP2P.Client.Data;

public sealed class AppDatabase
{
    private readonly string _databasePath;
    private SQLiteAsyncConnection? _connection;

    public AppDatabase(string databasePath)
    {
        _databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
    }

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
            await _connection.ExecuteAsync("ALTER TABLE messages ADD COLUMN TransferSizeBytes INTEGER NOT NULL DEFAULT 0");
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

        try
        {
            await _connection.ExecuteAsync(
                "ALTER TABLE ble_discovered_peers ADD COLUMN PeerNetworkIdHintHex TEXT NULL");
        }
        catch
        {
            // column already exists
        }

        return _connection;
    }
}
