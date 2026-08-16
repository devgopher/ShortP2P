using System.Security.Cryptography;
using System.Text;
using ShortP2P.Auth;

namespace ShortP2P.Client.Services.MessengerServers;

/// <summary>
/// Persistent install GUID → DeviceId = lowercase hex SHA-256 of UUID "D" string (UTF-8).
/// </summary>
public sealed class DeviceIdProvider
{
    public const string StorageKey = "messenger_install_id";

    private readonly ISessionStorage _storage;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cached;

    public DeviceIdProvider(ISessionStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<string> GetDeviceIdAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_cached))
            return _cached;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrEmpty(_cached))
                return _cached;

            var installId = await _storage.GetAsync(StorageKey).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(installId) || !Guid.TryParse(installId, out var guid))
            {
                guid = Guid.NewGuid();
                installId = guid.ToString("D");
                await _storage.SetAsync(StorageKey, installId).ConfigureAwait(false);
            }
            else
            {
                installId = guid.ToString("D");
            }

            _cached = ComputeDeviceId(installId);
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Canonical: UTF-8 of lowercase UUID "D" → SHA-256 hex.</summary>
    public static string ComputeDeviceId(string installIdUuidD)
    {
        var bytes = Encoding.UTF8.GetBytes(installIdUuidD.Trim().ToLowerInvariant());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
