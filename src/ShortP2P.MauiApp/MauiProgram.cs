using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using NLog.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Targets.Wrappers;
using ShortP2P.Auth;
using ShortP2P.Client;
using ShortP2P.Client.ChatMedia;
using ShortP2P.Client.Data;
using ShortP2P.Client.Bluetooth;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;
using ShortP2P.Discovery;
using ShortP2P.Discovery.Ble;
using ShortP2P.Discovery.Pings;
using ShortP2P.Discovery.RouteTables;
using ShortP2P.MauiApp.Services;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;
#if ANDROID
using ShortP2P.Transport.Bluetooth.Android;
#endif
#if WINDOWS
using ShortP2P.Transport.Bluetooth.Windows;
#endif

namespace ShortP2P.MauiApp;

public static class MauiProgram
{
    public static IServiceProvider Services { get; private set; } = null!;
    private static bool _globalExceptionHandlersInstalled;

    public static global::Microsoft.Maui.Hosting.MauiApp CreateMauiApp()
    {
        SQLitePCL.Batteries_V2.Init();
        ConfigureNLog();
        ConfigureGlobalExceptionHandlers();

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

        builder.Services.AddSingleton(_ =>
            ChatMediaOptions.LoadOrDefault(Path.Combine(FileSystem.AppDataDirectory, "chat-media.json")));
        builder.Services.AddSingleton(_ => new AppDatabase(Path.Combine(FileSystem.AppDataDirectory, "shortp2p.db")));
        builder.Services.AddSingleton<IUserAuthRepository, SqliteUserAuthRepository>();
        builder.Services.AddRouteDbContextWithPeerExpiryCleanup(
            Path.Combine(FileSystem.AppDataDirectory, "routes.db"), enableDiscovery: true);
        builder.Services.AddSingleton<ISessionStorage, MauiSecureStorage>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<ChatRepository>();
        builder.Services.AddSingleton<IBluetoothPresencePingTargetsProvider, BluetoothPresencePingTargetsProvider>();
        builder.Services.AddSingleton<IBleDiscoveredPeerStore, SqliteBleDiscoveredPeerStore>();
        builder.Services.AddSingleton<P2pRoutingSettingsStore>();
        builder.Services.AddSingleton<IUdpTransportFactory, UdpTransportFactory>();
        builder.Services.AddSingleton<ChatSessionCache>();
        builder.Services.AddSingleton<P2pCryptoSessionCache>();
#if ANDROID
        builder.Services.AddSingleton<IBluetoothRadioCatalog, AndroidBluetoothRadioCatalog>();
        builder.Services.AddSingleton<IBleShortP2PPeripheralScanner>(_ =>
            new AndroidBluetoothLeShortP2PScanner(global::Android.App.Application.Context));
#elif WINDOWS
        builder.Services.AddSingleton<IBluetoothRadioCatalog, WindowsBluetoothRadioCatalog>();
        builder.Services.AddSingleton<IBleShortP2PPeripheralScanner>(_ => new WindowsBluetoothLeShortP2PScanner());
#endif
        builder.Services.AddSingleton<MauiBluetoothTransportRegistration>();
        builder.Services.AddSingleton<IBluetoothTransportProvider>(sp =>
            sp.GetRequiredService<MauiBluetoothTransportRegistration>());
        builder.Services.AddSingleton(sp => new UserP2pRuntime(
            sp.GetRequiredService<P2pRoutingSettingsStore>(),
            sp.GetRequiredService<AuthService>(),
            sp.GetRequiredService<ChatRepository>(),
            sp.GetRequiredService<ChatMediaOptions>(),
            sp.GetRequiredService<IUdpTransportFactory>(),
            sp.GetRequiredService<ChatSessionCache>(),
            sp.GetRequiredService<P2pCryptoSessionCache>(),
            sp.GetService<IBluetoothTransportProvider>(),
            additionalDiscoveryTransports: null,
            sp.GetService<IRouteTableSnapshotSource>(),
            sp.GetService<IDiscoveryPingStore>(),
            sp.GetService<IBleShortP2PPeripheralScanner>(),
            sp.GetService<IBleDiscoveredPeerStore>(),
            sp.GetRequiredService<IBluetoothPresencePingTargetsProvider>()));
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
        Services.ApplyRouteDatabaseMigrationsAsync().GetAwaiter().GetResult();
#if ANDROID || WINDOWS
        var routing = Services.GetRequiredService<P2pRoutingSettingsStore>().LoadAsync().GetAwaiter().GetResult();
        Services.GetRequiredService<MauiBluetoothTransportRegistration>().ApplySettings(routing);
#endif
        IncomingMessageSound.EnsureHooked(Services.GetRequiredService<ChatRepository>(),
            Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(IncomingMessageSound)));
        Services.GetRequiredService<ILogger<MauiHost>>().LogInformation("GUI application started");

        AppDomain.CurrentDomain.ProcessExit += (_, _) => LogManager.Shutdown();
        return app;
    }

    private static void ConfigureNLog()
    {
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "nlog.config");
            if (File.Exists(configPath))
            {
                LogManager.Configuration = new XmlLoggingConfiguration(configPath);
                LogManager.GetCurrentClassLogger().Info("Loaded NLog config from {Path}", configPath);
                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load nlog.config: {ex}");
        }

        var logsDir = Path.Combine(FileSystem.AppDataDirectory, "logs");
        Directory.CreateDirectory(logsDir);

        var fallbackConfig = new LoggingConfiguration();
        var fileTarget = new FileTarget("gui-logfile")
        {
            FileName = Path.Combine(logsDir, "${date:format=yyyy-MM-dd}.log"),
            Layout = "${longdate}|${uppercase:${level}}|${logger}|${message}${onexception:inner=|${exception:format=tostring}}",
            Encoding = System.Text.Encoding.UTF8,
            KeepFileOpen = false,
            ConcurrentWrites = true,
            CreateDirs = true
        };

        // Async wrapper avoids blocking UI thread during app startup/log bursts.
        var asyncTarget = new AsyncTargetWrapper(fileTarget, 1000, AsyncTargetWrapperOverflowAction.Discard);
        fallbackConfig.AddTarget(asyncTarget);
        fallbackConfig.AddRuleForAllLevels(asyncTarget);
        LogManager.Configuration = fallbackConfig;
        LogManager.GetCurrentClassLogger().Info("Using fallback NLog file target: {Path}", logsDir);
    }

    private static void ConfigureGlobalExceptionHandlers()
    {
        if (_globalExceptionHandlersInstalled)
        {
            return;
        }

        _globalExceptionHandlersInstalled = true;
        var logger = LogManager.GetLogger("GlobalExceptions");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            logger.Error(ex, "UnhandledException. IsTerminating={IsTerminating}", args.IsTerminating);
            LogManager.Flush();
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logger.Error(args.Exception, "UnobservedTaskException");
            args.SetObserved();
            LogManager.Flush();
        };
    }
}
