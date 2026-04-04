using System.Text;

namespace ShortP2P.Discovery;

/// <summary>
///     Идентификация абонента: отображаемый nickname и уникальный номер в сети (<see cref="CompressedNetworkId" />).
/// </summary>
public sealed class PeerIdentity
{
    public PeerIdentity(string nickname, CompressedNetworkId networkId, int maxNicknameUtf8Bytes = 64)
    {
        ArgumentNullException.ThrowIfNull(nickname);
        var trimmed = nickname.Trim();
        if (trimmed.Length == 0) throw new ArgumentException("Nickname cannot be empty.", nameof(nickname));
        var utf8 = Encoding.UTF8.GetByteCount(trimmed);
        if (utf8 > maxNicknameUtf8Bytes)
            throw new ArgumentException($"Nickname exceeds {maxNicknameUtf8Bytes} UTF-8 bytes.", nameof(nickname));

        Nickname = trimmed;
        NetworkId = networkId;
    }

    public string Nickname { get; }

    public CompressedNetworkId NetworkId { get; }
}