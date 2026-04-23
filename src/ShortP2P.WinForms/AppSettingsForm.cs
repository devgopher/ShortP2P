using Microsoft.Extensions.Logging;

namespace ShortP2P.WinForms;

public sealed class AppSettingsForm : Form
{
    private readonly AppSettingsStore _settings;
    private readonly ILogger<UserAction> _userActions;
    private readonly ComboBox _audioSourceCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 440,
    };
    private readonly Button _refreshAudioSourcesButton = new() { Text = "Обновить список", AutoSize = true };
    private readonly Button _saveButton = new() { Text = "Сохранить", AutoSize = true };
    private readonly Button _cancelButton = new() { Text = "Отмена", AutoSize = true };

    public AppSettingsForm(AppSettingsStore settings, ILogger<UserAction> userActions)
    {
        _settings = settings;
        _userActions = userActions;
        Text = "Настройки приложения";
        StartPosition = FormStartPosition.CenterParent;
        Width = 560;
        Height = 300;
        MaximizeBox = false;
        MinimizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var soundTab = new TabPage("Звук");
        tabs.TabPages.Add(soundTab);

        var soundRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
        };
        soundRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        soundRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        soundRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        soundRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        soundTab.Controls.Add(soundRoot);

        var sourceLabel = new Label
        {
            AutoSize = true,
            Text = "Источник звука для голосовых сообщений:",
        };
        var sourceHint = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "Список включает все устройства записи Windows: микрофоны, веб-камеры с микрофоном, линейные входы.",
            MaximumSize = new Size(500, 0),
        };
        var sourceRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
        };
        sourceRow.Controls.Add(_audioSourceCombo);
        sourceRow.Controls.Add(_refreshAudioSourcesButton);

        soundRoot.Controls.Add(sourceLabel, 0, 0);
        soundRoot.Controls.Add(sourceRow, 0, 1);
        soundRoot.Controls.Add(sourceHint, 0, 2);

        var bottomButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
        };
        bottomButtons.Controls.Add(_cancelButton);
        bottomButtons.Controls.Add(_saveButton);

        Controls.Add(tabs);
        Controls.Add(bottomButtons);

        _refreshAudioSourcesButton.Click += (_, _) => ReloadAudioInputs(keepCurrentSelection: true);
        _cancelButton.Click += (_, _) => Close();
        _saveButton.Click += async (_, _) => await SaveAndCloseAsync().ConfigureAwait(true);
        Shown += async (_, _) => await OnShownAsync().ConfigureAwait(true);
    }

    private async Task OnShownAsync()
    {
        await _settings.InitializeAsync().ConfigureAwait(true);
        ReloadAudioInputs(keepCurrentSelection: false);
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
        await _settings.SetVoiceInputDeviceNumberAsync(selected.DeviceNumber).ConfigureAwait(true);
        _userActions.LogInformation("Settings: voice input device changed to {DeviceNumber} ({DeviceLabel})",
            selected.DeviceNumber, selected.DisplayText);
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record AudioInputOptionItem(int? DeviceNumber, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }
}
