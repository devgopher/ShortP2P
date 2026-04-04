namespace ShortP2P.Client;

/// <summary>Stores small secrets (e.g. logged-in user id). MAUI uses SecureStorage; WinForms uses <see cref="FileSessionStorage"/>.</summary>
public interface ISessionStorage
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);

    void Remove(string key);
}
