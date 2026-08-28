namespace ShortP2P.MessengerServer.Contracts;

/// <summary>HTTPS API route constants for messenger server v1.</summary>
public static class ApiRoutes
{
    public const string Prefix = "/api/v1";

    public const string Register = Prefix + "/auth/register";
    public const string Login = Prefix + "/auth/login";

    public const string ServerCertificate = Prefix + "/server/certificate";

    public const string Chats = Prefix + "/chats";
    public const string ChatRequests = Prefix + "/chats/requests";

    public const string Messages = Prefix + "/messages";
    public const string MessageReceipts = Prefix + "/messages/receipts";

    public const string Blobs = Prefix + "/blobs";

    /// <summary>GET long-poll inbox: messages + chat requests for the caller's device.</summary>
    public const string EventsPoll = Prefix + "/events/poll";

    public const string Clients = Prefix + "/clients";

    /// <summary>GET anonymous TotalPower (host hardware score).</summary>
    public const string ServerTechPower = Prefix + "/server-tech/power";

    /// <summary>GET anonymous FreePowers (host free capacity %).</summary>
    public const string ServerTechFreePowers = Prefix + "/server-tech/free-powers";

    /// <summary>GET anonymous liveness ping (200 OK).</summary>
    public const string ServerTechPing = Prefix + "/server-tech/ping";

    /// <summary>GET gossip + ratings: ensure the named server exists (default 0.8) and return all ratings.</summary>
    public const string TrustAskRating = Prefix + "/trust/ask-rating";

    /// <summary>GET trusted peers (rating ≥ 0.3) with ratings.</summary>
    public const string TrustAskServers = Prefix + "/trust/ask-servers";

    /// <summary>POST claim about another server (UNAVAILABLE / MALFUNCTIONED / WRONGCERT).</summary>
    public const string TrustClaim = Prefix + "/trust/claim";
}
