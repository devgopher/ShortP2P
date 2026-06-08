using System.Net;
using ShortP2P.Auth.Data;
using ShortP2P.Transport;

namespace ShortP2P.Client;

/// <summary>
///     Парсинг поля <see cref="Data.ChatEntity.PeerHost" />: IPv4/IPv6, short network id (base64url) и/или MAC Bluetooth
///     через запятую, точку с запятой, вертикальную черту или пробел.
/// </summary>
public static class PeerHostList
{
    private static readonly char[] Separators = [',', ';', '|', ' ', '\n', '\r', '\t'];

    /// <summary>Уникальные корректные IP в порядке появления.</summary>
    public static IReadOnlyList<string> ParseIpCandidates(string? peerHost)
    {
        return !string.IsNullOrWhiteSpace(peerHost)
            ? peerHost.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(part => IPAddress.TryParse(part, out _)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
    }

    /// <summary>Уникальные IP, network id и нормализованные MAC в порядке появления.</summary>
    public static IReadOnlyList<string> ParseEndpointCandidates(string? peerHost)
    {
        if (string.IsNullOrWhiteSpace(peerHost))
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var part in peerHost.Split(Separators,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (TryNormalizeEndpointToken(part, out var norm) && seen.Add(norm))
                list.Add(norm);

        return list;
    }

    private static bool TryNormalizeEndpointToken(string part, out string normalized)
    {
        normalized = part;
        if (IPAddress.TryParse(part, out _))
            return true;

        if (CompressedNetworkId.TryParseShortString(part, out var nid))
        {
            normalized = nid.ToShortString();
            return true;
        }

        if (!BluetoothTransportAddress.TryParseMac(part, out var mac))
            return false;
        normalized = BluetoothTransportAddress.ToMacString(mac);
        return true;
    }

    /// <summary>Первый endpoint (IP, network id или MAC) или <paramref name="fallback" />.</summary>
    public static string PrimaryHost(string? peerHost, string fallback = "127.0.0.1")
    {
        var c = ParseEndpointCandidates(peerHost);
        return c.Count > 0 ? c[0] : fallback;
    }

    /// <summary>Добавляет новые IP и MAC в конец списка без дубликатов (регистр не важен).</summary>
    public static string MergeAppend(string? existingPeerHost, params string?[] additionalHostTexts)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = ParseEndpointCandidates(existingPeerHost).Where(x => seen.Add(x)).ToList();

        foreach (var raw in additionalHostTexts)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            foreach (var part in raw.Split(Separators,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (TryNormalizeEndpointToken(part, out var token) && seen.Add(token))
                    list.Add(token);
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

        list.AddRange(ParseEndpointCandidates(existingPeerHost).Where(x => seen.Add(x)));

        return string.Join(", ", list);
    }
}