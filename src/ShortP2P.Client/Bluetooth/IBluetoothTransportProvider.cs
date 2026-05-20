using ShortP2P.Client.Routing;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Bluetooth;

/// <summary>Создаёт и пересоздаёт платформенный BLE-транспорт по настройкам Routing.</summary>
public interface IBluetoothTransportProvider
{
    ITransport? Current { get; }

    void ApplySettings(P2pRoutingSettings settings);
}
