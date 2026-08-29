using System.Text;
using ShortP2P.Auth;

namespace ShortP2P.Client;

/// <summary>File-based session storage under a directory (one file per key).</summary>
public sealed class FileSessionStorage : ISessionStorage
{
    private readonly string _directory;

    public FileSessionStorage(string directory)
    {
        _directory = directory ?? throw new global::System.ArgumentNullException(nameof(directory));
        Directory.CreateDirectory(_directory);
    }

    public async Task<string?> GetAsync(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
            return null;
#if NETCOREAPP
        return await File.ReadAllTextAsync(path).ConfigureAwait(false);
#else
        return await Task.Run(() => File.ReadAllText(path)).ConfigureAwait(false);
#endif
    }

    public async Task SetAsync(string key, string value)
    {
        var path = PathFor(key);
#if NETCOREAPP
        await File.WriteAllTextAsync(path, value).ConfigureAwait(false);
#else
        await Task.Run(() => File.WriteAllText(path, value)).ConfigureAwait(false);
#endif
    }

    public void Remove(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string PathFor(string key)
    {
        var safe = ToHex(Encoding.UTF8.GetBytes(key));
        return Path.Combine(_directory, safe + ".session");
    }

    private static string ToHex(byte[] bytes)
    {
#if NET5_0_OR_GREATER
        return Convert.ToHexString(bytes);
#else
        var c = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            c[i * 2] = ToHexChar((byte)(b >> 4));
            c[i * 2 + 1] = ToHexChar((byte)(b & 0xF));
        }

        return new string(c);
#endif
    }

    private static char ToHexChar(byte v) => (char)(v < 10 ? '0' + v : 'A' + (v - 10));
}