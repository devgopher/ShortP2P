using System.Text;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Routing;

/// <summary>Прямой UDP или цепочка ретрансляции к пиру.</summary>
public sealed class ChatRelayRoute
{
    public TransportAddress Direct { get; init; } = null!;

    /// <summary>Первый получатель (если null — отправляем на <see cref="Direct" />).</summary>
    public TransportAddress? FirstHop { get; init; }

    /// <summary>Адреса для strip-relay после первого хопа (включая финальный адрес пира).</summary>
    public IReadOnlyList<TransportAddress> RelayStrip { get; init; } = [];

    public static ChatRelayRoute DirectOnly(TransportAddress ep)
    {
        return new ChatRelayRoute { Direct = ep };
    }

    public static string? SerializeBlob(ChatRelayRoute route)
    {
        if (route.FirstHop == null || route.RelayStrip.Count == 0)
            return null;
        var sb = new StringBuilder();
        sb.Append(Convert.ToBase64String(route.FirstHop.Data));
        foreach (var a in route.RelayStrip)
        {
            sb.Append('|');
            sb.Append(Convert.ToBase64String(a.Data));
        }

        return sb.ToString();
    }

    public static ChatRelayRoute FromChat(TransportAddress direct, string? blob)
    {
        if (string.IsNullOrWhiteSpace(blob))
            return DirectOnly(direct);
        try
        {
            var parts = blob.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
                return DirectOnly(direct);
            var first = new TransportAddress(TransportKind.Udp, Convert.FromBase64String(parts[0]));
            var strip = new TransportAddress[parts.Length - 1];
            for (var i = 1; i < parts.Length; i++)
                strip[i - 1] = new TransportAddress(TransportKind.Udp, Convert.FromBase64String(parts[i]));
            return new ChatRelayRoute { Direct = direct, FirstHop = first, RelayStrip = strip };
        }
        catch (FormatException)
        {
            return DirectOnly(direct);
        }
    }
}