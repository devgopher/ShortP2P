namespace ShortP2P.Client;

/// <summary>
/// Shared contact/chat list order for MAUI and WinForms: online first, then display name.
/// </summary>
public static class ChatListOrder
{
    public static List<T> Sort<T>(
        IEnumerable<T> items,
        Func<T, bool> isOnline,
        Func<T, string?> displayName)
    {
        return items
            .OrderByDescending(isOnline)
            .ThenBy(x => displayName(x) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
