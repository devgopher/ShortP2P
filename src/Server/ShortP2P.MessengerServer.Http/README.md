# ShortP2P.MessengerServer.Http

HTTPS-клиент messenger-сервера для внешних приложений ShortP2P (`IMessengerServerApi`).

## Регистрация

```csharp
services.AddServerApiClient(configuration);
// или:
services.AddServerApiClient(o =>
{
    o.BaseUrl = "https://localhost:7196";
});
```

Конфиг (`ServerApiClientSettings`):

```json
{
  "ServerApiClientSettings": {
    "BaseUrl": "https://localhost:7196"
  }
}
```

## Использование

```csharp
IMessengerServerApi api = ...; // DI

await api.RegisterAsync(new RegisterRequest { ... });
var login = await api.LoginAsync(new LoginRequest { ... });
// JWT в IMessengerServerSession; Bearer добавляется автоматически.

var messages = await api.GetMessagesAsync();
```

Ошибки API → `MessengerServerApiException` (`StatusCode`, `ErrorCode`).
