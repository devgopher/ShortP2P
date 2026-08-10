# ShortP2P.MessengerServer.Api

Executable HTTPS host для центрального messenger server: **Kestrel**, **Swagger**, **JWT Bearer**, use cases + infrastructure + optional PostgreSQL.

## Запуск

```bash
dotnet run --project src/Server/ShortP2P.MessengerServer.Api --launch-profile https
```

- API: `https://localhost:7196`
- Swagger UI: `https://localhost:7196/swagger`
- В Development по умолчанию `Persistence:Enabled = false` (in-memory репозитории + кеш).

Нужен доверенный HTTPS-сертификат разработчика: `dotnet dev-certs https --trust`.

## DI

```csharp
builder.Services
    .AddInfrastructure(builder.Configuration)
    .WithInMemoryCache()
    .WithCachePromotion();

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddAuth(builder.Configuration);
builder.Services.AddMessengerUseCases();
```

| Метод | Назначение |
|-------|------------|
| `AddInfrastructure` | `IClock`, `MessengerCacheOptions` |
| `.WithInMemoryCache()` | In-memory cache из секции `InMemoryMessengerCacheOptions.Section` |
| `.WithCachePromotion()` | TTL promote cache → repository |
| `AddPersistence("Persistence")` | Postgres **или** in-memory repos, если `Enabled=false` |
| `AddAuth("Auth")` | PBKDF2 salt+hash, JWT, cert reader, bearer auth |
| `AddMessengerUseCases()` | Все use cases |

При `Persistence:Enabled=false` выставляется `MessengerCacheOptions.RepositoryEnabled=false`; сообщения живут только в кеше, аккаунты/чаты — в in-memory store.

## Конфигурация

См. `appsettings.json`: `InMemoryCacheOptions`, `MessengerCache`, `Persistence`, `Auth`, `Kestrel`.

Пароли хранятся как **salt + hash** (`ShortP2P.Crypto.PasswordHasher` / PBKDF2-SHA256).
