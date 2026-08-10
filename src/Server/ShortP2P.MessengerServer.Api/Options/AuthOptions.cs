namespace ShortP2P.MessengerServer.Api.Options;

/// <summary>JWT bearer authentication settings.</summary>
public sealed class AuthOptions
{
    /// <summary>Configuration section name in appsettings.</summary>
    public const string Section = "Auth";

    public string Issuer { get; set; } = "ShortP2P.MessengerServer";

    public string Audience { get; set; } = "ShortP2P.Clients";

    /// <summary>Symmetric signing key (UTF-8). Use at least 32 characters in production.</summary>
    public string SigningKey { get; set; } = "ShortP2P-Dev-Signing-Key-Change-Me-32+";

    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(8);
}
