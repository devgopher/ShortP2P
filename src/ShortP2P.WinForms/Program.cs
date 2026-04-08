using ShortP2P.Client;
using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;
using ShortP2P.Transport.Bluetooth.Windows;

namespace ShortP2P.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        SQLitePCL.Batteries_V2.Init();
        ApplicationConfiguration.Initialize();

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
        }
    }
}
