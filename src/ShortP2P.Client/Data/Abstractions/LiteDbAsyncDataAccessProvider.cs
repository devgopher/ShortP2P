using System.Linq;
using LiteDB;

namespace ShortP2P.Client.Data.Abstractions;

/// <summary>
/// LiteDB implementation of data connection
/// </summary>
internal sealed class LiteDbDataConnection : IDataConnection
{
    private readonly ILiteDatabase _database;

    public LiteDbDataConnection(ILiteDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public IQueryable<T> Table<T>() where T : class, new()
    {
        try
        {
            return _database.GetCollection<T>().Query().ToEnumerable().AsQueryable();
        }
        catch
        {
            return Enumerable.Empty<T>().AsQueryable();
        }
    }

    public int Execute(string sql, params object[] args)
    {
        // LiteDB doesn't support raw SQL like SQLite
        return 0;
    }

    public T? ExecuteScalar<T>(string sql, params object[] args)
    {
        // LiteDB doesn't support raw SQL like SQLite
        return default(T);
    }

    public int Insert(object entity)
    {
        var type = entity.GetType();
        var method = typeof(ILiteDatabase).GetMethod(nameof(ILiteDatabase.GetCollection), Type.EmptyTypes)?
            .MakeGenericMethod(type);
        
        if (method is null)
            throw new InvalidOperationException($"Cannot get collection for type {type}");

        var collection = (dynamic)method.Invoke(_database, Array.Empty<object>())!;
        collection.Insert((dynamic)entity);
        
        return 1;
    }

    public int Update(object entity)
    {
        var type = entity.GetType();
        var method = typeof(ILiteDatabase).GetMethod(nameof(ILiteDatabase.GetCollection), Type.EmptyTypes)?
            .MakeGenericMethod(type);
        
        if (method is null)
            throw new InvalidOperationException($"Cannot get collection for type {type}");

        var collection = (dynamic)method.Invoke(_database, Array.Empty<object>())!;
        var result = (bool)collection.Update((dynamic)entity);
        
        return result ? 1 : 0;
    }

    public int Delete(object entity)
    {
        var type = entity.GetType();
        var method = typeof(ILiteDatabase).GetMethod(nameof(ILiteDatabase.GetCollection), Type.EmptyTypes)?
            .MakeGenericMethod(type);
        
        if (method is null)
            throw new InvalidOperationException($"Cannot get collection for type {type}");

        // For LiteDB, we need the entity to have an Id property
        var idProperty = type.GetProperty("Id");
        if (idProperty is null)
            throw new InvalidOperationException($"Type {type} must have an Id property");

        var collection = (dynamic)method.Invoke(_database, Array.Empty<object>())!;
        var id = idProperty.GetValue(entity);
        var bsonValue = new BsonValue(id);
        var result = (bool)collection.Delete(bsonValue);
        
        return result ? 1 : 0;
    }

    public T? Find<T>(object pk) where T : class, new()
    {
        var collection = _database.GetCollection<T>();
        var bsonValue = new BsonValue(pk);
        return collection.FindById(bsonValue);
    }

    public int CreateTable<T>() where T : class, new()
    {
        // LiteDB creates collections automatically
        _database.GetCollection<T>();
        return 0;
    }

    public List<string> GetTableInfo<T>() where T : class
    {
        // Use reflection to get property names
        var properties = typeof(T).GetProperties();
        return properties.Select(p => p.Name).ToList();
    }
}

/// <summary>
/// LiteDB implementation of IDataAccessProvider
/// </summary>
public sealed class LiteDbAsyncDataAccessProvider : IDataAccessProvider
{
    private readonly string _databasePath;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    
    private ILiteDatabase? _database;
    private bool _initialized;

    public DatabaseProviderType ProviderType => DatabaseProviderType.LiteDbAsync;

    public LiteDbAsyncDataAccessProvider(string databasePath)
    {
        _databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
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
        await EnsureInitializedAsync().ConfigureAwait(false);
    }

    public async Task<IDataConnection> GetConnectionAsync()
    {
        return await GetWriteConnectionAsync().ConfigureAwait(false);
    }

    public async Task<IDataConnection> GetWriteConnectionAsync()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return new LiteDbDataConnection(_database!);
    }

    public async Task<IDataConnection> GetReadConnectionAsync()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return new LiteDbDataConnection(_database!);
    }

    public async Task WriteAsync(Func<IDataConnection, Task> work)
    {
        if (work is null)
            throw new ArgumentNullException(nameof(work));

        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            var conn = new LiteDbDataConnection(_database!);
            await work(conn).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<T> WriteAsync<T>(Func<IDataConnection, Task<T>> work)
    {
        if (work is null)
            throw new ArgumentNullException(nameof(work));

        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            var conn = new LiteDbDataConnection(_database!);
            return await work(conn).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<T> ReadAsync<T>(Func<IDataConnection, Task<T>> work)
    {
        if (work is null)
            throw new ArgumentNullException(nameof(work));

        await EnsureInitializedAsync().ConfigureAwait(false);
        var conn = new LiteDbDataConnection(_database!);
        return await work(conn).ConfigureAwait(false);
    }

    public async Task EnsureSchemaAsync()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
    }

    public async Task CloseAsync()
    {
        if (_database != null)
        {
            _database.Dispose();
            _database = null;
            _initialized = false;
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        await _initGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            var dir = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            _database = new LiteDatabase(_databasePath);
            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }
}
