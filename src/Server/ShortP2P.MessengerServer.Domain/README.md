# ShortP2P.MessengerServer.Domain

Доменная модель сервера мессенджера. Не зависит от Contracts; маппинг DTO ↔ domain — в хосте/application-слое.

## Сущности

| Тип | Поля |
|-----|------|
| `Message` | messageId, srcNetworkId, tgtNetworkId, createdUtc, updatedUtc, encryptedDataBase64 |
| `DeliveryTicket` | messageId, receivedAtUtc |
| `Chat` | chatId, networkIds, createdAtUtc |
| `ChatRequest` | requesterNetworkId, targetNetworkId, publicKey, createdAtUtc |
| `ClientAccount` | nick, networkId, passwordSalt, passwordHash, createdAtUtc |
| `ClientStatuses` | networkId, status (`Online`/`Offline`), createdAtUtc |
| `CryptoKeys` | srcNetworkId, tgtNetworkId, publicKey |

## Сборка

```bash
dotnet build src/Server/ShortP2P.MessengerServer.Domain
```
