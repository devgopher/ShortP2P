using ShortP2P.Client;
using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;

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
        var p2p = new UserP2pRuntime(auth, chats, routingStore);

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
}
