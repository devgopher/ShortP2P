using System.Net;
using ShortP2P.Client;
using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Crypto;
using ShortP2P.Discovery;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Services;

/// <summary>Начало чата с пиром из LAN discovery: отправка ChatInvite и ожидание ответного приглашения с ключом.</summary>
public static class LanChatStartFromDiscovery
{
    /// <param name="inviteListenerCoordinator">
    ///     Если задан, перед временным bind на <see cref="ChatInviteCodec.InviteUdpPort" /> останавливает фоновый
    ///     приёмник инвайтов и в <c>finally</c> снова его поднимает (тот же порт <see cref="ChatInviteCodec.InviteUdpPort" />).
    /// </param>
    public static async Task<LanChatStartResult> TryStartAsync(
        DiscoveredLocalPeer peer,
        AuthService auth,
        ChatRepository chats,
        UserP2pRuntime? inviteListenerCoordinator = null,
        CancellationToken cancellationToken = default)
    {
        var user = auth.CurrentUser;
        if (user == null)
            return LanChatStartResult.Failed("Выполните вход.");

        if (peer.TransportKind != TransportKind.Udp)
            return LanChatStartResult.Failed("Для чата из списка нужен пир, найденный по UDP.");

        var idShort = CompressedNetworkId.FromGuid(peer.NetworkId).ToShortString();
        var existing = await chats.FindChatByPeerNetworkIdAsync(user.Id, idShort).ConfigureAwait(false);
        if (existing != null)
        {
            var seenIp = UdpTransportAddress.ToIPEndPoint(peer.SourceAddress).Address.ToString();
            var mergedHost = PeerHostList.MergeAppend(existing.PeerHost, seenIp);
            if (!string.Equals(mergedHost, existing.PeerHost, StringComparison.Ordinal))
            {
                await chats.UpdateChatP2pRouteAsync(existing.Id, mergedHost, existing.PeerPort, existing.RelayRouteBlob)
                    .ConfigureAwait(false);
                chats.NotifyChatListChanged();
                var fresh = await chats.GetChatAsync(existing.Id).ConfigureAwait(false);
                return LanChatStartResult.AlreadyExists(fresh ?? existing);
            }

            return LanChatStartResult.AlreadyExists(existing);
        }

        var ip = UdpTransportAddress.ToIPEndPoint(peer.SourceAddress).Address;
        var ep = new IPEndPoint(ip, ChatInviteCodec.InviteUdpPort);
        var dest = UdpTransportAddress.FromIPEndPoint(ep);

        var host = LocalEndpointHelper.GetPreferredLanIPv4String();
        var nid = CompressedNetworkId.FromShortString(user.NetworkIdShort);
        var invite = ChatInviteCodec.Build(user.Nickname, nid,
            RsaKeySerializer.SerializePublic(auth.GetCurrentPublicKey()), host, ChatInviteCodec.InviteUdpPort);

        if (inviteListenerCoordinator != null)
            await inviteListenerCoordinator.StopInviteListenerAsync(CancellationToken.None).ConfigureAwait(false);
        var udp = new UdpTransport(ChatInviteCodec.InviteUdpPort);
        try
        {
            await udp.StartAsync(cancellationToken).ConfigureAwait(false);
            await udp.SendAsync(invite, dest, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await udp.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }

            if (inviteListenerCoordinator != null)
            {
                var u = auth.CurrentUser;
                if (u != null)
                    await inviteListenerCoordinator.EnsureInviteListenerRunningAsync(u, cancellationToken)
                        .ConfigureAwait(false);
            }
        }

        for (var i = 0; i < 50; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            var chat = await chats.FindChatByPeerNetworkIdAsync(user.Id, idShort).ConfigureAwait(false);
            if (chat != null)
                return LanChatStartResult.Created(chat);
        }

        return LanChatStartResult.WaitingForPeer();
    }
}

public enum LanChatStartKind
{
    AlreadyExists,
    Created,
    WaitingForPeer,
    Failed,
}

public sealed class LanChatStartResult
{
    public LanChatStartKind Kind { get; private init; }
    public ChatEntity? Chat { get; private init; }
    public string? Message { get; private init; }

    public static LanChatStartResult AlreadyExists(ChatEntity chat) => new()
    {
        Kind = LanChatStartKind.AlreadyExists,
        Chat = chat,
    };

    public static LanChatStartResult Created(ChatEntity chat) => new()
    {
        Kind = LanChatStartKind.Created,
        Chat = chat,
    };

    public static LanChatStartResult WaitingForPeer() => new()
    {
        Kind = LanChatStartKind.WaitingForPeer,
        Message =
            "Приглашение отправлено. Чат появится в списке, когда пир ответит (при необходимости откройте список чатов позже).",
    };

    public static LanChatStartResult Failed(string message) => new()
    {
        Kind = LanChatStartKind.Failed,
        Message = message,
    };
}
