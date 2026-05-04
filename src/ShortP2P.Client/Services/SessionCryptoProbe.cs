namespace ShortP2P.Client.Services;

/// <summary>Служебные текстовые кадры после RSA-handshake: проверка шифрования без записи в историю чата.</summary>
internal static class SessionCryptoProbe
{
    public const string Prefix = "CHAT ";

    public static string FormatAck(string sourcePeerIdShort, string targetPeerIdShort) =>
        $"{Prefix}{sourcePeerIdShort.Trim()} {targetPeerIdShort.Trim()} ACK";

    public static string FormatOk(string sourcePeerIdShort, string targetPeerIdShort) =>
        $"{Prefix}{sourcePeerIdShort.Trim()} {targetPeerIdShort.Trim()} OK";

    public static bool TryParse(string text, out SessionCryptoProbeKind kind, out string src, out string tgt)
    {
        kind = default;
        src = tgt = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        text = text.Trim();
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        var rest = text[Prefix.Length..];
        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return false;
        if (parts[2].Equals("ACK", StringComparison.OrdinalIgnoreCase))
            kind = SessionCryptoProbeKind.Ack;
        else if (parts[2].Equals("OK", StringComparison.OrdinalIgnoreCase))
            kind = SessionCryptoProbeKind.Ok;
        else
            return false;
        src = parts[0];
        tgt = parts[1];
        return true;
    }
}

internal enum SessionCryptoProbeKind
{
    Ack,
    Ok
}
