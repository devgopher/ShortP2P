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
| Send / Submit receipt | Параллельная запись в **кеш** и **репозиторий** |
| Get messages / receipts | Сначала кеш; если пусто — репозиторий. После выдачи — **удаление** из кеша и репозитория |
| TTL (`MessengerCacheOptions.TimeToLive`, default **60 с**) | `ExpiredCachePromotionHostedService` (poll default **10 с**) переносит expired в репозиторий и чистит кеш |

Зарегистрировать в host: `services.AddHostedService<ExpiredCachePromotionHostedService>()`.

## Правила

- `SendMessage`: повторный `MessageId` — **no-op OK** (кеш или репозиторий).
- `CreateChatRequest`: создаёт `Chat` (пара caller+target), `ChatRequest` и `CryptoKeys`; существующий чат пары не дублируется.
- Ошибки — `UseCaseException` с кодами `Validation`, `Conflict`, `NotFound`, `Unauthorized`.

## Сборка

```bash
dotnet build src/Server/ShortP2P.MessengerServer.UseCases
```
