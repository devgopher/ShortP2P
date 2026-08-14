using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Presence;

/// <summary>
/// Lists registered clients and their presence.
/// Online means a keep-alive was received within <see cref="OnlineTimeout"/>.
/// </summary>
public sealed class GetClientPresencesUseCase(
    IClientAccountRepository accounts,
    IClientStatusRepository statuses,
    IClock clock)
{
    /// <summary>
    /// Client keep-alive period is 15s; three missed beats mark the client offline.
    /// </summary>
    public static readonly TimeSpan OnlineTimeout = TimeSpan.FromSeconds(45);

    public async Task<IReadOnlyList<ClientPresenceInfo>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var allAccounts = await accounts.ListAllAsync(cancellationToken).ConfigureAwait(false);
        var allStatuses = await statuses.ListAllAsync(cancellationToken).ConfigureAwait(false);
        var statusByNetworkId = allStatuses.ToDictionary(s => s.NetworkId, StringComparer.Ordinal);
        var now = clock.UtcNow;

        return allAccounts
            .OrderBy(a => a.Nick, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.NetworkId, StringComparer.Ordinal)
            .Select(account =>
            {
                statusByNetworkId.TryGetValue(account.NetworkId, out var status);
                var lastSeen = status?.CreatedAtUtc ?? account.CreatedAtUtc;
                var online = status is { Status: ClientOnlineStatus.Online } &&
                             now - status.CreatedAtUtc <= OnlineTimeout;
                return new ClientPresenceInfo(
                    account.NetworkId,
                    account.Nick,
                    online ? ClientOnlineStatus.Online : ClientOnlineStatus.Offline,
                    lastSeen);
            })
            .ToArray();
    }
}
