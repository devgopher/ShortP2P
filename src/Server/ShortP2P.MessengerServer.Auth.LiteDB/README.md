# ShortP2P.MessengerServer.Auth.LiteDB

LiteDB-хранилище аккаунтов для messenger auth.

```csharp
services.AddAuth(configuration)
    .WithLiteDb();
```

Конфиг (`Auth:LiteDb`):

| Ключ | Default |
|------|---------|
| `ConnectionString` | `Filename=messenger-auth.litedb;Connection=shared` |

Коллекция: `auth_accounts` (уникальный `Nick`, Id = `NetworkId`).
