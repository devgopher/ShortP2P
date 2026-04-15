using System.Net;

namespace ShortP2P.Client;

/// <summary>Парсинг поля <see cref="Data.ChatEntity.PeerHost"/>: один или несколько IP через запятую, точку с запятой, вертикальную черту или пробел.</summary>
public static class PeerHostList
{
    private static readonly char[] Separators = [',', ';', '|', ' ', '\n', '\r', '\t'];

    /// <summary>Уникальные корректные IP в порядке появления.</summary>
    public static IReadOnlyList<string> ParseCandidates(string? peerHost)
    {
        if (string.IsNullOrWhiteSpace(peerHost))
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var part in peerHost.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IPAddress.TryParse(part, out _))
                continue;
            if (!seen.Add(part))
                continue;
            list.Add(part);
        }

        return list;
    }

    /// <summary>Первый адрес из списка или <paramref name="fallback"/>.</summary>
    public static string PrimaryHost(string? peerHost, string fallback = "127.0.0.1")
    {
        var c = ParseCandidates(peerHost);
        return c.Count > 0 ? c[0] : fallback;
    }
}
