using ShortP2P.Client.Data;

namespace ShortP2P.Client.Services;

public sealed class ChatRepository
{
    private readonly AppDatabase _db;

    public ChatRepository(AppDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<ChatEntity>> ListChatsAsync(int userId)
    {
        var conn = await _db.GetConnectionAsync();
        return await conn.Table<ChatEntity>()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UpdatedUtcTicks)
            .ToListAsync();
    }

    public async Task<ChatEntity?> GetChatAsync(int chatId)
    {
        var conn = await _db.GetConnectionAsync();
        return await conn.FindAsync<ChatEntity>(chatId);
    }

    public async Task<ChatEntity> AddChatAsync(int userId, string peerNickname, string peerNetworkIdShort,
        string peerRsaPublicJson, string peerHost, int peerPort)
    {
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
        return chat;
    }

    public async Task<IReadOnlyList<ChatMessageEntity>> ListMessagesAsync(int chatId)
    {
        var conn = await _db.GetConnectionAsync();
        return await conn.Table<ChatMessageEntity>()
            .Where(m => m.ChatId == chatId)
            .OrderBy(m => m.SentUtcTicks)
            .ToListAsync();
    }

    public async Task AddMessageAsync(int chatId, bool outgoing, string text)
    {
        var conn = await _db.GetConnectionAsync();
        var msg = new ChatMessageEntity
        {
            ChatId = chatId,
            Outgoing = outgoing,
            Text = text,
            SentUtcTicks = DateTime.UtcNow.Ticks,
        };
        await conn.InsertAsync(msg);

        var chat = await conn.FindAsync<ChatEntity>(chatId);
        if (chat != null)
        {
            chat.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
            await conn.UpdateAsync(chat);
        }
    }
}
