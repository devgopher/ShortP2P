#if NETFRAMEWORK
using System.Threading.Channels;

namespace ShortP2P.Client.Compat;

internal static class ChannelReaderFx
{
    public static async IAsyncEnumerable<T> ReadAllAsync<T>(
        this ChannelReader<T> reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (reader.TryRead(out var item))
                yield return item;
        }
    }
}
#endif
