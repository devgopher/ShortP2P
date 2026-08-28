using System.Security.Cryptography;

namespace ShortP2P.Crypto;

/// <summary>
/// Deterministic 4-emoji safety number from an RSA public key (SHA-256 of modulus||exponent).
/// </summary>
public static class SafetyNumber
{
    public const int EmojiCount = 4;

    /// <summary>256 distinct pictographs (U+1F400–U+1F4FF), indexed by a hash byte.</summary>
    public static readonly string[] Glyphs = BuildGlyphs();

    public static string FromPublicKey(RsaPublicKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var hash = SHA256.HashData(Concat(key.Modulus, key.Exponent));
        return $"{Glyphs[hash[0]]}{Glyphs[hash[1]]}{Glyphs[hash[2]]}{Glyphs[hash[3]]}";
    }

    public static bool TryFromPublicKeyJson(string? json, out string emojis)
    {
        emojis = "";
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            emojis = FromPublicKey(RsaKeySerializer.DeserializePublic(json));
            return emojis.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static string FromPublicKeyJsonOrEmpty(string? json) =>
        TryFromPublicKeyJson(json, out var emojis) ? emojis : "";

    public static string FormatPair(string myNick, string myEmojis, string peerNick, string peerEmojis) =>
        $"{(myNick ?? "").Trim()}: {myEmojis}, {(peerNick ?? "").Trim()}: {peerEmojis}";

    public static bool PublicKeyJsonEquals(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b))
            return true;
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;
        if (string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal))
            return true;
        try
        {
            var ka = RsaKeySerializer.DeserializePublic(a);
            var kb = RsaKeySerializer.DeserializePublic(b);
            return ka.Modulus.AsSpan().SequenceEqual(kb.Modulus) &&
                   ka.Exponent.AsSpan().SequenceEqual(kb.Exponent);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, r, 0, a.Length);
        Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
        return r;
    }

    private static string[] BuildGlyphs()
    {
        var glyphs = new string[256];
        for (var i = 0; i < 256; i++)
            glyphs[i] = char.ConvertFromUtf32(0x1F400 + i);
        return glyphs;
    }
}
