using System.Collections.Concurrent;

namespace ShortP2P.MessengerServer.UseCases.Hosting;

/// <summary>Single-process inbox wait registry.</summary>
public sealed class InboxWaitService : Abstractions.IInboxWaitService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource>> _waiters =
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
        var byDevice = _waiters.GetOrAdd(net, _ => new ConcurrentDictionary<string, TaskCompletionSource>(StringComparer.Ordinal));
        byDevice[dev] = tcs;

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
            if (byDevice.TryGetValue(dev, out var current) && ReferenceEquals(current, tcs))
                byDevice.TryRemove(dev, out _);
        }
    }

    public void Notify(string networkId)
    {
        var net = networkId.Trim();
        if (!_waiters.TryGetValue(net, out var byDevice))
            return;

        foreach (var kv in byDevice)
            kv.Value.TrySetResult();
    }
}
