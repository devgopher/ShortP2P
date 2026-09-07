using System.Collections.Concurrent;

namespace ShortP2P.MessengerServer.UseCases.Hosting;

/// <summary>Single-process inbox wait registry. Multiple polls per device stay registered.</summary>
public sealed class InboxWaitService : Abstractions.IInboxWaitService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WaitGroup>> _waiters =
        new(StringComparer.Ordinal);

    public async Task WaitAsync(
        string networkId,
        string deviceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var net = networkId.Trim();
        var dev = deviceId.Trim();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var byDevice = _waiters.GetOrAdd(net, _ => new ConcurrentDictionary<string, WaitGroup>(StringComparer.Ordinal));
        var group = byDevice.GetOrAdd(dev, _ => new WaitGroup());
        lock (group.Sync)
            group.Waiters.Add(tcs);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // timeout — normal empty poll
        }
        finally
        {
            lock (group.Sync)
                group.Waiters.Remove(tcs);
        }
    }

    public void Notify(string networkId)
    {
        var net = networkId.Trim();
        if (!_waiters.TryGetValue(net, out var byDevice))
            return;

        foreach (var kv in byDevice)
        {
            List<TaskCompletionSource> snapshot;
            lock (kv.Value.Sync)
                snapshot = [.. kv.Value.Waiters];
            foreach (var tcs in snapshot)
                tcs.TrySetResult();
        }
    }

    private sealed class WaitGroup
    {
        public readonly Lock Sync = new();
        public readonly List<TaskCompletionSource> Waiters = [];
    }
}
