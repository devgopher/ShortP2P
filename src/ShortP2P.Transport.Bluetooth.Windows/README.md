# ShortP2P.Transport.Bluetooth.Windows

Реализация Bluetooth-транспорта для **Windows** через WinRT: **BLE (GATT)** — исходящие записи в RX-характеристику пира и приём через локальный GATT-сервер. Целевой TFM — `net8.0-windows*` (минимальная версия Windows задаётся в `.csproj`).

## Структура

| Файл | Назначение |
|------|------------|
| `WindowsBluetoothTransport.cs` | Реализация `ITransport` для Bluetooth на Windows |
| `BluetoothMacAddress.cs` | Вспомогательная работа с MAC-адресом |

Зависимость: `ShortP2P.Transport.Abstractions`.
