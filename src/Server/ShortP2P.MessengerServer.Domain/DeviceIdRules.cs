using System.Text.RegularExpressions;

namespace ShortP2P.MessengerServer.Domain;

/// <summary>Wire/DB rules for device identifiers (install GUID → SHA-256 hex).</summary>
public static partial class DeviceIdRules
{
    public const int Length = 64;

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexSha256Regex();

    public static bool IsValid(string? deviceId) =>
        !string.IsNullOrEmpty(deviceId) && HexSha256Regex().IsMatch(deviceId);

    public static string RequireValid(string? deviceId, string paramName = "deviceId")
    {
        var trimmed = deviceId?.Trim() ?? "";
        if (!IsValid(trimmed))
            throw new ArgumentException("DeviceId must be 64 lowercase hex characters (SHA-256).", paramName);
        return trimmed;
    }
}
