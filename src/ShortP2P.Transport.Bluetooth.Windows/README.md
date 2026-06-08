# ShortP2P.Transport.Bluetooth.Windows

Реализация Bluetooth-транспорта для **Windows** через WinRT: **BLE (GATT)** с раздельными **RX** (приём) и **TX** (
передача notify). Целевой TFM — `net10.0-windows*`.

Зеркало для Android: [
`ShortP2P.Transport.Bluetooth.Android`](../ShortP2P.Transport.Bluetooth.Android/ShortP2P.Transport.Bluetooth.Android.csproj).
Протокол: [`BleShortP2PGattProtocol`](../ShortP2P.Transport/BleShortP2PGattProtocol.cs).

## Роли и дуплекс

| Характеристика (на GATT Server)     | Свойства                            | Направление                                                                                  |
|-------------------------------------|-------------------------------------|----------------------------------------------------------------------------------------------|
| **RX** (`PeerRxCharacteristicUuid`) | Write, Write Without Response, Read | Central **пишет** → peripheral **принимает** (`WriteRequested` → `Inbound`)                  |
| **TX** (`PeerTxCharacteristicUuid`) | Notify, Read                        | Peripheral **шлёт notify** → Central **читает** (подписка CCCD → `ValueChanged` → `Inbound`) |

**Исходящий `SendAsync`:** запись в **RX пира** (локальный Central).

**Входящий приём:** Write на **локальный RX** и Notify с **TX пира** (после подписки при connect).

Симметричный чат: у каждого узла Server (RX+TX) и клиент к пиру (write RX + subscribe TX).

## UUID

| Элемент               | UUID                                   |
|-----------------------|----------------------------------------|
| Сервис ShortP2P       | `9FE8E58B-AF85-4D91-B245-2B40EA0439C7` |
| RX (приём на сервере) | `8DFE6F10-6CB7-4E73-A918-DC47AC34D9E9` |
| TX (передача notify)  | `7CF03A12-8B5E-4D91-B245-2B40EA0439C8` |

## Большие объёмы (без OTS)

Последовательные GATT Write в RX пира + chunking мессенджера; опционально кадры `SP2C` в `BleShortP2PGattProtocol`.
L2CAP CoC / RFCOMM — вне scope.

## Файлы

| Файл                                  | Назначение         |
|---------------------------------------|--------------------|
| `WindowsBluetoothTransport.cs`        | `ITransport`       |
| `WindowsBluetoothTransportOptions.cs` | `GattDiscoverable` |
| `BluetoothMacAddress.cs`              | MAC                |
