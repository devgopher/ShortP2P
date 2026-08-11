using Microsoft.Extensions.Logging;
using ShortP2P.Client.Data;
using ShortP2P.Client.Services.MessengerServers;

namespace ShortP2P.WinForms;

public sealed class MessengerServersForm : Form
{
    private readonly TextBox _baseUrl = new()
    {
        Width = 420,
        PlaceholderText = "https://host:7196"
    };

    private readonly Button _addButton = new() { Text = "Добавить", AutoSize = true };
    private readonly Button _toggleActiveButton = new() { Text = "Вкл/выкл", AutoSize = true };
    private readonly Button _deleteButton = new() { Text = "Удалить", AutoSize = true };
    private readonly Button _refreshButton = new() { Text = "Обновить", AutoSize = true };
    private readonly Button _closeButton = new() { Text = "Закрыть", AutoSize = true };
    private readonly ListView _list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        Dock = DockStyle.Fill
    };

    private readonly Label _status = new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText
    };

    private readonly MessengerServerManager _manager;
    private readonly ILogger<MessengerServersForm> _logger;

    public MessengerServersForm(MessengerServerManager manager, ILogger<MessengerServersForm> logger)
    {
        _manager = manager;
        _logger = logger;
        Text = "Messenger servers";
        StartPosition = FormStartPosition.CenterParent;
        Width = 780;
        Height = 480;
        MinimizeBox = false;

        _list.Columns.Add("URL", 280);
        _list.Columns.Add("Active", 60);
        _list.Columns.Add("Trusted", 70);
        _list.Columns.Add("Registered", 80);
        _list.Columns.Add("Fingerprint", 260);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var hint = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(740, 0),
            Text =
                "До 32 HTTPS-серверов. При добавлении сохраняется fingerprint сертификата. " +
                "При несовпадении сервер отключается и помечается как недоверенный."
        };

        var addRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        addRow.Controls.Add(new Label { Text = "Base URL:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        addRow.Controls.Add(_baseUrl);
        addRow.Controls.Add(_addButton);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        actions.Controls.Add(_toggleActiveButton);
        actions.Controls.Add(_deleteButton);
        actions.Controls.Add(_refreshButton);
        actions.Controls.Add(_closeButton);

        root.Controls.Add(hint, 0, 0);
        root.Controls.Add(addRow, 0, 1);
        root.Controls.Add(_list, 0, 2);
        root.Controls.Add(actions, 0, 3);
        root.Controls.Add(_status, 0, 4);
        Controls.Add(root);

        _addButton.Click += async (_, _) => await AddServerAsync().ConfigureAwait(true);
        _toggleActiveButton.Click += async (_, _) => await ToggleActiveAsync().ConfigureAwait(true);
        _deleteButton.Click += async (_, _) => await DeleteSelectedAsync().ConfigureAwait(true);
        _refreshButton.Click += async (_, _) => await ReloadAsync().ConfigureAwait(true);
        _closeButton.Click += (_, _) => Close();

        Shown += async (_, _) =>
        {
            _manager.TrustThreatDetected += OnTrustThreat;
            await ReloadAsync().ConfigureAwait(true);
        };
        FormClosed += (_, _) => _manager.TrustThreatDetected -= OnTrustThreat;
    }

    private void OnTrustThreat(object? sender, MessengerServerTrustThreatEventArgs e)
    {
        if (IsDisposed)
            return;
        BeginInvoke(() => _ = ReloadAsync());
    }

    private async Task ReloadAsync()
    {
        try
        {
            var servers = await _manager.ListAsync().ConfigureAwait(true);
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var s in servers.OrderByDescending(x => x.UpdatedUtcTicks))
            {
                var item = new ListViewItem(s.BaseUrl)
                {
                    Tag = s,
                    ForeColor = s.Trusted ? SystemColors.WindowText : Color.DarkRed
                };
                item.SubItems.Add(s.Active ? "yes" : "no");
                item.SubItems.Add(s.Trusted ? "yes" : "NO");
                item.SubItems.Add(s.IsRegistered ? "yes" : "no");
                item.SubItems.Add(s.FingerprintSha256);
                _list.Items.Add(item);
            }

            _list.EndUpdate();
            _status.Text = $"Серверов: {_list.Items.Count} / {MessengerServerLimits.MaxServersPerUser}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reload messenger servers");
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task AddServerAsync()
    {
        var url = _baseUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(this, "Укажите Base URL сервера.", "Ошибка", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _addButton.Enabled = false;
        _status.Text = "Подключение и регистрация…";
        try
        {
            var entity = await _manager.AddServerAsync(url).ConfigureAwait(true);
            _baseUrl.Text = "";
            _status.Text = $"Добавлен: {entity.BaseUrl}";
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Add messenger server");
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _addButton.Enabled = true;
        }
    }

    private async Task ToggleActiveAsync()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not MessengerServerEntity entity)
        {
            MessageBox.Show(this, "Выберите сервер в списке.", "Messenger servers", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!entity.Active && !entity.Trusted)
        {
            MessageBox.Show(
                this,
                "Fingerprint не совпал. Удалите сервер и добавьте заново, только если доверяете новому сертификату.",
                "Недоверенный сервер",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            await _manager.SetActiveAsync(entity.Id, !entity.Active).ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SetActive messenger server {Id}", entity.Id);
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not MessengerServerEntity entity)
        {
            MessageBox.Show(this, "Выберите сервер в списке.", "Messenger servers", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"{entity.BaseUrl}\n\nУчётная запись на сервере не удаляется — только запись на этом ПК.",
            "Удалить сервер?",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes)
            return;

        try
        {
            await _manager.DeleteServerAsync(entity.Id).ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Delete messenger server {Id}", entity.Id);
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
