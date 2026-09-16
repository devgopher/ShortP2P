using ShortP2P.Auth;
using ShortP2P.Discovery.Profile;

namespace ShortP2P.Client.Services;

/// <summary>Отдаёт Avatar/AboutMe текущего пользователя для ответов на peer profile request.</summary>
public sealed class AuthLocalPeerProfileSource(AuthService auth) : ILocalPeerProfileSource
{
    private readonly AuthService _auth = auth ?? throw new ArgumentNullException(nameof(auth));

    public ValueTask<(string AboutMe, byte[]? Avatar)> GetLocalProfileAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = _auth.CurrentUser;
        if (user == null)
            return ValueTask.FromResult(("", (byte[]?)null));
        return ValueTask.FromResult((user.AboutMe ?? "", user.Avatar));
    }
}
