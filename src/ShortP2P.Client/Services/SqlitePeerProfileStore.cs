using ShortP2P.Auth.Data;
using ShortP2P.Client.Data;
using ShortP2P.Discovery.Profile;

namespace ShortP2P.Client.Services;

public sealed class SqlitePeerProfileStore(AppDatabase appDatabase) : IPeerProfileStore
{
    private readonly AppDatabase _db = appDatabase ?? throw new ArgumentNullException(nameof(appDatabase));

    public async ValueTask UpsertAsync(CompressedNetworkId networkId, string? nickname, string aboutMe,
        byte[]? avatar, CancellationToken cancellationToken = default)
    {
        if (networkId.IsEmpty)
            return;

        aboutMe ??= "";
        if (aboutMe.Length > PeerProfileLimits.MaxAboutMeChars)
            aboutMe = aboutMe[..PeerProfileLimits.MaxAboutMeChars];
        if (avatar != null && avatar.Length > PeerProfileLimits.MaxAvatarBytes)
            avatar = avatar.AsSpan(0, PeerProfileLimits.MaxAvatarBytes).ToArray();
        if (avatar is { Length: 0 })
            avatar = null;

        var idShort = networkId.ToShortString();
        var nick = string.IsNullOrWhiteSpace(nickname) ? "" : nickname.Trim();

        await _db.WriteAsync(async conn =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTime.UtcNow.Ticks;
            var row = await conn.FindAsync<PeerProfileEntity>(idShort).ConfigureAwait(false);
            if (row == null)
            {
                await conn.InsertAsync(new PeerProfileEntity
                {
                    NetworkIdShort = idShort,
                    Nickname = nick,
                    AboutMe = aboutMe,
                    Avatar = avatar,
                    UpdatedUtcTicks = now
                }).ConfigureAwait(false);
            }
            else
            {
                if (nick.Length > 0)
                    row.Nickname = nick;
                row.AboutMe = aboutMe;
                row.Avatar = avatar;
                row.UpdatedUtcTicks = now;
                await conn.UpdateAsync(row).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    public async ValueTask<PeerProfileSnapshot?> GetAsync(CompressedNetworkId networkId,
        CancellationToken cancellationToken = default)
    {
        if (networkId.IsEmpty)
            return null;

        var idShort = networkId.ToShortString();
        return await _db.ReadAsync(async conn =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = await conn.FindAsync<PeerProfileEntity>(idShort).ConfigureAwait(false);
            if (row == null)
                return null;
            return new PeerProfileSnapshot
            {
                NetworkId = networkId,
                Nickname = row.Nickname ?? "",
                AboutMe = row.AboutMe ?? "",
                Avatar = row.Avatar,
                UpdatedUtc = new DateTimeOffset(row.UpdatedUtcTicks, TimeSpan.Zero)
            };
        }).ConfigureAwait(false);
    }
}
