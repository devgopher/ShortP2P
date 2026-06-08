namespace ShortP2P.Auth;

/// <summary>
///     Stores small secrets (e.g. logged-in user id). Host apps use file-based or secure storage implementations.
/// </summary>
public interface ISessionStorage
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);

    void Remove(string key);
}