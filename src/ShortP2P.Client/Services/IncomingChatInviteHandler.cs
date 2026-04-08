using System.Net;
using ShortP2P.Client.Data;
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
    ///     Если задано, после создания нового чата отправляет пиру обратное приглашение с нашим ключом (уведомление о контакте).
    /// </param>
    public static async Task TryAcceptAsync(ReadOnlyMemory<byte> datagram, AuthService auth, ChatRepository repo,
        Func<ReadOnlyMemory<byte>, TransportAddress, CancellationToken, Task>? sendInviteReplyAsync,
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
        cancellationToken.ThrowIfCancellationRequested();
        var existing = await repo.FindChatByPeerNetworkIdAsync(user.Id, idShort).ConfigureAwait(false);
        if (existing != null)
        {
            await repo.UpdateChatP2pRouteAsync(existing.Id, host, port, relayRouteBlob: null).ConfigureAwait(false);
            repo.NotifyChatListChanged();
            return;
        }

        await repo.AddChatAsync(user.Id, nick, idShort, pubJson, host, port).ConfigureAwait(false);

        if (sendInviteReplyAsync != null)
        {
            var myHost = LocalEndpointHelper.GetPreferredLanIPv4String();
            var nid = CompressedNetworkId.FromShortString(user.NetworkIdShort);
            var reply = ChatInviteCodec.Build(user.Nickname, nid,
                RsaKeySerializer.SerializePublic(auth.GetCurrentPublicKey()), myHost, user.DataUdpPort);
            var back = UdpTransportAddress.FromIPEndPoint(new IPEndPoint(IPAddress.Parse(host), port));
            await sendInviteReplyAsync(reply, back, cancellationToken).ConfigureAwait(false);
        }
    }
}
