using ShortP2P.Client.Qr;

namespace ShortP2P.MauiApp;

public sealed class MessengerServerQrPage : ContentPage
{
    private readonly byte[] _qrPng;

    public MessengerServerQrPage(MessengerServerQrPayload payload, byte[] qrPng)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(qrPng);
        _qrPng = qrPng;

        Title = "Поделиться сервером";
        var caption = $"{payload.H}:{payload.P}";

        var image = new Image
        {
            HeightRequest = 280,
            WidthRequest = 280,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            BackgroundColor = Color.FromRgb(245, 245, 245),
            Source = ImageSource.FromStream(() => new MemoryStream(_qrPng))
        };

        var share = new Button { Text = "Поделиться", HorizontalOptions = LayoutOptions.Center };
        share.Clicked += OnShareClicked;
        var close = new Button { Text = "Закрыть", HorizontalOptions = LayoutOptions.Center };
        close.Clicked += async (_, _) => await Navigation.PopAsync().ConfigureAwait(true);

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 12,
                Children =
                {
                    new Label
                    {
                        Text = $"QR-код сервера {caption}. Другой клиент может импортировать этот файл.",
                        FontSize = 12,
                        TextColor = Colors.Gray
                    },
                    image,
                    share,
                    close
                }
            }
        };
    }

    private async void OnShareClicked(object? sender, EventArgs e)
    {
        try
        {
            var filename = $"shortp2p-server-qr-{DateTime.UtcNow:yyyyMMddHHmmss}.png";
            var path = Path.Combine(FileSystem.CacheDirectory, filename);
            await File.WriteAllBytesAsync(path, _qrPng).ConfigureAwait(true);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Поделиться сервером",
                File = new ShareFile(path)
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await DisplayAlert("QR", $"Не удалось поделиться QR-кодом: {ex.Message}", "OK").ConfigureAwait(true);
        }
    }
}
