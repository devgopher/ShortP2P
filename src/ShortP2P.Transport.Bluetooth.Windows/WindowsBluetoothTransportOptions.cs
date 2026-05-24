using Microsoft.Extensions.Logging;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>
///     Параметры GATT-сервера ShortP2P на Windows. Peripheral = локальный GATT Server.
/// </summary>
/// <param name="GattDiscoverable">
///     Если <see langword="true" />, устройство видно в BLE-скане. Если <see langword="false" />, WinRT
///     вызывает StartAdvertising с <c>IsConnectable = true</c> и <c>IsDiscoverable = false</c>.
/// </param>
/// <param name="LocalAdapterBluetoothAddress">
///     MAC выбранного радио (ulong WinRT). <see langword="null" /> — адаптер по умолчанию.
/// </param>
/// <param name="LocalNetworkId">NetworkId hint в GATT Service Data и Manufacturer Data.</param>
/// <param name="Logger">Опциональный логгер (реклама, publisher, приём ADV).</param>
public readonly record struct WindowsBluetoothTransportOptions(
    bool GattDiscoverable = true,
    ulong? LocalAdapterBluetoothAddress = null,
    Guid? LocalNetworkId = null,
    ILogger? Logger = null);
