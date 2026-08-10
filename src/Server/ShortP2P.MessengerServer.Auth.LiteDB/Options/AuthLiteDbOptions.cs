namespace ShortP2P.MessengerServer.Auth.LiteDB.Options;

/// <summary>LiteDB auth store settings.</summary>
public sealed class AuthLiteDbOptions
{
    /// <summary>Nested under <c>Auth</c>: <c>Auth:LiteDb</c>.</summary>
    public const string Section = "Auth:LiteDb";

    public string ConnectionString { get; set; } = "Filename=messenger-auth.litedb;Connection=shared";
}
