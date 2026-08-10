# ShortP2P.MessengerServer.Infrastructure

Реализации портов use cases (кеш, позже БД и т.д.).

## In-memory кеш

| Тип | Назначение |
|-----|------------|
| `InMemoryMessageCache` | `IMessageCache` |
| `InMemoryDeliveryTicketCache` | `IDeliveryTicketCache` |
| `InMemoryCacheMemoryTracker` | общий счётчик памяти |
| `InMemoryMessengerCacheOptions.Section` | имя секции appsettings (`InMemoryCacheOptions`) |
| `InMemoryMessengerCacheOptions.MaxMemoryMegabytes` | лимит в МБ; `null`/≤0 — без ограничений |

`IsWriteAvailable` = нет лимита **или** текущий объём &lt; лимита.

Регистрация:

```csharp
services.AddSingleton<IClock, SystemClock>();
services.AddInMemoryMessengerCaches(o => o.MaxMemoryMegabytes = 1024); // optional
```

## Сборка

```bash
dotnet build src/Server/ShortP2P.MessengerServer.Infrastructure
```
