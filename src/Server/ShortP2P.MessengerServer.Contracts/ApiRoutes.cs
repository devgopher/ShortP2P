namespace ShortP2P.MessengerServer.Contracts;

/// <summary>HTTPS API route constants for messenger server v1.</summary>
public static class ApiRoutes
{
    public const string Prefix = "/api/v1";

    public const string Register = Prefix + "/auth/register";
    public const string Login = Prefix + "/auth/login";

    public const string ServerCertificate = Prefix + "/server/certificate";

    public const string ChatRequests = Prefix + "/chats/requests";

    public const string Messages = Prefix + "/messages";
    public const string MessageReceipts = Prefix + "/messages/receipts";

    public const string KeepAlive = Prefix + "/keepalive";
}
