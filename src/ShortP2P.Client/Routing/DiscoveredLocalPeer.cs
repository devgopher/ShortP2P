using ShortP2P.Auth.Data;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery;

/// <summary>
///     Узел, обнаруженный по discovery-пингу (порт <see cref="PresencePingCodec.UdpPort" /> или Bluetooth).
/// </summary>
/// <param name="PeerDataUdpPort">
///     Порт пира для data/чата (из пинга или <see cref="PresencePingCodec.DefaultDataUdpPort" />
///     ).
/// </param>
/// <param name="AdvertisedLinkTechnology">Пресет канала из пинга отправителя.</param>
/// <param name="AdvertisedCapabilities">
///     Маска возможностей из пинга; у старых клиентов без поля — только
///     <see cref="PresencePeerCapabilities.Chat" />.
/// </param>
/// <param name="MessengerServerOnline">Online по каталогу messenger-сервера (GetClients).</param>
public sealed record DiscoveredLocalPeer(
    CompressedNetworkId NetworkId,
    string Nickname,
    TransportAddress SourceAddress,
    TransportKind TransportKind,
    DateTimeOffset LastSeenUtc,
    int PeerDataUdpPort,
    LinkTechnologyPreset AdvertisedLinkTechnology = LinkTechnologyPreset.Unlimited,
    PresencePeerCapabilities AdvertisedCapabilities = PresencePeerCapabilities.Chat,
    bool MessengerServerOnline = false);

/// <summary>Клиент из каталога messenger-сервера (без прямого UDP/BT адреса).</summary>
/// <param name="NetworkIdShort">Короткий network id (base64url).</param>
/// <param name="Nickname">Ник на сервере.</param>
/// <param name="Online">Online по keep-alive сервера.</param>
/// <param name="LastSeenUtc">Последний keep-alive или регистрация.</param>
public sealed record MessengerServerDirectoryEntry(
    string NetworkIdShort,
    string Nickname,
    bool Online,
    DateTimeOffset LastSeenUtc);

public sealed class DiscoveryPingReceivedEventArgs(DiscoveredLocalPeer peer) : EventArgs
{
    public DiscoveredLocalPeer Peer { get; } = peer;
}
