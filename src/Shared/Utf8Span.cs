using System.Text;

namespace ShortP2P;

/// <summary>UTF-8 decode that works on net48 (no GetString(ReadOnlySpan&lt;byte&gt;)).</summary>
internal static class Utf8Span
{
    public static string GetString(ReadOnlySpan<byte> span)
    {
#if NETCOREAPP
        return Encoding.UTF8.GetString(span);
#else
        return Encoding.UTF8.GetString(span.ToArray());
#endif
    }
}
