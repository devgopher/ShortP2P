using System.Globalization;
using System.Net;

namespace ShortP2P.TrustSystem;

/// <summary>Normalized host + port of a messenger server under rating.</summary>
public readonly record struct ServerEndpoint(string Host, int Port)
{
    public string Key => $"{Host}:{Port.ToString(CultureInfo.InvariantCulture)}";

    public static ServerEndpoint Parse(string host, int port)
    {
        if (!TryParse(host, port, out var endpoint, out var error))
            throw new TrustException(error);
        return endpoint;
    }

    public static bool TryParse(string? host, int port, out ServerEndpoint endpoint, out string error)
    {
        endpoint = default;
        error = "";
        var trimmed = (host ?? "").Trim().Trim('[', ']');
        if (trimmed.Length == 0)
        {
            error = "serverIp is required.";
            return false;
        }

        if (port is < 1 or > 65535)
        {
            error = "serverPort must be in 1..65535.";
            return false;
        }

        if (IPAddress.TryParse(trimmed, out var ip))
            trimmed = ip.ToString();
        else
            trimmed = trimmed.ToLowerInvariant();

        endpoint = new ServerEndpoint(trimmed, port);
        return true;
    }

    public bool EqualsEndpoint(ServerEndpoint other) =>
        Port == other.Port &&
        string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase);
}
