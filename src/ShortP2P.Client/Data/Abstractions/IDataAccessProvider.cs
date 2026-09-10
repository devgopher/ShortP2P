namespace ShortP2P.Client.Data.Abstractions;

/// <summary>
/// Represents database provider type (SQLite, LiteDB, etc.)
/// </summary>
public enum DatabaseProviderType
{
    Sqlite = 0,
    LiteDbAsync = 1
}

/// <summary>
/// Abstract connection for database operations. Allows abstraction over SQLite and LiteDB connections.
/// Supports synchronous operations (matching SQLite's capabilities).
/// </summary>
public interface IDataConnection
{
    /// <summary>
    /// Get a table/collection queryable interface for type T
    /// </summary>
    IQueryable<T> Table<T>() where T : class, new();

    /// <summary>
    /// Execute raw SQL/query without returning results
    /// </summary>
    int Execute(string sql, params object[] args);

    /// <summary>
    /// Execute raw SQL and return scalar value
    /// </summary>
    T? ExecuteScalar<T>(string sql, params object[] args);

    /// <summary>
    /// Insert entity
    /// </summary>
    int Insert(object entity);

    /// <summary>
    /// Update entity
    /// </summary>
    int Update(object entity);

    /// <summary>
    /// Delete entity
    /// </summary>
    int Delete(object entity);

    /// <summary>
    /// Find entity by primary key
    /// </summary>
    T? Find<T>(object pk) where T : class, new();

    /// <summary>
    /// Create table for type T if not exists
    /// </summary>
    int CreateTable<T>() where T : class, new();

    /// <summary>
    /// Get table info for type T
    /// </summary>
    List<string> GetTableInfo<T>() where T : class;
}

/// <summary>
/// Abstract data access provider that supports multiple database backends (SQLite, LiteDB, etc.)
/// </summary>
public interface IDataAccessProvider
{
    /// <summary>
    /// Provider type identifier
    /// </summary>
    DatabaseProviderType ProviderType { get; }

    /// <summary>
    /// Initialize the provider (create/migrate database, etc.)
    /// </summary>
    void Initialize();

    /// <summary>
    /// Get a connection for database operations
    /// </summary>
    IDataConnection GetConnection();

    /// <summary>
    /// Execute a write operation with exclusive access
    /// </summary>
    void Write(Action<IDataConnection> work);

    /// <summary>
    /// Execute a write operation with exclusive access and return result
    /// </summary>
    T Write<T>(Func<IDataConnection, T> work);

    /// <summary>
    /// Execute a read operation
    /// </summary>
    T Read<T>(Func<IDataConnection, T> work);

    /// <summary>
    /// Ensure that database schema is created
    /// </summary>
    void EnsureSchema();

    /// <summary>
    /// Close provider and release resources
    /// </summary>
    void Close();

    // Async methods
    /// <summary>
    /// Initialize the provider asynchronously
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Get a connection asynchronously (alias for GetWriteConnectionAsync for backward compatibility)
    /// </summary>
    Task<IDataConnection> GetConnectionAsync();

    /// <summary>
    /// Get a write connection asynchronously
    /// </summary>
    Task<IDataConnection> GetWriteConnectionAsync();

    /// <summary>
    /// Get a read connection asynchronously
    /// </summary>
    Task<IDataConnection> GetReadConnectionAsync();

    /// <summary>
    /// Execute a write operation asynchronously with exclusive access
    /// </summary>
    Task WriteAsync(Func<IDataConnection, Task> work);

    /// <summary>
    /// Execute a write operation asynchronously with exclusive access and return result
    /// </summary>
    Task<T> WriteAsync<T>(Func<IDataConnection, Task<T>> work);

    /// <summary>
    /// Execute a read operation asynchronously
    /// </summary>
    Task<T> ReadAsync<T>(Func<IDataConnection, Task<T>> work);

    /// <summary>
    /// Ensure that database schema is created asynchronously
    /// </summary>
    Task EnsureSchemaAsync();

    /// <summary>
    /// Close provider and release resources asynchronously
    /// </summary>
    Task CloseAsync();
}

/// <summary>
/// Configuration for data access provider
/// </summary>
public class DataAccessProviderConfig
{
    /// <summary>
    /// Path to database file
    /// </summary>
    public string DatabasePath { get; set; } = null!;

    /// <summary>
    /// Database provider type to use
    /// </summary>
    public DatabaseProviderType ProviderType { get; set; } = DatabaseProviderType.Sqlite;

    /// <summary>
    /// Read connection pool size (for SQLite)
    /// </summary>
    public int ReadPoolSize { get; set; } = 4;
}
