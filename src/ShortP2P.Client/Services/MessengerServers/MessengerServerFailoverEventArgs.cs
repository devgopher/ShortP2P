using ShortP2P.Client.Data;

namespace ShortP2P.Client.Services.MessengerServers;

public sealed class MessengerServerFailoverEventArgs(
    MessengerServerEntity? untrustedServer,
    MessengerServerEntity? fallbackServer,
    bool switchedToMesh) : EventArgs
{
    public MessengerServerEntity? UntrustedServer { get; } = untrustedServer;

    public MessengerServerEntity? FallbackServer { get; } = fallbackServer;

    public bool SwitchedToMesh { get; } = switchedToMesh;
}
