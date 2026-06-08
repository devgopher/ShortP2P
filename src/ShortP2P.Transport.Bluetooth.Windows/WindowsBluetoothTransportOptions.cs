using Microsoft.Extensions.Logging;
using ShortP2P.Auth.Data;
using ShortP2P.Transport.Abstractions;

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
/// <param name="LocalNetworkId">Локальный NetworkId для GATT-кадра 0x32 сопряжённым пирам.</param>
/// <param name="OnPeerNetworkIdReceived">Вызывается после приёма GATT NetworkId announce от пира.</param>
/// <param name="Logger">Опциональный логгер (реклама, приём ADV).</param>
public readonly record struct WindowsBluetoothTransportOptions(
    bool GattDiscoverable = true,
    ulong? LocalAdapterBluetoothAddress = null,
    CompressedNetworkId? LocalNetworkId = null,
    Action<TransportAddress, CompressedNetworkId>? OnPeerNetworkIdReceived = null,
    ILogger? Logger = null);