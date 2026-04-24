using System.Net;
using System.Net.Sockets;
using ShortP2P.Discovery.RouteTables;

namespace ShortP2P.Discovery.Gossip;

/// <summary>
///     Клиент запроса маршрутной таблицы по UDP (ожидание одного ответа с совпадающим nonce).
/// </summary>
public static class RouteTableWireClient
{
    /// <returns>Маршруты и признак усечения или <see langword="null" /> по таймауту.</returns>
    public static async Task<(IReadOnlyList<Route> Routes, bool Truncated)?> QueryRoutesAsync(
        IPEndPoint remoteHost,
        Guid localSenderNetworkId,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken = default)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var nonce = Random.Shared.NextInt64();
        var req = RouteTableWireCodec.BuildRequest(nonce, localSenderNetworkId);
        var target = new IPEndPoint(remoteHost.Address, GossipWireCodec.UdpPort);
        await udp.SendAsync(req, target, cancellationToken).ConfigureAwait(false);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(waitTimeout);
        try
        {
            while (true)
            {
                var r = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                if (!RouteTableWireCodec.TryParseReply(r.Buffer, out var n, out _, out var routes, out var truncated))
                    continue;
                if (n != nonce)
                    continue;
                return (routes, truncated);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
