using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using ShortP2P.Client.Data;
using ShortP2P.Client.Qr;
using ShortP2P.Client.Services.MessengerServers;

namespace ShortP2P.MauiApp;

public sealed class MessengerServersPage : ContentPage
{
    private readonly Entry _baseUrlEntry = new()
    {
        Placeholder = "https://host:7196",
        Keyboard = Keyboard.Url
    };

    private readonly Button _addButton = new() { Text = "Добавить сервер" };
    private readonly Button _importButton = new() { Text = "Импортировать сервер" };
    private readonly CollectionView _list = new() { SelectionMode = SelectionMode.None };
    private readonly ObservableCollection<MessengerServerRowVm> _rows = [];
    private readonly MessengerServerManager _manager;
    private readonly ILogger<MessengerServersPage> _logger;
    private readonly Label _status = new() { FontSize = 12, TextColor = Colors.Gray };
    private bool _suppressActiveToggle;

    public MessengerServersPage(MessengerServerManager manager, ILogger<MessengerServersPage> logger)
    {
        _manager = manager;
        _logger = logger;
        Title = "Messenger servers";

        _list.ItemsSource = _rows;
        _list.ItemTemplate = new DataTemplate(() =>
        {
            var url = new Label { FontSize = 16 };
            url.SetBinding(Label.TextProperty, nameof(MessengerServerRowVm.BaseUrl));

            var meta = new Label { FontSize = 12, TextColor = Colors.Gray };
            meta.SetBinding(Label.TextProperty, nameof(MessengerServerRowVm.MetaLine));

            var active = new Switch();
            active.SetBinding(Switch.IsToggledProperty, new Binding(nameof(MessengerServerRowVm.Active),
                BindingMode.OneWay));
            active.Toggled += OnActiveToggled;

            var share = new Button
            {
                Text = "Поделиться",
                Padding = new Thickness(10, 4)
            };
            share.Clicked += OnShareClicked;

            var recheck = new Button
            {
                Text = "Проверить",
                Padding = new Thickness(10, 4)
            };
            recheck.Clicked += OnRecheckClicked;

            var delete = new Button
            {
                Text = "Удалить",
                BackgroundColor = Colors.DarkRed,
                TextColor = Colors.White,
                Padding = new Thickness(10, 4)
            };
            delete.Clicked += OnDeleteClicked;

            var texts = new VerticalStackLayout
            {
                Spacing = 2,
                Children = { url, meta }
            };

            return new Grid
            {
                Padding = new Thickness(0, 8),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 8,
                Children =
                {
                    texts,
                    share.AtColumn(1),
                    recheck.AtColumn(2),
                    active.AtColumn(3),
                    delete.AtColumn(4)
                }
            };
        });

        _addButton.Clicked += OnAddClicked;
        _importButton.Clicked += OnImportClicked;

        var root = new Grid
        {
            Padding = 16,
            RowSpacing = 10,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };
        root.Add(new Label
        {
            Text =
                "До 32 HTTPS-серверов. При добавлении сохраняется fingerprint сертификата. Если сервер не отвечает, он помечается как неактивный; при несовпадении fingerprint — как недоверенный.",
            FontSize = 12,
            TextColor = Colors.Gray
        }, 0, 0);
        root.Add(new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label { Text = "Base URL" },
                _baseUrlEntry,
                _addButton,
                _importButton
            }
        }, 0, 1);
        root.Add(_status, 0, 2);
        root.Add(_list, 0, 3);
        root.Add(new Label
        {
            Text = "Active = использовать сервер. Выключенный сервер не опрашивается.",
            FontSize = 12,
            TextColor = Colors.Gray
        }, 0, 4);
        Content = root;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _manager.TrustThreatDetected -= OnTrustThreat;
        _manager.TrustThreatDetected += OnTrustThreat;
        await ReloadAsync().ConfigureAwait(true);
    }

    protected override void OnDisappearing()
    {
        _manager.TrustThreatDetected -= OnTrustThreat;
        base.OnDisappearing();
    }

    private void OnTrustThreat(object? sender, MessengerServerTrustThreatEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () => await ReloadAsync().ConfigureAwait(true));
    }

    private async Task ReloadAsync()
    {
        try
        {
            var servers = await _manager.ListAsync().ConfigureAwait(true);
            _suppressActiveToggle = true;
            _rows.Clear();
            foreach (var s in servers.OrderByDescending(x => x.UpdatedUtcTicks))
                _rows.Add(new MessengerServerRowVm(s));
            _status.Text = $"Серверов: {_rows.Count} / {MessengerServerLimits.MaxServersPerUser}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reload messenger servers");
            _status.Text = ex.Message;
        }
        finally
        {
            _suppressActiveToggle = false;
        }
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        var url = _baseUrlEntry.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            await DisplayAlert("Ошибка", "Укажите Base URL сервера.", "OK").ConfigureAwait(true);
            return;
        }

        _addButton.IsEnabled = false;
        _status.Text = "Подключение и регистрация…";
        try
        {
            var entity = await _manager.AddServerAsync(url).ConfigureAwait(true);
            _baseUrlEntry.Text = "";
            _status.Text = $"Добавлен: {entity.BaseUrl}";
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Add messenger server");
            await DisplayAlert("Ошибка", ex.Message, "OK").ConfigureAwait(true);
            _status.Text = ex.Message;
        }
        finally
        {
            _addButton.IsEnabled = true;
        }
    }

    private async void OnShareClicked(object? sender, EventArgs e)
    {
        if (sender is not BindableObject { BindingContext: MessengerServerRowVm row })
            return;

        if (!MessengerServerQrService.TryBuildPayload(row.BaseUrl, out var payload, out var err))
        {
            await DisplayAlert("Поделиться сервером", err ?? "Не удалось собрать QR-код сервера.", "OK")
                .ConfigureAwait(true);
            return;
        }

        try
        {
            var png = MessengerServerQrService.EncodeQrPng(payload);
            await Navigation.PushAsync(new MessengerServerQrPage(payload, png)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Share messenger server QR {Id}", row.Id);
            await DisplayAlert("Поделиться сервером", ex.Message, "OK").ConfigureAwait(true);
        }
    }

    private async void OnImportClicked(object? sender, EventArgs e)
    {
        FileResult? picked;
        try
        {
            picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Файл с QR-кодом сервера",
                FileTypes = FilePickerFileType.Images
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pick messenger server QR file");
            await DisplayAlert("Импортировать сервер", ex.Message, "OK").ConfigureAwait(true);
            return;
        }

        if (picked == null)
            return;

        byte[] bytes;
        try
        {
            await using var stream = await picked.OpenReadAsync().ConfigureAwait(true);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms).ConfigureAwait(true);
            bytes = ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Read messenger server QR file");
            await DisplayAlert("Импортировать сервер", ex.Message, "OK").ConfigureAwait(true);
            return;
        }

        if (!MessengerServerQrService.TryDecodeImage(bytes, out var payload, out var err))
        {
            _logger.LogWarning("Messenger server QR decode failed from file: {Error}", err);
            await DisplayAlert("Импортировать сервер", err ?? "Не удалось прочитать QR-код сервера.", "OK")
                .ConfigureAwait(true);
            return;
        }

        var url = MessengerServerQrCodec.ToBaseUrl(payload);
        _importButton.IsEnabled = false;
        _status.Text = "Импорт сервера…";
        try
        {
            var existing = await _manager.FindExistingByEndpointAsync(url).ConfigureAwait(true);
            if (existing != null)
            {
                _status.Text = $"Сервер уже добавлен: {existing.BaseUrl}";
                await DisplayAlert(
                    "Импортировать сервер",
                    $"Этот сервер уже есть в списке:\n{existing.BaseUrl}",
                    "OK").ConfigureAwait(true);
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
            await DisplayAlert("Импортировать сервер", ex.Message, "OK").ConfigureAwait(true);
        }
        finally
        {
            _importButton.IsEnabled = true;
        }
    }

    private async void OnActiveToggled(object? sender, ToggledEventArgs e)
    {
        if (_suppressActiveToggle)
            return;
        if (sender is not BindableObject { BindingContext: MessengerServerRowVm row } sw)
            return;

        if (e.Value && !row.Trusted)
        {
            _suppressActiveToggle = true;
            if (sw is Switch switchControl)
                switchControl.IsToggled = false;
            _suppressActiveToggle = false;
            await DisplayAlert(
                "Недоверенный сервер",
                "Fingerprint не совпал. Удалите сервер и добавьте заново, только если доверяете новому сертификату.",
                "OK").ConfigureAwait(true);
            return;
        }

        if (row.Active == e.Value)
            return;

        try
        {
            await _manager.SetActiveAsync(row.Id, e.Value).ConfigureAwait(true);
            row.Active = e.Value;
            row.RefreshMeta();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SetActive messenger server {Id}", row.Id);
            _suppressActiveToggle = true;
            if (sw is Switch switchControl)
                switchControl.IsToggled = !e.Value;
            _suppressActiveToggle = false;
            await DisplayAlert("Ошибка", ex.Message, "OK").ConfigureAwait(true);
        }
    }

    private async void OnRecheckClicked(object? sender, EventArgs e)
    {
        if (sender is not BindableObject { BindingContext: MessengerServerRowVm row } bindable)
            return;

        if (bindable is Button button)
            button.IsEnabled = false;
        _status.Text = $"Проверка {row.BaseUrl}…";
        try
        {
            var result = await _manager.RecheckServerAsync(row.Id).ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
            switch (result.Status)
            {
                case MessengerServerRecheckStatus.AvailableAndTrusted:
                    _status.Text = $"Сервер доступен, сертификат совпадает: {result.Server.BaseUrl}";
                    await DisplayAlert(
                        "Проверка сервера",
                        "Сервер доступен, отпечаток сертификата совпадает.\nСтатус: active, доверенный.",
                        "OK").ConfigureAwait(true);
                    break;
                case MessengerServerRecheckStatus.Unreachable:
                    _status.Text = $"Сервер недоступен: {result.Server.BaseUrl}";
                    await DisplayAlert(
                        "Проверка сервера",
                        string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Сервер недоступен. Помечен как неактивный (доверенный статус сохранён)."
                            : $"Сервер недоступен. Помечен как неактивный (доверенный статус сохранён).\n\n{result.ErrorMessage}",
                        "OK").ConfigureAwait(true);
                    break;
                case MessengerServerRecheckStatus.FingerprintMismatch:
                    _status.Text = $"Fingerprint не совпал: {result.Server.BaseUrl}";
                    await DisplayAlert(
                        "Проверка сервера",
                        "Сертификат сервера не совпадает с сохранённым fingerprint.\n" +
                        $"Ожидался: {result.ExpectedFingerprint}\nПолучен: {result.ActualFingerprint}\n\n" +
                        "Сервер отключён и помечен как недоверенный.",
                        "OK").ConfigureAwait(true);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recheck messenger server {Id}", row.Id);
            _status.Text = ex.Message;
            await DisplayAlert("Ошибка", ex.Message, "OK").ConfigureAwait(true);
        }
        finally
        {
            if (bindable is Button restore)
                restore.IsEnabled = true;
        }
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is not BindableObject { BindingContext: MessengerServerRowVm row })
            return;

        var ok = await DisplayAlert(
            "Удалить сервер?",
            $"{row.BaseUrl}\nУчётная запись на сервере не удаляется — только запись на этом устройстве.",
            "Удалить",
            "Отмена").ConfigureAwait(true);
        if (!ok)
            return;

        try
        {
            await _manager.DeleteServerAsync(row.Id).ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Delete messenger server {Id}", row.Id);
            await DisplayAlert("Ошибка", ex.Message, "OK").ConfigureAwait(true);
        }
    }

    private sealed class MessengerServerRowVm : INotifyPropertyChanged
    {
        private bool _active;
        private string _metaLine;

        public MessengerServerRowVm(MessengerServerEntity entity)
        {
            Id = entity.Id;
            BaseUrl = entity.BaseUrl;
            Trusted = entity.Trusted;
            _active = entity.Active;
            Fingerprint = entity.FingerprintSha256;
            IsRegistered = entity.IsRegistered;
            _metaLine = BuildMeta(entity.Trusted, entity.Active, entity.IsRegistered, entity.FingerprintSha256);
        }

        public int Id { get; }
        public string BaseUrl { get; }
        public bool Trusted { get; }
        public bool IsRegistered { get; }
        public string Fingerprint { get; }

        public bool Active
        {
            get => _active;
            set
            {
                if (_active == value)
                    return;
                _active = value;
                OnPropertyChanged();
            }
        }

        public string MetaLine
        {
            get => _metaLine;
            private set
            {
                if (_metaLine == value)
                    return;
                _metaLine = value;
                OnPropertyChanged();
            }
        }

        public void RefreshMeta() =>
            MetaLine = BuildMeta(Trusted, Active, IsRegistered, Fingerprint);

        private static string BuildMeta(bool trusted, bool active, bool registered, string fp)
        {
            var shortFp = string.IsNullOrEmpty(fp)
                ? "—"
                : fp.Length <= 16
                    ? fp
                    : fp[..8] + "…" + fp[^8..];
            var trust = trusted ? "trusted" : "UNTRUSTED";
            var act = active ? "active" : "off";
            var reg = registered ? "registered" : "not registered";
            return $"{trust} · {act} · {reg} · fp {shortFp}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

file static class MessengerServersPageViewExtensions
{
    public static T AtColumn<T>(this T view, int column) where T : View
    {
        Grid.SetColumn(view, column);
        return view;
    }
}
