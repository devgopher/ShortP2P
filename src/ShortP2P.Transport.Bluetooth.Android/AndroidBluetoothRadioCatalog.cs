namespace ShortP2P.Transport.Bluetooth.Android;

public sealed class AndroidBluetoothRadioCatalog : IBluetoothRadioCatalog
{
    public ValueTask<IReadOnlyList<BluetoothRadioInfo>> ListRadiosAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var list = new List<BluetoothRadioInfo>();
        try
        {
            var a = BluetoothAdapter.DefaultAdapter;
            if (a == null)
                return ValueTask.FromResult<IReadOnlyList<BluetoothRadioInfo>>(list);
            var addr = a.Address;
            if (string.IsNullOrWhiteSpace(addr) ||
                string.Equals(addr, "02:00:00:00:00:00", StringComparison.OrdinalIgnoreCase))
                return ValueTask.FromResult<IReadOnlyList<BluetoothRadioInfo>>(list);
            var mac = addr.Replace("-", ":", StringComparison.Ordinal);
            list.Add(new BluetoothRadioInfo("default", a.Name ?? mac, mac, true));
        }
        catch
        {
            // ignore
        }

        return ValueTask.FromResult<IReadOnlyList<BluetoothRadioInfo>>(list);
    }

    public async ValueTask<string?> ResolveMacStringAsync(string? deviceId,
        CancellationToken cancellationToken = default)
    {
        var radios = await ListRadiosAsync(cancellationToken).ConfigureAwait(false);
        if (radios.Count == 0)
            return null;
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var byId = radios.FirstOrDefault(r => r.DeviceId == deviceId);
            if (byId != null)
                return byId.MacString;
        }

        return radios.FirstOrDefault(r => r.IsDefault)?.MacString ?? radios[0].MacString;
    }
}