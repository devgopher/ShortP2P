# ShortP2P.MessengerServer.Auth

Ядро авторизации messenger-сервера: JWT Bearer, хеш пароля (salt+hash), fluent `AddAuth()`.

Хранилище аккаунтов подключается отдельно:

```csharp
services.AddAuth(configuration)
    .WithEntityFrameworkDb(); // или .WithLiteDb()
```

| Тип | Назначение |
|-----|------------|
| `AuthOptions.Section` | секция `Auth` в appsettings |
| `CryptoPasswordHasher` | `IPasswordHasher` |
| `JwtAuthTokenService` | `IAuthTokenService` |
| `AuthBuilder` | продолжение DI для persistence |
