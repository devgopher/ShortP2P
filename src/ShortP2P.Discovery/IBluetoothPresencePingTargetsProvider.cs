using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery;

/// <summary>
///     MAC-адреса BLE, на которые нужно слать presence-пинг в каждом раунде (чаты, скан и т.д.).
/// </summary>
public interface IBluetoothPresencePingTargetsProvider
{
    ValueTask<IReadOnlyList<TransportAddress>> GetBluetoothPingTargetsAsync(
        CancellationToken cancellationToken = default);
}
