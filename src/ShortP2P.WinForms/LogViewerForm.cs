using System.Text;

namespace ShortP2P.WinForms;

/// <summary>Shows NLog file targets from nlog.config (gui log + user actions), refreshed on a timer.</summary>
public sealed class LogViewerForm : Form
{
    private const int RefreshIntervalMs = 5000;
    private const long MaxTailBytes = 768 * 1024;

    private readonly TextBox _appLog = CreateLogTextBox();
    private readonly TextBox _userLog = CreateLogTextBox();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = RefreshIntervalMs };

    public LogViewerForm()
    {
        Text = "ShortP2P — Logs";
        StartPosition = FormStartPosition.CenterParent;
        Width = 720;
        Height = 520;
        MinimumSize = new Size(400, 280);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("Application") { Controls = { _appLog } });
        tabs.TabPages.Add(new TabPage("User actions") { Controls = { _userLog } });
        foreach (TabPage p in tabs.TabPages)
        {
            p.Padding = new Padding(4);
            p.Controls[0].Dock = DockStyle.Fill;
        }

        Controls.Add(tabs);

        _timer.Tick += (_, _) => RefreshLogs();
        Shown += (_, _) =>
        {
            RefreshLogs();
            _timer.Start();
        };
        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _timer.Dispose();
        };
    }

    private static TextBox CreateLogTextBox() => new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 9f),
        Dock = DockStyle.Fill,
    };

    private void RefreshLogs()
    {
        var date = DateTime.Now.ToString("dd.MM.yyyy");
        var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
        _appLog.Text = ReadLogFile(Path.Combine(logsDir, $"{date}.log"));
        _userLog.Text = ReadLogFile(Path.Combine(logsDir, $"winFormsClient_{date}.log"));
    }

    private static string ReadLogFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return "(No log file for today yet.)";

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var prefix = "";
            if (fs.Length > MaxTailBytes)
            {
                fs.Seek(fs.Length - MaxTailBytes, SeekOrigin.Begin);
                prefix = $"(Showing last {MaxTailBytes} bytes.)\r\n\r\n";
            }

            using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return prefix + reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            return "Could not read log: " + ex.Message;
        }
    }
}
