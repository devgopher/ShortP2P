using ShortP2P.Client.Routing;
using ShortP2P.Transport;

namespace ShortP2P.Client.Bluetooth;

public static class BluetoothRoutingMac
{
    public static async ValueTask<string?> GetEffectiveMacAsync(P2pRoutingSettings settings,
        IBluetoothRadioCatalog? catalog, CancellationToken cancellationToken = default)
    {
        if (!settings.EnableBluetoothTransport)
            return null;
        if (!string.IsNullOrWhiteSpace(settings.SelectedBluetoothAdapterMac))
            return settings.SelectedBluetoothAdapterMac.Trim();
        if (catalog != null)
            return await catalog.ResolveMacStringAsync(settings.SelectedBluetoothAdapterDeviceId, cancellationToken)
                .ConfigureAwait(false);
        return null;
    }
}
