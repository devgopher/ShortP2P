# ShortP2P Messenger Server

Библиотечный слой **центрального HTTPS store-and-forward сервера** мессенджера ShortP2P.

Сервер принимает **уже зашифрованные** полезные нагрузки (`encryptedDataBase64` — opaque), регистрирует клиентов по `networkId` (короткий id в base64url), хранит чаты/запросы/ключи, доставляет сообщения и квитанции, ведёт presence (keep-alive).

Executable-хост: **`ShortP2P.MessengerServer.Api`** (Kestrel + Swagger + HTTPS + JWT). Библиотечные слои: контракт, домен, use cases, in-memory кеш, PostgreSQL.

Подробности по каждому проекту — в его собственном `README.md`.

---

## Назначение

| Задача | Как решается |
|--------|--------------|
| Регистрация / логин | `nick` + `networkId` + `password` → аккаунт; логин выдаёт токен (порт `IAuthTokenService`) |
| Pinning сертификата | `GET /api/v1/server/certificate` → SHA-256 fingerprint |
| Чаты | Список чатов; запрос чата создаёт `Chat` + `ChatRequest` + `CryptoKeys` |
| Сообщения | Store-and-forward: запись в кеш и/или БД; выдача адресату с последующим удалением |
| Квитанции доставки | Delivery ticket для отправителя; только получатель может подтвердить |
| Presence | `POST /keepalive` выставляет статус `Online` |

Клиентский mesh (UDP/BT) и этот сервер — **разные каналы**: сервер — центральная точка обмена, когда P2P недоступен или нужна регистрация/офлайн-доставка.

---

## Структура папки

```
src/Server/
├── ShortP2P.MessengerServer.Api/                 # Kestrel host: HTTPS, Swagger, JWT
├── ShortP2P.MessengerServer.Auth/                # JWT, hasher, AddAuth()
├── ShortP2P.MessengerServer.Auth.EntityFramework/ # EF Core accounts + migrations
├── ShortP2P.MessengerServer.Auth.LiteDB/         # LiteDB accounts
├── ShortP2P.MessengerServer.Contracts/           # DTO, маршруты, OpenAPI
├── ShortP2P.MessengerServer.Domain/              # Доменные сущности
├── ShortP2P.MessengerServer.UseCases/            # Application-слой
├── ShortP2P.MessengerServer.Infrastructure/      # In-memory кеш, SystemClock
└── ShortP2P.MessengerServer.Persistence.Psql/    # Messenger Postgres (без аккаунтов)
```

Все проекты входят в `ShortP2P.sln`.

---

## Архитектура и зависимости

```
                    ┌─────────────────────────┐
                    │  MessengerServer.Api    │
                    │  HTTPS, JWT, Swagger    │
                    └───────────┬─────────────┘
                                │
         ┌──────────────────────┼──────────────────────┐
         ▼                      ▼                      ▼
   Contracts              UseCases              Infrastructure
   (DTO/OpenAPI)              │                 (кеш, clock)
         │                    │                        │
         │                    ▼                        │
         │                 Domain ◄────────────────────┘
         │                    ▲
         │                    │
         │            Persistence.Psql
         │            (репозитории Postgres)
         └─────────────────────────────────────────────
```

| Проект | TFM | Зависит от |
|--------|-----|------------|
| **Contracts** | `net10.0` | — |
| **Domain** | `net8.0; net10.0` | — |
| **UseCases** | `net8.0; net10.0` | Domain |
| **Infrastructure** | `net10.0` | UseCases |
| **Persistence.Psql** | `net8.0` | Domain, UseCases |

Правила слоёв:

- **Domain** не знает про Contracts и инфраструктуру.
- **UseCases** зависят только от Domain; порты (`I*Repository`, `I*Cache`, hasher, token, cert) — в `Abstractions/`.
- **Contracts** — контракт для HTTP-клиента и хоста; маппинг DTO ↔ domain — в хосте.
- Реализации портов — в Infrastructure / Persistence / host.

---

## Проекты

### 1. `ShortP2P.MessengerServer.Contracts`

HTTPS JSON-контракт API v1.

| Артефакт | Назначение |
|----------|------------|
| `ApiRoutes.cs` | Константы путей `/api/v1/...` |
| `IMessengerServerApi.cs` | Скелет операций без реализации |
| `Dtos/` | Request/response модели |
| `openapi.yaml` | OpenAPI 3.0.3 (копируется в output) |

Авторизация в OpenAPI: **Bearer JWT** (`bearerAuth`).  
Даты — **UTC**. Сервер **не расшифровывает** `encryptedDataBase64`.

### 2. `ShortP2P.MessengerServer.Domain`

Чистая доменная модель.

| Тип | Смысл |
|-----|--------|
| `ClientAccount` | nick, networkId, passwordSalt/Hash, createdAtUtc |
| `ClientStatuses` / `ClientOnlineStatus` | Online / Offline |
| `Chat` | chatId, networkIds[], createdAtUtc |
| `ChatRequest` | requester → target + publicKey |
| `CryptoKeys` | направленная пара src/tgt + publicKey |
| `Message` | store-and-forward ciphertext |
| `DeliveryTicket` | messageId + receivedAtUtc |

### 3. `ShortP2P.MessengerServer.UseCases`

Сценарии приложения и порты.

| Папка | Use cases |
|-------|-----------|
| `Auth/` | `RegisterClient`, `LoginClient` |
| `Chats/` | `GetChats`, `CreateChatRequest`, `GetChatRequests` |
| `Messages/` | `SendMessage`, `GetMessages`, `SubmitDeliveryReceipt`, `GetDeliveryReceipts` |
| `Presence/` | `KeepAlive` |
| `Server/` | `GetServerCertificate` |
| `Hosting/` | `ExpiredCachePromotionHostedService` |
| `Abstractions/` | репозитории, кеши, `MessengerCacheOptions`, `IClock`, hasher, token, cert |

Ошибки — `UseCaseException` с кодами: `Validation`, `Conflict`, `NotFound`, `Unauthorized`, `Unavailable`.

### 4. `ShortP2P.MessengerServer.Infrastructure`

| Тип | Порт / роль |
|-----|-------------|
| `InMemoryMessageCache` | `IMessageCache` |
| `InMemoryDeliveryTicketCache` | `IDeliveryTicketCache` |
| `InMemoryCacheMemoryTracker` | общий счётчик байт |
| `SystemClock` | `IClock` |

Лимит памяти: `InMemoryMessengerCacheOptions.MaxMemoryMegabytes` (`null`/≤0 — без лимита).  
`IsWriteAvailable` = нет лимита **или** текущий объём &lt; лимита.

### 5. `ShortP2P.MessengerServer.Persistence.Psql`

EF Core 8 + Npgsql: сущности-записи, репозитории, миграция `Initial`, авто-migrate при старте.

Таблицы: `client_accounts`, `chats`, `chat_requests`, `crypto_keys`, `client_statuses`, `messages`, `delivery_tickets`.

---

## HTTPS API (`/api/v1`)

| Метод | Путь | Auth | Описание | Типичный код |
|-------|------|------|----------|--------------|
| `POST` | `/auth/register` | — | Регистрация (nick, networkId, password) | `201` / `409` |
| `POST` | `/auth/login` | — | Логин → `{ token, expiresAtUtc }` | `200` / `401` |
| `GET` | `/server/certificate` | — | Fingerprint TLS-сертификата | `200` |
| `GET` | `/chats?networkId=` | JWT | Чаты клиента | `200` |
| `POST` | `/chats/requests` | JWT | Запрос чата (publicKey, targetNetworkId) | `202` |
| `GET` | `/chats/requests` | JWT | Входящие запросы чата | `200` |
| `GET` | `/messages` | JWT | Сообщения для текущего networkId | `200` |
| `POST` | `/messages` | JWT | Отправка `MessageDto` | `202` |
| `POST` | `/messages/receipts` | JWT | Квитанция доставки | `202` |
| `GET` | `/messages/receipts` | JWT | Квитанции для текущего networkId | `200` |
| `POST` | `/keepalive` | JWT | Presence keep-alive | `204` |

Ошибки API: `ApiError { code, message }`.

Полная схема: [`ShortP2P.MessengerServer.Contracts/openapi.yaml`](ShortP2P.MessengerServer.Contracts/openapi.yaml).

---

## Поведение use cases (важные правила)

### Auth

- **Register**: уникальны `networkId` и `nick`; создаётся аккаунт + статус `Offline`.
- **Login**: проверка пароля через `IPasswordHasher`; токен через `IAuthTokenService.IssueToken(networkId)`.

### Чаты

- **CreateChatRequest**: нельзя создать чат с собой; если пары ещё нет — создаётся `Chat` (`ChatId = Guid N`); всегда добавляются `ChatRequest` и upsert `CryptoKeys` (caller → target). Существующий чат пары не дублируется.

### Сообщения и квитанции (кеш ↔ БД)

Настройки: `MessengerCacheOptions`

| Свойство | Default | Смысл |
|----------|---------|--------|
| `CacheEnabled` | `true` | Использовать hot-cache |
| `RepositoryEnabled` | `true` | Использовать durable store |
| `TimeToLive` | `60s` | Возраст в кеше до промоушена в БД |
| `PollInterval` | `10s` | Интервал фонового промоушена |

Оба флага `false` → `Validation`.

| Операция | Поведение |
|----------|-----------|
| **Send / Submit receipt** | Перед записью в кеш — `IsWriteAvailable`. Пишем в доступные хранилища; оба недоступны → `Unavailable` |
| **Get messages / receipts** | Сначала кеш; если пусто — репозиторий; после выдачи — best-effort удаление из доступных хранилищ |
| **Повторный MessageId** | **no-op OK** (идемпотентность) |
| **Submit receipt** | Сообщение должно существовать; квитанцию может отправить только `tgtNetworkId` |
| **TTL promotion** | `ExpiredCachePromotionHostedService`: expired → БД → удаление из кеша; сбой БД — элемент остаётся в кеше |

---

## Порты (Abstractions)

| Порт | Реализация |
|------|------------|
| `IMessageCache` / `IDeliveryTicketCache` | Infrastructure (in-memory) |
| Репозитории | Persistence.Psql **или** in-memory в Api при `Persistence:Enabled=false` |
| `IClock` | Infrastructure (`SystemClock`) |
| `IPasswordHasher` / `IAuthTokenService` | Auth |
| `IClientAccountRepository` | Auth.EntityFramework / Auth.LiteDB |
| `IServerCertificateReader` | Api (`KestrelServerCertificateReader`) |

---

## Регистрация в DI (host)

См. `ShortP2P.MessengerServer.Api/Program.cs`:

```csharp
builder.Services
    .AddInfrastructure(builder.Configuration)
    .WithInMemoryCache()
    .WithCachePromotion();

builder.Services.AddPersistence(builder.Configuration);

builder.Services
    .AddAuth(builder.Configuration)
    .WithLiteDb(); // или .WithEntityFrameworkDb();

builder.Services.AddServerCertificateReader();
builder.Services.AddMessengerUseCases();
```

При `Persistence:Enabled=false` messenger-репозитории — in-memory; аккаунты всегда через Auth (`WithLiteDb` / `WithEntityFrameworkDb`).

---

## PostgreSQL

### Таблицы и ключи

| Таблица | PK / индексы |
|-------|---------------|
| `chats` | PK `chat_id`; `network_ids` как **jsonb** |
| `chat_requests` | PK `id` (identity); index по `target`; index пары requester+target |
| `crypto_keys` | PK (`src_network_id`, `tgt_network_id`) |
| `client_statuses` | PK `network_id`; status как string |
| `messages` | PK `message_id`; indexes по src/tgt |
| `delivery_tickets` | PK `message_id` |

Аккаунты: таблица `auth_accounts` в Auth.EntityFramework (отдельная БД).

### Миграции

```bash
# Design-time connection: env MESSENGER_DB или default localhost в MessengerDbContextDesignTimeFactory
dotnet ef migrations add <Name> \
  --project src/Server/ShortP2P.MessengerServer.Persistence.Psql \
  --startup-project src/Server/ShortP2P.MessengerServer.Persistence.Psql
```

При `applyMigrationsOnStartup: true` `MessengerDbMigrationHostedService` вызывает `Database.MigrateAsync()` на старте.

---

## Сборка

Из корня репозитория:

```bash
dotnet build src/Server/ShortP2P.MessengerServer.Contracts
dotnet build src/Server/ShortP2P.MessengerServer.Domain
dotnet build src/Server/ShortP2P.MessengerServer.UseCases
dotnet build src/Server/ShortP2P.MessengerServer.Infrastructure
dotnet build src/Server/ShortP2P.MessengerServer.Persistence.Psql
```

Или целиком решение: `dotnet build ShortP2P.sln`.

---

## Что ещё не сделано

- Клиентский HTTP SDK (можно опираться на `IMessengerServerApi` + OpenAPI).
- Интеграционные/юнит-тесты серверного слоя.

---

## Связанные README

- [Api](ShortP2P.MessengerServer.Api/README.md)
- [Auth](ShortP2P.MessengerServer.Auth/README.md)
- [Auth.EntityFramework](ShortP2P.MessengerServer.Auth.EntityFramework/README.md)
- [Auth.LiteDB](ShortP2P.MessengerServer.Auth.LiteDB/README.md)
- [Contracts](ShortP2P.MessengerServer.Contracts/README.md)
- [Domain](ShortP2P.MessengerServer.Domain/README.md)
- [UseCases](ShortP2P.MessengerServer.UseCases/README.md)
- [Infrastructure](ShortP2P.MessengerServer.Infrastructure/README.md)
- [Persistence.Psql](ShortP2P.MessengerServer.Persistence.Psql/README.md)
