# ShortP2P.MessengerServer.Api

Executable HTTPS host: **Kestrel**, **Swagger**, **JWT Bearer**.

## Запуск

```bash
dotnet run --project src/Server/ShortP2P.MessengerServer.Api --launch-profile https
```

Swagger: `https://localhost:7196/swagger`

## DI

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

| Метод | Назначение |
|-------|------------|
| `AddAuth().WithLiteDb()` | JWT + PBKDF2 + аккаунты в LiteDB |
| `AddAuth().WithEntityFrameworkDb()` | JWT + PBKDF2 + аккаунты в EF (Sqlite/Npgsql) |
| `AddPersistence` | чаты/сообщения (Postgres или in-memory) |

Аккаунты больше не живут в messenger Persistence — только в Auth.*Db.
