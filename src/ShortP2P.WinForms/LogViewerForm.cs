using System.Runtime.InteropServices;
using System.Text;
using Timer = System.Windows.Forms.Timer;

namespace ShortP2P.WinForms;

/// <summary>Shows NLog file targets from nlog.config (gui log + user actions), refreshed on a timer.</summary>
public sealed class LogViewerForm : Form
{
    private const int RefreshIntervalMs = 5000;
    private const long MaxTailBytes = 768 * 1024;
    private const uint WmVscroll = 0x0115;
    private static readonly IntPtr SbBottom = 7;

    private readonly TextBox _appLog = CreateLogTextBox();
    private readonly Timer _timer = new() { Interval = RefreshIntervalMs };
    private readonly TextBox _userLog = CreateLogTextBox();

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static TextBox CreateLogTextBox()
    {
        return new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            Dock = DockStyle.Fill
        };
    }

    private void RefreshLogs()
    {
        var date = DateTime.Now.ToString("dd.MM.yyyy");
        var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
        _appLog.Text = ReadLogFile(Path.Combine(logsDir, $"{date}.log"));
        _userLog.Text = ReadLogFile(Path.Combine(logsDir, $"winFormsClient_{date}.log"));
        ScrollLogToLatest(_appLog);
        ScrollLogToLatest(_userLog);
    }

    /// <summary>Moves caret and viewport to the end (works even when the text box is not focused).</summary>
    private static void ScrollLogToLatest(TextBox box)
    {
        if (box.TextLength == 0)
            return;
        box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;
        if (box.IsHandleCreated)
            _ = SendMessage(box.Handle, WmVscroll, SbBottom, IntPtr.Zero);
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

            using var reader = new StreamReader(fs, Encoding.UTF8, true);
            return prefix + reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            return "Could not read log: " + ex.Message;
        }
    }
}