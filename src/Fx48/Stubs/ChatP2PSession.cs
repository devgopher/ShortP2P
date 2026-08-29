namespace ShortP2P.Client.Services;

/// <summary>
/// Stub for the net48 client: P2P/UDP/BLE sessions are not compiled. Server ingest uses the repository path.
/// </summary>
public sealed class ChatP2PSession
{
    public Task IngestIncomingWireFromServerAsync(
        byte[] wire,
        CancellationToken cancellationToken,
        string? serverBaseUrl = null) =>
        Task.CompletedTask;
}
