# ShortP2P.MessengerServer.Contracts

Контракт HTTPS API центрального сервера мессенджера: DTO, константы маршрутов, скелет
`IMessengerServerApi` и `openapi.yaml`. Реализации хоста в этом проекте нет.

## Структура

| Файл / папка | Назначение |
|--------------|------------|
| `ApiRoutes.cs` | Пути `/api/v1/...` |
| `IMessengerServerApi.cs` | Операции API без реализации |
| `Dtos/` | Request/response модели |
| `openapi.yaml` | OpenAPI 3 описание |

## Эндпоинты

| Метод | Путь | Описание |
|-------|------|----------|
| `POST` | `/api/v1/auth/register` | Регистрация (nick, networkId, password, deviceId) |
| `POST` | `/api/v1/auth/login` | Авторизация → JWT (`network_id` + `device_id`) |
| `GET` | `/api/v1/server/certificate` | Fingerprint сертификата сервера |
| `GET` | `/api/v1/chats?networkId=` | Список чатов клиента |
| `POST` | `/api/v1/chats/requests` | Запрос чата (fan-out на устройства tgt) |
| `POST` | `/api/v1/messages` | Отправка `MessageDto` (fan-out) |
| `PUT` | `/api/v1/blobs/{blobId}` | Opaque encrypted attachment (`application/octet-stream`) |
| `GET` | `/api/v1/blobs/{blobId}` | Скачать ciphertext вложения |
| `DELETE` | `/api/v1/blobs/{blobId}` | Удалить blob после успешного приёма |
| `POST` | `/api/v1/messages/receipts` | Квитанция; удаляет inbox-копию устройства |
| `GET` | `/api/v1/messages/receipts` | Квитанции для текущего networkId |
| `GET` | `/api/v1/events/poll` | Long-poll inbox (messages + chatRequests) |
| `GET` | `/api/v1/clients` | Presence (Online если любое устройство в OnlineTimeout) |
| `GET` | `/api/v1/server-tech/power` | TotalPower (anonymous) |
| `GET` | `/api/v1/server-tech/free-powers` | FreePowers % (anonymous) |
| `GET` | `/api/v1/server-tech/ping` | Liveness 200 OK (anonymous) |
| `GET` | `/api/v1/trust/ask-rating` | Сообщить о сервере (создаётся с рейтингом 0.8) и получить список рейтингов |
| `GET` | `/api/v1/trust/ask-servers` | Список серверов с рейтингом ≥ **0.3** |
| `POST` | `/api/v1/trust/claim` | Жалоба на другой сервер (`UNAVAILABLE` / `MALFUNCTIONED` / `WRONGCERT`) |
| `POST` | `/api/v1/bot_server_interaction/register` | Регистрация бота → выдача `BotKey` |
| `POST` | `/api/v1/bot_server_interaction/login` | Авторизация бота по `BotKey` → JWT |
| `POST` | `/api/v1/bot_server_interaction/remove` | Удаление бота по `networkId` + `BotKey` |

`deviceId` — 64 lowercase hex (SHA-256 от install GUID). Даты — UTC. `encryptedDataBase64` — opaque.

## Боты (`/api/v1/bot_server_interaction`)

`BotKey` — base64, ровно 64 символа; **строго секретный**, известен только серверу и боту.
Не логировать и не передавать клиентам или третьим лицам.

**Обязанность бота:** хранить связки «сервер → `BotKey`» в безопасном хранилище.
Для каждого сервера ключ **уникален**; один и тот же бот на разных серверах имеет разные `BotKey`.
Потеря ключа = потеря доступа к этому серверу (нужна повторная регистрация, если сервер это допускает).

### Регистрация

Сервер генерирует `BotKey` и отдаёт его боту **один раз** в ответе.

| Сторона | DTO | Поля |
|---------|-----|------|
| Бот → сервер | `BotRegisterRequest` | `networkId`, `botName` (≤256), `botReadableName` (≤100), `botDescription` (≤500) |
| Сервер → бот | `BotRegisterResponse` | те же поля + сгенерированный `botKey` |

После успешной регистрации бот **обязан** сохранить полученный `botKey` в привязке к этому серверу **в безопасном хранилище**.

### Авторизация

Бот предъявляет ранее выданный `BotKey` вместе с идентификаторами.

| Сторона | DTO | Поля |
|---------|-----|------|
| Бот → сервер | `BotLoginRequest` | `networkId`, `botName`, `botKey` |
| Сервер → бот | `BotLoginResponse` | `token` (JWT), `expiresAtUtc` |

### Удаление

| Сторона | DTO | Поля |
|---------|-----|------|
| Бот → сервер | `BotRemoveRequest` | `networkId`, `botKey` |
| Сервер → бот | `BotRemoveResponse` | `networkId`, `botKey` |

После удаления бот должен удалить связку «сервер → `BotKey`» из своего хранилища.

## Сборка

```bash
dotnet build src/Server/ShortP2P.MessengerServer.Contracts
```
