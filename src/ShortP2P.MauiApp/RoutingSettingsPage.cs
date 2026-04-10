using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;

namespace ShortP2P.MauiApp;

public class RoutingSettingsPage : ContentPage
{
    private readonly P2pRoutingSettingsStore _store;
    private readonly UserP2pRuntime _runtime;
    private readonly Entry _maxHops = new() { Keyboard = Keyboard.Numeric, Placeholder = "1–3" };
    private readonly Entry _attempts = new() { Keyboard = Keyboard.Numeric };
    private readonly Entry _delayMs = new() { Keyboard = Keyboard.Numeric };
    private readonly Entry _searchTimeoutMs = new() { Keyboard = Keyboard.Numeric };
    private readonly Picker _linkTechnology = new();

    public RoutingSettingsPage(P2pRoutingSettingsStore store, UserP2pRuntime runtime)
    {
        _store = store;
        _runtime = runtime;
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
                    new Label { Text = "Simulated link (min bitrate, TX/RX)" },
                    _linkTechnology,
                    new Button { Text = "Save", Command = new Command(async () => await SaveAsync()) },
                },
            },
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var s = await _store.LoadAsync().ConfigureAwait(true);
        _maxHops.Text = s.MaxSearchHops.ToString();
        _attempts.Text = s.SendFailureSearchAttempts.ToString();
        _delayMs.Text = ((int)s.SendFailureRetryDelay.TotalMilliseconds).ToString();
        _searchTimeoutMs.Text = ((int)s.SearchWaitTimeout.TotalMilliseconds).ToString();
        var idx = Array.IndexOf(LinkTechnologyPresetExtensions.AllPresets, s.LinkTechnology);
        _linkTechnology.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private async Task SaveAsync()
    {
        if (!int.TryParse(_maxHops.Text, out var mh) || mh is < 1 or > 3)
        {
            await DisplayAlert("Error", "Max depth must be 1–3.", "OK").ConfigureAwait(true);
            return;
        }

        if (!int.TryParse(_attempts.Text, out var at) || at < 1)
        {
            await DisplayAlert("Error", "Attempts must be ≥ 1.", "OK").ConfigureAwait(true);
            return;
        }

        if (!int.TryParse(_delayMs.Text, out var dm) || dm < 0)
        {
            await DisplayAlert("Error", "Delay must be ≥ 0 ms.", "OK").ConfigureAwait(true);
            return;
        }

        if (!int.TryParse(_searchTimeoutMs.Text, out var st) || st < 500)
        {
            await DisplayAlert("Error", "Search timeout must be ≥ 500 ms.", "OK").ConfigureAwait(true);
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
        };
        await _store.SaveAsync(settings).ConfigureAwait(true);
        _runtime.Settings.MaxSearchHops = settings.MaxSearchHops;
        _runtime.Settings.SendFailureSearchAttempts = settings.SendFailureSearchAttempts;
        _runtime.Settings.SendFailureRetryDelay = settings.SendFailureRetryDelay;
        _runtime.Settings.SearchWaitTimeout = settings.SearchWaitTimeout;
        _runtime.Settings.LinkTechnology = settings.LinkTechnology;
        await Navigation.PopAsync().ConfigureAwait(true);
    }
}
