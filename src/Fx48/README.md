# ShortP2P .NET Framework 4.8 libraries

Separate `net48` assemblies that compile the existing ShortP2P sources (plus a few compat shims).
They are for the Iskra Win7/WinForms client: **HTTPS messenger servers, local SQLite, UDP LAN scan, QR from image file**. No BLE, no camera.

| Project | Assembly name | What it contains |
|---|---|---|
| `ShortP2P.Crypto.Fx48` | `ShortP2P.Crypto` | RSA, handshake, password hash |
| `ShortP2P.Auth.Fx48` | `ShortP2P.Auth` | `AuthService`, network id |
| `ShortP2P.TrustSystem.Fx48` | `ShortP2P.TrustSystem` | Server ratings |
| `ShortP2P.MessengerServer.Contracts.Fx48` | `ShortP2P.MessengerServer.Contracts` | HTTP DTOs |
| `ShortP2P.MessengerServer.Http.Fx48` | `ShortP2P.MessengerServer.Http` | `MessengerServerApiClient` |
| `ShortP2P.Transport.Abstractions.Fx48` | `ShortP2P.Transport.Abstractions` | Address types only |
| `ShortP2P.Transport.Fx48` | `ShortP2P.Transport` | UDP/MAC codecs only |
| `ShortP2P.Client.Fx48` | `ShortP2P.Client` | Server sync, chats, QR encode/decode from files |

Build (AnyCPU by default; also `x86` / `x64`):

```
dotnet build src/Fx48/ShortP2P.Client.Fx48.csproj
dotnet build src/Fx48/ShortP2P.Client.Fx48.csproj -p:Platform=x86
dotnet build src/Fx48/ShortP2P.Client.Fx48.csproj -p:Platform=x64
```
