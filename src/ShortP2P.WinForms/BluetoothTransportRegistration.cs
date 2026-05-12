using ShortP2P.Transport.Abstractions;
using ShortP2P.Transport.Bluetooth.Windows;

namespace ShortP2P.WinForms;

internal sealed class BluetoothTransportRegistration
{
    public ITransport? Instance { get; }

    /// <summary>Сканер BLE-рекламы ShortP2P (WinRT); не зависит от успеха поднятия GATT-сервера транспорта.</summary>
    public IBleShortP2PPeripheralScanner PeripheralScanner { get; } = new WindowsBluetoothLeShortP2PScanner();

    public BluetoothTransportRegistration()
    {
        try
        {
            Instance = new WindowsBluetoothTransport();
        }
        catch
        {
            Instance = null;
        }
    }
}
