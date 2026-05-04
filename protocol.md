# ShortP2P — описание сетевого протокола

Документ описывает форматы кадров и логику обмена, реализованные в репозитории ShortP2P (клиент, discovery, крипто-слой). Числа целочисленные, если не указано иное: **BE** — big-endian, **LE** — little-endian.

---

## 1. Идентификация пиров

- **NetworkId** — 16 байт в wire-формате (стандартный UUID `Guid` в порядке байт платформы .NET при сериализации в буфер).
- В UI и в JSON QR используется **короткая строка** (`CompressedNetworkId.ToShortString()`): base64url от 16 байт UUID (без padding).
- Сравнение ролей в крипто-сессии чата: лексикографическое сравнение **бинарного представления Guid** (как `Guid.CompareTo`): пир с **меньшим** NetworkId — **лидер** RSA-handshake; пир с **большим** — только запрос установки сессии (см. §6).

---

## 2. Транспорты и порты UDP

| Назначение | Порт (константа в коде) | Примечание |
|------------|-------------------------|------------|
| Presence / LAN-«пинг» присутствия | **50101** (`PresencePingCodec.UdpPort`) | Широковещательно / с адреса пира; не data-чат. |
| Data-канал мессенджера (UDP) | **50100** по умолчанию (`PresencePingCodec.DefaultDataUdpPort`) | Задаётся в профиле пользователя (`UserEntity.DataUdpPort`); handshake, шифр, служебное. |
| Приглашение в чат (invite) | **50102** (`ChatInviteCodec.InviteUdpPort`) | Отдельный сокет на клиенте; в пакете invite указывается порт для **ответного** invite (обычно тоже 50102). |
| Discovery / gossip / маршрутная таблица | **17890** (`UdpPeerDiscoveryOptions.DefaultDiscoveryUdpPort`) | Beacon, probe, маршруты — см. §8. |

Помимо UDP поддерживается **Bluetooth** (`TransportKind.Bluetooth`): адреса в виде MAC; те же логические кадры invite / data могут передаваться по BT при включённом транспорте.

---

## 3. Presence ping (`0x31`)

Кадр объявляет узел в LAN: кто он, какой у него порт data-UDP, пресет «канала» и маска возможностей.

**Формат (минимально совместимые варианты длины):**

| Смещение | Поле |
|----------|------|
| 0 | `0x31` |
| 1–16 | NetworkId (16 байт) |
| 17–18 | Длина ника UTF-8, **uint16 BE** |
| 19… | Ник UTF-8 (до 512 байт) |
| После ника | `uint16 BE` — data UDP port (если поле отсутствует у очень старых клиентов — принимается **50100**) |
| +1 байт | `LinkTechnologyPreset` (enum byte) |
| +2 байта | `PresencePeerCapabilities`, **uint16 BE** (маска; бит `Chat` принудительно считается выставленным при разборе/сборке) |

Короткие датаграммы (только 17 байт и т.д.) поддерживаются для обратной совместимости — см. `PresencePingCodec.TryParse`.

---

## 4. Приглашение в чат — `ChatInviteCodec` (`0x30`)

Отправляется на **50102** (или по Bluetooth на адрес пира). После приёма пир добавляет/обновляет контакт в локальной БД и может ответить симметричным invite.

**Структура:**

| Смещение | Поле |
|----------|------|
| 0 | `0x30` |
| 1–4 | Магия ASCII `SP2I` |
| 5 | Версия wire: **1** |
| 6–21 | NetworkId инициатора (16 байт) |
| 22–23 | Длина ника UTF-8, **uint16 BE** |
| … | Ник |
| … | Длина RSA public key JSON, **uint32 BE** |
| … | UTF-8 JSON публичного ключа (`RsaKeySerializer`) |
| … | Длина списка хостов UTF-8, **uint16 BE** |
| … | Хосты (часто IPv4 через запятую; могут включать MAC для BT) |
| … | Порт для ответного invite / достижимости, **uint16 BE** (не порт data-чата) |

Поля строк кодируются в **UTF-8**. Длины — **BE**, кроме отдельно оговорённых форматов.

---

## 5. Канал данных чата (один UDP-сокет на пользователя)

На порту **data** (`UserEntity.DataUdpPort`, по умолчанию 50100) слушает **один** `UdpTransport` на `IPAddress.Any`; входящие датаграммы мультиплексируются по **первому байту** полезной нагрузки.

### 5.1. Допустимые первые байты на data-канале

| Байт | Имя | Назначение |
|------|-----|------------|
| `0x30` | Chat invite | Обрабатывается `IncomingChatInviteHandler` (тот же формат, что §4). |
| `0x01` | RSA handshake | Ровно **129** байт: `0x01` + **128** байт RSA-OAEP(SHA1) ciphertext. |
| `0x04` | Session setup request | Ровно **17** байт: `0x04` + **16** байт NetworkId отправителя (wire Guid). |
| `0x02` | Cipher | `0x02` + внутренний шифропакет `P2PSession` (см. §7). |

Иные кадры с префиксом `0x02` и длиной > 1 идут в расшифровку и далее в messenger.

### 5.2. Отправка

Исходящие шифросообщения: один байт `0x02` + ciphertext от `P2PSession.Encrypt`. Invite и handshake уходят «сырыми» по тому же транспорту маршрутизации к адресам пира (UDP/BT + при необходимости relay, см. §9).

---

## 6. Установка крипто-сессии (RSA → общий AES)

- Ключи: **RSA-1024**, шифрование сессионного материала **RSA-OAEP(SHA1)**.
- Сессионный материал: **16 байт** AES-128 key + **32 байта** MAC key (итого 48 байт plaintext внутри 128-байтного RSA блока).

### 6.1. Роли по NetworkId

- Сравниваются `Guid` локального пользователя и пира из чата.
- **Лидер** (меньший Guid): формирует handshake через `P2PCrypto.CreateHandshakeInitiation`, отправляет **`0x01` + 128 байт**.
- **Подписчик** (больший Guid): **не** шлёт `0x01` на data-канал; отправляет только **`0x04` + 16 байт** своего NetworkId и ждёт handshake от лидера.
- Лидер при приёме **`0x04`** проверяет, что Guid совпадает с ожидаемым пиром, затем при необходимости выполняет свой handshake.
- Лидер **игнорирует** входящий `0x01` (не строит сессию как ответчик по чужому пакету).
- Подписчик при приёме `0x01` вызывает `P2PCrypto.CreateSession(localPrivate, handshake128)` и поднимает `MessengerService`.

---

## 7. Симметричное шифрование (`P2PSession`)

После обмена RSA стороны используют общий AES-128 + HMAC.

- **Пакет**: `IV (16)` || `ciphertext` || `tag (16)` — длина ciphertext кратна 16 (PKCS7), **общая длина пакета ≤ 128 байт**.
- **AES**: CBC, PKCS7.
- **Tag**: первые 16 байт HMAC-SHA256 от конкатенации IV||ciphertext с ключом MAC.

Ограничение размера: максимальный plaintext подбирается так, чтобы зашифрованный блок укладывался в 128 байт (**до 95 байт** полезной нагрузки в одном пакете — см. `P2PSession.MaxPlaintextBytes`).

---

## 8. Discovery / gossip (UDP 17890)

Общий порт с настройкой `UdpPeerDiscoveryOptions.DiscoveryPort` (по умолчанию **17890**).

### 8.1. Gossip probe / ack

| Кадр | Первый байт | Описание |
|------|-------------|----------|
| Probe | `0x40` | `nonce int64 LE` + `senderNetworkId 16` + `targetNetworkId 16` (длина фиксирована, см. `GossipWireCodec.ProbeLength`). |
| Ack | `0x41` | `nonce LE` + `responderNetworkId 16` + `dataUdpPort uint16 BE` + `nickLen uint16 BE` + UTF-8 nick. |

### 8.2. Маршрутная таблица по wire

| Кадр | Первый байт | Описание |
|------|-------------|----------|
| Request | `0x42` | `nonce LE` + `senderNetworkId 16`. |
| Reply | `0x43` | Заголовок с nonce, id ответчика, флагами и количеством маршрутов + сериализованные маршруты (`RouteTableWireCodec`). |

Подробности сериализации маршрутов — в исходниках `RouteTableWireCodec`.

---

## 9. LAN-маршрутизация чата (`LanRoutingCodec`)

Используется для поиска пира и UDP-relay цепочек (до **3** хопов).

| Байт | Кадр | Магия / версия |
|------|------|----------------|
| `0x10` | FIND | Тело: `SP2F` + версия **1** + тип сообщения + searchId + targetNetworkId + ник + TTL + visited + path hops (UDP-адреса с префиксом длины **uint16 BE**). |
| `0x11` | FOUND | Аналогично с магией `SP2F`, публичный ключ JSON, host, порт, опционально relay hop и strip path. |
| `0x22` | RELAY | `hopCount` (1 байт) + для каждого хопа `uint16 BE` длина + raw UDP endpoint + **внутренняя** полезная нагрузка (например `0x02`+шифртекст). |

FIND: TTL **1…3**, число visited **≤ 8**, длина пути **≤ 3** UDP-адреса.

---

## 10. Сообщения мессенджера внутри шифра

Plaintext для `P2PSession` — либо **устаревший** сырой UTF-8 текста, либо бинарный кадр **`ChatWireCodec`**:

- Магия: ASCII **`S2P1`** (4 байта).
- Далее байт **типа**: `0x01` текст, `0x02` изображение, `0x03` файл.
- Длины внутри кадра — **little-endian** (`uint32` для текста/данных файла, `uint16` для MIME и имени файла).

Текст кодируется как UTF-8.

### 10.1. Служебный зонд крипто-сессии (не в истории UI)

После успешного handshake **лидер** (тот, кто слал `0x01`) может гонять проверку по зашифрованному каналу текстовыми строками:

- `CHAT <srcNetworkIdShort> <tgtNetworkIdShort> ACK`
- `CHAT <srcNetworkIdShort> <tgtNetworkIdShort> OK`

Формат разбора: `SessionCryptoProbe` (префикс `CHAT `, три токена, третий `ACK` или `OK`). Строки упаковываются через `ChatWireCodec.EncodeText` и передаются внутри `0x02`. Не сохраняются как обычные сообщения чата в БД.

---

## 11. QR контакта (вне UDP, JSON текст)

Версия **`v`: 1**. Поля (`PeerQrPayload`): `n` ник, `id` короткий NetworkId, `k` RSA public key JSON, `p` порт, `h`/`ha` IPv4/IPv6, `b`/`ba` Bluetooth MAC. Сериализация: `System.Text.Json` (`PeerQrCodec`). Это **не** бинарный wire-формат сети, а обмен контактом из приложения.

---

## 12. Порядок типичного сценария «открыли чат»

1. Стороны обмениваются **invite** (`0x30`) на **50102** (и/или по BT), в БД появляется контакт с ключом и адресами.
2. Поднимается **data**-сессия: `StartAsync` шлёт invite на маршрут пира, затем выполняется логика §6 (лидер — `0x01`, подписчик — `0x04` и ожидание).
3. После `P2PSession` — обмен сообщениями кадрами **`0x02`**, внутри — `ChatWireCodec` или legacy UTF-8.
4. Опционально лидер запускает **crypto probe** ACK/OK по зашифрованному тексту.

---

## 13. Замечания по совместимости

- Расширения presence-пинга (порт, link preset, capabilities) опциональны; старые клиенты шлют короткие пакеты.
- Invite требует магию `SP2I` и версию **1**.
- LAN FIND/FOUND требуют магию `SP2F` и версию **1**.
- Ответчик с **большим** NetworkId не должен слать RSA-handshake первым: старые клиенты с другой политикой могут быть несовместимы с текущим правилом лидера.

---

## 14. Ссылки на код

| Тема | Файлы / типы |
|------|----------------|
| Invite | `ShortP2P.Client/Routing/ChatInviteCodec.cs` |
| Presence | `ShortP2P.Client/Routing/PresencePingCodec.cs`, `LinkTechnologyPreset`, `PresencePeerCapabilities` |
| Data кадры чата | `ShortP2P.Client/Services/ChatP2pSession.cs` |
| RSA / AES | `ShortP2P.Crypto/P2PHandshake.cs`, `P2PSession.cs`, `P2PCrypto.cs` |
| LAN find / relay | `ShortP2P.Client/Routing/LanRoutingCodec.cs` |
| Wire чата | `ShortP2P.Client/ChatMedia/ChatWireCodec.cs` |
| Зонд ACK/OK | `ShortP2P.Client/Services/SessionCryptoProbe.cs` |
| Gossip / route table | `ShortP2P.Discovery/Gossip/GossipWireCodec.cs`, `RouteTableWireCodec.cs` |
| Порт discovery | `ShortP2P.Discovery/UdpPeerDiscoveryOptions.cs` |
| QR JSON | `ShortP2P.Client/Qr/PeerQrCodec.cs`, `PeerQrPayload.cs` |
| NetworkId | `ShortP2P.Auth/Data/CompressedNetworkId.cs` |

Дополнительно: README проекта **ShortP2P.Discovery** (узлы и capability-маски в продуктовой терминологии).
