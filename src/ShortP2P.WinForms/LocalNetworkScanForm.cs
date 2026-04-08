using ShortP2P.Client.LocalNetwork;
using ShortP2P.Client.Routing;
using ShortP2P.Discovery;
using ShortP2P.Client.Services;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.WinForms;

/// <summary>Ручное сканирование LAN по discovery-пингам (UDP 565).</summary>
public sealed class LocalNetworkScanForm : Form
{
    private readonly UserP2pRuntime _p2p;
    private readonly ListView _list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        Dock = DockStyle.Fill,
        MultiSelect = false,
    };

    private readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Text = "" };
    private readonly Button _scan = new() { Text = "Сканировать", AutoSize = true };
    private readonly Button _close = new() { Text = "Закрыть", DialogResult = DialogResult.OK };

    public LocalNetworkScanForm(UserP2pRuntime p2p)
    {
        _p2p = p2p;
        Text = "Локальная сеть";
        StartPosition = FormStartPosition.CenterParent;
        Width = 640;
        Height = 420;
        MinimizeBox = false;

        _list.Columns.Add("Ник", 140);
        _list.Columns.Add("Network id", 200);
        _list.Columns.Add("Транспорт", 90);
        _list.Columns.Add("Последний пинг", 140);

        var bottom = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Bottom,
            Padding = new Padding(0, 8, 0, 0),
        };
        bottom.Controls.Add(_scan);
        bottom.Controls.Add(_close);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_status, 0, 0);
        root.Controls.Add(_list, 0, 1);
        root.Controls.Add(bottom, 0, 2);
        Controls.Add(root);

        _scan.Click += async (_, _) => await OnScanAsync().ConfigureAwait(true);
        AcceptButton = _close;

        Shown += (_, _) =>
        {
            _p2p.LocalScan.ClientsChanged += OnClientsChanged;
            RefreshList();
        };
        FormClosed += (_, _) => _p2p.LocalScan.ClientsChanged -= OnClientsChanged;
    }

    private void OnClientsChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(RefreshList);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void RefreshList()
    {
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var p in _p2p.LocalScan.Clients)
            {
                var idShort = CompressedNetworkId.FromGuid(p.NetworkId).ToShortString();
                var row = new ListViewItem(string.IsNullOrEmpty(p.Nickname) ? "—" : p.Nickname)
                {
                    Tag = p,
                };
                row.SubItems.Add(idShort);
                row.SubItems.Add(FormatTransport(p.TransportKind));
                row.SubItems.Add(p.LastSeenUtc.ToLocalTime().ToString("T"));
                _list.Items.Add(row);
            }
        }
        finally
        {
            _list.EndUpdate();
        }
    }

    private static string FormatTransport(TransportKind k) =>
        k switch
        {
            TransportKind.Udp => "UDP",
            TransportKind.Bluetooth => "Bluetooth",
            TransportKind.Infrared => "IrDA",
            _ => k.ToString(),
        };

    private async Task OnScanAsync()
    {
        _scan.Enabled = false;
        var sec = (int)Math.Round(LocalNetworkScanner.DefaultScanListenDuration.TotalSeconds);
        _status.Text = $"Слушаем пинги {sec} с…";
        try
        {
            await _p2p.LocalScan.ScanAsync(LocalNetworkScanner.DefaultScanListenDuration).ConfigureAwait(true);
            RefreshList();
        }
        finally
        {
            _status.Text = "";
            _scan.Enabled = true;
        }
    }
}
