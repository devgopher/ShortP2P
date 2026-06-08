using System.Net;
using ShortP2P.Auth;
using ShortP2P.Auth.Data;
using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Crypto;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Services;

/// <summary>Начало чата с пиром из LAN discovery: отправка ChatInvite и ожидание ответного приглашения с ключом.</summary>
public static class LanChatStartFromDiscovery
{
    /// <param name="inviteListenerCoordinator">
    ///     Если задан и в рантайме есть общая <see cref="IUdpTransportFactory" />, инвайт уходит через тот же сокет,
    ///     что и фоновый приёмник (без stop/start). Иначе — временно освобождает порт и поднимает слушатель снова.
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
        {
            if (peer.TransportKind != TransportKind.Bluetooth)
                return LanChatStartResult.Failed("Неподдерживаемый транспорт пира.");
            if (inviteListenerCoordinator?.BluetoothTransport == null)
                return LanChatStartResult.Failed("Bluetooth transport is unavailable.");
        }

        var idShort = peer.NetworkId.ToShortString();
        var existing = await chats.FindChatByPeerNetworkIdAsync(user.Id, idShort).ConfigureAwait(false);
        if (existing != null)
        {
            var seenIp = peer.TransportKind == TransportKind.Bluetooth
                ? BluetoothTransportAddress.ToMacString(peer.SourceAddress.Data)
                : UdpTransportAddress.ToIPEndPoint(peer.SourceAddress).Address.ToString();
            var mergedHost = PeerHostList.WithPrimaryFirst(existing.PeerHost, seenIp);
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

        var dest = peer.TransportKind == TransportKind.Bluetooth
            ? peer.SourceAddress
            : UdpTransportAddress.FromIPEndPoint(
                new IPEndPoint(UdpTransportAddress.ToIPEndPoint(peer.SourceAddress).Address,
                    ChatInviteCodec.InviteUdpPort));

        var host = InviteHostsBuilder.BuildCommaSeparated(
            inviteListenerCoordinator?.Settings,
            inviteListenerCoordinator?.Settings.EnableBluetoothTransport == false
                ? null
                : inviteListenerCoordinator?.Settings.SelectedBluetoothAdapterMac,
            user.NetworkIdShort,
            TimeSpan.FromSeconds(10));
        var nid = CompressedNetworkId.FromShortString(user.NetworkIdShort);
        var invite = ChatInviteCodec.Build(user.Nickname, nid,
            RsaKeySerializer.SerializePublic(auth.GetCurrentPublicKey()), host, ChatInviteCodec.InviteUdpPort);

        var sharedFactory = inviteListenerCoordinator?.UdpTransportFactory;
        if (sharedFactory != null)
        {
            if (peer.TransportKind == TransportKind.Udp)
            {
                var udp = sharedFactory.Acquire(IPAddress.Any, ChatInviteCodec.InviteUdpPort);
                try
                {
                    await udp.StartAsync(cancellationToken).ConfigureAwait(false);
                    await udp.SendAsync(invite, dest, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        await sharedFactory.ReleaseAsync(udp, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            else
            {
                var bt = inviteListenerCoordinator!.BluetoothTransport!;
                await bt.StartAsync(cancellationToken).ConfigureAwait(false);
                await bt.SendAsync(invite, dest, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            if (inviteListenerCoordinator != null)
                await inviteListenerCoordinator.StopInviteListenerAsync(cancellationToken).ConfigureAwait(false);
            var udp = UdpTransport.CreateUdpTransport(IPAddress.Any, ChatInviteCodec.InviteUdpPort);
            try
            {
                await udp.StartAsync(cancellationToken).ConfigureAwait(false);
                if (peer.TransportKind == TransportKind.Bluetooth)
                {
                    var bt = inviteListenerCoordinator!.BluetoothTransport!;
                    await bt.StartAsync(cancellationToken).ConfigureAwait(false);
                    await bt.SendAsync(invite, dest, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await udp.SendAsync(invite, dest, cancellationToken).ConfigureAwait(false);
                }
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
    Failed
}

public sealed class LanChatStartResult
{
    public LanChatStartKind Kind { get; private init; }
    public ChatEntity? Chat { get; private init; }
    public string? Message { get; private init; }

    public static LanChatStartResult AlreadyExists(ChatEntity chat)
    {
        return new LanChatStartResult
        {
            Kind = LanChatStartKind.AlreadyExists,
            Chat = chat
        };
    }

    public static LanChatStartResult Created(ChatEntity chat)
    {
        return new LanChatStartResult
        {
            Kind = LanChatStartKind.Created,
            Chat = chat
        };
    }

    public static LanChatStartResult WaitingForPeer()
    {
        return new LanChatStartResult
        {
            Kind = LanChatStartKind.WaitingForPeer,
            Message =
                "Приглашение отправлено. Чат появится в списке, когда пир ответит (при необходимости откройте список чатов позже)."
        };
    }

    public static LanChatStartResult Failed(string message)
    {
        return new LanChatStartResult
        {
            Kind = LanChatStartKind.Failed,
            Message = message
        };
    }
}