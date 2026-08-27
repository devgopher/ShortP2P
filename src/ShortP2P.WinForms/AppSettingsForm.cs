using Microsoft.Extensions.Logging;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;
using ShortP2P.Discovery;

namespace ShortP2P.WinForms;

public sealed class AppSettingsForm : Form
{
    private readonly ComboBox _audioSourceCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 440
    };

    private readonly Button _cancelButton = new() { Text = "Отмена", AutoSize = true };
    private readonly Button _refreshAudioSourcesButton = new() { Text = "Обновить список", AutoSize = true };
    private readonly Button _refreshVideoSourcesButton = new() { Text = "Обновить список", AutoSize = true };
    private readonly P2pRoutingSettingsStore _routingStore;
    private readonly UserP2pRuntime _runtime;
    private readonly Button _saveButton = new() { Text = "Сохранить", AutoSize = true };
    private readonly AppSettingsStore _settings;

    private readonly ComboBox _trafficQualityCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 440
    };

    private readonly ILogger<UserAction> _userActions;

    private readonly ComboBox _videoSourceCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 440
    };

    public AppSettingsForm(AppSettingsStore settings, P2pRoutingSettingsStore routingStore, UserP2pRuntime runtime,
        ILogger<UserAction> userActions)
    {
        _settings = settings;
        _routingStore = routingStore;
        _runtime = runtime;
        _userActions = userActions;
        Text = "Настройки приложения";
        StartPosition = FormStartPosition.CenterParent;
        Width = 560;
        Height = 320;
        MaximizeBox = false;
        MinimizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var soundTab = new TabPage("Звук");
        tabs.TabPages.Add(soundTab);
        var videoTab = new TabPage("Видео");
        tabs.TabPages.Add(videoTab);
        var trafficTab = new TabPage("Экономия трафика");
        tabs.TabPages.Add(trafficTab);

        var soundRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        soundRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        soundRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        soundRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        soundRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        soundTab.Controls.Add(soundRoot);

        var sourceLabel = new Label
        {
            AutoSize = true,
            Text = "Источник звука для голосовых сообщений:"
        };
        var sourceHint = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "Список включает все устройства записи Windows: микрофоны, веб-камеры с микрофоном, линейные входы.",
            MaximumSize = new Size(500, 0)
        };
        var sourceRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight
        };
        sourceRow.Controls.Add(_audioSourceCombo);
        sourceRow.Controls.Add(_refreshAudioSourcesButton);

        soundRoot.Controls.Add(sourceLabel, 0, 0);
        soundRoot.Controls.Add(sourceRow, 0, 1);
        soundRoot.Controls.Add(sourceHint, 0, 2);

        var videoRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        videoRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        videoRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        videoRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        videoRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        videoTab.Controls.Add(videoRoot);
        var videoSourceLabel = new Label
        {
            AutoSize = true,
            Text = "Источник видео для записи видеосообщений:"
        };
        var videoSourceRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight
        };
        videoSourceRow.Controls.Add(_videoSourceCombo);
        videoSourceRow.Controls.Add(_refreshVideoSourcesButton);
        var videoHint = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(500, 0),
            Text = "Выбранный источник используется в окне записи с камеры (кнопка 📹 в чате)."
        };
        videoRoot.Controls.Add(videoSourceLabel, 0, 0);
        videoRoot.Controls.Add(videoSourceRow, 0, 1);
        videoRoot.Controls.Add(videoHint, 0, 2);

        var trafficRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        trafficRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        trafficRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        trafficRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        trafficRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var trafficLabel = new Label
        {
            AutoSize = true,
            Text = "Режим экономии трафика:"
        };
        _trafficQualityCombo.Items.Add(new TrafficQualityOptionItem(TrafficQualityMode.Normal));
        _trafficQualityCombo.Items.Add(new TrafficQualityOptionItem(TrafficQualityMode.Economy));
        _trafficQualityCombo.Items.Add(new TrafficQualityOptionItem(TrafficQualityMode.UltraEconomy));
        var trafficHint = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(500, 0),
            Text = "Ультраэкономия: голос 4 kbit/s, видео 144p. Экономия: голос 6 kbit/s, видео 240p. " +
                   "Нормальный: голос 24 kbit/s, видео 480p. При экономии и ультраэкономии presence-пинги — раз в 10 с."
        };
        trafficRoot.Controls.Add(trafficLabel, 0, 0);
        trafficRoot.Controls.Add(_trafficQualityCombo, 0, 1);
        trafficRoot.Controls.Add(trafficHint, 0, 2);
        trafficTab.Controls.Add(trafficRoot);

        var bottomButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        bottomButtons.Controls.Add(_cancelButton);
        bottomButtons.Controls.Add(_saveButton);

        Controls.Add(tabs);
        Controls.Add(bottomButtons);

        _refreshAudioSourcesButton.Click += (_, _) => ReloadAudioInputs(true);
        _refreshVideoSourcesButton.Click += async (_, _) => await ReloadVideoInputsAsync(true).ConfigureAwait(true);
        _cancelButton.Click += (_, _) => Close();
        _saveButton.Click += async (_, _) => await SaveAndCloseAsync().ConfigureAwait(true);
        Shown += async (_, _) => await OnShownAsync().ConfigureAwait(true);
    }

    private async Task OnShownAsync()
    {
        await _settings.InitializeAsync().ConfigureAwait(true);
        ReloadAudioInputs(false);
        await ReloadVideoInputsAsync(false).ConfigureAwait(true);
        SelectTrafficQuality(_settings.Current.TrafficQuality);
    }

    private void SelectTrafficQuality(TrafficQualityMode mode)
    {
        for (var i = 0; i < _trafficQualityCombo.Items.Count; i++)
        {
            if (_trafficQualityCombo.Items[i] is not TrafficQualityOptionItem item || item.Mode != mode)
                continue;
            _trafficQualityCombo.SelectedIndex = i;
            return;
        }

        _trafficQualityCombo.SelectedIndex = 0;
    }

    private void ReloadAudioInputs(bool keepCurrentSelection)
    {
        var previouslySelected = keepCurrentSelection
            ? (_audioSourceCombo.SelectedItem as AudioInputOptionItem)?.DeviceNumber
            : _settings.Current.VoiceInputDeviceNumber;

        _audioSourceCombo.BeginUpdate();
        _audioSourceCombo.Items.Clear();
        _audioSourceCombo.Items.Add(new AudioInputOptionItem(null, "Системное устройство по умолчанию"));
        foreach (var dev in AudioInputDeviceCatalog.GetAll())
            _audioSourceCombo.Items.Add(new AudioInputOptionItem(dev.DeviceNumber, dev.DisplayName));
        _audioSourceCombo.EndUpdate();

        for (var i = 0; i < _audioSourceCombo.Items.Count; i++)
        {
            if (_audioSourceCombo.Items[i] is not AudioInputOptionItem item)
                continue;
            if (item.DeviceNumber != previouslySelected)
                continue;
            _audioSourceCombo.SelectedIndex = i;
            return;
        }

        _audioSourceCombo.SelectedIndex = 0;
    }

    private async Task SaveAndCloseAsync()
    {
        if (_audioSourceCombo.SelectedItem is not AudioInputOptionItem selected)
            return;
        if (_videoSourceCombo.SelectedItem is not VideoInputOptionItem selectedVideo)
            return;
        if (_trafficQualityCombo.SelectedItem is not TrafficQualityOptionItem selectedTraffic)
            return;
        await _settings.SetVoiceInputDeviceNumberAsync(selected.DeviceNumber).ConfigureAwait(true);
        await _settings.SetVideoInputDeviceIdAsync(selectedVideo.DeviceId).ConfigureAwait(true);
        await _settings.SetTrafficQualityAsync(selectedTraffic.Mode).ConfigureAwait(true);
        var routing = await _routingStore.LoadAsync().ConfigureAwait(true);
        routing.TrafficQuality = selectedTraffic.Mode;
        await _routingStore.SaveAsync(routing).ConfigureAwait(true);
        _runtime.Settings.TrafficQuality = routing.TrafficQuality;
        _userActions.LogInformation("Settings: voice input device changed to {DeviceNumber} ({DeviceLabel})",
            selected.DeviceNumber, selected.DisplayText);
        _userActions.LogInformation("Settings: video input device changed to {DeviceId} ({DeviceLabel})",
            selectedVideo.DeviceId, selectedVideo.DisplayText);
        _userActions.LogInformation("Settings: traffic quality mode {Mode}", selectedTraffic.Mode);
        DialogResult = DialogResult.OK;
        Close();
    }

    private async Task ReloadVideoInputsAsync(bool keepCurrentSelection)
    {
        var previouslySelected = keepCurrentSelection
            ? (_videoSourceCombo.SelectedItem as VideoInputOptionItem)?.DeviceId
            : _settings.Current.VideoInputDeviceId;
        var devices = await VideoInputDeviceCatalog.GetAllAsync().ConfigureAwait(true);

        _videoSourceCombo.BeginUpdate();
        _videoSourceCombo.Items.Clear();
        _videoSourceCombo.Items.Add(new VideoInputOptionItem(null, "Системная камера по умолчанию"));
        foreach (var dev in devices)
            _videoSourceCombo.Items.Add(new VideoInputOptionItem(dev.DeviceId, dev.DisplayName));
        _videoSourceCombo.EndUpdate();

        for (var i = 0; i < _videoSourceCombo.Items.Count; i++)
        {
            if (_videoSourceCombo.Items[i] is not VideoInputOptionItem item)
                continue;
            if (!string.Equals(item.DeviceId, previouslySelected, StringComparison.Ordinal))
                continue;
            _videoSourceCombo.SelectedIndex = i;
            return;
        }

        _videoSourceCombo.SelectedIndex = 0;
    }

    private sealed record AudioInputOptionItem(int? DeviceNumber, string DisplayText)
    {
        public override string ToString()
        {
            return DisplayText;
        }
    }

    private sealed record VideoInputOptionItem(string? DeviceId, string DisplayText)
    {
        public override string ToString()
        {
            return DisplayText;
        }
    }

    private sealed record TrafficQualityOptionItem(TrafficQualityMode Mode)
    {
        public override string ToString()
        {
            return Mode.GetDisplayLabel();
        }
    }
}
