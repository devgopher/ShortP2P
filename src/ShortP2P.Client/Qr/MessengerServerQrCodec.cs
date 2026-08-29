using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShortP2P.Client.Qr;

public static class MessengerServerQrCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(MessengerServerQrPayload payload) =>
        JsonSerializer.Serialize(payload, JsonOptions);

    public static bool TryDeserialize(string text, [NotNullWhen(true)] out MessengerServerQrPayload? payload,
        out string? error)
    {
        payload = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "QR payload is empty.";
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed[0] == '{')
            return TryDeserializeJson(trimmed, out payload, out error);

        if (TryParseEndpoint(trimmed, out var endpoint))
        {
            payload = FromEndpoint(endpoint);
            error = null;
            return true;
        }

        error = "QR is not a messenger server share code (IP and port).";
        return false;
    }

    public static bool TryBuildFromBaseUrl(string baseUrl, [NotNullWhen(true)] out MessengerServerQrPayload? payload,
        out string? error)
    {
        payload = null;
        error = null;
        if (!TryParseEndpoint(baseUrl, out var endpoint))
        {
            error = "Server URL is not a valid http(s) address.";
            return false;
        }

        payload = FromEndpoint(endpoint);
        return true;
    }

    public static string ToBaseUrl(MessengerServerQrPayload payload)
    {
        Require.NotNull(payload);
        var scheme = NormalizeScheme(payload.S);
        var hostPart = FormatHostForUrl(payload.H);
        return $"{scheme}://{hostPart}:{payload.P}";
    }

    public static bool EndpointsEqual(string leftUrl, string rightUrl) =>
        TryParseEndpoint(leftUrl, out var left) &&
        TryParseEndpoint(rightUrl, out var right) &&
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port;

    public static bool TryParseEndpoint(string text, out MessengerServerEndpoint endpoint)
    {
        endpoint = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute) &&
            TryFromUri(absolute, out endpoint))
            return true;

        if (Uri.TryCreate("https://" + trimmed.TrimStart('/'), UriKind.Absolute, out var implied) &&
            TryFromUri(implied, out endpoint))
            return true;

        return false;
    }

    private static bool TryDeserializeJson(string json, [NotNullWhen(true)] out MessengerServerQrPayload? payload,
        out string? error)
    {
        payload = null;
        error = null;
        if (json.Length == 0 || json[0] != '{')
        {
            error = "QR is not a messenger server share code (IP and port).";
            return false;
        }

        MessengerServerQrPayload? p;
        try
        {
            p = JsonSerializer.Deserialize<MessengerServerQrPayload>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            error = $"Invalid QR data: {ex.Message}";
            return false;
        }

        if (p == null)
        {
            error = "Invalid QR data.";
            return false;
        }

        if (p.V != 1)
        {
            error = "Unsupported QR version.";
            return false;
        }

        if (!string.Equals(p.T, MessengerServerQrPayload.TypeMessengerServer, StringComparison.OrdinalIgnoreCase))
        {
            error = "QR is not a messenger server share code.";
            return false;
        }

        if (!IsValidHost(p.H))
        {
            error = "QR has an invalid server host.";
            return false;
        }

        if (p.P is < 1 or > 65535)
        {
            error = "QR has an invalid server port.";
            return false;
        }

        if (!IsAllowedScheme(p.S))
        {
            error = "QR has an invalid URL scheme.";
            return false;
        }

        p.H = p.H.Trim();
        p.T = MessengerServerQrPayload.TypeMessengerServer;
        p.S = string.IsNullOrWhiteSpace(p.S) ||
              string.Equals(p.S.Trim(), Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? null
            : Uri.UriSchemeHttp;
        payload = p;
        return true;
    }

    private static MessengerServerQrPayload FromEndpoint(MessengerServerEndpoint endpoint) =>
        new()
        {
            V = 1,
            T = MessengerServerQrPayload.TypeMessengerServer,
            H = endpoint.Host,
            P = endpoint.Port,
            S = string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                ? null
                : Uri.UriSchemeHttp
        };

    private static bool TryFromUri(Uri uri, out MessengerServerEndpoint endpoint)
    {
        endpoint = default;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return false;
        if (string.IsNullOrWhiteSpace(uri.Host))
            return false;
        if (uri.Port is < 1 or > 65535)
            return false;
        if (!IsValidHost(uri.Host))
            return false;

        endpoint = new MessengerServerEndpoint(uri.Scheme, uri.Host, uri.Port);
        return true;
    }

    private static bool IsAllowedScheme(string? scheme) =>
        string.IsNullOrWhiteSpace(scheme) ||
        string.Equals(scheme.Trim(), Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scheme.Trim(), Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeScheme(string? scheme) =>
        string.Equals(scheme?.Trim(), Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            ? Uri.UriSchemeHttp
            : Uri.UriSchemeHttps;

    private static bool IsValidHost(string? host)
    {
        var t = host?.Trim() ?? "";
        if (t.Length == 0)
            return false;
        if (IPAddress.TryParse(t, out _))
            return true;
        var kind = Uri.CheckHostName(t);
        return kind is UriHostNameType.Dns or UriHostNameType.Basic;
    }

    private static string FormatHostForUrl(string host)
    {
        var t = host.Trim();
        if (IPAddress.TryParse(t, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return $"[{t}]";
        return t;
    }
}

public readonly record struct MessengerServerEndpoint(string Scheme, string Host, int Port);
