# ShortP2P.Discovery

Обнаружение пиров в локальной сети по **UDP**: маяки, идентификация, события о найденных узлах. Опирается на типы из `ShortP2P.Transport` (UDP).

## Структура

| Файл | Назначение |
|------|------------|
| `IPeerDiscoveryService.cs`, `UdpPeerDiscoveryService.cs` | Контракт и реализация сервиса discovery |
| `UdpPeerDiscoveryOptions.cs` | Параметры (порты, таймауты и т.д.) |
| `DiscoveredPeer.cs`, `PeerIdentity.cs` | Модели найденного пира и идентичности |
| `DiscoveryBeaconCodec.cs`, `CompressedNetworkId.cs` | Кодирование маяков и сжатый идентификатор сети |
| `DiscoveryNotifications.cs` | Уведомления о событиях discovery |

Зависимость: `ShortP2P.Transport`.
