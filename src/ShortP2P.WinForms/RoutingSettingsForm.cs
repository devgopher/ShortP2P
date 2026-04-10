using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;

namespace ShortP2P.WinForms;

internal sealed class RoutingSettingsForm : Form
{
    private readonly P2pRoutingSettingsStore _store;
    private readonly UserP2pRuntime _runtime;
    private readonly NumericUpDown _maxHops = new() { Minimum = 1, Maximum = 3, Width = 80 };
    private readonly NumericUpDown _attempts = new() { Minimum = 1, Maximum = 20, Width = 80 };
    private readonly NumericUpDown _delayMs = new() { Minimum = 0, Maximum = 120_000, Width = 100 };
    private readonly NumericUpDown _searchTimeoutMs = new() { Minimum = 500, Maximum = 120_000, Width = 100 };
    private readonly ComboBox _linkTechnology = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 360,
        Anchor = AnchorStyles.Left,
    };

    public RoutingSettingsForm(P2pRoutingSettingsStore store, UserP2pRuntime runtime)
    {
        _store = store;
        _runtime = runtime;
        foreach (var p in LinkTechnologyPresetExtensions.AllPresets)
            _linkTechnology.Items.Add(p.GetDisplayLabel());
        Text = "P2P routing";
        StartPosition = FormStartPosition.CenterParent;
        Width = 520;
        Height = 320;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(12),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        void Row(int r, string label, Control c)
        {
            root.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, r);
            root.Controls.Add(c, 1, r);
        }

        Row(0, "Max search depth (graph edges, 1–3)", _maxHops);
        Row(1, "Send failure: search attempts", _attempts);
        Row(2, "Pause between attempts (ms)", _delayMs);
        Row(3, "FIND wait timeout (ms)", _searchTimeoutMs);
        Row(4, "Connection speed (presence ping, min bitrate)", _linkTechnology);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Bottom,
            Padding = new Padding(0, 8, 0, 0),
        };
        var ok = new Button { Text = "Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true };
        cancel.Click += (_, _) => Close();
        ok.Click += async (_, _) =>
        {
            await SaveAsync().ConfigureAwait(true);
            Close();
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        Controls.Add(root);
        Controls.Add(buttons);
        Load += async (_, _) => await LoadAsync().ConfigureAwait(true);
    }

    private async Task LoadAsync()
    {
        var s = await _store.LoadAsync().ConfigureAwait(true);
        _maxHops.Value = s.MaxSearchHops;
        _attempts.Value = s.SendFailureSearchAttempts;
        _delayMs.Value = (decimal)s.SendFailureRetryDelay.TotalMilliseconds;
        _searchTimeoutMs.Value = (decimal)s.SearchWaitTimeout.TotalMilliseconds;
        var li = Array.IndexOf(LinkTechnologyPresetExtensions.AllPresets, s.LinkTechnology);
        _linkTechnology.SelectedIndex = li >= 0 ? li : 0;
    }

    private async Task SaveAsync()
    {
        var li = _linkTechnology.SelectedIndex;
        if (li < 0 || li >= LinkTechnologyPresetExtensions.AllPresets.Length)
            li = 0;

        var s = new P2pRoutingSettings
        {
            MaxSearchHops = (int)_maxHops.Value,
            SendFailureSearchAttempts = (int)_attempts.Value,
            SendFailureRetryDelay = TimeSpan.FromMilliseconds((double)_delayMs.Value),
            SearchWaitTimeout = TimeSpan.FromMilliseconds((double)_searchTimeoutMs.Value),
            LinkTechnology = LinkTechnologyPresetExtensions.AllPresets[li],
        };
        await _store.SaveAsync(s).ConfigureAwait(true);
        _runtime.Settings.MaxSearchHops = s.MaxSearchHops;
        _runtime.Settings.SendFailureSearchAttempts = s.SendFailureSearchAttempts;
        _runtime.Settings.SendFailureRetryDelay = s.SendFailureRetryDelay;
        _runtime.Settings.SearchWaitTimeout = s.SearchWaitTimeout;
        _runtime.Settings.LinkTechnology = s.LinkTechnology;
    }
}
