using System.Collections.ObjectModel;
using ShortP2P.Client.LocalNetwork;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;
using ShortP2P.Discovery;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.MauiApp;

public sealed class LanScanRow
{
    public string Nickname { get; init; } = "";
    public string NetworkIdShort { get; init; } = "";
    public string DetailLine { get; init; } = "";

    public static LanScanRow From(DiscoveredLocalPeer p)
    {
        var idShort = CompressedNetworkId.FromGuid(p.NetworkId).ToShortString();
        var transport = p.TransportKind switch
        {
            TransportKind.Udp => "UDP",
            TransportKind.Bluetooth => "Bluetooth",
            TransportKind.Infrared => "IrDA",
            _ => p.TransportKind.ToString(),
        };
        var seen = p.LastSeenUtc.ToLocalTime().ToString("g");
        return new LanScanRow
        {
            Nickname = string.IsNullOrEmpty(p.Nickname) ? "—" : p.Nickname,
            NetworkIdShort = idShort,
            DetailLine = $"{transport} · last ping {seen}",
        };
    }
}

public partial class LanScanPage : ContentPage
{
    private readonly UserP2pRuntime _p2p;
    private readonly ObservableCollection<LanScanRow> _rows = new();

    public LanScanPage(UserP2pRuntime p2p)
    {
        InitializeComponent();
        _p2p = p2p;
        PeerCollection.ItemsSource = _rows;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _p2p.LocalScan.ClientsChanged += OnClientsChanged;
        RefreshRows();
    }

    protected override void OnDisappearing()
    {
        _p2p.LocalScan.ClientsChanged -= OnClientsChanged;
        base.OnDisappearing();
    }

    private void OnClientsChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RefreshRows);

    private void RefreshRows()
    {
        _rows.Clear();
        foreach (var p in _p2p.LocalScan.Clients)
            _rows.Add(LanScanRow.From(p));
    }

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        ScanButton.IsEnabled = false;
        var sec = (int)Math.Round(LocalNetworkScanner.DefaultScanListenDuration.TotalSeconds);
        StatusLabel.Text = $"Listening {sec} s…";
        try
        {
            await _p2p.LocalScan.ScanAsync(LocalNetworkScanner.DefaultScanListenDuration).ConfigureAwait(true);
            RefreshRows();
        }
        finally
        {
            StatusLabel.Text = "";
            ScanButton.IsEnabled = true;
        }
    }
}
