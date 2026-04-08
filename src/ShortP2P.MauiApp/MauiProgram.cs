using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using ShortP2P.Client;
using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;
using ShortP2P.MauiApp.Services;

namespace ShortP2P.MauiApp;

public static class MauiProgram
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static global::Microsoft.Maui.Hosting.MauiApp CreateMauiApp()
    {
        SQLitePCL.Batteries_V2.Init();

        var builder = global::Microsoft.Maui.Hosting.MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton(_ => new AppDatabase(Path.Combine(FileSystem.AppDataDirectory, "shortp2p.db")));
        builder.Services.AddSingleton<ISessionStorage, MauiSecureStorage>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<ChatRepository>();
        builder.Services.AddSingleton<P2pRoutingSettingsStore>();
        builder.Services.AddSingleton<UserP2pRuntime>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<RegisterPage>();
        builder.Services.AddTransient<ChatsPage>();
        builder.Services.AddTransient<ChatDetailPage>();
        builder.Services.AddTransient<AddChatPage>();
        builder.Services.AddTransient<MyQrPage>();
        builder.Services.AddTransient<RoutingSettingsPage>();
        builder.Services.AddTransient<LanScanPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        Services = app.Services;
        return app;
    }
}
