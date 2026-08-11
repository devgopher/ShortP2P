using ShortP2P.Client.Data;

namespace ShortP2P.Client.Services.MessengerServers;

public sealed class MessengerServerTrustThreatEventArgs(
    MessengerServerEntity server,
    string expectedFingerprint,
    string actualFingerprint) : EventArgs
{
    public MessengerServerEntity Server { get; } = server;
    public string ExpectedFingerprint { get; } = expectedFingerprint;
    public string ActualFingerprint { get; } = actualFingerprint;
}
