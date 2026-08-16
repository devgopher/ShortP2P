namespace ShortP2P.MessengerServer.UseCases.Abstractions;

/// <summary>In-process wake-up for long-poll waiters, keyed by (networkId, deviceId).</summary>
public interface IInboxWaitService
{
    Task WaitAsync(
        string networkId,
        string deviceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>Wakes all device waiters for the given network id.</summary>
    void Notify(string networkId);
}
