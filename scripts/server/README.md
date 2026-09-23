# Сборка пакетов ShortP2P Messenger Server

Только **отдельная нода** `ShortP2P.MessengerServer.Api`. Клиент не входит.

Версия берётся из `<Version>` / `<InformationalVersion>` в
`src/Server/ShortP2P.MessengerServer.Api/ShortP2P.MessengerServer.Api.csproj`,
иначе из `scripts/server/VERSION` (сейчас `0.1.0`).

Скрипты можно запускать из корня репозитория или из `scripts/server`.

## Windows (Inno Setup)

Нужны .NET SDK и [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
powershell -File scripts\server\windows\build-installer.ps1
```

Или по шагам:

```powershell
powershell -File scripts\server\publish.ps1 -Rid win-x64
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=0.1.0 scripts\server\windows\shortp2p-messengerserver.iss
```

Результат: `scripts/server/out/installer/shortp2p-messengerserver-<версия>-win-x64.exe`.

Установка (админ):

| Что | Путь |
|-----|------|
| Бинарники | `C:\Program Files\ShortP2P\MessengerServer\` |
| Конфиг и данные | `C:\ProgramData\ShortP2P\MessengerServer\` |
| LiteDB | `...\data\messenger-auth.litedb`, `messenger-trust.litedb`, `messenger-host-powers.litedb` |
| Сертификат | `...\certs\` (пусто, свой PFX) |
| Служба | `ShortP2PMessengerServer`, `ASPNETCORE_ENVIRONMENT=Production` |

Апгрейд не затирает `appsettings.Production.json`, `certs\` и `*.litedb`.
Деинсталлятор не удаляет данные.

## Linux (.deb)

Нужны .NET SDK и (для финального файла) `dpkg-deb`.

```bash
./scripts/server/debian/build-deb.sh
```

Или по шагам:

```bash
./scripts/server/publish.sh linux-x64
./scripts/server/debian/build-deb.sh --skip-publish
```

Результат: `scripts/server/out/deb/shortp2p-messengerserver_<версия>-1_amd64.deb`
(если `dpkg-deb` нет — только дерево `scripts/server/out/deb-staging`).

```bash
sudo dpkg -i scripts/server/out/deb/shortp2p-messengerserver_0.1.0-1_amd64.deb
```

| Что | Путь |
|-----|------|
| Пакет | `shortp2p-messengerserver` |
| Бинарники | `/opt/shortp2p-messengerserver/` |
| Конфиг (conffile) | `/etc/shortp2p/appsettings.Production.json` |
| Данные / LiteDB | `/var/lib/shortp2p/data/` |
| Сертификат | `/etc/shortp2p/certs/` |
| Unit | `shortp2p-messengerserver.service`, `User=shortp2p` |

`postgresql` только в Recommends. `apt remove` данные не трогает, `apt purge` — удаляет.

## После установки

1. Задайте `Auth:SigningKey` (≥ 32 символов) в Production json.
2. Укажите `Trust:SelfHost` (адрес ноды для клиентов, не оставляйте `127.0.0.1` в LAN/WAN).
3. Положите свой TLS-сертификат в каталог `certs` (инсталлятор PFX не кладёт).
4. Postgres по желанию: поставьте СУБД сами, создайте БД, выставьте `Persistence:Enabled=true` и строку подключения. По умолчанию `false`.

Что переживает рестарт: LiteDB (аккаунты, trust, host-powers) по абсолютным путям.
RAM-кеш — нет. Inbox и blobs — только при Persistence + Postgres.

## Ограничения хоста (нужны правки C#)

Пакеты ставят службу **сейчас**, но процесс ещё не оформлен как Windows Service / systemd notify:

- В `Program.cs` нет `UseWindowsService()`. Служба Windows, скорее всего, упадёт с ошибкой 1053, пока хост не начнёт отвечать SCM.
- Нет `UseSystemd()`. Unit специально `Type=simple` (не `notify`).
- `KestrelServerCertificateReader` читает только хранилище `CurrentUser\My` (удобно для dev-сертификата Windows). Файловый PFX/PEM и fingerprint с него — отдельная доработка хоста. В Production-шаблоне секция `Kestrel:Endpoints:Https:Certificate` закомментирована.
- HTTPS без сертификата на Linux не поднимется, пока не положите cert и хост не начнёт его читать.
