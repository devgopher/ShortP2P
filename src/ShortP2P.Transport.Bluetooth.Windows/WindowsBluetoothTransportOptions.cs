namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>
///     Параметры GATT-сервера ShortP2P на Windows. Peripheral = локальный GATT Server.
/// </summary>
/// <param name="GattDiscoverable">
///     Если <see langword="true" />, устройство видно в BLE-скане. Если <see langword="false" />, WinRT
///     вызывает StartAdvertising с <c>IsConnectable = true</c> и <c>IsDiscoverable = false</c>.
/// </param>
public readonly record struct WindowsBluetoothTransportOptions(bool GattDiscoverable = true);
