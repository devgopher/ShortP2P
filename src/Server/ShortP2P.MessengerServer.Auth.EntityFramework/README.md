# ShortP2P.MessengerServer.Auth.EntityFramework

EF Core хранилище аккаунтов (реляционное, без привязки к одному провайдеру).

```csharp
services.AddAuth(configuration)
    .WithEntityFrameworkDb();
```

Конфиг (`Auth:EntityFramework`):

| Ключ | Default | Описание |
|------|---------|----------|
| `Provider` | `Sqlite` | `Sqlite` или `Npgsql` |
| `ConnectionString` | `Data Source=messenger-auth.db` | строка подключения |
| `ApplyMigrationsOnStartup` | `true` | `MigrateAsync` при старте |

Таблица: `auth_accounts`.

## Миграции

```bash
dotnet ef migrations add Initial \
  --project src/Server/ShortP2P.MessengerServer.Auth.EntityFramework \
  --startup-project src/Server/ShortP2P.MessengerServer.Auth.EntityFramework
```
