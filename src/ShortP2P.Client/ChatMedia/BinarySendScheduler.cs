using ShortP2P.Discovery;

namespace ShortP2P.Client.ChatMedia;

/// <summary>
/// Runs compress + binary send off the caller thread and caps how many of those
/// jobs run at once (7 in normal traffic mode, 3 in economy / ultra-economy).
/// </summary>
public sealed class BinarySendScheduler
{
    private const int NormalMaxConcurrency = 7;
    private const int ReducedMaxConcurrency = 3;

    private readonly Lock _sync = new();
    private readonly Queue<TaskCompletionSource> _waiters = new();
    private int _active;
    private int _limit = NormalMaxConcurrency;

    public static int MaxConcurrency(TrafficQualityMode mode) =>
        mode is TrafficQualityMode.Economy or TrafficQualityMode.UltraEconomy
            ? ReducedMaxConcurrency
            : NormalMaxConcurrency;

    public async Task RunAsync(
        TrafficQualityMode mode,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        Volatile.Write(ref _limit, MaxConcurrency(mode));
        await AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() => work(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Release();
        }
    }

    private async Task AcquireAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource tcs;
        lock (_sync)
        {
            if (_waiters.Count == 0 && _active < Volatile.Read(ref _limit))
            {
                _active++;
                return;
            }

            tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(tcs);
        }

        try
        {
            await using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
                await tcs.Task.ConfigureAwait(false);
        }
        catch
        {
            Pump();
            throw;
        }
    }

    private void Release()
    {
        lock (_sync)
            _active = Math.Max(0, _active - 1);
        Pump();
    }

    private void Pump()
    {
        while (true)
        {
            TaskCompletionSource? next = null;
            lock (_sync)
            {
                var limit = Volatile.Read(ref _limit);
                while (_waiters.Count > 0 && _active < limit)
                {
                    var waiter = _waiters.Dequeue();
                    if (waiter.Task.IsCompleted)
                        continue;
                    _active++;
                    next = waiter;
                    break;
                }
            }

            if (next == null)
                return;
            if (next.TrySetResult())
                return;
            lock (_sync)
                _active = Math.Max(0, _active - 1);
        }
    }
}
