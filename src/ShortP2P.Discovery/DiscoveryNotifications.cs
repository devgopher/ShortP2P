using ShortP2P.Auth.Data;

namespace ShortP2P.Discovery;

public abstract record DiscoveryNotification;

/// <summary>Абонент объявил о себе (новый или обновлённый).</summary>
public sealed record PeerSeenNotification(DiscoveredPeer Peer) : DiscoveryNotification;

/// <summary>Абонент не объявлялся дольше таймаута.</summary>
public sealed record PeerLostNotification(CompressedNetworkId NetworkId) : DiscoveryNotification;