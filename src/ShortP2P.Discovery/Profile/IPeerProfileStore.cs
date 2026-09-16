using ShortP2P.Auth.Data;

namespace ShortP2P.Discovery.Profile;

/// <summary>
///     Локальный снимок профиля пира (Avatar / AboutMe). Не синхронизируется с messenger-сервером.
/// </summary>
public sealed class PeerProfileSnapshot
{
    public required CompressedNetworkId NetworkId { get; init; }

    public string Nickname { get; init; } = "";

    public string AboutMe { get; init; } = "";

    public byte[]? Avatar { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }
}

/// <summary>
///     Локальное хранилище профилей пиров, полученных по wire (0x44/0x45) при discovery/скане.
/// </summary>
public interface IPeerProfileStore
{
    ValueTask UpsertAsync(CompressedNetworkId networkId, string? nickname, string aboutMe, byte[]? avatar,
        CancellationToken cancellationToken = default);

    ValueTask<PeerProfileSnapshot?> GetAsync(CompressedNetworkId networkId,
        CancellationToken cancellationToken = default);
}

/// <summary>Источник собственного профиля для ответов на peer profile request.</summary>
public interface ILocalPeerProfileSource
{
    ValueTask<(string AboutMe, byte[]? Avatar)> GetLocalProfileAsync(
        CancellationToken cancellationToken = default);
}
