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
| `POST` | `/api/v1/messages/receipts` | Квитанция; удаляет inbox-копию устройства |
| `GET` | `/api/v1/messages/receipts` | Квитанции для текущего networkId |
| `GET` | `/api/v1/events/poll` | Long-poll inbox (messages + chatRequests) |
| `GET` | `/api/v1/clients` | Presence (Online если любое устройство в OnlineTimeout) |
| `GET` | `/api/v1/server-tech/power` | TotalPower (anonymous) |
| `GET` | `/api/v1/server-tech/free-powers` | FreePowers % (anonymous) |
| `GET` | `/api/v1/server-tech/ping` | Liveness 200 OK (anonymous) |

`deviceId` — 64 lowercase hex (SHA-256 от install GUID). Даты — UTC. `encryptedDataBase64` — opaque.

## Сборка

```bash
dotnet build src/Server/ShortP2P.MessengerServer.Contracts
```
