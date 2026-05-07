using System.Text;
using ShortP2P.Auth.Data;

namespace ShortP2P.Discovery;

/// <summary>
///     Идентификация абонента: отображаемый nickname и уникальный номер в сети (<see cref="CompressedNetworkId" />).
/// </summary>
public sealed class PeerIdentity
{
#pragma warning disable CS8618 // materialization (EF Core)
    private PeerIdentity()
    {
    }
#pragma warning restore CS8618

    public PeerIdentity(string nickname, CompressedNetworkId networkId, int dataUdpPort = 17500,
        int maxNicknameUtf8Bytes = 64)
    {
        ArgumentNullException.ThrowIfNull(nickname);
        var trimmed = nickname.Trim();
        if (trimmed.Length == 0) throw new ArgumentException("Nickname cannot be empty.", nameof(nickname));
        var utf8 = Encoding.UTF8.GetByteCount(trimmed);
        if (utf8 > maxNicknameUtf8Bytes)
            throw new ArgumentException($"Nickname exceeds {maxNicknameUtf8Bytes} UTF-8 bytes.", nameof(nickname));

        if (dataUdpPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(dataUdpPort));

        Nickname = trimmed;
        NetworkId = networkId;
        DataUdpPort = dataUdpPort;
    }

    public string Nickname { get; init; } = null!;

    public CompressedNetworkId NetworkId { get; init; }

    /// <summary>Порт UDP для данных мессенджера (объявляется в beacon для построения маршрутов).</summary>
    public int DataUdpPort { get; init; }
}