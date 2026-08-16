namespace ShortP2P.MessengerServer.Infrastructure.HostPowers;

public sealed class HostPowersLiteDbOptions
{
    public const string Section = "HostPowers:LiteDb";

    public string ConnectionString { get; set; } =
        "Filename=messenger-host-powers.litedb;Connection=shared";
}
