#if NETFRAMEWORK
namespace System.Threading.Tasks;

internal static class ParallelFx
{
    public static async Task ForEachAsync<TSource>(
        IEnumerable<TSource> source,
        ParallelOptions options,
        Func<TSource, CancellationToken, ValueTask> body)
    {
        if (source == null)
            throw new global::System.ArgumentNullException(nameof(source));
        if (options == null)
            throw new global::System.ArgumentNullException(nameof(options));
        if (body == null)
            throw new global::System.ArgumentNullException(nameof(body));

        var ct = options.CancellationToken;
        var dop = options.MaxDegreeOfParallelism <= 0 ? 4 : options.MaxDegreeOfParallelism;
        using var gate = new SemaphoreSlim(dop, dop);
        var tasks = new List<Task>();
        foreach (var item in source)
        {
            ct.ThrowIfCancellationRequested();
            var captured = item;
            await gate.WaitAsync(ct).ConfigureAwait(false);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await body(captured, ct).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            }, ct));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
#endif
