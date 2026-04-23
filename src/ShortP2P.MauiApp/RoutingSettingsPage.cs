using Microsoft.Extensions.Logging;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;

namespace ShortP2P.MauiApp;

public class RoutingSettingsPage : ContentPage
{
    private const string ErrorHeader = "Error";
    private readonly Entry _attempts = new() { Keyboard = Keyboard.Numeric };
    private readonly Entry _delayMs = new() { Keyboard = Keyboard.Numeric };
    private readonly Picker _linkTechnology = new();
    private readonly Entry _maxHops = new() { Keyboard = Keyboard.Numeric, Placeholder = "1–3" };
    private readonly UserP2pRuntime _runtime;
    private readonly Entry _searchTimeoutMs = new() { Keyboard = Keyboard.Numeric };
    private readonly P2pRoutingSettingsStore _store;
    private readonly ILogger<RoutingSettingsPage> _logger;
    private bool _trafficSavingEnabled;

    public RoutingSettingsPage(P2pRoutingSettingsStore store, UserP2pRuntime runtime,
        ILogger<RoutingSettingsPage> logger)
    {
        _store = store;
        _runtime = runtime;
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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Load P2P routing settings");
        }
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

        var settings = new P2pRoutingSettings
        {
            MaxSearchHops = mh,
            SendFailureSearchAttempts = at,
            SendFailureRetryDelay = TimeSpan.FromMilliseconds(dm),
            SearchWaitTimeout = TimeSpan.FromMilliseconds(st),
            LinkTechnology = LinkTechnologyPresetExtensions.AllPresets[li],
            TrafficSavingEnabled = _trafficSavingEnabled
        };
        await _store.SaveAsync(settings).ConfigureAwait(true);
        _runtime.Settings.MaxSearchHops = settings.MaxSearchHops;
        _runtime.Settings.SendFailureSearchAttempts = settings.SendFailureSearchAttempts;
        _runtime.Settings.SendFailureRetryDelay = settings.SendFailureRetryDelay;
        _runtime.Settings.SearchWaitTimeout = settings.SearchWaitTimeout;
        _runtime.Settings.LinkTechnology = settings.LinkTechnology;
        _runtime.Settings.TrafficSavingEnabled = settings.TrafficSavingEnabled;
        await Navigation.PopAsync().ConfigureAwait(true);
    }
}
