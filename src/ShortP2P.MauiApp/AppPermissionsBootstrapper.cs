namespace ShortP2P.MauiApp;

internal static class AppPermissionsBootstrapper
{
    private static int _requested;

    public static async Task EnsureRequestedAsync(ILogger logger)
    {
        if (Interlocked.Exchange(ref _requested, 1) == 1)
            return;

#if ANDROID
        await RequestIfNeededAsync<Permissions.Camera>(logger, "camera").ConfigureAwait(false);
        await RequestIfNeededAsync<Permissions.Bluetooth>(logger, "bluetooth").ConfigureAwait(false);
        if (OperatingSystem.IsAndroidVersionAtLeast(31) == false)
            await RequestIfNeededAsync<Permissions.LocationWhenInUse>(logger, "location-when-in-use")
                .ConfigureAwait(false);
        await RequestIfNeededAsync<Permissions.StorageRead>(logger, "storage-read").ConfigureAwait(false);
        await RequestIfNeededAsync<Permissions.StorageWrite>(logger, "storage-write").ConfigureAwait(false);
        await RequestIfNeededAsync<Permissions.PostNotifications>(logger, "notifications").ConfigureAwait(false);
#endif
    }

    private static async Task RequestIfNeededAsync<TPermission>(ILogger logger, string permissionName)
        where TPermission : Permissions.BasePermission, new()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<TPermission>().ConfigureAwait(false);
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<TPermission>().ConfigureAwait(false);
            logger.LogInformation("Permission {PermissionName}: {Status}", permissionName, status);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Permission request failed for {PermissionName}", permissionName);
        }
    }
}