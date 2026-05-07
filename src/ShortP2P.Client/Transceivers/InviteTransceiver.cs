using ShortP2P.Auth.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Discovery;
using ShortP2P.Discovery.Transceivers;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Transceivers;

/// <summary>
///     Приёмопередатчик invite-кадров (frame 0x30) на UDP <see cref="ChatInviteCodec.InviteUdpPort" />.
///     Поддерживает unicast и IPv4 broadcast.
/// </summary>
public sealed class InviteTransceiver(ITransport transport, int udpPort = ChatInviteCodec.InviteUdpPort)
    : IBroadcastTransceiver<InviteMessage>
{
    private readonly ITransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    private bool _started;

    public event EventHandler<InviteMessage>? GotData;

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

    public async ValueTask SendAsync(InviteMessage message, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(destination);
        var packet = BuildPacket(message);
        await _transport.SendAsync(packet, destination, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SendBroadcastAsync(InviteMessage message, CancellationToken cancellationToken = default)
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
                // best-effort broadcast: одна интерфейсная подсеть может быть недоступна
            }
        }
    }

    public ValueTask DisposeAsync() => StopAsync();

    private static byte[] BuildPacket(InviteMessage message)
    {
        if (!message.RawPayload.IsEmpty)
            return message.RawPayload.ToArray();
        var nid = CompressedNetworkId.FromGuid(message.InitiatorNetworkId);
        return ChatInviteCodec.Build(message.Nickname, nid, message.RsaPublicKeyJson,
            message.DataHost, message.DataPort);
    }

    /// <summary>
    ///     Передаёт в транспивер входящую invite-датаграмму. Вызывается внешним pump/owner.
    /// </summary>
    public void HandleIncoming(TransportReceiveMessage msg)
    {
        if (!_started)
            return;
        if (!ChatInviteCodec.TryParse(msg.Payload.Span, out var peerGuid, out var nick, out var pub,
                out var host, out var port))
            return;
        var invite = new InviteMessage(peerGuid, nick, pub, host, port, msg.Payload, msg.RemoteAddress);
        try
        {
            GotData?.Invoke(this, invite);
        }
        catch
        {
            // подписчик не должен ронять вызывающий цикл
        }
    }
}
