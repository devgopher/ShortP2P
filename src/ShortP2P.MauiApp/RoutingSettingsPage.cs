using Microsoft.Extensions.Logging;
using ShortP2P.Client.Bluetooth;
using ShortP2P.Client.WifiDirect;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;
using ShortP2P.Transport;

namespace ShortP2P.MauiApp;

public class RoutingSettingsPage : ContentPage
{
    private const string ErrorHeader = "Error";
    private readonly List<BluetoothRadioInfo> _adapterRadios = [];
    private readonly Switch _advertisePeerSearch = new();
    private readonly Entry _attempts = new() { Keyboard = Keyboard.Numeric };
    private readonly Picker _bluetoothAdapter = new();
    private readonly IBluetoothRadioCatalog _bluetoothCatalog;
    private readonly IBluetoothTransportProvider _bluetoothTransport;
#if WINDOWS
    private readonly IWifiDirectTransportProvider _wifiDirectTransport;
#endif
    private readonly Entry _delayMs = new() { Keyboard = Keyboard.Numeric };
    private readonly Switch _enableBluetoothTransport = new();
    private readonly Switch _enableWifiDirectTransport = new();
    private readonly Switch _enableUdpTransport = new();
    private readonly Picker _linkTechnology = new();
    private readonly ILogger<RoutingSettingsPage> _logger;
    private readonly Entry _maxHops = new() { Keyboard = Keyboard.Numeric, Placeholder = "1–3" };
    private readonly UserP2pRuntime _runtime;
    private readonly Entry _searchTimeoutMs = new() { Keyboard = Keyboard.Numeric };
    private readonly P2pRoutingSettingsStore _store;
    private readonly Switch _suggestBluetoothPairing = new();
    private bool _trafficSavingEnabled;

    public RoutingSettingsPage(P2pRoutingSettingsStore store, UserP2pRuntime runtime,
        IBluetoothRadioCatalog bluetoothCatalog, IBluetoothTransportProvider bluetoothTransport,
#if WINDOWS
        IWifiDirectTransportProvider wifiDirectTransport,
#endif
        ILogger<RoutingSettingsPage> logger)
    {
        _store = store;
        _runtime = runtime;
        _bluetoothCatalog = bluetoothCatalog;
        _bluetoothTransport = bluetoothTransport;
#if WINDOWS
        _wifiDirectTransport = wifiDirectTransport;
#endif
        _logger = logger;
        foreach (var p in LinkTechnologyPresetExtensions.AllPresets)
            _linkTechnology.Items.Add(p.GetDisplayLabel());
        Title = "P2P routing";
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 12,
                Children =
                {
                    new Label { Text = "Max search depth (edges, 1–3)" },
                    _maxHops,
                    new Label { Text = "Send failure: search attempts" },
                    _attempts,
                    new Label { Text = "Pause between attempts (ms)" },
                    _delayMs,
                    new Label { Text = "FIND wait timeout (ms)" },
                    _searchTimeoutMs,
                    new Label { Text = "Connection speed (in presence ping; affects ping interval)" },
                    _linkTechnology,
                    new Label { Text = "UDP transport" },
                    _enableUdpTransport,
                    new Label { Text = "Bluetooth transport" },
                    _enableBluetoothTransport,
#if WINDOWS
                    new Label { Text = "Wi-Fi Direct transport" },
                    _enableWifiDirectTransport,
#endif
                    new Label { Text = "Bluetooth adapter (for contacts)" },
                    _bluetoothAdapter,
                    new Label { Text = "Suggest Bluetooth pairing" },
                    _suggestBluetoothPairing,
                    new Label { Text = "Share route table on UDP request (PeerSearch)" },
                    _advertisePeerSearch,
                    new Button { Text = "Save", Command = new Command(async () => await SaveAsync()) }
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        try
        {
            base.OnAppearing();
            var s = await _store.LoadAsync().ConfigureAwait(true);
            _maxHops.Text = s.MaxSearchHops.ToString();
            _attempts.Text = s.SendFailureSearchAttempts.ToString();
            _delayMs.Text = ((int)s.SendFailureRetryDelay.TotalMilliseconds).ToString();
            _searchTimeoutMs.Text = ((int)s.SearchWaitTimeout.TotalMilliseconds).ToString();
            var idx = Array.IndexOf(LinkTechnologyPresetExtensions.AllPresets, s.LinkTechnology);
            _linkTechnology.SelectedIndex = idx >= 0 ? idx : 0;
            _trafficSavingEnabled = s.TrafficSavingEnabled;
            _enableUdpTransport.IsToggled = s.EnableUdpTransport;
            _enableBluetoothTransport.IsToggled = s.EnableBluetoothTransport;
#if WINDOWS
            _enableWifiDirectTransport.IsToggled = s.EnableWifiDirectTransport;
#endif
            _suggestBluetoothPairing.IsToggled = s.SuggestBluetoothPairing;
            _advertisePeerSearch.IsToggled = s.AdvertisedPeerCapabilities.HasFlag(PresencePeerCapabilities.PeerSearch);
            await LoadBluetoothAdaptersAsync(s).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Load P2P routing settings");
        }
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
                var suffix = r.IsDefault ? " — default" : string.Empty;
                _bluetoothAdapter.Items.Add($"{r.DisplayName} ({r.MacString}){suffix}");
            }

            var pick = 0;
            if (!string.IsNullOrWhiteSpace(settings.SelectedBluetoothAdapterDeviceId))
            {
                var i = _adapterRadios.FindIndex(r =>
                    r.DeviceId == settings.SelectedBluetoothAdapterDeviceId);
                if (i >= 0)
                    pick = i;
            }
            else
            {
                var def = _adapterRadios.FindIndex(r => r.IsDefault);
                if (def >= 0)
                    pick = def;
            }

            if (_bluetoothAdapter.Items.Count > 0)
                _bluetoothAdapter.SelectedIndex = pick;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list Bluetooth adapters");
            _bluetoothAdapter.Items.Add("(adapters unavailable)");
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
        if (!int.TryParse(_maxHops.Text, out var mh) || mh is < 1 or > 3)
        {
            await DisplayAlert(ErrorHeader, "Max depth must be 1–3.", "OK").ConfigureAwait(true);
            return;
        }

        if (!int.TryParse(_attempts.Text, out var at) || at < 1)
        {
            await DisplayAlert(ErrorHeader, "Attempts must be ≥ 1.", "OK").ConfigureAwait(true);
            return;
        }

        if (!int.TryParse(_delayMs.Text, out var dm) || dm < 0)
        {
            await DisplayAlert(ErrorHeader, "Delay must be ≥ 0 ms.", "OK").ConfigureAwait(true);
            return;
        }

        if (!int.TryParse(_searchTimeoutMs.Text, out var st) || st < 500)
        {
            await DisplayAlert(ErrorHeader, "Search timeout must be ≥ 500 ms.", "OK").ConfigureAwait(true);
            return;
        }

        var li = _linkTechnology.SelectedIndex;
        if (li < 0 || li >= LinkTechnologyPresetExtensions.AllPresets.Length)
            li = 0;

        var cap = (_runtime.Settings.AdvertisedPeerCapabilities & ~PresencePeerCapabilities.PeerSearch) |
                  PresencePeerCapabilities.Chat;
        if (_advertisePeerSearch.IsToggled)
            cap |= PresencePeerCapabilities.PeerSearch;
        var settings = new P2pRoutingSettings
        {
            MaxSearchHops = mh,
            SendFailureSearchAttempts = at,
            SendFailureRetryDelay = TimeSpan.FromMilliseconds(dm),
            SearchWaitTimeout = TimeSpan.FromMilliseconds(st),
            LinkTechnology = LinkTechnologyPresetExtensions.AllPresets[li],
            TrafficSavingEnabled = _trafficSavingEnabled,
            EnableUdpTransport = _enableUdpTransport.IsToggled,
            EnableBluetoothTransport = _enableBluetoothTransport.IsToggled,
#if WINDOWS
            EnableWifiDirectTransport = _enableWifiDirectTransport.IsToggled,
#endif
            SuggestBluetoothPairing = _suggestBluetoothPairing.IsToggled,
            AdvertisedPeerCapabilities = cap
        };
        ApplySelectedAdapter(settings);
        await _store.SaveAsync(settings).ConfigureAwait(true);
        _runtime.Settings.MaxSearchHops = settings.MaxSearchHops;
        _runtime.Settings.SendFailureSearchAttempts = settings.SendFailureSearchAttempts;
        _runtime.Settings.SendFailureRetryDelay = settings.SendFailureRetryDelay;
        _runtime.Settings.SearchWaitTimeout = settings.SearchWaitTimeout;
        _runtime.Settings.LinkTechnology = settings.LinkTechnology;
        _runtime.Settings.TrafficSavingEnabled = settings.TrafficSavingEnabled;
        _runtime.Settings.EnableUdpTransport = settings.EnableUdpTransport;
        _runtime.Settings.EnableBluetoothTransport = settings.EnableBluetoothTransport;
#if WINDOWS
        _runtime.Settings.EnableWifiDirectTransport = settings.EnableWifiDirectTransport;
#endif
        _runtime.Settings.SelectedBluetoothAdapterDeviceId = settings.SelectedBluetoothAdapterDeviceId;
        _runtime.Settings.SelectedBluetoothAdapterMac = settings.SelectedBluetoothAdapterMac;
        _runtime.Settings.SuggestBluetoothPairing = settings.SuggestBluetoothPairing;
        _runtime.Settings.AdvertisedPeerCapabilities =
            settings.AdvertisedPeerCapabilities | PresencePeerCapabilities.Chat;
        _bluetoothTransport.ApplySettings(settings);
#if WINDOWS
        _wifiDirectTransport.ApplySettings(settings);
#endif
        await Navigation.PopAsync().ConfigureAwait(true);
    }
}