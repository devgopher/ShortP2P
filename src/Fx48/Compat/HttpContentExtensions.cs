#if NETFRAMEWORK
namespace System.Net.Http;

internal static class HttpContentNetFxExtensions
{
    public static Task<string> ReadAsStringAsync(this HttpContent content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return content.ReadAsStringAsync();
    }

    public static Task<byte[]> ReadAsByteArrayAsync(this HttpContent content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return content.ReadAsByteArrayAsync();
    }
}
#endif
