using Android.App;
using Android.Content;
using Android.Content.PM;

namespace ShortP2P.MauiApp;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int QrScanRequestCode = 0x51A7;
    private static TaskCompletionSource<string?>? _scanTcs;

    public static Task<string?> TryScanQrWithSystemScannerAsync()
    {
        var activity = Platform.CurrentActivity;
        if (activity == null)
            return Task.FromResult<string?>(null);
        var packageManager = activity.PackageManager;
        if (packageManager == null)
            return Task.FromResult<string?>(null);

        var intent = new Intent("com.google.zxing.client.android.SCAN");
        intent.PutExtra("SCAN_MODE", "QR_CODE_MODE");
        if (intent.ResolveActivity(packageManager) == null)
            return Task.FromResult<string?>(null);

        _scanTcs?.TrySetCanceled();
        _scanTcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.StartActivityForResult(intent, QrScanRequestCode);
        return _scanTcs.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode == QrScanRequestCode)
        {
            var tcs = _scanTcs;
            _scanTcs = null;
            if (tcs != null)
            {
                if (resultCode == Result.Ok)
                    tcs.TrySetResult(data?.GetStringExtra("SCAN_RESULT"));
                else
                    tcs.TrySetResult(null);
            }

            return;
        }

        base.OnActivityResult(requestCode, resultCode, data);
    }
}