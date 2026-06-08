# ShortP2P.WinForms

Десктопное приложение **Windows Forms** (.NET 10, Windows): UI для чатов, локального сканирования, QR, настроек
маршрутизации, входа и регистрации. Подключает клиентскую логику и Bluetooth-транспорт Windows.

## Структура

| Файл                                | Назначение               |
|-------------------------------------|--------------------------|
| `Program.cs`                        | Точка входа              |
| `MainChatsForm.cs`                  | список чатов             |
| `ChatForm.cs`, `MessageViewForm.cs` | чат и просмотр сообщений |
| `AddChatForm.cs`                    | добавление чата          |
| `LocalNetworkScanForm.cs`           | сканирование LAN         |
| `MyQrForm.cs`                       | отображение своего QR    |
| `RoutingSettingsForm.cs`            | настройки маршрутизации  |
| `LoginForm.cs`, `RegisterForm.cs`   | вход и регистрация       |

Зависимости: `ShortP2P.Client`, `ShortP2P.Transport.Bluetooth.Windows`.
