using ShortP2P.Client.Data;

namespace ShortP2P.Client.Services.MessengerServers;

public enum MessengerServerRecheckStatus
{
    AvailableAndTrusted,
    Unreachable,
    FingerprintMismatch
}

public sealed class MessengerServerRecheckResult
{
    public required MessengerServerEntity Server { get; init; }

    public required MessengerServerRecheckStatus Status { get; init; }

    public string ExpectedFingerprint { get; init; } = "";

    public string ActualFingerprint { get; init; } = "";

    public string? ErrorMessage { get; init; }
}
