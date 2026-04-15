using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using NLog.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Targets;
using ShortP2P.Client;
using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;
using ShortP2P.MauiApp.Services;
using System.Text;

namespace ShortP2P.MauiApp;

public static class MauiProgram
{
    private static readonly Logger StartupLogger = LogManager.GetCurrentClassLogger();
    public static IServiceProvider Services { get; private set; } = null!;

    public static global::Microsoft.Maui.Hosting.MauiApp CreateMauiApp()
    {
        SQLitePCL.Batteries_V2.Init();
        ConfigureNLog();

        var builder = global::Microsoft.Maui.Hosting.MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
        builder.Logging.AddNLog();

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
        StartupLogger.Info("GUI application started");

        AppDomain.CurrentDomain.ProcessExit += (_, _) => LogManager.Shutdown();
        return app;
    }

    private static void ConfigureNLog()
    {
        var logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logsDirectory);

        var config = new LoggingConfiguration();
        var fileTarget = new FileTarget("gui-logfile")
        {
            FileName = Path.Combine(logsDirectory, "${date:format=dd.MM.yyyy}.log"),
            Layout = "${longdate}|${uppercase:${level}}|${logger}|${message}${onexception:inner=|${exception:format=tostring}}",
            KeepFileOpen = false,
            ConcurrentWrites = true,
            Encoding = Encoding.UTF8
        };

        config.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, fileTarget);
        LogManager.Configuration = config;
    }
}
