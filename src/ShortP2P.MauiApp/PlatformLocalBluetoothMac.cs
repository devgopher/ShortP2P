#if WINDOWS
using ShortP2P.Transport.Bluetooth.Windows;
#endif
#if ANDROID
using Android.Bluetooth;
#endif

namespace ShortP2P.MauiApp;

internal static class PlatformLocalBluetoothMac
{
    internal static async Task<string?> TryGetAsync()
    {
#if WINDOWS
        try
        {
            return await LocalAdapterBluetoothMac.TryGetAdapterMacStringAsync().ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
#elif ANDROID
        await Task.CompletedTask.ConfigureAwait(false);
        return TryAndroid();
#else
        await Task.CompletedTask.ConfigureAwait(false);
        return null;
#endif
    }

#if ANDROID
    private static string? TryAndroid()
    {
        try
        {
            var a = BluetoothAdapter.DefaultAdapter;
            if (a == null)
                return null;
            var addr = a.Address;
            if (string.IsNullOrWhiteSpace(addr))
                return null;
            if (string.Equals(addr, "02:00:00:00:00:00", StringComparison.OrdinalIgnoreCase))
                return null;
            return addr;
        }
        catch
        {
            return null;
        }
    }
#endif
}
