namespace ShortP2P.MessengerServer.UseCases.Abstractions;

/// <summary>Best-effort access to cache/repository; treats failures as store unavailability.</summary>
public static class StorageAccess
{
    public static async Task TryExecuteAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Store unavailable.
        }
    }

    public static async Task<bool> TryWriteAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        try
        {
            await action().ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<T?> TryGetAsync<T>(Func<Task<T?>> action, CancellationToken cancellationToken = default)
        where T : class
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<IReadOnlyList<T>> TryListAsync<T>(
        Func<Task<IReadOnlyList<T>>> action,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Array.Empty<T>();
        }
    }

    /// <summary>
    /// Writes to cache only when enabled and <paramref name="isWriteAvailable"/> is true.
    /// </summary>
    public static async Task<bool> TryWriteToCacheAsync(
        bool cacheEnabled,
        Func<bool> isWriteAvailable,
        Func<Task> addAsync,
        CancellationToken cancellationToken = default)
    {
        if (!cacheEnabled)
            return false;

        try
        {
            if (!isWriteAvailable())
                return false;
        }
        catch
        {
            return false;
        }

        return await TryWriteAsync(addAsync, cancellationToken).ConfigureAwait(false);
    }

    public static void EnsureAnyStoreEnabled(MessengerCacheOptions options)
    {
        if (!options.CacheEnabled && !options.RepositoryEnabled)
            throw UseCaseException.Validation("Both cache and repository are disabled.");
    }
}
