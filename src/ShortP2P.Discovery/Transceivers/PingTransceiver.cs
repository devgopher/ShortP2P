using ShortP2P.Client.Routing;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Transceivers;

/// <summary>
///     Приёмопередатчик presence-ping (frame 0x31) на UDP <see cref="PresencePingCodec.UdpPort" />.
///     Поддерживает unicast и IPv4 broadcast.
/// </summary>
public sealed class PingTransceiver(ITransport transport, int udpPort = PresencePingCodec.UdpPort)
    : IBroadcastTransceiver<PingMessage>
{
    private readonly ITransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    private bool _started;

    public event EventHandler<PingMessage>? GotData;

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        _started = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        _started = false;
        return ValueTask.CompletedTask;
    }

    public async ValueTask SendAsync(PingMessage message, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(destination);
        var packet = BuildPacket(message);
        await _transport.SendAsync(packet, destination, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SendBroadcastAsync(PingMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (_transport.Kind != TransportKind.Udp)
            return;
        var packet = BuildPacket(message);
        foreach (var ep in LanBroadcastHelper.GetIpv4BroadcastEndpoints(udpPort))
        {
            try
            {
                await _transport.SendAsync(packet, UdpTransportAddress.FromIPEndPoint(ep), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // best-effort
            }
        }
    }

    public ValueTask DisposeAsync() => StopAsync();

    private static byte[] BuildPacket(PingMessage message)
    {
        if (!message.RawPayload.IsEmpty)
            return message.RawPayload.ToArray();
        return PresencePingCodec.Build(message.PeerNetworkId, message.Nickname, message.PeerDataUdpPort,
            message.AdvertisedLink, message.AdvertisedCapabilities);
    }

    /// <summary>
    ///     Передаёт в транспивер входящую датаграмму presence-пинга. Вызывается внешним pump/owner.
    /// </summary>
    public void HandleIncoming(TransportReceiveMessage msg)
    {
        if (!_started)
            return;
        if (!PresencePingCodec.TryParse(msg.Payload.Span, out var nid, out var nick, out var dataPort,
                out var link, out var caps))
            return;
        var ping = new PingMessage(nid, nick, dataPort, link, caps, msg.Payload, msg.RemoteAddress);
        try
        {
            GotData?.Invoke(this, ping);
        }
        catch
        {
            // подписчик не должен ронять вызывающий цикл
        }
    }
}
