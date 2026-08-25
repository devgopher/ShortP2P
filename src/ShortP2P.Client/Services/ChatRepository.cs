using System.Net;
using ShortP2P.Client.Data;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

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

public sealed class ChatRepository(AppDatabase db)
{
    private readonly SemaphoreSlim _addChatGate = new(1, 1);
    private readonly AppDatabase _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <summary>Список чатов на главном экране: обновить после входящего приглашения и т.п.</summary>
    public event EventHandler? ChatListChanged;

    /// <summary>Новое сообщение записано в БД (входящее или исходящее).</summary>
    public event EventHandler<ChatMessageAppendedEventArgs>? ChatMessageAppended;

    /// <summary>В БД вставлен новый чат (не обновление существующего).</summary>
    public event EventHandler<ChatCreatedEventArgs>? ChatCreated;

    public void NotifyChatListChanged()
    {
        ChatListChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<ChatEntity>> ListChatsAsync(int userId)
    {
        var conn = await _db.GetConnectionAsync();
        var rows = await conn.Table<ChatEntity>()
            .Where(c => c.UserId == userId)
            .ToListAsync();
        // Один пир на network id (защита от дубликатов после повторной доставки UDP / гонки).
        return rows
            .GroupBy(c => c.PeerNetworkIdShort.Trim(), StringComparer.Ordinal)
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
        string peerRsaPublicJson, string peerHost, int peerPort, bool remote = false)
    {
        await _addChatGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var existing = await FindChatByPeerNetworkIdAsync(userId, peerNetworkIdShort).ConfigureAwait(false);
            if (existing != null)
            {
                var mergedHost = PeerHostList.MergeAppend(existing.PeerHost, peerHost);
                var mergedEndpoints = MergePeerEndpoints(existing, peerHost, peerPort);
                var newPub = peerRsaPublicJson?.Trim();
                var pubChanged = newPub != null &&
                                 !string.Equals(existing.PeerRsaPublicJson, newPub, StringComparison.Ordinal);
                var changed =
                    !string.Equals(mergedHost, existing.PeerHost, StringComparison.Ordinal) ||
                    existing.PeerPort != peerPort ||
                    !string.Equals(mergedEndpoints, existing.PeerEndpointsJson ?? "", StringComparison.Ordinal) ||
                    pubChanged ||
                    !string.IsNullOrEmpty(existing.RelayRouteBlob);

                if (!changed)
                    return existing;

                await UpdateChatP2pRouteAsync(existing.Id, mergedHost, peerPort, null, peerRsaPublicJson)
                    .ConfigureAwait(false);
                existing.PeerHost = mergedHost;
                existing.PeerPort = peerPort;
                existing.PeerEndpointsJson = mergedEndpoints;
                if (newPub != null)
                    existing.PeerRsaPublicJson = newPub;
                existing.RelayRouteBlob = null;
                existing.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
                NotifyChatListChanged();
                return existing;
            }

            var conn = await _db.GetConnectionAsync();
            var chat = new ChatEntity
            {
                UserId = userId,
                PeerNickname = peerNickname.Trim(),
                PeerNetworkIdShort = peerNetworkIdShort.Trim(),
                PeerRsaPublicJson = peerRsaPublicJson.Trim(),
                PeerHost = peerHost.Trim(),
                PeerPort = peerPort,
                PeerEndpointsJson = MergePeerEndpoints(null, peerHost, peerPort),
                UpdatedUtcTicks = DateTime.UtcNow.Ticks
            };
            await conn.InsertAsync(chat);
            NotifyChatListChanged();
            ChatCreated?.Invoke(this, new ChatCreatedEventArgs(chat.Id, remote));
            return chat;
        }
        finally
        {
            _addChatGate.Release();
        }
    }

    public async Task<ChatEntity?> FindChatByPeerNetworkIdAsync(int userId, string peerNetworkIdShort)
    {
        var id = peerNetworkIdShort.Trim();
        var conn = await _db.GetConnectionAsync();
        var list = await conn.Table<ChatEntity>()
            .Where(c => c.UserId == userId && c.PeerNetworkIdShort == id)
            .ToListAsync();
        return list.OrderByDescending(c => c.UpdatedUtcTicks).FirstOrDefault();
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
        string? peerRsaPublicJson = null)
    {
        var conn = await _db.GetConnectionAsync();
        var chat = await conn.FindAsync<ChatEntity>(chatId);
        if (chat == null) return;
        chat.PeerHost = peerHost.Trim();
        chat.PeerPort = peerPort;
        chat.PeerEndpointsJson = MergePeerEndpoints(chat, peerHost, peerPort);
        chat.RelayRouteBlob = string.IsNullOrWhiteSpace(relayRouteBlob) ? null : relayRouteBlob.Trim();
        if (peerRsaPublicJson != null)
            chat.PeerRsaPublicJson = peerRsaPublicJson.Trim();
        chat.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
        await conn.UpdateAsync(chat);
    }

    private static string MergePeerEndpoints(ChatEntity? existing, string peerHost, int peerPort)
    {
        var merged = new List<TransportAddress>();
        if (existing != null)
            merged.AddRange(PeerTransportEndpoints.Parse(existing));
        foreach (var host in (peerHost ?? string.Empty).Split([',', ';', '|', ' ', '\n', '\r', '\t'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, 0);

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

        ChatMessageAppended?.Invoke(this, new ChatMessageAppendedEventArgs(chatId, outgoing));
        return msg.Id;
    }

    public async Task<int> AddImageMessageAsync(int chatId, bool outgoing, string mimeType, byte[] imageBytes,
        MessageDeliveryStatus deliveryStatus = MessageDeliveryStatus.Delivered)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
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

        ChatMessageAppended?.Invoke(this, new ChatMessageAppendedEventArgs(chatId, outgoing));
        return msg.Id;
    }

    public async Task<int> AddFileMessageAsync(int chatId, bool outgoing, string fileName, string mimeType,
        byte[] fileBytes, MessageDeliveryStatus deliveryStatus = MessageDeliveryStatus.Delivered)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);
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

        ChatMessageAppended?.Invoke(this, new ChatMessageAppendedEventArgs(chatId, outgoing));
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