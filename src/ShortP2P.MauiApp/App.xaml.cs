using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ShortP2P.MauiApp;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var login = MauiProgram.Services.GetRequiredService<LoginPage>();
        MauiProgram.Services.GetRequiredService<ILogger<App>>().LogInformation("Application window created");
        return new Window(new NavigationPage(login));
    }
}
