using ShortP2P.Client;
using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;
using ShortP2P.Transport.Bluetooth.Windows;
using NLog;
using NLog.Config;
using NLog.Targets;
using System.Text;

namespace ShortP2P.WinForms;

internal static class Program
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    [STAThread]
    private static void Main()
    {
        ConfigureNLog();
        RegisterGlobalExceptionLogging();

        SQLitePCL.Batteries_V2.Init();
        ApplicationConfiguration.Initialize();
        Logger.Info("WinForms application started");

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShortP2P",
            "WinForms");
        var db = new AppDatabase(Path.Combine(root, "shortp2p.db"));
        var session = new FileSessionStorage(Path.Combine(root, "session"));
        var auth = new AuthService(db, session);
        var chats = new ChatRepository(db);
        var routingStore = new P2pRoutingSettingsStore(session);

        WindowsBluetoothTransport? bluetooth = null;
        try
        {
            bluetooth = new WindowsBluetoothTransport();
        }
        catch
        {
            // Нет адаптера, отключённый Bluetooth или среда без WinRT — работаем только по UDP.
        }

        UserP2pRuntime? p2p = null;
        try
        {
            p2p = new UserP2pRuntime(auth, chats, routingStore, bluetooth);

            while (true)
            {
                using var login = new LoginForm(auth);
                if (login.ShowDialog() != DialogResult.OK)
                    return;

                using var main = new MainChatsForm(auth, chats, p2p, routingStore);
                if (main.ShowDialog() != DialogResult.Retry)
                    return;
            }
        }
        finally
        {
            if (p2p != null)
                p2p.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (bluetooth != null)
                bluetooth.DisposeAsync().AsTask().GetAwaiter().GetResult();

            LogManager.Shutdown();
        }
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

    private static void RegisterGlobalExceptionLogging()
    {
        Application.ThreadException += (_, args) =>
            Logger.Error(args.Exception, "Unhandled UI thread exception");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Fatal(args.ExceptionObject as Exception, "Unhandled domain exception");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }
}
