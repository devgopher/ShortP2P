using ShortP2P.Client.Data;
using ShortP2P.Crypto;

namespace ShortP2P.Client.Services;

public static class PeerSafetyDisplay
{
    public const string MeshWarning =
        "больше нет доверенных серверов, переключаюсь в режим mesh.";

    public const string KeyChangeTitle = "Смена публичного ключа";

    public static string FormatFingerprints(
        string myNick,
        RsaPublicKey myKey,
        string peerNick,
        string? peerPublicKeyJson)
    {
        var mine = SafetyNumber.FromPublicKey(myKey);
        var peer = SafetyNumber.FromPublicKeyJsonOrEmpty(peerPublicKeyJson);
        if (peer.Length == 0)
            peer = "—";
        return SafetyNumber.FormatPair(myNick, mine, peerNick, peer);
    }

    public static string FormatChannel(string? kind, string? detail)
    {
        var k = (kind ?? "").Trim();
        var d = (detail ?? "").Trim();
        if (k.Length == 0)
            return "";
        if (string.Equals(k, PeerKeySourceKinds.Udp, StringComparison.OrdinalIgnoreCase))
            return "UDP";
        if (string.Equals(k, PeerKeySourceKinds.Bluetooth, StringComparison.OrdinalIgnoreCase))
            return "Bluetooth";
        if (string.Equals(k, PeerKeySourceKinds.Server, StringComparison.OrdinalIgnoreCase))
            return d.Length == 0 ? "Server" : $"Server {FormatServerEndpoint(d)}";
        if (string.Equals(k, PeerKeySourceKinds.Qr, StringComparison.OrdinalIgnoreCase))
            return "QR";
        if (string.Equals(k, PeerKeySourceKinds.Manual, StringComparison.OrdinalIgnoreCase))
            return "вручную";
        return k;
    }

    public static string FormatChannel(ChatEntity chat) =>
        FormatChannel(chat.PeerKeySourceKind, chat.PeerKeySourceDetail);

    public static string FormatPanel(string fingerprints, ChatEntity chat)
    {
        var channel = FormatChannel(chat);
        return channel.Length == 0 ? fingerprints : fingerprints + Environment.NewLine + "Канал: " + channel;
    }

    public static string FormatKeyChangeWarning(PeerPublicKeyChangedEventArgs e)
    {
        return
            $"Публичный ключ собеседника «{e.PeerNickname}» изменился.\n\n" +
            $"Было: {e.PreviousSafetyNumber}\nСтало: {e.NewSafetyNumber}\n\n" +
            "Если вы не меняли ключ намеренно, это может быть атака «человек посередине».";
    }

    public static string FormatServerEndpoint(string baseUrl)
    {
        if (!Uri.TryCreate((baseUrl ?? "").Trim(), UriKind.Absolute, out var uri))
            return (baseUrl ?? "").Trim();
        var port = uri.IsDefaultPort
            ? uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80
            : uri.Port;
        return $"{uri.Host}:{port}";
    }
}
