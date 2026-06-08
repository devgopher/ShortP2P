namespace ShortP2P.Discovery.Transceivers;

/// <summary>
///     Приёмопередатчик, поддерживающий и unicast (через <see cref="IUnicastTransceiver{TMessage}.SendAsync" />),
///     и широковещательную IPv4 рассылку через <see cref="SendBroadcastAsync" />.
/// </summary>
public interface IBroadcastTransceiver<TMessage> : IUnicastTransceiver<TMessage>
{
    /// <summary>
    ///     Рассылает сообщение на все активные IPv4 broadcast-адреса (limited 255.255.255.255 + per-subnet).
    ///     На транспортах без broadcast (например Bluetooth) выполняется как no-op.
    /// </summary>
    ValueTask SendBroadcastAsync(TMessage message, CancellationToken cancellationToken = default);
}