using ShortP2P.Transport.Abstractions;
using ShortP2P.Transport.Bluetooth.Windows;

namespace ShortP2P.WinForms;

internal sealed class BluetoothTransportRegistration
{
    public ITransport? Instance { get; }

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
