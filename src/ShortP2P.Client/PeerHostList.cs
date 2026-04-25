using System.Net;

namespace ShortP2P.Client;

/// <summary>
///     Парсинг поля <see cref="Data.ChatEntity.PeerHost" />: один или несколько IP через запятую, точку с запятой,
///     вертикальную черту или пробел.
/// </summary>
public static class PeerHostList
{
    private static readonly char[] Separators = [',', ';', '|', ' ', '\n', '\r', '\t'];

    /// <summary>Уникальные корректные IP в порядке появления.</summary>
    public static IReadOnlyList<string> ParseCandidates(string? peerHost)
    {
        return !string.IsNullOrWhiteSpace(peerHost)
            ? peerHost.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(part => IPAddress.TryParse(part, out _)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
    }

    /// <summary>Первый адрес из списка или <paramref name="fallback" />.</summary>
    public static string PrimaryHost(string? peerHost, string fallback = "127.0.0.1")
    {
        var c = ParseCandidates(peerHost);
        return c.Count > 0 ? c[0] : fallback;
    }

    /// <summary>Добавляет новые IP в конец списка без дубликатов (регистр не важен).</summary>
    public static string MergeAppend(string? existingPeerHost, params string?[] additionalHostTexts)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var list = ParseCandidates(existingPeerHost).Where(x => seen.Add(x)).ToList();

        foreach (var raw in additionalHostTexts)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            foreach (var part in raw.Split(Separators,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!IPAddress.TryParse(part, out _))
                    continue;
                if (seen.Add(part))
                    list.Add(part);
            }
        }

        return string.Join(", ", list);
    }

    /// <summary>Указанный адрес становится первым (основной для доставки), остальные прежние кандидаты сохраняются.</summary>
    public static string WithPrimaryFirst(string? existingPeerHost, string primaryHost)
    {
        primaryHost = primaryHost.Trim();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        if (seen.Add(primaryHost))
            list.Add(primaryHost);

        list.AddRange(ParseCandidates(existingPeerHost).Where(x => seen.Add(x)));

        return string.Join(", ", list);
    }
}