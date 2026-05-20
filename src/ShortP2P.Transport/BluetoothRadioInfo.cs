namespace ShortP2P.Transport;

/// <summary>Локальный Bluetooth-радиоадаптер (для выбора в Routing).</summary>
public sealed record BluetoothRadioInfo(
    string DeviceId,
    string DisplayName,
    string MacString,
    bool IsDefault);
