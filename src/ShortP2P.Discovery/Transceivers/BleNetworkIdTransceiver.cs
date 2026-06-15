using ShortP2P.Auth.Data;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Transceivers;

/// <summary>
///     Приёмопередатчик BLE-кадров NetworkId (префикс <see cref="BleNetworkIdPacketCodec.Prefix" /> или legacy 0x32)
///     на data-порту. Разбор входящих датаграмм выполняет <see cref="DataPortMultiplexer" />.
/// </summary>
public sealed class BleNetworkIdTransceiver(
    Func<ReadOnlyMemory<byte>, TransportAddress, CancellationToken, ValueTask> sendRaw)
    : IUnicastTransceiver<BleNetworkIdMessage>
{
    private readonly Func<ReadOnlyMemory<byte>, TransportAddress, CancellationToken, ValueTask> _sendRaw = sendRaw ?? throw new ArgumentNullException(nameof(sendRaw));
    private bool _started;

    public event EventHandler<BleNetworkIdMessage>? GotData;

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

    public async ValueTask SendAsync(BleNetworkIdMessage message, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(destination);
        if (message.NetworkId.IsEmpty)
            throw new ArgumentException("NetworkId must not be empty.", nameof(message));
        if (BroadcastAddressFilter.IsLocalIpv4Broadcast(destination))
            throw new InvalidOperationException("Broadcast is not allowed for BLE NetworkId transceiver.");

        var packet = BleNetworkIdPacketCodec.BuildPacket(message.NetworkId);
        await _sendRaw(packet, destination, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        return StopAsync();
    }

    /// <summary>Вызывается из <see cref="DataPortMultiplexer" /> при разборе входящей датаграммы.</summary>
    internal void HandleIncoming(ReadOnlyMemory<byte> packet, TransportAddress remoteAddress)
    {
        if (!_started)
            return;
        if (BroadcastAddressFilter.IsLocalIpv4Broadcast(remoteAddress))
            return;
        if (!BleNetworkIdPacketCodec.TryParsePacket(packet.Span, out var networkId))
            return;

        var msg = new BleNetworkIdMessage(networkId, remoteAddress);
        try
        {
            GotData?.Invoke(this, msg);
        }
        catch
        {
            // не ронять цикл приёма
        }
    }
}
