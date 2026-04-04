using SQLite;

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
        return _connection;
    }
}
