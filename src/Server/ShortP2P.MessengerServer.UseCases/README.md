# ShortP2P.MessengerServer.UseCases

Слой application use cases сервера мессенджера. Зависит только от Domain.
Реализации портов (БД, кеш, JWT, TLS) — в инфраструктуре / host.

## Структура

| Папка | Содержимое |
|-------|------------|
| `Abstractions/` | Порты репозиториев/кеша, `MessengerCacheOptions`, `MessengerInboxOptions`, wait |
| `Auth/` | Register, Login (DeviceId + lazy fan-out) |
| `Chats/` | GetChats, CreateChatRequest (fan-out) |
| `Messages/` | Send, Submit receipt (delete device inbox), Get receipts |
| `Inbox/` | `DeviceFanoutService`, `PollInboxEventsUseCase` |
| `Hosting/` | Cache promotion TTL; message retention 30d; `InboxWaitService` |
| `Presence/` | GetClientPresences (OnlineTimeout = 1.5 × MaxPoll) |
| `Server/` | GetServerCertificate |

## Inbox / presence

| Правило | Поведение |
|---------|-----------|
| Long-poll | `PollInboxEvents` — list messages (no delete), take chatRequests per DeviceId |
| Submit | Ticket + delete `MessageInbox(MessageId, DeviceId)`; GC Message when no copies |
| Fan-out | Send / ChatRequest → copies for known DeviceIds; lazy on login |
| Retention | Purge Message/ChatRequest older than 30 days |
| Presence | Touch на каждый authed-запрос; Online если любое устройство в окне |

## Кеш Message / DeliveryTicket

| Операция | Поведение |
|----------|-----------|
| Send / Submit receipt | Пишем в доступные хранилища; оба недоступны → `Unavailable` |
| Настройки | `CacheEnabled` / `RepositoryEnabled` |
| TTL кеша | Promotion в durable repo (`ExpiredCachePromotionHostedService`) |

## Сборка

```bash
dotnet build src/Server/ShortP2P.MessengerServer.UseCases
```
