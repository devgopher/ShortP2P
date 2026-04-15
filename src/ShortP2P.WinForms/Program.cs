using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Config;
using NLog.Extensions.Logging;
using ShortP2P.Client;
using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;
using NLog;

namespace ShortP2P.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ConfigureNLog();

        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
            logging.AddNLog();
        });

        var appRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShortP2P", "WinForms");
        services.AddSingleton(_ => new AppDatabase(Path.Combine(appRoot, "shortp2p.db")));
        services.AddSingleton<ISessionStorage>(_ => new FileSessionStorage(Path.Combine(appRoot, "session")));
        services.AddSingleton<AuthService>();
        services.AddSingleton<ChatRepository>();
        services.AddSingleton<P2pRoutingSettingsStore>();
        services.AddSingleton<BluetoothTransportRegistration>();
        services.AddSingleton(sp =>
            new UserP2pRuntime(
                sp.GetRequiredService<AuthService>(),
                sp.GetRequiredService<ChatRepository>(),
                sp.GetRequiredService<P2pRoutingSettingsStore>(),
                sp.GetRequiredService<BluetoothTransportRegistration>().Instance));

        services.AddTransient<LoginForm>();
        services.AddTransient<RegisterForm>();
        services.AddTransient<MainChatsForm>();
        services.AddTransient<AddChatForm>();
        services.AddTransient<MyQrForm>();
        services.AddTransient<RoutingSettingsForm>();

        using var provider = services.BuildServiceProvider();
        var hostLogger = provider.GetRequiredService<ILogger<WinFormsHost>>();
        var userActionLogger = provider.GetRequiredService<ILogger<UserAction>>();
        RegisterGlobalExceptionLogging(hostLogger);

        SQLitePCL.Batteries_V2.Init();
        ApplicationConfiguration.Initialize();
        hostLogger.LogInformation("WinForms application started");

        var p2p = provider.GetRequiredService<UserP2pRuntime>();
        var bluetoothRegistration = provider.GetRequiredService<BluetoothTransportRegistration>();
        try
        {
            while (true)
            {
                using var login = provider.GetRequiredService<LoginForm>();
                if (login.ShowDialog() != DialogResult.OK)
                {
                    userActionLogger.LogInformation("Login: closed without signing in");
                    return;
                }

                using var main = provider.GetRequiredService<MainChatsForm>();
                var mainResult = main.ShowDialog();
                if (mainResult != DialogResult.Retry)
                {
                    userActionLogger.LogInformation("Main: closed (dialog result {Result})", mainResult);
                    return;
                }
            }
        }
        finally
        {
            p2p.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (bluetoothRegistration.Instance is IAsyncDisposable asyncTransport)
                asyncTransport.DisposeAsync().AsTask().GetAwaiter().GetResult();

            LogManager.Shutdown();
        }
    }

    private static void ConfigureNLog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "nlog.config");
        LogManager.Configuration = new XmlLoggingConfiguration(path);
    }

    private static void RegisterGlobalExceptionLogging(ILogger<WinFormsHost> logger)
    {
        Application.ThreadException += (_, args) =>
            logger.LogError(args.Exception, "Unhandled UI thread exception");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                logger.LogCritical(ex, "Unhandled domain exception");
            else
                logger.LogCritical("Unhandled domain exception (non-CLR): {Data}", args.ExceptionObject);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logger.LogError(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }
}
