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
| `POST` | `/api/v1/auth/register` | Регистрация (nick, networkId, password) |
| `POST` | `/api/v1/auth/login` | Авторизация (networkId, password) → token |
| `GET` | `/api/v1/server/certificate` | Fingerprint сертификата сервера |
| `GET` | `/api/v1/chats?networkId=` | Список чатов клиента |
| `POST` | `/api/v1/chats/requests` | Запрос чата (publicKey, targetNetworkId) |
| `GET` | `/api/v1/chats/requests` | Входящие запросы чата |
| `GET` | `/api/v1/messages` | Сообщения для текущего networkId |
| `POST` | `/api/v1/messages` | Отправка `MessageDto` |
| `POST` | `/api/v1/messages/receipts` | Отправка квитанции |
| `GET` | `/api/v1/messages/receipts` | Все квитанции для текущего networkId |
| `POST` | `/api/v1/keepalive` | KeepAlive |
| `GET` | `/api/v1/clients` | Список клиентов и статус (Online/Offline) |

Даты — UTC. `encryptedDataBase64` — opaque (сервер не расшифровывает).

## Сборка

```bash
dotnet build src/Server/ShortP2P.MessengerServer.Contracts
```
