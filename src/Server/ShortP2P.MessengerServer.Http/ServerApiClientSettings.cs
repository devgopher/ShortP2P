namespace ShortP2P.MessengerServer.Http;

/// <summary>Settings for the messenger server HTTPS API client.</summary>
public sealed class ServerApiClientSettings
{
    public const string Section = "ServerApiClientSettings";

    /// <summary>Base URL of the API host, e.g. <c>https://localhost:7196</c>.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>HTTP timeout. Default 100 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);
}
