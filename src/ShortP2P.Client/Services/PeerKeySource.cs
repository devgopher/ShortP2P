namespace ShortP2P.Client.Services;

/// <summary>How the peer RSA public key was obtained for this chat.</summary>
public static class PeerKeySourceKinds
{
    public const string Udp = "udp";
    public const string Bluetooth = "bluetooth";
    public const string Server = "server";
    public const string Qr = "qr";
    public const string Manual = "manual";
}

public readonly record struct PeerKeySource(string Kind, string? Detail = null)
{
    public static PeerKeySource Udp() => new(PeerKeySourceKinds.Udp);

    public static PeerKeySource Bluetooth() => new(PeerKeySourceKinds.Bluetooth);

    public static PeerKeySource Server(string baseUrl) =>
        new(PeerKeySourceKinds.Server, (baseUrl ?? "").Trim());

    public static PeerKeySource Qr() => new(PeerKeySourceKinds.Qr);

    public static PeerKeySource Manual() => new(PeerKeySourceKinds.Manual);

    public static bool IsServer(string? kind) =>
        string.Equals(kind, PeerKeySourceKinds.Server, StringComparison.OrdinalIgnoreCase);
}
