using ShortP2P.Discovery.Gossip;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Transceivers;

/// <summary>
///     Приёмопередатчик discovery wire-пакетов (gossip 0x40/0x41 + route table 0x42/0x43)
///     на UDP <see cref="GossipWireCodec.UdpPort" />. Поддерживает unicast и IPv4 broadcast.
/// </summary>
public sealed class DiscoveryWireTransceiver(ITransport transport, int udpPort = GossipWireCodec.UdpPort)
    : IBroadcastTransceiver<DiscoveryWireMessage>
{
    private readonly ITransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    private bool _started;

    public event EventHandler<DiscoveryWireMessage>? GotData;

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

    public async ValueTask SendAsync(DiscoveryWireMessage message, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(destination);
        EnsureRawNotEmpty(message);
        await _transport.SendAsync(message.RawPayload, destination, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SendBroadcastAsync(DiscoveryWireMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (_transport.Kind != TransportKind.Udp)
            return;
        EnsureRawNotEmpty(message);
        foreach (var ep in LanBroadcastHelper.GetIpv4BroadcastEndpoints(udpPort))
            try
            {
                await _transport.SendAsync(message.RawPayload, UdpTransportAddress.FromIPEndPoint(ep),
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // best-effort
            }
    }

    public ValueTask DisposeAsync()
    {
        return StopAsync();
    }

    private static void EnsureRawNotEmpty(DiscoveryWireMessage message)
    {
        if (message.RawPayload.IsEmpty)
            throw new ArgumentException("DiscoveryWireMessage.RawPayload must contain a built wire packet.",
                nameof(message));
    }

    /// <summary>
    ///     Передаёт в транспивер входящую discovery-wire датаграмму. Вызывается внешним pump/owner.
    /// </summary>
    public void HandleIncoming(TransportReceiveMessage msg)
    {
        if (!_started)
            return;
        if (msg.Payload.IsEmpty)
            return;
        var first = msg.Payload.Span[0];
        if (!IsKnownKind(first, out var kind))
            return;

        var wire = new DiscoveryWireMessage(kind, msg.Payload, msg.RemoteAddress);
        try
        {
            GotData?.Invoke(this, wire);
        }
        catch
        {
            // подписчик не должен ронять вызывающий цикл
        }
    }

    private static bool IsKnownKind(byte first, out DiscoveryWireKind kind)
    {
        switch (first)
        {
            case (byte)DiscoveryWireKind.GossipProbe:
                kind = DiscoveryWireKind.GossipProbe;
                return true;
            case (byte)DiscoveryWireKind.GossipAck:
                kind = DiscoveryWireKind.GossipAck;
                return true;
            case (byte)DiscoveryWireKind.RouteTableRequest:
                kind = DiscoveryWireKind.RouteTableRequest;
                return true;
            case (byte)DiscoveryWireKind.RouteTableReply:
                kind = DiscoveryWireKind.RouteTableReply;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}