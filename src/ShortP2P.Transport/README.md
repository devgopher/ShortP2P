# ShortP2P.Transport

Реализации транспорта поверх **ShortP2P.Transport.Abstractions**: UDP, заглушка Bluetooth, инфракрасный/последовательный
порт (Serial). Используется `System.IO.Ports` для COM-порта.

## Структура

| Файл                                                        | Назначение                                                                |
|-------------------------------------------------------------|---------------------------------------------------------------------------|
| `UdpTransport.cs`, `UdpTransportAddress.cs`                 | UDP-транспорт и адрес                                                     |
| `BluetoothTransportStub.cs`, `BluetoothTransportAddress.cs` | Заглушка/адрес Bluetooth (платформенная реализация — в отдельном проекте) |
| `InfraredSerialTransport.cs`                                | Транспорт через последовательный порт                                     |

Зависимость: проект `ShortP2P.Transport.Abstractions`.
