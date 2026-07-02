using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>
///     Минимальные данные GATT-сессии пира на локальном сервере (для сопоставления с <see cref="GattSubscribedClient" />).
/// </summary>
internal sealed class LocalGattSession(string? deviceId)
{
    public string? DeviceId { get; } = deviceId;

    public static LocalGattSession FromGattSession(GattSession session) =>
        new(session.DeviceId?.Id);
}
