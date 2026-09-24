namespace ShortP2P.MessengerServer.Tests.Fakes;

internal static class TestIds
{
    public const string DeviceA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public const string DeviceB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    public const string DeviceC = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    public static string Hex64(char c) => new(c, 64);
}
