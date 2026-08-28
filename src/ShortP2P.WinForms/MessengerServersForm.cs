using Microsoft.Extensions.Logging;
using ShortP2P.Client.Data;
using ShortP2P.Client.Qr;
using ShortP2P.Client.Services.MessengerServers;
using ShortP2P.TrustSystem;

namespace ShortP2P.WinForms;

public sealed class MessengerServersForm : Form
{
    private readonly TextBox _baseUrl = new()
    {
        Width = 420,
        PlaceholderText = "https://host:7196"
    };

    private readonly Button _addButton = new() { Text = "Добавить", AutoSize = true };
    private readonly Button _importButton = new() { Text = "Импортировать сервер", AutoSize = true };
    private readonly Button _shareButton = new() { Text = "Поделиться сервером", AutoSize = true };
    private readonly Button _recheckButton = new() { Text = "Проверить", AutoSize = true };
    private readonly Button _askServersButton = new() { Text = "Запросить серверы", AutoSize = true };
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
        Width = 1100;
        Height = 480;
        MinimizeBox = false;

        _list.Columns.Add("URL", 260);
        _list.Columns.Add("Rating", 70);
        _list.Columns.Add("Запросить серверы", 140);
        _list.Columns.Add("Active", 60);
        _list.Columns.Add("Trusted", 70);
        _list.Columns.Add("Registered", 80);
        _list.Columns.Add("Fingerprint", 240);

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
                "До 32 HTTPS-серверов. При добавлении сохраняется fingerprint сертификата."
        };

        var addRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        addRow.Controls.Add(new Label { Text = "Base URL:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        addRow.Controls.Add(_baseUrl);
        addRow.Controls.Add(_addButton);
        addRow.Controls.Add(_importButton);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        actions.Controls.Add(_shareButton);
        actions.Controls.Add(_recheckButton);
        actions.Controls.Add(_askServersButton);
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
        _importButton.Click += async (_, _) => await ImportServerAsync().ConfigureAwait(true);
        _shareButton.Click += (_, _) => ShareSelected();
        _recheckButton.Click += async (_, _) => await RecheckSelectedAsync().ConfigureAwait(true);
        _askServersButton.Click += async (_, _) => await AskServersSelectedAsync().ConfigureAwait(true);
        _toggleActiveButton.Click += async (_, _) => await ToggleActiveAsync().ConfigureAwait(true);
        _deleteButton.Click += async (_, _) => await DeleteSelectedAsync().ConfigureAwait(true);
        _refreshButton.Click += async (_, _) => await ReloadAsync().ConfigureAwait(true);
        _closeButton.Click += (_, _) => Close();
        _list.MouseClick += async (_, e) => await OnListMouseClickAsync(e).ConfigureAwait(true);
        _list.SelectedIndexChanged += (_, _) => UpdateAskServersButton();

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
                    ForeColor = !s.Trusted || s.TrustRating < TrustRatings.Floor
                        ? Color.DarkRed
                        : SystemColors.WindowText
                };
                item.SubItems.Add(FormatRating(s.TrustRating));
                item.SubItems.Add(s.TrustRating >= TrustRatings.Floor ? "Запросить" : "");
                item.SubItems.Add(s.Active ? "yes" : "no");
                item.SubItems.Add(s.Trusted ? "yes" : "NO");
                item.SubItems.Add(s.IsRegistered ? "yes" : "no");
                item.SubItems.Add(s.FingerprintSha256);
                _list.Items.Add(item);
            }

            _list.EndUpdate();
            _status.Text = $"Серверов: {_list.Items.Count} / {MessengerServerLimits.MaxServersPerUser}";
            UpdateAskServersButton();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reload messenger servers");
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string FormatRating(float rating) =>
        rating.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    private const int AskServersColumnIndex = 2;

    private async Task OnListMouseClickAsync(MouseEventArgs e)
    {
        var hit = _list.HitTest(e.Location);
        if (hit.Item == null || hit.SubItem == null)
            return;
        if (hit.Item.SubItems.IndexOf(hit.SubItem) != AskServersColumnIndex)
            return;
        if (hit.Item.Tag is not MessengerServerEntity entity)
            return;
        if (entity.TrustRating < TrustRatings.Floor)
            return;

        await AskServersFromAsync(entity).ConfigureAwait(true);
    }

    private void UpdateAskServersButton()
    {
        _askServersButton.Enabled = TryGetSelectedServer(out var entity) &&
                                    entity.TrustRating >= TrustRatings.Floor;
    }

    private bool TryGetSelectedServer(out MessengerServerEntity entity)
    {
        if (_list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is MessengerServerEntity selected)
        {
            entity = selected;
            return true;
        }

        entity = null!;
        return false;
    }

    private async Task AskServersSelectedAsync()
    {
        if (!TryGetSelectedServer(out var entity))
        {
            MessageBox.Show(this, "Выберите сервер в списке.", "Запросить серверы", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (entity.TrustRating < TrustRatings.Floor)
        {
            MessageBox.Show(this, "Рейтинг сервера ниже 0.3 — запрос списка недоступен.", "Запросить серверы",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await AskServersFromAsync(entity).ConfigureAwait(true);
    }

    private async Task AskServersFromAsync(MessengerServerEntity entity)
    {
        _askServersButton.Enabled = false;
        _status.Text = $"Запрос серверов у {entity.BaseUrl}…";
        try
        {
            var result = await _manager.AskServersFromAsync(entity.Id).ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
            _status.Text =
                $"Получено {result.ReceivedCount}, обновлено {result.UpdatedCount}, добавлено {result.AddedCount}: {entity.BaseUrl}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AskServers from messenger server {Id}", entity.Id);
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Запросить серверы", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateAskServersButton();
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

    private void ShareSelected()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not MessengerServerEntity entity)
        {
            MessageBox.Show(this, "Выберите сервер в списке.", "Поделиться сервером", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!MessengerServerQrService.TryBuildPayload(entity.BaseUrl, out var payload, out var err))
        {
            MessageBox.Show(this, err ?? "Не удалось собрать QR-код сервера.", "Поделиться сервером",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var png = MessengerServerQrService.EncodeQrPng(payload);
            using var form = new MessengerServerQrForm(payload, png);
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Share messenger server QR {Id}", entity.Id);
            MessageBox.Show(this, ex.Message, "Поделиться сервером", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ImportServerAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Файл с QR-кодом сервера",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(dlg.FileName).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Read messenger server QR file");
            MessageBox.Show(this, ex.Message, "Импортировать сервер", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!MessengerServerQrService.TryDecodeImage(bytes, out var payload, out var err))
        {
            _logger.LogWarning("Messenger server QR decode failed from file {File}: {Error}", dlg.FileName, err);
            MessageBox.Show(this, err ?? "Не удалось прочитать QR-код сервера.", "Импортировать сервер",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var url = MessengerServerQrCodec.ToBaseUrl(payload);
        _importButton.Enabled = false;
        _status.Text = "Импорт сервера…";
        try
        {
            var existing = await _manager.FindExistingByEndpointAsync(url).ConfigureAwait(true);
            if (existing != null)
            {
                _status.Text = $"Сервер уже добавлен: {existing.BaseUrl}";
                MessageBox.Show(this, $"Этот сервер уже есть в списке:\n{existing.BaseUrl}", "Импортировать сервер",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var entity = await _manager.AddServerAsync(url).ConfigureAwait(true);
            _status.Text = $"Импортирован: {entity.BaseUrl}";
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Import messenger server from QR");
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Импортировать сервер", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _importButton.Enabled = true;
        }
    }

    private async Task RecheckSelectedAsync()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not MessengerServerEntity entity)
        {
            MessageBox.Show(this, "Выберите сервер в списке.", "Messenger servers", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _recheckButton.Enabled = false;
        _status.Text = $"Проверка {entity.BaseUrl}…";
        try
        {
            var result = await _manager.RecheckServerAsync(entity.Id).ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
            switch (result.Status)
            {
                case MessengerServerRecheckStatus.AvailableAndTrusted:
                    _status.Text = $"Сервер доступен, сертификат совпадает: {result.Server.BaseUrl}";
                    MessageBox.Show(
                        this,
                        "Сервер доступен, отпечаток сертификата совпадает.\nСтатус: active, доверенный.",
                        "Проверка сервера",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    break;
                case MessengerServerRecheckStatus.Unreachable:
                    _status.Text = $"Сервер недоступен: {result.Server.BaseUrl}";
                    MessageBox.Show(
                        this,
                        string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Сервер недоступен. Помечен как неактивный (доверенный статус сохранён)."
                            : $"Сервер недоступен. Помечен как неактивный (доверенный статус сохранён).\n\n{result.ErrorMessage}",
                        "Проверка сервера",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    break;
                case MessengerServerRecheckStatus.FingerprintMismatch:
                    _status.Text = $"Fingerprint не совпал: {result.Server.BaseUrl}";
                    MessageBox.Show(
                        this,
                        "Сертификат сервера не совпадает с сохранённым fingerprint.\n" +
                        $"Ожидался: {result.ExpectedFingerprint}\nПолучен: {result.ActualFingerprint}\n\n" +
                        "Сервер отключён и помечен как недоверенный.",
                        "Проверка сервера",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recheck messenger server {Id}", entity.Id);
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _recheckButton.Enabled = true;
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
