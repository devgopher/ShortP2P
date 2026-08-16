using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Abstractions;

/// <summary>Hot cache for per-device message inbox rows.</summary>
public interface IMessageInboxCache
{
    bool IsWriteAvailable { get; }

    Task AddAsync(MessageInboxEntry entry, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        string messageId,
        string deviceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListMessageIdsForDeviceAsync(
        string tgtNetworkId,
        string deviceId,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        string messageId,
        string deviceId,
        CancellationToken cancellationToken = default);

    Task<int> CountForMessageAsync(string messageId, CancellationToken cancellationToken = default);

    Task RemoveAllForMessageAsync(string messageId, CancellationToken cancellationToken = default);
}
