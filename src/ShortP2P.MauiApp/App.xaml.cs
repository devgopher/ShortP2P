namespace ShortP2P.MauiApp;

public partial class App : Application
{
    private int _permissionsBootstrapped;

    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var login = MauiProgram.Services.GetRequiredService<LoginPage>();
        var logger = MauiProgram.Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Application window created");
        if (Interlocked.Exchange(ref _permissionsBootstrapped, 1) == 0)
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await AppPermissionsBootstrapper.EnsureRequestedAsync(logger).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Permission bootstrap failed");
                }
            });
        return new Window(new NavigationPage(login));
    }
}