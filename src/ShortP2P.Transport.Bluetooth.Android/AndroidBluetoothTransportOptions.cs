using Android.Bluetooth;
using ShortP2P.Auth.Data;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport.Bluetooth.Android;

/// <summary>
///     Peripheral = <see cref="BluetoothGattServer" /> + реклама LE.
/// </summary>
/// <param name="GattDiscoverable">Включать имя устройства в scan response (выше видимость в скане).</param>
/// <param name="LocalNetworkId">Локальный NetworkId для GATT-кадра 0x32 сопряжённым пирам.</param>
/// <param name="OnPeerNetworkIdReceived">Вызывается после приёма GATT NetworkId announce от пира.</param>
public readonly record struct AndroidBluetoothTransportOptions(
    bool GattDiscoverable = true,
    CompressedNetworkId? LocalNetworkId = null,
    Action<TransportAddress, CompressedNetworkId>? OnPeerNetworkIdReceived = null);