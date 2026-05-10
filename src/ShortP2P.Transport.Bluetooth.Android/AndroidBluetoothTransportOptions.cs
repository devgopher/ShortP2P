namespace ShortP2P.Transport.Bluetooth.Android;

/// <summary>
///     Peripheral = <see cref="Android.Bluetooth.BluetoothGattServer" /> + реклама LE.
/// </summary>
/// <param name="GattDiscoverable">Включать имя устройства в scan response (выше видимость в скане).</param>
public readonly record struct AndroidBluetoothTransportOptions(bool GattDiscoverable = true);
