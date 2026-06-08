using ShortP2P.Auth;

namespace ShortP2P.MauiApp.Services;

/// <summary>Maps MAUI secure storage to <see cref="ISessionStorage" />.</summary>
public sealed class MauiSecureStorage : ISessionStorage
{
    public void Remove(string key)
    {
        SecureStorage.Default.Remove(key);
    }

    public Task<string?> GetAsync(string key)
    {
        return SecureStorage.Default.GetAsync(key);
    }

    public Task SetAsync(string key, string value)
    {
        return SecureStorage.Default.SetAsync(key, value);
    }
}