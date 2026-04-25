using System.Net;
using ShortP2P.Auth;
using ShortP2P.Auth.Data;
using ShortP2P.Client.Data;
using ShortP2P.Client.Qr;
using ShortP2P.Client.Routing;
using ShortP2P.Crypto;
using ShortP2P.Discovery;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Services;

/// <summary>Добавляет чат с инициатором, если его ещё нет (ответ на <see cref="ChatInviteCodec"/>).</summary>
public static class IncomingChatInviteHandler
{
    /// <param name="sendInviteReplyAsync">
    ///     Если задано, отправляет пиру обратное приглашение с нашим ключом (новый чат или обновление маршрута).
    /// </param>
    public static async Task TryAcceptAsync(ReadOnlyMemory<byte> datagram, AuthService auth, ChatRepository repo,
        Func<ReadOnlyMemory<byte>, TransportAddress, CancellationToken, Task>? sendInviteReplyAsync,
        TransportAddress? sourceAddress = null,
        CancellationToken cancellationToken = default)
    {
        if (!ChatInviteCodec.TryParse(datagram.Span, out var peerGuid, out var nick, out var pubJson, out var host,
                out var port))
            return;

        var user = auth.CurrentUser;
        if (user == null)
            return;

        if (peerGuid == CompressedNetworkId.FromShortString(user.NetworkIdShort).Value)
            return;

        try
        {
            RsaKeySerializer.DeserializePublic(pubJson);
        }
        catch
        {
            return;
        }

        var idShort = CompressedNetworkId.FromGuid(peerGuid).ToShortString();
        var effectiveHost = ResolvePeerHost(host, sourceAddress);
        cancellationToken.ThrowIfCancellationRequested();
        var existing = await repo.FindChatByPeerNetworkIdAsync(user.Id, idShort).ConfigureAwait(false);
        if (existing != null)
        {
            var mergedHost = PeerHostList.WithPrimaryFirst(existing.PeerHost, effectiveHost);
            var chatPeerPort = existing.PeerPort is < 1 or > 65535 || existing.PeerPort == ChatInviteCodec.InviteUdpPort
                ? PresencePingCodec.DefaultDataUdpPort
                : existing.PeerPort;
            await repo.UpdateChatP2pRouteAsync(existing.Id, mergedHost, chatPeerPort, relayRouteBlob: null)
                .ConfigureAwait(false);
            existing.PeerHost = mergedHost;
            existing.PeerPort = chatPeerPort;
            existing.RelayRouteBlob = null;
            existing.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
            repo.NotifyChatListChanged();
            await TrySendInviteReplyAsync(auth, user, host, port, sourceAddress, sendInviteReplyAsync, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await repo.AddChatAsync(user.Id, nick, idShort, pubJson, effectiveHost, PresencePingCodec.DefaultDataUdpPort)
            .ConfigureAwait(false);

        await TrySendInviteReplyAsync(auth, user, host, port, sourceAddress, sendInviteReplyAsync, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task TrySendInviteReplyAsync(AuthService auth, UserEntity user, string inviteHostList,
        int invitePacketPort, TransportAddress? sourceAddress,
        Func<ReadOnlyMemory<byte>, TransportAddress, CancellationToken, Task>? sendInviteReplyAsync,
        CancellationToken cancellationToken)
    {
        if (sendInviteReplyAsync == null)
            return;
        var myHost = LocalIPv4Resolver.GetInviteHostsCommaSeparated(TimeSpan.FromSeconds(2));
        var nid = CompressedNetworkId.FromShortString(user.NetworkIdShort);
        var reply = ChatInviteCodec.Build(user.Nickname, nid,
            RsaKeySerializer.SerializePublic(auth.GetCurrentPublicKey()), myHost, ChatInviteCodec.InviteUdpPort);
        var parseHost = PeerHostList.PrimaryHost(inviteHostList);
        var back = sourceAddress ?? UdpTransportAddress.FromIPEndPoint(
            new IPEndPoint(IPAddress.Parse(parseHost), invitePacketPort));
        await sendInviteReplyAsync(reply, back, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolvePeerHost(string inviteHost, TransportAddress? sourceAddress)
    {
        if (sourceAddress?.Kind == TransportKind.Bluetooth)
            return BluetoothTransportAddress.ToMacString(sourceAddress.Data);
        if (sourceAddress?.Kind == TransportKind.Udp)
        {
            var seenIp = UdpTransportAddress.ToIPEndPoint(sourceAddress).Address.ToString();
            return PeerHostList.WithPrimaryFirst(inviteHost, seenIp);
        }
        return inviteHost;
    }
}
