# ShortP2P.MauiApp

Клиентское приложение на **.NET MAUI** (сейчас в решении настроен таргет **Windows**; в `.csproj` указано, как добавить
Android/iOS/Mac Catalyst после `dotnet workload restore`). Использует `ShortP2P.Client` как общую логику.

## Структура

| Папка / файлы                                                                                                               | Назначение                                                                 |
|-----------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------|
| `MauiProgram.cs`, `App.xaml`, `AppShell.xaml`                                                                               | Запуск MAUI, оболочка навигации                                            |
| `LoginPage`, `RegisterPage`, `ChatsPage`, `ChatDetailPage`, `AddChatPage`, `LanScanPage`, `MyQrPage`, `RoutingSettingsPage` | Экраны приложения (XAML + code-behind)                                     |
| `Services/MauiSecureStorage.cs`                                                                                             | Платформенное безопасное хранилище                                         |
| `Platforms/`                                                                                                                | Точки входа и специфика **Windows**, **Android**, **iOS**, **MacCatalyst** |
| `Resources/`                                                                                                                | Стили, иконки, шрифты, splash, изображения                                 |

Зависимость: `ShortP2P.Client`.
