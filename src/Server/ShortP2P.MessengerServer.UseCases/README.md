# ShortP2P.MessengerServer.UseCases

Слой application use cases сервера мессенджера. Зависит только от Domain.
Реализации портов (БД, кеш, JWT, TLS) — в инфраструктуре / host.

## Структура

| Папка | Содержимое |
|-------|------------|
| `Abstractions/` | Порты репозиториев и кеша, `MessengerCacheOptions`, `IClock`, hasher, token, cert |
| `Auth/` | Register, Login |
| `Chats/` | GetChats, CreateChatRequest, GetChatRequests |
| `Messages/` | Get/Send messages, delivery receipts |
| `Hosting/` | `ExpiredCachePromotionHostedService` — TTL монитор кеша |
| `Presence/` | KeepAlive |
| `Server/` | GetServerCertificate |

## Кеш Message / DeliveryTicket

| Операция | Поведение |
|----------|-----------|
| Send / Submit receipt | Перед записью в кеш проверяется `IsWriteAvailable`. Пишем в включённые/доступные хранилища. Оба недоступны → `Unavailable` |
| Get messages / receipts | Сначала кеш (если включён); если пусто/недоступен — репозиторий. После выдачи — best-effort удаление из доступных хранилищ |
| Настройки | `CacheEnabled` / `RepositoryEnabled` (оба default **true**). Оба false → ошибка Validation |
| TTL (`TimeToLive`, default **60 с**) | `ExpiredCachePromotionHostedService` (poll default **10 с**) работает только если включены **оба** хранилища; при сбое записи в БД элемент возвращается в кеш |

Зарегистрировать в host: `services.AddHostedService<ExpiredCachePromotionHostedService>()`.

## Правила

- `SendMessage`: повторный `MessageId` — **no-op OK** (кеш или репозиторий).
- `CreateChatRequest`: создаёт `Chat` (пара caller+target), `ChatRequest` и `CryptoKeys`; существующий чат пары не дублируется.
- Ошибки — `UseCaseException` с кодами `Validation`, `Conflict`, `NotFound`, `Unauthorized`.

## Сборка

```bash
dotnet build src/Server/ShortP2P.MessengerServer.UseCases
```
