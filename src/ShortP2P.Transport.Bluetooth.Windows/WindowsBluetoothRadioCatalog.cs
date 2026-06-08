using System.Runtime.Versioning;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using TransportAddressFmt = ShortP2P.Transport.BluetoothTransportAddress;

namespace ShortP2P.Transport.Bluetooth.Windows;

[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsBluetoothRadioCatalog : IBluetoothRadioCatalog
{
    public async ValueTask<IReadOnlyList<BluetoothRadioInfo>> ListRadiosAsync(
        CancellationToken cancellationToken = default)
    {
        var list = new List<BluetoothRadioInfo>();
        string? defaultId = null;
        try
        {
            var def = await BluetoothAdapter.GetDefaultAsync().AsTask(cancellationToken).ConfigureAwait(false);
            defaultId = def?.DeviceId;
        }
        catch
        {
            // ignore
        }

        IReadOnlyList<DeviceInformation> infos;
        try
        {
            infos = await DeviceInformation.FindAllAsync(BluetoothAdapter.GetDeviceSelector())
                .AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return list;
        }

        foreach (var info in infos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var adapter = await BluetoothAdapter.FromIdAsync(info.Id).AsTask(cancellationToken)
                    .ConfigureAwait(false);
                if (adapter == null)
                    continue;
                var mac = BluetoothMacAddress.FromBluetoothAddress(adapter.BluetoothAddress);
                var macStr = TransportAddressFmt.ToMacString(mac);
                var name = string.IsNullOrWhiteSpace(info.Name) ? macStr : info.Name.Trim();
                list.Add(new BluetoothRadioInfo(info.Id, name, macStr, info.Id == defaultId));
            }
            catch
            {
                // ignore broken adapter entry
            }
        }

        if (list.Count == 0 && defaultId != null)
            try
            {
                var def = await BluetoothAdapter.GetDefaultAsync().AsTask(cancellationToken).ConfigureAwait(false);
                if (def != null)
                {
                    var mac = BluetoothMacAddress.FromBluetoothAddress(def.BluetoothAddress);
                    var macStr = TransportAddressFmt.ToMacString(mac);
                    list.Add(new BluetoothRadioInfo(def.DeviceId, macStr, macStr, true));
                }
            }
            catch
            {
                // ignore
            }

        return list;
    }

    public async ValueTask<string?> ResolveMacStringAsync(string? deviceId,
        CancellationToken cancellationToken = default)
    {
        var radios = await ListRadiosAsync(cancellationToken).ConfigureAwait(false);
        if (radios.Count == 0)
            return await LocalAdapterBluetoothMac.TryGetAdapterMacStringAsync().ConfigureAwait(false);

        BluetoothRadioInfo? pick = null;
        if (!string.IsNullOrWhiteSpace(deviceId))
            pick = radios.FirstOrDefault(r => r.DeviceId == deviceId);
        pick ??= radios.FirstOrDefault(r => r.IsDefault) ?? radios[0];
        return pick.MacString;
    }
}