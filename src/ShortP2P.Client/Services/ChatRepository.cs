using System.Net;
using ShortP2P.Auth.Data;
using ShortP2P.Client.Data;
using ShortP2P.Crypto;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;
using SQLite;

namespace ShortP2P.Client.Services;

public sealed class ChatMessageAppendedEventArgs(int chatId, bool outgoing) : EventArgs
{
    public int ChatId { get; } = chatId;
    public bool Outgoing { get; } = outgoing;
}

public sealed class ChatCreatedEventArgs(int chatId, bool remote) : EventArgs
{
    public int ChatId { get; } = chatId;
    /// <summary>True — чат появился извне (invite / сервер), не создан вручную пользователем.</summary>
    public bool Remote { get; } = remote;
}

public sealed class PeerPublicKeyChangedEventArgs : EventArgs
{
    public int ChatId { get; init; }
    public string PeerNickname { get; init; } = "";
    public string PeerNetworkIdShort { get; init; } = "";
    public string PreviousSafetyNumber { get; init; } = "";
    public string NewSafetyNumber { get; init; } = "";
}

public sealed class ChatRepository(AppDatabase db, PeerBlacklist? blacklist = null)
{
    private readonly SemaphoreSlim _addChatGate = new(1, 1);
    private readonly PeerBlacklist? _blacklist = blacklist;
    private readonly AppDatabase _db = db ?? throw new global::System.ArgumentNullException(nameof(db));

    /// <summary>Список чатов на главном экране: обновить после входящего приглашения и т.п.</summary>
    public event EventHandler? ChatListChanged;

    /// <summary>Новое сообщение записано в БД (входящее или исходящее).</summary>
    public event EventHandler<ChatMessageAppendedEventArgs>? ChatMessageAppended;

    /// <summary>В БД вставлен новый чат (не обновление существующего).</summary>
    public event EventHandler<ChatCreatedEventArgs>? ChatCreated;

    /// <summary>Входящее приглашение (ChatRequest / LAN): открыть чат, даже если строка уже была.</summary>
    public event EventHandler<ChatCreatedEventArgs>? IncomingChatInvite;

    /// <summary>Сохранённый публичный ключ пира заменён другим (возможный MITM).</summary>
    public event EventHandler<PeerPublicKeyChangedEventArgs>? PeerPublicKeyChanged;

    public void NotifyChatListChanged() =>
        RaiseEvent(ChatListChanged, EventArgs.Empty);

    public async Task NotifyIncomingChatInviteAsync(int chatId, CancellationToken cancellationToken = default)
    {
        var chat = await GetChatAsync(chatId).ConfigureAwait(false);
        if (chat != null &&
            await IsPeerBlockedAsync(chat.UserId, chat.PeerNetworkIdShort, cancellationToken).ConfigureAwait(false))
            return;
        RaiseEvent(IncomingChatInvite, new ChatCreatedEventArgs(chatId, remote: true));
    }

    public async Task<bool> IsPeerBlockedAsync(int userId, string? peerNetworkId,
        CancellationToken cancellationToken = default)
    {
        if (_blacklist == null)
            return false;
        await _blacklist.EnsureLoadedAsync(userId, cancellationToken).ConfigureAwait(false);
        return _blacklist.IsBlocked(userId, peerNetworkId);
    }

    public async Task<bool> IsChatFromBlockedPeerAsync(int chatId, CancellationToken cancellationToken = default)
    {
        var chat = await GetChatAsync(chatId).ConfigureAwait(false);
        if (chat == null)
            return false;
        return await IsPeerBlockedAsync(chat.UserId, chat.PeerNetworkIdShort, cancellationToken).ConfigureAwait(false);
    }

    private async Task NotifyChatListChangedUnlessBlockedAsync(int userId, string? peerNetworkId)
    {
        if (await IsPeerBlockedAsync(userId, peerNetworkId).ConfigureAwait(false))
            return;
        NotifyChatListChanged();
    }

    private async Task RaiseChatMessageAppendedAsync(int chatId, bool outgoing, ChatEntity? chat)
    {
        if (!outgoing && chat != null &&
            await IsPeerBlockedAsync(chat.UserId, chat.PeerNetworkIdShort).ConfigureAwait(false))
            return;
        RaiseEvent(ChatMessageAppended, new ChatMessageAppendedEventArgs(chatId, outgoing));
    }

    /// <summary>
    /// Invoke multicast handlers one-by-one so a disposed UI subscriber cannot abort chat create
    /// or block other listeners (common after WinForms logout/login).
    /// </summary>
    private void RaiseEvent<TEventArgs>(EventHandler<TEventArgs>? handlers, TEventArgs args)
        where TEventArgs : EventArgs
    {
        if (handlers == null)
            return;
        foreach (EventHandler<TEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch
            {
                // UI / listener failures must not roll back DB writes.
            }
        }
    }

    private void RaiseEvent(EventHandler? handlers, EventArgs args)
    {
        if (handlers == null)
            return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch
            {
                // UI / listener failures must not roll back DB writes.
            }
        }
    }

    public async Task<IReadOnlyList<ChatEntity>> ListChatsAsync(int userId)
    {
        var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
        var rows = await conn.Table<ChatEntity>()
            .Where(c => c.UserId == userId)
            .ToListAsync()
            .ConfigureAwait(false);

        // Persist merge of duplicate peers (same canonical network id) so offline history stays on one row.
        rows = await DeduplicateChatsInDbAsync(conn, rows, notify: false).ConfigureAwait(false);

        return rows
            .GroupBy(CanonicalPeerNetworkIdKey, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(c => c.UpdatedUtcTicks).First())
            .OrderByDescending(c => c.UpdatedUtcTicks)
            .ToList();
    }

    public async Task<ChatEntity?> GetChatAsync(int chatId)
    {
        var conn = await _db.GetConnectionAsync();
        return await conn.FindAsync<ChatEntity>(chatId);
    }

    public async Task<ChatEntity> AddChatAsync(int userId, string peerNickname, string peerNetworkIdShort,
        string peerRsaPublicJson, string peerHost, int peerPort, bool remote = false,
        PeerKeySource? keySource = null)
    {
        await _addChatGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var idShort = CanonicalPeerNetworkId(peerNetworkIdShort);
            if (idShort.Length == 0)
                throw new global::System.ArgumentException("Peer network id is required.", nameof(peerNetworkIdShort));

            var existing = await FindChatByPeerNetworkIdAsync(userId, idShort).ConfigureAwait(false);
            var preferredNick = PreferPeerNickname(peerNickname, idShort);
            if (existing != null)
            {
                var mergedHost = PeerHostList.MergeAppend(existing.PeerHost, peerHost);
                var mergedEndpoints = MergePeerEndpoints(existing, peerHost, peerPort);
                var newPub = peerRsaPublicJson?.Trim();
                var pubChanged = !string.IsNullOrEmpty(newPub) &&
                                 !SafetyNumber.PublicKeyJsonEquals(existing.PeerRsaPublicJson, newPub);
                var nickChanged = ShouldReplacePeerNickname(existing.PeerNickname, preferredNick, idShort);
                var sourceChanged = keySource.HasValue &&
                                    (!string.Equals(existing.PeerKeySourceKind, keySource.Value.Kind,
                                         StringComparison.Ordinal) ||
                                     !string.Equals(existing.PeerKeySourceDetail ?? "",
                                         keySource.Value.Detail ?? "", StringComparison.Ordinal));
                var changed =
                    !string.Equals(mergedHost, existing.PeerHost, StringComparison.Ordinal) ||
                    existing.PeerPort != peerPort ||
                    !string.Equals(mergedEndpoints, existing.PeerEndpointsJson ?? "", StringComparison.Ordinal) ||
                    pubChanged ||
                    nickChanged ||
                    (pubChanged && sourceChanged) ||
                    (existing.PeerKeySourceKind is null or "" && keySource.HasValue) ||
                    !string.IsNullOrEmpty(existing.RelayRouteBlob);

                if (!changed)
                    return existing;

                await UpdateChatP2pRouteAsync(existing.Id, mergedHost, peerPort, null, peerRsaPublicJson,
                        pubChanged || string.IsNullOrWhiteSpace(existing.PeerKeySourceKind) ? keySource : null)
                    .ConfigureAwait(false);
                existing.PeerHost = mergedHost;
                existing.PeerPort = peerPort;
                existing.PeerEndpointsJson = mergedEndpoints;
                if (newPub != null && newPub.Length > 0)
                    existing.PeerRsaPublicJson = newPub;
                if (nickChanged)
                    existing.PeerNickname = preferredNick;
                if (keySource.HasValue &&
                    (pubChanged || string.IsNullOrWhiteSpace(existing.PeerKeySourceKind)))
                {
                    existing.PeerKeySourceKind = keySource.Value.Kind;
                    existing.PeerKeySourceDetail = keySource.Value.Detail;
                }

                existing.RelayRouteBlob = null;
                existing.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
                if (nickChanged)
                {
                    var connUpdate = await _db.GetConnectionAsync().ConfigureAwait(false);
                    await connUpdate.UpdateAsync(existing).ConfigureAwait(false);
                }

                await NotifyChatListChangedUnlessBlockedAsync(userId, idShort).ConfigureAwait(false);
                return existing;
            }

            var conn = await _db.GetConnectionAsync();
            var chat = new ChatEntity
            {
                UserId = userId,
                PeerNickname = preferredNick,
                PeerNetworkIdShort = idShort,
                PeerRsaPublicJson = peerRsaPublicJson.Trim(),
                PeerHost = peerHost.Trim(),
                PeerPort = peerPort,
                PeerEndpointsJson = MergePeerEndpoints(null, peerHost, peerPort),
                PeerKeySourceKind = keySource?.Kind,
                PeerKeySourceDetail = keySource?.Detail,
                UpdatedUtcTicks = DateTime.UtcNow.Ticks
            };
            await conn.InsertAsync(chat);
            if (!await IsPeerBlockedAsync(userId, idShort).ConfigureAwait(false))
            {
                NotifyChatListChanged();
                RaiseEvent(ChatCreated, new ChatCreatedEventArgs(chat.Id, remote));
            }

            return chat;
        }
        finally
        {
            _addChatGate.Release();
        }
    }

    public async Task<ChatEntity?> FindChatByPeerNetworkIdAsync(int userId, string peerNetworkIdShort)
    {
        var id = CanonicalPeerNetworkId(peerNetworkIdShort);
        if (id.Length == 0)
            return null;

        var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
        var list = await conn.Table<ChatEntity>()
            .Where(c => c.UserId == userId)
            .ToListAsync()
            .ConfigureAwait(false);
        var matches = list.Where(c => PeerNetworkIdsEqual(c.PeerNetworkIdShort, id)).ToList();
        if (matches.Count == 0)
            return null;
        if (matches.Count == 1)
        {
            var only = matches[0];
            if (!string.Equals(only.PeerNetworkIdShort, id, StringComparison.Ordinal))
            {
                only.PeerNetworkIdShort = id;
                only.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
                await conn.UpdateAsync(only).ConfigureAwait(false);
            }

            return only;
        }

        var merged = await DeduplicateChatsInDbAsync(conn, matches, notify: true).ConfigureAwait(false);
        return merged.OrderByDescending(c => c.UpdatedUtcTicks).FirstOrDefault();
    }

    /// <summary>Canonical base64url network id used as the chat list key.</summary>
    public static string CanonicalPeerNetworkId(string? peerNetworkIdShort)
    {
        var raw = peerNetworkIdShort?.Trim() ?? "";
        if (raw.Length == 0)
            return "";
        return CompressedNetworkId.TryParseShortString(raw, out var parsed) && !parsed.IsEmpty
            ? parsed.ToShortString()
            : raw;
    }

    public static bool PeerNetworkIdsEqual(string? a, string? b)
    {
        var ca = CanonicalPeerNetworkId(a);
        var cb = CanonicalPeerNetworkId(b);
        if (ca.Length == 0 || cb.Length == 0)
            return string.Equals(a?.Trim(), b?.Trim(), StringComparison.Ordinal);
        return string.Equals(ca, cb, StringComparison.Ordinal);
    }

    private static string CanonicalPeerNetworkIdKey(ChatEntity chat) =>
        CanonicalPeerNetworkId(chat.PeerNetworkIdShort);

    /// <summary>
    /// Merges chats that represent the same peer into one DB row (keeps the row with the most messages).
    /// </summary>
    private async Task<List<ChatEntity>> DeduplicateChatsInDbAsync(SQLiteAsyncConnection conn,
        List<ChatEntity> rows, bool notify)
    {
        if (rows.Count <= 1)
        {
            if (rows.Count == 1)
            {
                var single = rows[0];
                var canonical = CanonicalPeerNetworkId(single.PeerNetworkIdShort);
                if (canonical.Length > 0 &&
                    !string.Equals(single.PeerNetworkIdShort, canonical, StringComparison.Ordinal))
                {
                    single.PeerNetworkIdShort = canonical;
                    single.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
                    await conn.UpdateAsync(single).ConfigureAwait(false);
                }
            }

            return rows;
        }

        var result = new List<ChatEntity>(rows.Count);
        var changed = false;
        foreach (var group in rows.GroupBy(CanonicalPeerNetworkIdKey, StringComparer.Ordinal))
        {
            var members = group.ToList();
            if (members.Count == 1)
            {
                var single = members[0];
                var canonical = group.Key;
                if (canonical.Length > 0 &&
                    !string.Equals(single.PeerNetworkIdShort, canonical, StringComparison.Ordinal))
                {
                    single.PeerNetworkIdShort = canonical;
                    single.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
                    await conn.UpdateAsync(single).ConfigureAwait(false);
                    changed = true;
                }

                result.Add(single);
                continue;
            }

            var keeper = await PickChatToKeepAsync(conn, members).ConfigureAwait(false);
            var canonicalId = group.Key.Length > 0 ? group.Key : CanonicalPeerNetworkId(keeper.PeerNetworkIdShort);
            foreach (var orphan in members.Where(c => c.Id != keeper.Id))
            {
                await conn.ExecuteAsync("UPDATE messages SET ChatId = ? WHERE ChatId = ?", keeper.Id, orphan.Id)
                    .ConfigureAwait(false);
                keeper.PeerHost = PeerHostList.MergeAppend(keeper.PeerHost, orphan.PeerHost);
                if (orphan.PeerPort > 0)
                    keeper.PeerPort = orphan.PeerPort;
                keeper.PeerEndpointsJson = MergePeerEndpointsJson(keeper.PeerEndpointsJson, orphan.PeerEndpointsJson);
                if (!string.IsNullOrWhiteSpace(orphan.PeerRsaPublicJson) &&
                    (string.IsNullOrWhiteSpace(keeper.PeerRsaPublicJson) ||
                     orphan.UpdatedUtcTicks >= keeper.UpdatedUtcTicks))
                {
                    keeper.PeerRsaPublicJson = orphan.PeerRsaPublicJson;
                    if (!string.IsNullOrWhiteSpace(orphan.PeerKeySourceKind))
                    {
                        keeper.PeerKeySourceKind = orphan.PeerKeySourceKind;
                        keeper.PeerKeySourceDetail = orphan.PeerKeySourceDetail;
                    }
                }

                if (string.IsNullOrWhiteSpace(keeper.PeerKeySourceKind) &&
                    !string.IsNullOrWhiteSpace(orphan.PeerKeySourceKind))
                {
                    keeper.PeerKeySourceKind = orphan.PeerKeySourceKind;
                    keeper.PeerKeySourceDetail = orphan.PeerKeySourceDetail;
                }
                if (ShouldReplacePeerNickname(keeper.PeerNickname, orphan.PeerNickname, canonicalId))
                    keeper.PeerNickname = PreferPeerNickname(orphan.PeerNickname, canonicalId);
                if (string.IsNullOrWhiteSpace(keeper.RelayRouteBlob) &&
                    !string.IsNullOrWhiteSpace(orphan.RelayRouteBlob))
                    keeper.RelayRouteBlob = orphan.RelayRouteBlob;
                await conn.DeleteAsync(orphan).ConfigureAwait(false);
                changed = true;
            }

            if (!string.Equals(keeper.PeerNetworkIdShort, canonicalId, StringComparison.Ordinal) &&
                canonicalId.Length > 0)
                keeper.PeerNetworkIdShort = canonicalId;
            keeper.UpdatedUtcTicks = Math.Max(keeper.UpdatedUtcTicks,
                members.Max(c => c.UpdatedUtcTicks));
            await conn.UpdateAsync(keeper).ConfigureAwait(false);
            result.Add(keeper);
            changed = true;
        }

        if (changed && notify)
            NotifyChatListChanged();
        return result;
    }

    private static async Task<ChatEntity> PickChatToKeepAsync(SQLiteAsyncConnection conn, List<ChatEntity> members)
    {
        ChatEntity? best = null;
        var bestCount = -1;
        foreach (var chat in members.OrderBy(c => c.Id))
        {
            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM messages WHERE ChatId = ?", chat.Id).ConfigureAwait(false);
            if (best == null || count > bestCount ||
                (count == bestCount && chat.UpdatedUtcTicks > best.UpdatedUtcTicks))
            {
                best = chat;
                bestCount = count;
            }
        }

        return best!;
    }

    private static string MergePeerEndpointsJson(string? a, string? b)
    {
        var merged = new List<TransportAddress>();
        if (!string.IsNullOrWhiteSpace(a))
            merged.AddRange(PeerTransportEndpoints.Parse(new ChatEntity { PeerEndpointsJson = a }));
        if (!string.IsNullOrWhiteSpace(b))
            merged.AddRange(PeerTransportEndpoints.Parse(new ChatEntity { PeerEndpointsJson = b }));
        var dedup = new Dictionary<string, TransportAddress>(StringComparer.Ordinal);
        foreach (var x in merged)
            dedup[$"{(int)x.Kind}:{Convert.ToBase64String(x.Data)}"] = x;
        return PeerTransportEndpoints.Serialize(dedup.Values);
    }

    /// <returns>True if the stored MAC / endpoints actually changed.</returns>
    public async Task<bool> ReplaceChatBluetoothMacAsync(int chatId, string mac)
    {
        if (!BluetoothTransportAddress.TryParseMac(mac, out var macBytes))
            return false;

        var conn = await _db.GetConnectionAsync();
        var chat = await conn.FindAsync<ChatEntity>(chatId);
        if (chat == null)
            return false;

        var newHost = PeerHostList.ReplaceBluetoothMac(chat.PeerHost, mac);
        var btEndpoint = BluetoothTransportAddress.FromMac(macBytes);
        var newEndpointsJson = PeerTransportEndpoints.ReplaceBluetooth(PeerTransportEndpoints.Parse(chat), btEndpoint);

        if (string.Equals(newHost, chat.PeerHost, StringComparison.Ordinal)
            && string.Equals(newEndpointsJson, chat.PeerEndpointsJson, StringComparison.Ordinal))
            return false;

        chat.PeerHost = newHost;
        chat.PeerEndpointsJson = newEndpointsJson;
        chat.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
        await conn.UpdateAsync(chat);
        return true;
    }

    public async Task UpdateChatP2pRouteAsync(int chatId, string peerHost, int peerPort, string? relayRouteBlob,
        string? peerRsaPublicJson = null, PeerKeySource? keySource = null)
    {
        var conn = await _db.GetConnectionAsync();
        var chat = await conn.FindAsync<ChatEntity>(chatId);
        if (chat == null) return;
        chat.PeerHost = peerHost.Trim();
        chat.PeerPort = peerPort;
        chat.PeerEndpointsJson = MergePeerEndpoints(chat, peerHost, peerPort);
        chat.RelayRouteBlob = string.IsNullOrWhiteSpace(relayRouteBlob) ? null : relayRouteBlob.Trim();
        if (peerRsaPublicJson != null)
        {
            var trimmed = peerRsaPublicJson.Trim();
            if (trimmed.Length > 0)
            {
                var previous = chat.PeerRsaPublicJson;
                var pubChanged = !SafetyNumber.PublicKeyJsonEquals(previous, trimmed);
                chat.PeerRsaPublicJson = trimmed;
                if (keySource.HasValue &&
                    (pubChanged || string.IsNullOrWhiteSpace(chat.PeerKeySourceKind)))
                {
                    chat.PeerKeySourceKind = keySource.Value.Kind;
                    chat.PeerKeySourceDetail = keySource.Value.Detail;
                }

                if (pubChanged && !string.IsNullOrWhiteSpace(previous))
                    RaisePeerPublicKeyChanged(chat, previous, trimmed);
            }
        }
        else if (keySource.HasValue && string.IsNullOrWhiteSpace(chat.PeerKeySourceKind))
        {
            chat.PeerKeySourceKind = keySource.Value.Kind;
            chat.PeerKeySourceDetail = keySource.Value.Detail;
        }

        chat.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
        await conn.UpdateAsync(chat);
    }

    private void RaisePeerPublicKeyChanged(ChatEntity chat, string previousJson, string newJson)
    {
        RaiseEvent(PeerPublicKeyChanged, new PeerPublicKeyChangedEventArgs
        {
            ChatId = chat.Id,
            PeerNickname = chat.PeerNickname,
            PeerNetworkIdShort = chat.PeerNetworkIdShort,
            PreviousSafetyNumber = SafetyNumber.FromPublicKeyJsonOrEmpty(previousJson),
            NewSafetyNumber = SafetyNumber.FromPublicKeyJsonOrEmpty(newJson)
        });
    }

    /// <summary>
    /// Обновляет ник пира, если пришёл реальный ник (не пустой и не совпадающий с network id),
    /// а в БД сейчас пусто / network id / другой устаревший placeholder.
    /// </summary>
    public async Task<bool> TryUpdatePeerNicknameAsync(int chatId, string? peerNickname)
    {
        var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
        var chat = await conn.FindAsync<ChatEntity>(chatId).ConfigureAwait(false);
        if (chat == null)
            return false;

        var preferred = PreferPeerNickname(peerNickname, chat.PeerNetworkIdShort);
        if (!ShouldReplacePeerNickname(chat.PeerNickname, preferred, chat.PeerNetworkIdShort))
            return false;

        chat.PeerNickname = preferred;
        chat.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
        await conn.UpdateAsync(chat).ConfigureAwait(false);
        await NotifyChatListChangedUnlessBlockedAsync(chat.UserId, chat.PeerNetworkIdShort).ConfigureAwait(false);
        return true;
    }

    public static bool IsPlaceholderNickname(string? nickname, string networkIdShort)
    {
        var nick = nickname?.Trim() ?? "";
        var id = networkIdShort?.Trim() ?? "";
        return nick.Length == 0 ||
               (id.Length > 0 && string.Equals(nick, id, StringComparison.OrdinalIgnoreCase));
    }

    private static string PreferPeerNickname(string? peerNickname, string networkIdShort)
    {
        var nick = peerNickname?.Trim() ?? "";
        var id = networkIdShort.Trim();
        if (nick.Length == 0)
            return id;
        return nick;
    }

    private static bool ShouldReplacePeerNickname(string? currentNickname, string incomingNickname, string networkIdShort)
    {
        var incoming = PreferPeerNickname(incomingNickname, networkIdShort);
        var current = currentNickname?.Trim() ?? "";
        if (string.Equals(current, incoming, StringComparison.Ordinal))
            return false;

        var incomingIsPlaceholder = IsPlaceholderNickname(incoming, networkIdShort);
        var currentIsPlaceholder = IsPlaceholderNickname(current, networkIdShort);
        if (incomingIsPlaceholder)
            return current.Length == 0;
        return currentIsPlaceholder || !string.Equals(current, incoming, StringComparison.Ordinal);
    }

    private static string MergePeerEndpoints(ChatEntity? existing, string peerHost, int peerPort)
    {
        var merged = new List<TransportAddress>();
        if (existing != null)
            merged.AddRange(PeerTransportEndpoints.Parse(existing));
        foreach (var host in (peerHost ?? string.Empty).Split([',', ';', '|', ' ', '\n', '\r', '\t'],
#if NET5_0_OR_GREATER
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
#else
                     StringSplitOptions.RemoveEmptyEntries))
#endif
            if (IPAddress.TryParse(host, out var ip) && peerPort is >= 1 and <= 65535)
                merged.Add(UdpTransportAddress.FromIPEndPoint(new IPEndPoint(ip, peerPort)));
            else if (BluetoothTransportAddress.TryParseMac(host, out var mac))
                merged.Add(BluetoothTransportAddress.FromMac(mac));

        var dedup = new Dictionary<string, TransportAddress>(StringComparer.Ordinal);
        foreach (var x in merged)
            dedup[$"{(int)x.Kind}:{Convert.ToBase64String(x.Data)}"] = x;
        return PeerTransportEndpoints.Serialize(dedup.Values);
    }

    public async Task<IReadOnlyList<ChatMessageEntity>> ListMessagesAsync(int chatId)
    {
        var conn = await _db.GetConnectionAsync();
        return await conn.Table<ChatMessageEntity>()
            .Where(m => m.ChatId == chatId)
            .OrderBy(m => m.SentUtcTicks)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<ChatMessageEntity>> ListMessagesPageDescAsync(int chatId, int offset, int limit)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit));

        var conn = await _db.GetConnectionAsync();
        return await conn.Table<ChatMessageEntity>()
            .Where(m => m.ChatId == chatId)
            .OrderByDescending(m => m.SentUtcTicks)
            .ThenByDescending(m => m.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> AddMessageAsync(int chatId, bool outgoing, string text,
        MessageDeliveryStatus deliveryStatus = MessageDeliveryStatus.Delivered)
    {
        var conn = await _db.GetConnectionAsync();
        var status = outgoing
            ? deliveryStatus
            : MessageDeliveryStatus.NotApplicable;
        var msg = new ChatMessageEntity
        {
            ChatId = chatId,
            Outgoing = outgoing,
            Text = text,
            SentUtcTicks = DateTime.UtcNow.Ticks,
            DeliveryStatus = (int)status,
            PayloadKind = (int)ChatPayloadKind.Text,
            MimeType = "",
            ImageBlob = null,
            TransferId = "",
            TransferToken = "",
            TransferPayloadKind = "",
            TransferFileName = "",
            TransferSizeBytes = 0,
            TransferHost = "",
            TransferPort = 0,
            TransferExpiresUtcTicks = 0,
            TransferState = (int)ChatTransferState.None
        };
        await conn.InsertAsync(msg);

        var chat = await conn.FindAsync<ChatEntity>(chatId);
        if (chat != null)
        {
            chat.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
            await conn.UpdateAsync(chat);
        }

        await RaiseChatMessageAppendedAsync(chatId, outgoing, chat).ConfigureAwait(false);
        return msg.Id;
    }

    public async Task<int> AddImageMessageAsync(int chatId, bool outgoing, string mimeType, byte[] imageBytes,
        MessageDeliveryStatus deliveryStatus = MessageDeliveryStatus.Delivered)
    {
        Require.NotNull(imageBytes);
        var conn = await _db.GetConnectionAsync();
        var status = outgoing
            ? deliveryStatus
            : MessageDeliveryStatus.NotApplicable;
        var msg = new ChatMessageEntity
        {
            ChatId = chatId,
            Outgoing = outgoing,
            Text = "",
            SentUtcTicks = DateTime.UtcNow.Ticks,
            DeliveryStatus = (int)status,
            PayloadKind = (int)ChatPayloadKind.Image,
            MimeType = mimeType.Trim(),
            ImageBlob = imageBytes,
            TransferId = "",
            TransferToken = "",
            TransferPayloadKind = "",
            TransferFileName = "",
            TransferSizeBytes = 0,
            TransferHost = "",
            TransferPort = 0,
            TransferExpiresUtcTicks = 0,
            TransferState = (int)ChatTransferState.None
        };
        await conn.InsertAsync(msg);

        var chat = await conn.FindAsync<ChatEntity>(chatId);
        if (chat != null)
        {
            chat.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
            await conn.UpdateAsync(chat);
        }

        await RaiseChatMessageAppendedAsync(chatId, outgoing, chat).ConfigureAwait(false);
        return msg.Id;
    }

    public async Task<int> AddFileMessageAsync(int chatId, bool outgoing, string fileName, string mimeType,
        byte[] fileBytes, MessageDeliveryStatus deliveryStatus = MessageDeliveryStatus.Delivered)
    {
        Require.NotNull(fileBytes);
        var conn = await _db.GetConnectionAsync();
        var status = outgoing
            ? deliveryStatus
            : MessageDeliveryStatus.NotApplicable;
        var msg = new ChatMessageEntity
        {
            ChatId = chatId,
            Outgoing = outgoing,
            Text = fileName.Trim(),
            SentUtcTicks = DateTime.UtcNow.Ticks,
            DeliveryStatus = (int)status,
            PayloadKind = (int)ChatPayloadKind.File,
            MimeType = mimeType.Trim(),
            ImageBlob = fileBytes,
            TransferId = "",
            TransferToken = "",
            TransferPayloadKind = "",
            TransferFileName = "",
            TransferSizeBytes = 0,
            TransferHost = "",
            TransferPort = 0,
            TransferExpiresUtcTicks = 0,
            TransferState = (int)ChatTransferState.None
        };
        await conn.InsertAsync(msg);

        var chat = await conn.FindAsync<ChatEntity>(chatId);
        if (chat != null)
        {
            chat.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
            await conn.UpdateAsync(chat);
        }

        await RaiseChatMessageAppendedAsync(chatId, outgoing, chat).ConfigureAwait(false);
        return msg.Id;
    }

    public async Task<ChatMessageEntity?> GetMessageAsync(int messageId)
    {
        var conn = await _db.GetConnectionAsync();
        return await conn.FindAsync<ChatMessageEntity>(messageId);
    }

    public async Task UpdateMessageDeliveryStatusAsync(int messageId, MessageDeliveryStatus status)
    {
        var conn = await _db.GetConnectionAsync();
        var m = await conn.FindAsync<ChatMessageEntity>(messageId);
        if (m == null)
            return;
        m.DeliveryStatus = (int)status;
        await conn.UpdateAsync(m);
    }

    public async Task UpdateTransferStateAsync(int messageId, ChatTransferState state)
    {
        var conn = await _db.GetConnectionAsync();
        var m = await conn.FindAsync<ChatMessageEntity>(messageId);
        if (m == null)
            return;
        m.TransferState = (int)state;
        await conn.UpdateAsync(m);
    }

    public async Task UpdateMessageTransferMetadataAsync(int messageId, string transferId, string transferToken,
        string transferPayloadKind, string transferFileName, long transferSizeBytes, string transferHost,
        int transferPort,
        long transferExpiresUtcTicks, ChatTransferState transferState)
    {
        var conn = await _db.GetConnectionAsync();
        var m = await conn.FindAsync<ChatMessageEntity>(messageId);
        if (m == null)
            return;
        m.TransferId = transferId?.Trim() ?? "";
        m.TransferToken = transferToken?.Trim() ?? "";
        m.TransferPayloadKind = transferPayloadKind?.Trim() ?? "";
        m.TransferFileName = transferFileName?.Trim() ?? "";
        m.TransferSizeBytes = Math.Max(0, transferSizeBytes);
        m.TransferHost = transferHost?.Trim() ?? "";
        m.TransferPort = transferPort;
        m.TransferExpiresUtcTicks = transferExpiresUtcTicks;
        m.TransferState = (int)transferState;
        await conn.UpdateAsync(m);
    }

    public async Task UpdateMessagePayloadAsync(int messageId, ChatPayloadKind payloadKind, string text,
        string mimeType,
        byte[] payloadBytes)
    {
        var conn = await _db.GetConnectionAsync();
        var m = await conn.FindAsync<ChatMessageEntity>(messageId);
        if (m == null)
            return;
        m.PayloadKind = (int)payloadKind;
        m.Text = text ?? "";
        m.MimeType = mimeType ?? "";
        m.ImageBlob = payloadBytes;
        await conn.UpdateAsync(m);
    }

    /// <summary>
    ///     Удаляет все сообщения чата локально. Чат остаётся. Возвращает false, если чат не найден или не принадлежит
    ///     userId.
    /// </summary>
    public async Task<bool> ClearMessagesAsync(int chatId, int userId, CancellationToken cancellationToken = default)
    {
        var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
        var chat = await conn.FindAsync<ChatEntity>(chatId).ConfigureAwait(false);
        if (chat == null || chat.UserId != userId)
            return false;

        await conn.ExecuteAsync("DELETE FROM messages WHERE ChatId = ?", chatId).ConfigureAwait(false);
        chat.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
        await conn.UpdateAsync(chat).ConfigureAwait(false);
        return true;
    }

    /// <summary>Удаляет чат и все его сообщения локально. Возвращает false, если чат не найден или не принадлежит userId.</summary>
    public async Task<bool> DeleteChatAsync(int chatId, int userId, CancellationToken cancellationToken = default)
    {
        var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
        var chat = await conn.FindAsync<ChatEntity>(chatId).ConfigureAwait(false);
        if (chat == null || chat.UserId != userId)
            return false;

        await conn.ExecuteAsync("DELETE FROM messages WHERE ChatId = ?", chatId).ConfigureAwait(false);
        await conn.DeleteAsync(chat).ConfigureAwait(false);
        NotifyChatListChanged();
        return true;
    }
}