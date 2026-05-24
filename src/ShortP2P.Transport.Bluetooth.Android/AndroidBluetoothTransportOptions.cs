namespace ShortP2P.Transport.Bluetooth.Android;

/// <summary>
///     Peripheral = <see cref="Android.Bluetooth.BluetoothGattServer" /> + реклама LE.
/// </summary>
/// <param name="GattDiscoverable">Включать имя устройства в scan response (выше видимость в скане).</param>
/// <param name="LocalNetworkId">8-байтный hint NetworkId в Manufacturer Data scan response (v2).</param>
public readonly record struct AndroidBluetoothTransportOptions(bool GattDiscoverable = true, Guid? LocalNetworkId = null);
