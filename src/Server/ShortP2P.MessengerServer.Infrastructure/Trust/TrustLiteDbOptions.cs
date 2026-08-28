namespace ShortP2P.MessengerServer.Infrastructure.Trust;

public sealed class TrustLiteDbOptions
{
    public const string Section = "Trust:LiteDb";

    public string ConnectionString { get; set; } =
        "Filename=messenger-trust.litedb;Connection=shared";
}
