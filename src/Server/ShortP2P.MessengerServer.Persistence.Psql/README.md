# ShortP2P.MessengerServer.Persistence.Psql

PostgreSQL-реализации репозиториев мессенджер-сервера (EF Core + Npgsql), миграции и автоприменение при старте.

## Регистрация

```csharp
services.AddMessengerPostgres(
    connectionString: "Host=localhost;Port=5432;Database=shortp2p_messenger;Username=postgres;Password=postgres",
    applyMigrationsOnStartup: true);
```

`MessengerDbMigrationHostedService` вызывает `Database.MigrateAsync()` при старте host.

## Миграции

```bash
dotnet ef migrations add Initial --project src/Server/ShortP2P.MessengerServer.Persistence.Psql --startup-project src/Server/ShortP2P.MessengerServer.Persistence.Psql
```

Design-time connection: env `MESSENGER_DB` или localhost default в `MessengerDbContextDesignTimeFactory`.

## Таблицы

`chats`, `chat_requests`, `crypto_keys`, `client_statuses`, `messages`, `delivery_tickets`

Аккаунты вынесены в Auth (таблица `auth_accounts` / LiteDB).
