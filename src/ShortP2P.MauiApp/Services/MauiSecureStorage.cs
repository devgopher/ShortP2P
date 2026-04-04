using Microsoft.Maui.Storage;

namespace ShortP2P.MauiApp.Services;

/// <summary>Maps MAUI secure storage to <see cref="ShortP2P.Client.ISessionStorage"/>.</summary>
public sealed class MauiSecureStorage : ShortP2P.Client.ISessionStorage
{
    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);

    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public void Remove(string key) => SecureStorage.Default.Remove(key);
}
