using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ShortP2P.Client.Bluetooth;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;
using ShortP2P.Transport;

namespace ShortP2P.WinForms;

internal sealed class RoutingSettingsForm : Form
{
    private readonly List<BluetoothRadioInfo> _adapterRadios = [];

    private readonly CheckBox _advertisePeerSearch = new()
    {
        AutoSize = true,
        Text = "Discovery: отдавать маршрутную таблицу по UDP (PeerSearch)",
        Anchor = AnchorStyles.Left
    };

    private readonly NumericUpDown _attempts = new() { Minimum = 1, Maximum = 20, Width = 80 };

    private readonly ComboBox _bluetoothAdapter = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 360,
        Anchor = AnchorStyles.Left
    };

    private readonly IBluetoothRadioCatalog _bluetoothCatalog;
    private readonly IBluetoothTransportProvider _bluetoothTransport;
    private readonly NumericUpDown _delayMs = new() { Minimum = 0, Maximum = 120_000, Width = 100 };

    private readonly CheckBox _enableBluetoothTransport = new()
        { AutoSize = true, Text = "Bluetooth", Anchor = AnchorStyles.Left };

    private readonly CheckBox _enableUdpTransport = new() { AutoSize = true, Text = "UDP", Anchor = AnchorStyles.Left };

    private readonly ComboBox _linkTechnology = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 360,
        Anchor = AnchorStyles.Left
    };

    private readonly ILogger<RoutingSettingsForm> _logger;
    private readonly NumericUpDown _maxHops = new() { Minimum = 1, Maximum = 3, Width = 80 };
    private readonly UserP2pRuntime _runtime;
    private readonly NumericUpDown _searchTimeoutMs = new() { Minimum = 500, Maximum = 120_000, Width = 100 };
    private readonly P2pRoutingSettingsStore _store;

    private readonly CheckBox _suggestBluetoothPairing = new()
    {
        AutoSize = true,
        Text = "Предлагать сопряжение по Bluetooth",
        Anchor = AnchorStyles.Left
    };

    private readonly ILogger<UserAction> _userActions;
    private bool _trafficSavingEnabled;

    public RoutingSettingsForm(P2pRoutingSettingsStore store, UserP2pRuntime runtime,
        IBluetoothRadioCatalog bluetoothCatalog, IBluetoothTransportProvider bluetoothTransport,
        ILogger<RoutingSettingsForm> logger,
        ILogger<UserAction> userActions)
    {
        _store = store;
        _runtime = runtime;
        _bluetoothCatalog = bluetoothCatalog;
        _bluetoothTransport = bluetoothTransport;
        _logger = logger;
        _userActions = userActions;
        foreach (var p in LinkTechnologyPresetExtensions.AllPresets)
            _linkTechnology.Items.Add(p.GetDisplayLabel());
        Text = "P2P routing";
        StartPosition = FormStartPosition.CenterParent;
        Width = 520;
        Height = 420;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(12)
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
        Row(5, "Маршрутизация", _advertisePeerSearch);
        var transportPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Anchor = AnchorStyles.Left
        };
        transportPanel.Controls.Add(_enableUdpTransport);
        transportPanel.Controls.Add(_enableBluetoothTransport);
        Row(6, "Транспорт", transportPanel);
        Row(7, "Bluetooth-адаптер", _bluetoothAdapter);
        Row(8, "Bluetooth", _suggestBluetoothPairing);

        var bluetoothTools = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Anchor = AnchorStyles.Left
        };
        var openBluetoothSettings = new Button
        {
            AutoSize = true,
            Text = "Открыть Bluetooth настройки"
        };
        openBluetoothSettings.Click += (_, _) => OpenBluetoothSettings();
        bluetoothTools.Controls.Add(openBluetoothSettings);
        Row(9, "Быстрое действие", bluetoothTools);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Bottom,
            Padding = new Padding(0, 8, 0, 0)
        };
        var ok = new Button { Text = "Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true };
        cancel.Click += (_, _) =>
        {
            _userActions.LogInformation("P2P routing: cancelled without save");
            Close();
        };
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
        _logger.LogInformation("P2P routing settings dialog opened");
    }

    private void OpenBluetoothSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
            _userActions.LogInformation("P2P routing: opened system bluetooth settings");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open Bluetooth settings");
            MessageBox.Show(this, ex.Message, "Bluetooth", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
        _enableUdpTransport.Checked = s.EnableUdpTransport;
        _enableBluetoothTransport.Checked = s.EnableBluetoothTransport;
        _suggestBluetoothPairing.Checked = s.SuggestBluetoothPairing;
        _trafficSavingEnabled = s.TrafficSavingEnabled;
        _advertisePeerSearch.Checked = s.AdvertisedPeerCapabilities.HasFlag(PresencePeerCapabilities.PeerSearch);
        await LoadBluetoothAdaptersAsync(s).ConfigureAwait(true);
    }

    private async Task LoadBluetoothAdaptersAsync(P2pRoutingSettings settings)
    {
        _bluetoothAdapter.Items.Clear();
        _adapterRadios.Clear();
        try
        {
            var radios = await _bluetoothCatalog.ListRadiosAsync().ConfigureAwait(true);
            _adapterRadios.AddRange(radios);
            foreach (var r in radios)
            {
                var suffix = r.IsDefault ? " — по умолчанию" : string.Empty;
                _bluetoothAdapter.Items.Add($"{r.DisplayName} ({r.MacString}){suffix}");
            }

            var idx = 0;
            if (!string.IsNullOrWhiteSpace(settings.SelectedBluetoothAdapterDeviceId))
            {
                var i = _adapterRadios.FindIndex(r =>
                    r.DeviceId == settings.SelectedBluetoothAdapterDeviceId);
                if (i >= 0)
                    idx = i;
            }
            else
            {
                var def = _adapterRadios.FindIndex(r => r.IsDefault);
                if (def >= 0)
                    idx = def;
            }

            if (_bluetoothAdapter.Items.Count > 0)
                _bluetoothAdapter.SelectedIndex = idx;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list Bluetooth adapters");
            _bluetoothAdapter.Items.Add("(адаптеры недоступны)");
            _bluetoothAdapter.SelectedIndex = 0;
        }
    }

    private void ApplySelectedAdapter(P2pRoutingSettings s)
    {
        var sel = _bluetoothAdapter.SelectedIndex;
        if (_adapterRadios.Count == 0 || sel < 0 || sel >= _adapterRadios.Count)
        {
            s.SelectedBluetoothAdapterDeviceId = null;
            s.SelectedBluetoothAdapterMac = null;
            return;
        }

        var r = _adapterRadios[sel];
        s.SelectedBluetoothAdapterDeviceId = r.DeviceId;
        s.SelectedBluetoothAdapterMac = r.MacString;
    }

    private async Task SaveAsync()
    {
        var li = _linkTechnology.SelectedIndex;
        if (li < 0 || li >= LinkTechnologyPresetExtensions.AllPresets.Length)
            li = 0;

        var cap = (_runtime.Settings.AdvertisedPeerCapabilities & ~PresencePeerCapabilities.PeerSearch) |
                  PresencePeerCapabilities.Chat;
        if (_advertisePeerSearch.Checked)
            cap |= PresencePeerCapabilities.PeerSearch;
        var s = new P2pRoutingSettings
        {
            MaxSearchHops = (int)_maxHops.Value,
            SendFailureSearchAttempts = (int)_attempts.Value,
            SendFailureRetryDelay = TimeSpan.FromMilliseconds((double)_delayMs.Value),
            SearchWaitTimeout = TimeSpan.FromMilliseconds((double)_searchTimeoutMs.Value),
            LinkTechnology = LinkTechnologyPresetExtensions.AllPresets[li],
            EnableUdpTransport = _enableUdpTransport.Checked,
            EnableBluetoothTransport = _enableBluetoothTransport.Checked,
            SuggestBluetoothPairing = _suggestBluetoothPairing.Checked,
            TrafficSavingEnabled = _trafficSavingEnabled,
            AdvertisedPeerCapabilities = cap
        };
        ApplySelectedAdapter(s);
        await _store.SaveAsync(s).ConfigureAwait(true);
        _userActions.LogInformation(
            "P2P routing: saved (max hops {MaxHops}, attempts {Attempts}, delay ms {DelayMs}, find timeout ms {TimeoutMs}, link {Link}, bt adapter {BtAdapter})",
            s.MaxSearchHops, s.SendFailureSearchAttempts, s.SendFailureRetryDelay.TotalMilliseconds,
            s.SearchWaitTimeout.TotalMilliseconds, s.LinkTechnology, s.SelectedBluetoothAdapterMac ?? "(default)");
        _runtime.Settings.MaxSearchHops = s.MaxSearchHops;
        _runtime.Settings.SendFailureSearchAttempts = s.SendFailureSearchAttempts;
        _runtime.Settings.SendFailureRetryDelay = s.SendFailureRetryDelay;
        _runtime.Settings.SearchWaitTimeout = s.SearchWaitTimeout;
        _runtime.Settings.LinkTechnology = s.LinkTechnology;
        _runtime.Settings.EnableUdpTransport = s.EnableUdpTransport;
        _runtime.Settings.EnableBluetoothTransport = s.EnableBluetoothTransport;
        _runtime.Settings.SelectedBluetoothAdapterDeviceId = s.SelectedBluetoothAdapterDeviceId;
        _runtime.Settings.SelectedBluetoothAdapterMac = s.SelectedBluetoothAdapterMac;
        _runtime.Settings.SuggestBluetoothPairing = s.SuggestBluetoothPairing;
        _runtime.Settings.TrafficSavingEnabled = s.TrafficSavingEnabled;
        _runtime.Settings.AdvertisedPeerCapabilities = s.AdvertisedPeerCapabilities | PresencePeerCapabilities.Chat;
        _bluetoothTransport.ApplySettings(s);
    }
}