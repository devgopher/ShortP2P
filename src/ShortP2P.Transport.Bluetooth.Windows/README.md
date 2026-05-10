# ShortP2P.Transport.Bluetooth.Windows

Реализация Bluetooth-транспорта для **Windows** через WinRT: **BLE (GATT)** — исходящие записи в RX-характеристику пира и приём через локальный GATT-сервер. Целевой TFM — `net8.0-windows*` (минимальная версия Windows задаётся в `.csproj`).

Зеркальная реализация для **Android (MAUI)** — проект [`ShortP2P.Transport.Bluetooth.Android`](../ShortP2P.Transport.Bluetooth.Android/ShortP2P.Transport.Bluetooth.Android.csproj); общие UUID и описание ролей — [`BleShortP2PGattProtocol`](../ShortP2P.Transport/BleShortP2PGattProtocol.cs) в `ShortP2P.Transport`. На Android приём GATT Write на сервере опирается на API **31+** (ниже — транспорт не стартует, приложение не падает).

## Роли BLE (peripheral / central)

| Роль BLE | В продукте | Поведение в ShortP2P |
|----------|-------------|----------------------|
| **Peripheral** | GATT **Server** | Локально поднимается сервис ShortP2P и характеристика «RX» (`PeerRxCharacteristicUuid`). Входящие данные — это **Write** удалённого central в эту характеристику (`WriteRequested` / Android `OnCharacteristicWriteRequest`). |
| **Central** | GATT **Client** | Подключение к пиру, поиск сервиса/характеристики, **Write (по возможности WithoutResponse)** в «RX» пира для исходящего пакета. |

Текущий сценарий чата **симметричный**: у каждого узла есть и GATT Server, и клиентские исходящие записи к пиру (как два периферийных сервиса, взаимно доступных по MAC).

**Асимметрия и дуплекс без второго «сервера» у одной стороны:** если одна сторона не должна быть видна в скане, на Windows можно создать `WindowsBluetoothTransport` с `WindowsBluetoothTransportOptions { GattDiscoverable = false }`: реклама остаётся **connectable**, но не **discoverable** (подключение по известному адресу/сопряжению). Полностью убрать peripheral на одной стороне нельзя, если нужен приём тех же GATT Write, что и сейчас, — иначе пир некуда писать; альтернатива на будущее — **вторая характеристика** на сервере (например notify/indicate сервер → клиент) и одна только запись клиент→сервер.

## UUID

| Элемент | UUID |
|---------|------|
| Сервис ShortP2P | `9FE8E58B-AF85-4D91-B245-2B40EA0439C7` |
| Характеристика «RX пира» (куда пишет central) | `8DFE6F10-6CB7-4E73-A918-DC47AC34D9E9` |

В коде: `BleShortP2PGattProtocol.ServiceUuid` и `BleShortP2PGattProtocol.PeerRxCharacteristicUuid`.

## Большие объёмы (без OTS)

**Выбранная стратегия для продукта:**

1. **Основной путь:** последовательные **GATT Write** (в т.ч. Write Without Response) с полезной нагрузкой, уже **нарезанной слоем мессенджера** под лимит сессии (`ChunkCodec` и шифрование). Отдельный Object Transfer Service (**OTS**) не используется.
2. **Уведомления / indicate** с сервера — не задействованы в текущем wire-формате; возможны позже для backpressure или дополнительного канала.
3. **Custom L2CAP Connection-oriented Channel** или **Classic Bluetooth (RFCOMM)** — вне текущей реализации; разумны как отдельный этап, если GATT станет узким местом на целевых ОС.

**Опциональный прикладной чанк** (если понадобится единый CRC/нумерация поверх сырого BLE): заголовок 16 байт с magic `SP2C`, CRC32 потока, индекс и число чанков — см. `BleShortP2PGattProtocol.BuildApplicationChunk` / `TryParseApplicationChunk`. Текущий обмен сообщениями может обходиться без этого слоя.

## Структура проекта

| Файл | Назначение |
|------|------------|
| `WindowsBluetoothTransport.cs` | Реализация `ITransport` для Bluetooth на Windows |
| `WindowsBluetoothTransportOptions.cs` | Реклама: `GattDiscoverable` (скан vs только connectable) |
| `BluetoothMacAddress.cs` | Вспомогательная работа с MAC-адресом |

Зависимости: `ShortP2P.Transport.Abstractions`, `ShortP2P.Transport`.
