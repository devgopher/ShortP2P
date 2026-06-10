using ShortP2P.MauiApp.Services;

namespace ShortP2P.MauiApp;

/// <summary>Shows today's NLog file, refreshed on a timer.</summary>
public class LogsPage : ContentPage
{
    private const int RefreshIntervalSeconds = 5;

    private readonly Editor _logEditor = new()
    {
        IsReadOnly = true,
        FontFamily = "Courier",
        FontSize = 11,
        VerticalOptions = LayoutOptions.Fill,
        HorizontalOptions = LayoutOptions.Fill
    };

    private readonly Label _pathLabel = new()
    {
        FontSize = 11,
        TextColor = Colors.Gray,
        LineBreakMode = LineBreakMode.CharacterWrap
    };

    private IDispatcherTimer? _refreshTimer;

    public LogsPage()
    {
        Title = "Logs";
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Copy",
            Order = ToolbarItemOrder.Primary,
            Priority = 0,
            Command = new Command(async () => await CopyLogsAsync())
        });
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Refresh",
            Order = ToolbarItemOrder.Primary,
            Priority = 1,
            Command = new Command(RefreshLogs)
        });
        _logEditor.SetValue(Grid.RowProperty, 1);
        Content = new Grid
        {
            Padding = 12,
            RowDefinitions =
            [
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            ],
            Children = { _pathLabel, _logEditor }
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshLogs();
        EnsureRefreshTimerStarted();
    }

    protected override void OnDisappearing()
    {
        if (_refreshTimer != null)
            _refreshTimer.Stop();
        base.OnDisappearing();
    }

    private void EnsureRefreshTimerStarted()
    {
        _refreshTimer ??= Dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(RefreshIntervalSeconds);
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _refreshTimer.Tick += OnRefreshTimerTick;
        if (!_refreshTimer.IsRunning)
            _refreshTimer.Start();
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        RefreshLogs();
    }

    private void RefreshLogs()
    {
        var text = AppLogReader.ReadTodayLog(out var path);
        _pathLabel.Text = path == null
            ? "Log file: (not created yet)"
            : $"Log file: {path}";
        _logEditor.Text = text;
    }

    private async Task CopyLogsAsync()
    {
        if (string.IsNullOrWhiteSpace(_logEditor.Text))
            return;

        await Clipboard.Default.SetTextAsync(_logEditor.Text).ConfigureAwait(true);
        await DisplayAlert("Copied", "Log text copied to clipboard.", "OK").ConfigureAwait(true);
    }
}
