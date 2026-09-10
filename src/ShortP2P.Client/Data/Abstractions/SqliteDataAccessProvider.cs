using SQLite;

namespace ShortP2P.Client.Data.Abstractions;

/// <summary>
/// SQLite implementation of data connection (synchronous)
/// </summary>
internal sealed class SqliteDataConnection : IDataConnection
{
    private readonly SQLiteConnection _connection;

    public SqliteDataConnection(SQLiteConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public IQueryable<T> Table<T>() where T : class, new() =>
        _connection.Table<T>().AsQueryable();

    public int Execute(string sql, params object[] args) =>
        _connection.Execute(sql, args);

    public T? ExecuteScalar<T>(string sql, params object[] args) =>
        _connection.ExecuteScalar<T>(sql, args);

    public int Insert(object entity) =>
        _connection.Insert(entity);

    public int Update(object entity) =>
        _connection.Update(entity);

    public int Delete(object entity) =>
        _connection.Delete(entity);

    public T? Find<T>(object pk) where T : class, new() =>
        _connection.Find<T>(pk);

    public int CreateTable<T>() where T : class, new()
    {
        var result = _connection.CreateTable<T>();
        return result == CreateTableResult.Created ? 1 : 0;
    }

    public List<string> GetTableInfo<T>() where T : class
    {
        var mapping = _connection.GetMapping(typeof(T));
        var columns = new List<string>();
        foreach (var column in mapping.Columns)
        {
            columns.Add(column.Name);
        }
        return columns;
    }
}

/// <summary>
/// SQLite async implementation of data connection
/// </summary>
internal sealed class SqliteAsyncDataConnection : IDataConnection
{
    private readonly SQLiteAsyncConnection _connection;

    public SqliteAsyncDataConnection(SQLiteAsyncConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public IQueryable<T> Table<T>() where T : class, new() =>
        _connection.Table<T>().ToListAsync().GetAwaiter().GetResult().AsQueryable();

    public int Execute(string sql, params object[] args) =>
        _connection.ExecuteAsync(sql, args).GetAwaiter().GetResult();

    public T? ExecuteScalar<T>(string sql, params object[] args) =>
        _connection.ExecuteScalarAsync<T>(sql, args).GetAwaiter().GetResult();

    public int Insert(object entity) =>
        _connection.InsertAsync(entity).GetAwaiter().GetResult();

    public int Update(object entity) =>
        _connection.UpdateAsync(entity).GetAwaiter().GetResult();

    public int Delete(object entity) =>
        _connection.DeleteAsync(entity).GetAwaiter().GetResult();

    public T? Find<T>(object pk) where T : class, new() =>
        _connection.FindAsync<T>(pk).GetAwaiter().GetResult();

    public int CreateTable<T>() where T : class, new()
    {
        var result = _connection.CreateTableAsync<T>().GetAwaiter().GetResult();
        return result == CreateTableResult.Created ? 1 : 0;
    }

    public List<string> GetTableInfo<T>() where T : class
    {
        // SQLiteAsyncConnection doesn't have GetMapping, we need to use a workaround
        // Get the table info via query
        var tableName = typeof(T).Name;
        var query = $"PRAGMA table_info({tableName})";
        var result = _connection.QueryAsync<dynamic>(query).GetAwaiter().GetResult();
        var columns = new List<string>();
        foreach (var row in result)
        {
            columns.Add(row.name);
        }
        return columns;
    }
}

/// <summary>
/// Adapter that wraps existing AppDatabase with IDataAccessProvider interface
/// </summary>
public sealed class SqliteDataAccessProvider : IDataAccessProvider
{
    private readonly AppDatabase _appDatabase;

    public DatabaseProviderType ProviderType => DatabaseProviderType.Sqlite;

    public SqliteDataAccessProvider(AppDatabase appDatabase)
    {
        _appDatabase = appDatabase ?? throw new ArgumentNullException(nameof(appDatabase));
    }

    // Synchronous methods (for backward compatibility)
    public void Initialize()
    {
        InitializeAsync().GetAwaiter().GetResult();
    }

    public IDataConnection GetConnection()
    {
        return GetWriteConnectionAsync().GetAwaiter().GetResult();
    }

    public void Write(Action<IDataConnection> work)
    {
        WriteAsync(conn =>
        {
            work(conn);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
    }

    public T Write<T>(Func<IDataConnection, T> work)
    {
        return WriteAsync(conn => Task.FromResult(work(conn))).GetAwaiter().GetResult();
    }

    public T Read<T>(Func<IDataConnection, T> work)
    {
        return ReadAsync(conn => Task.FromResult(work(conn))).GetAwaiter().GetResult();
    }

    public void EnsureSchema()
    {
        EnsureSchemaAsync().GetAwaiter().GetResult();
    }

    public void Close()
    {
        CloseAsync().GetAwaiter().GetResult();
    }

    // Async methods (preferred)
    public async Task InitializeAsync()
    {
        // AppDatabase initializes itself on first connection
        await _appDatabase.GetWriteConnectionAsync().ConfigureAwait(false);
    }

    public async Task<IDataConnection> GetConnectionAsync()
    {
        return await GetWriteConnectionAsync().ConfigureAwait(false);
    }

    public async Task<IDataConnection> GetWriteConnectionAsync()
    {
        var conn = await _appDatabase.GetWriteConnectionAsync().ConfigureAwait(false);
        return new SqliteAsyncDataConnection(conn);
    }

    public async Task<IDataConnection> GetReadConnectionAsync()
    {
        var conn = await _appDatabase.GetReadConnectionAsync().ConfigureAwait(false);
        return new SqliteAsyncDataConnection(conn);
    }

    public async Task WriteAsync(Func<IDataConnection, Task> work)
    {
        await _appDatabase.WriteAsync(async conn =>
        {
            var wrappedConn = new SqliteAsyncDataConnection(conn);
            await work(wrappedConn).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    public async Task<T> WriteAsync<T>(Func<IDataConnection, Task<T>> work)
    {
        return await _appDatabase.WriteAsync(async conn =>
        {
            var wrappedConn = new SqliteAsyncDataConnection(conn);
            return await work(wrappedConn).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task<T> ReadAsync<T>(Func<IDataConnection, Task<T>> work)
    {
        return await _appDatabase.ReadAsync(async conn =>
        {
            var wrappedConn = new SqliteAsyncDataConnection(conn);
            return await work(wrappedConn).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task EnsureSchemaAsync()
    {
        await _appDatabase.GetWriteConnectionAsync().ConfigureAwait(false);
    }

    public async Task CloseAsync()
    {
        // AppDatabase doesn't expose close, it manages connections internally
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
