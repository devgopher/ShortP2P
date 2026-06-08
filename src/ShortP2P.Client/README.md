# ShortP2P.Client

Прикладная логика клиента ShortP2P: регистрация/логин, SQLite-хранилище чатов, P2P-сессии, сценарии маршрутизации и
чата (LAN, relay, QR), адаптивный выбор транспорта, интеграция с `Messenger` и проектами **overlay-слоя**.

Слой **Discovery / Retranslation** (поиск узлов, ретрансляция трафика для других пиров) проектируется **в отдельных
сборках** решения, а не как часть `ShortP2P.Client`; клиент только подключает эти проекты и orchestration. Часть кода
маршрутизации и ping пока живёт здесь исторически и со временем уходит в overlay-проекты (см.
`ShortP2P.Discovery/README.md`, раздел «Граница слоя»).

## Структура

| Папка / файлы                                 | Назначение                                                                                                                                     |
|-----------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------|
| `Data/`                                       | `AppDatabase`, сущности пользователя и чата (`UserEntity`, `ChatEntity`, `ChatMessageEntity`)                                                  |
| `Services/`                                   | `UserP2pRuntime`, `ChatP2pSession`, `ChatRepository`, `AuthService`, `AdaptiveChatTransportLayer`, обработка инвайтов, политики доставки и др. |
| `Routing/`                                    | Настройки маршрутизации, LAN/relay, кодеки приглашений и пингов, `SharedUserUdpGateway`                                                        |
| `LocalNetwork/`                               | Сканирование сети, broadcast-хелперы                                                                                                           |
| `Qr/`                                         | QR-коды и пейлоады для обмена пирами (`PeerQrService`, кодеки)                                                                                 |
| `ISessionStorage.cs`, `FileSessionStorage.cs` | Абстракция и файловое хранилище сессии                                                                                                         |

Внешние пакеты: Polly, QRCoder, SQLite, ZXing, ImageSharp и др. (см. `.csproj`).

Зависимости: `ShortP2P.Crypto`, `ShortP2P.Transport`, `ShortP2P.Messenger`, `ShortP2P.Discovery`.
