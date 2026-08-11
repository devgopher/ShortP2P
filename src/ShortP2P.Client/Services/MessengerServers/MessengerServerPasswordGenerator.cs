using System.Security.Cryptography;

namespace ShortP2P.Client.Services.MessengerServers;

public static class MessengerServerLimits
{
    public const int MaxServersPerUser = 32;
}

/// <summary>
/// Generates server account passwords: length 64–256, Latin letters + digits, mixed case required.
/// </summary>
public static class MessengerServerPasswordGenerator
{
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";
    private const string All = Lower + Upper + Digits;

    public static string Generate(int length = 96)
    {
        if (length is < 64 or > 256)
            throw new ArgumentOutOfRangeException(nameof(length), "Password length must be 64..256.");

        var chars = new char[length];
        chars[0] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
        chars[1] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
        chars[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        for (var i = 3; i < length; i++)
            chars[i] = All[RandomNumberGenerator.GetInt32(All.Length)];

        // Shuffle
        for (var i = length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    public static bool IsValid(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length is < 64 or > 256)
            return false;

        var hasLower = false;
        var hasUpper = false;
        var hasDigit = false;
        foreach (var c in password)
        {
            if (c is >= 'a' and <= 'z') hasLower = true;
            else if (c is >= 'A' and <= 'Z') hasUpper = true;
            else if (c is >= '0' and <= '9') hasDigit = true;
            else return false;
        }

        return hasLower && hasUpper && hasDigit;
    }
}
