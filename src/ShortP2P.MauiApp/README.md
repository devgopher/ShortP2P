# ShortP2P.MauiApp

Клиент на **.NET MAUI**. По умолчанию собирается **Windows**. **Android** включается автоматически, если найден Android SDK (`%LocalAppData%\Android\Sdk` или `ANDROID_HOME` / `ANDROID_SDK_ROOT`), либо явно: `-p:IncludeAndroid=true`.

Без SDK ошибка **XA5300** больше не блокирует Windows-сборку. Установка SDK: https://aka.ms/dotnet-android-install-sdk

## Структура

| Папка / файлы                                                                                                               | Назначение                                                                 |
|-----------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------|
| `MauiProgram.cs`, `App.xaml`, `AppShell.xaml`                                                                               | Запуск MAUI, оболочка навигации                                            |
| `LoginPage`, `RegisterPage`, `ChatsPage`, `ChatDetailPage`, `AddChatPage`, `LanScanPage`, `MyQrPage`, `RoutingSettingsPage` | Экраны приложения (XAML + code-behind)                                     |
| `Services/MauiSecureStorage.cs`                                                                                             | Платформенное безопасное хранилище                                         |
| `Platforms/`                                                                                                                | Точки входа и специфика **Windows**, **Android**, **iOS**, **MacCatalyst** |
| `Resources/`                                                                                                                | Стили, иконки, шрифты, splash, изображения                                 |

Зависимость: `ShortP2P.Client`.
