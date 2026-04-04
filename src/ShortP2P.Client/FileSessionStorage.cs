namespace ShortP2P.Client;

/// <summary>File-based session storage under a directory (one file per key).</summary>
public sealed class FileSessionStorage : ISessionStorage
{
    private readonly string _directory;

    public FileSessionStorage(string directory)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        Directory.CreateDirectory(_directory);
    }

    public async Task<string?> GetAsync(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
            return null;
        return await File.ReadAllTextAsync(path).ConfigureAwait(false);
    }

    public async Task SetAsync(string key, string value)
    {
        var path = PathFor(key);
        await File.WriteAllTextAsync(path, value).ConfigureAwait(false);
    }

    public void Remove(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string PathFor(string key)
    {
        var safe = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(key));
        return Path.Combine(_directory, safe + ".session");
    }
}
