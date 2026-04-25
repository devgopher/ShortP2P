using Microsoft.Maui.Storage;
using ShortP2P.Auth;

namespace ShortP2P.MauiApp.Services;

/// <summary>Maps MAUI secure storage to <see cref="ISessionStorage"/>.</summary>
public sealed class MauiSecureStorage : ISessionStorage
{
    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);

    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public void Remove(string key) => SecureStorage.Default.Remove(key);
}
