using Microsoft.Extensions.DependencyInjection;

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
        return new Window(new NavigationPage(login));
    }
}
