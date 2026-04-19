using ShortP2P.Client.Data;
using ShortP2P.Client;

namespace ShortP2P.Client.Services;

public sealed class ChatMessageAppendedEventArgs(int chatId, bool outgoing) : EventArgs
{
    public int ChatId { get; } = chatId;
    public bool Outgoing { get; } = outgoing;
}

public sealed class ChatRepository
{
    private readonly AppDatabase _db;
    private readonly SemaphoreSlim _addChatGate = new(1, 1);

    public ChatRepository(AppDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>Список чатов на главном экране: обновить после входящего приглашения и т.п.</summary>
    public event EventHandler? ChatListChanged;

    /// <summary>Новое сообщение записано в БД (входящее или исходящее).</summary>
    public event EventHandler<ChatMessageAppendedEventArgs>? ChatMessageAppended;

    public void NotifyChatListChanged() => ChatListChanged?.Invoke(this, EventArgs.Empty);

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
        string peerRsaPublicJson, string peerHost, int peerPort)
    {
        await _addChatGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var existing = await FindChatByPeerNetworkIdAsync(userId, peerNetworkIdShort).ConfigureAwait(false);
            if (existing != null)
            {
                var mergedHost = PeerHostList.MergeAppend(existing.PeerHost, peerHost);
                await UpdateChatP2pRouteAsync(existing.Id, mergedHost, peerPort, relayRouteBlob: null, peerRsaPublicJson)
                    .ConfigureAwait(false);
                existing.PeerHost = mergedHost;
                existing.PeerPort = peerPort;
                if (peerRsaPublicJson != null)
                    existing.PeerRsaPublicJson = peerRsaPublicJson.Trim();
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
                UpdatedUtcTicks = DateTime.UtcNow.Ticks,
            };
            await conn.InsertAsync(chat);
            NotifyChatListChanged();
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

    public async Task UpdateChatP2pRouteAsync(int chatId, string peerHost, int peerPort, string? relayRouteBlob,
        string? peerRsaPublicJson = null)
    {
        var conn = await _db.GetConnectionAsync();
        var chat = await conn.FindAsync<ChatEntity>(chatId);
        if (chat == null) return;
        chat.PeerHost = peerHost.Trim();
        chat.PeerPort = peerPort;
        chat.RelayRouteBlob = string.IsNullOrWhiteSpace(relayRouteBlob) ? null : relayRouteBlob.Trim();
        if (peerRsaPublicJson != null)
            chat.PeerRsaPublicJson = peerRsaPublicJson.Trim();
        chat.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
        await conn.UpdateAsync(chat);
    }

    public async Task<IReadOnlyList<ChatMessageEntity>> ListMessagesAsync(int chatId)
    {
        var conn = await _db.GetConnectionAsync();
        return await conn.Table<ChatMessageEntity>()
            .Where(m => m.ChatId == chatId)
            .OrderBy(m => m.SentUtcTicks)
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
