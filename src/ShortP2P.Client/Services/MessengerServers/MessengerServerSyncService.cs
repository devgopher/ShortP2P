using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShortP2P.Auth;
using ShortP2P.Auth.Data;
using ShortP2P.Client.ChatMedia;
using ShortP2P.Client.Data;
using ShortP2P.Crypto;
using ShortP2P.MessengerServer.Contracts.Dtos;

namespace ShortP2P.Client.Services.MessengerServers;

/// <summary>
/// Background long-poll inbox for messenger servers, plus outbound server-first message delivery.
/// </summary>
public sealed class MessengerServerSyncService : IAsyncDisposable
{
    /// <summary>Default long-poll wait requested from the server (seconds).</summary>
    public const int LongPollTimeoutSeconds = 25;

    /// <summary>Max concurrent long-poll workers (one server per worker).</summary>
    public const int MaxLongPollWorkers = 3;

    private readonly AuthService _auth;
    private readonly ChatRepository _chats;
    private readonly ChatSessionCache _sessions;
    private readonly MessengerServerManager _manager;
    private readonly ILogger<MessengerServerSyncService> _logger;
    private readonly object _startGate = new();
    private readonly SemaphoreSlim _ingestGate = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _longPollLoop;
    private bool _started;

    public MessengerServerSyncService(
        AuthService auth,
        ChatRepository chats,
        ChatSessionCache sessions,
        MessengerServerManager manager,
        ILogger<MessengerServerSyncService>? logger = null)
    {
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _chats = chats ?? throw new ArgumentNullException(nameof(chats));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _logger = logger ?? NullLogger<MessengerServerSyncService>.Instance;
    }

    public MessengerServerManager Manager => _manager;

    public void Start()
    {
        lock (_startGate)
        {
            if (_started)
                return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _longPollLoop = Task.Run(() => LongPollSupervisorAsync(token), token);
            _started = true;
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? longPoll;
        lock (_startGate)
        {
            if (!_started)
                return;
            _started = false;
            cts = _cts;
            longPoll = _longPollLoop;
            _cts = null;
            _longPollLoop = null;
        }

        if (cts != null)
        {
            try
            {
                await cts.CancelAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        await WaitIgnoreCancelAsync(longPoll).ConfigureAwait(false);
        cts?.Dispose();
    }

    /// <summary>Discovery priority hook: auth + GetClients before UDP/BT.</summary>
    public Task RunDiscoveryRoundAsync(CancellationToken cancellationToken) =>
        ProbeAndListRemoteClientsAsync(cancellationToken);

    public async Task<IReadOnlyList<ClientPresenceDto>> ProbeAndListRemoteClientsAsync(
        CancellationToken cancellationToken)
    {
        var user = _auth.CurrentUser;
        if (user == null)
            return [];

        var self = user.NetworkIdShort.Trim();
        var ready = await _manager.EnsureAllActiveReadyAsync(cancellationToken).ConfigureAwait(false);

        return await ListRemoteClientsAsync(ready, self, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Obsolete name kept for call sites; prefers GetClients probe.</summary>
    public Task<IReadOnlyList<ClientPresenceDto>> KeepAliveAndListRemoteClientsAsync(
        CancellationToken cancellationToken) =>
        ProbeAndListRemoteClientsAsync(cancellationToken);

    /// <summary>Publish (or refresh) our public key to the peer via all ready servers.</summary>
    public async Task PublishChatRequestAsync(string targetNetworkId, CancellationToken cancellationToken = default)
    {
        var user = _auth.CurrentUser;
        if (user == null)
            return;

        var publicKey = RsaKeySerializer.SerializePublic(_auth.GetCurrentPublicKey());
        var target = targetNetworkId.Trim();
        if (string.IsNullOrEmpty(target) ||
            string.Equals(target, user.NetworkIdShort.Trim(), StringComparison.Ordinal))
            return;

        var ready = await _manager.EnsureAllActiveReadyAsync(cancellationToken).ConfigureAwait(false);
        foreach (var conn in ready)
        {
            try
            {
                await TrackAsync(conn, () => conn.Api.CreateChatRequestAsync(
                    new ChatRequestCreateRequest
                    {
                        PublicKey = publicKey,
                        TargetNetworkId = target
                    },
                    cancellationToken)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "CreateChatRequest failed on {BaseUrl}", conn.Entity.BaseUrl);
            }
        }
    }

    /// <summary>
    /// Tries to deliver an already-built chat wire via active servers (E2EE envelope).
    /// Returns true if at least one server accepted the message.
    /// </summary>
    public async Task<bool> TryDeliverWireAsync(
        ChatEntity chat,
        UserEntity user,
        byte[] wire,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(wire);

        if (string.IsNullOrWhiteSpace(chat.PeerRsaPublicJson))
            return false;

        RsaPublicKey peerKey;
        try
        {
            peerKey = RsaKeySerializer.DeserializePublic(chat.PeerRsaPublicJson);
        }
        catch
        {
            return false;
        }

        string encrypted;
        try
        {
            encrypted = MessengerServerPayloadCodec.EncryptToBase64(wire, peerKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to encrypt server payload for chat {ChatId}", chat.Id);
            return false;
        }

        var ready = await _manager.EnsureAllActiveReadyAsync(cancellationToken).ConfigureAwait(false);
        if (ready.Count == 0)
            return false;

        var now = DateTime.UtcNow;
        var dto = new MessageDto
        {
            MessageId = Guid.NewGuid().ToString("N"),
            SrcNetworkId = user.NetworkIdShort.Trim(),
            TgtNetworkId = chat.PeerNetworkIdShort.Trim(),
            CreatedUtc = now,
            UpdatedUtc = now,
            EncryptedDataBase64 = encrypted
        };

        var any = false;
        foreach (var conn in ready)
        {
            try
            {
                await TrackAsync(conn, () => conn.Api.SendMessageAsync(dto, cancellationToken))
                    .ConfigureAwait(false);
                any = true;
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "SendMessage failed on {BaseUrl}", conn.Entity.BaseUrl);
            }
        }

        return any;
    }

    private async Task<IReadOnlyList<ClientPresenceDto>> ListRemoteClientsAsync(
        IReadOnlyList<MessengerServerConnection> ready,
        string selfNetworkId,
        CancellationToken cancellationToken)
    {
        var byId = new Dictionary<string, ClientPresenceDto>(StringComparer.Ordinal);
        foreach (var conn in ready)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ClientPresenceDto> list;
            var sw = Stopwatch.StartNew();
            try
            {
                list = await TrackAsync(conn, () => conn.Api.GetClientsAsync(cancellationToken))
                    .ConfigureAwait(false);
                sw.Stop();
                _manager.RecordProbeSuccess(conn.Entity.Id, sw.Elapsed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "GetClients failed on {BaseUrl}", conn.Entity.BaseUrl);
                continue;
            }

            foreach (var client in list)
            {
                var id = client.NetworkId.Trim();
                if (id.Length == 0 ||
                    string.Equals(id, selfNetworkId, StringComparison.Ordinal))
                    continue;

                if (!byId.TryGetValue(id, out var existing) || PreferClient(client, existing))
                    byId[id] = client;
            }
        }

        return byId.Values.ToArray();
    }

    private static bool PreferClient(ClientPresenceDto candidate, ClientPresenceDto existing)
    {
        var candidateOnline = IsOnline(candidate);
        var existingOnline = IsOnline(existing);
        if (candidateOnline != existingOnline)
            return candidateOnline;
        return candidate.LastSeenAtUtc > existing.LastSeenAtUtc;
    }

    private static bool IsOnline(ClientPresenceDto client) =>
        string.Equals(client.Status, "Online", StringComparison.OrdinalIgnoreCase);


    private async Task LongPollSupervisorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var user = _auth.CurrentUser;
                if (user == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var ready = await _manager.EnsureAllActiveReadyAsync(cancellationToken).ConfigureAwait(false);
                var available = _manager.FilterAvailable(ready);
                if (available.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var degree = Math.Clamp(available.Count, 1, MaxLongPollWorkers);
                var selected = available.Take(degree).ToArray();
                var workers = selected
                    .Select(conn => LongPollServerLoopAsync(conn, user, cancellationToken))
                    .ToArray();

                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Messenger server long-poll supervisor failed");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task LongPollServerLoopAsync(
        MessengerServerConnection connection,
        UserEntity user,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var inbox = await TrackAsync(
                        connection,
                        () => connection.Api.PollEventsAsync(LongPollTimeoutSeconds, cancellationToken))
                    .ConfigureAwait(false);
                sw.Stop();

                if (inbox.Messages.Count > 0 ||
                    inbox.ChatRequests.Count > 0 ||
                    sw.Elapsed < TimeSpan.FromSeconds(LongPollTimeoutSeconds - 2))
                {
                    _manager.RecordProbeSuccess(connection.Entity.Id, sw.Elapsed);
                }

                if (inbox.ChatRequests.Count > 0)
                    await ProcessChatRequestsAsync(connection, user, inbox.ChatRequests, cancellationToken)
                        .ConfigureAwait(false);

                if (inbox.Messages.Count > 0)
                    await ProcessIncomingMessagesAsync(connection, user, inbox.Messages, cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Long-poll failed on {BaseUrl}", connection.Entity.BaseUrl);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ProcessChatRequestsAsync(
        MessengerServerConnection connection,
        UserEntity user,
        IReadOnlyList<ChatRequestDto> requests,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            return;

        var ourPublic = RsaKeySerializer.SerializePublic(_auth.GetCurrentPublicKey());
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var peerId = request.NetworkId.Trim();
            if (string.IsNullOrEmpty(peerId) ||
                string.Equals(peerId, user.NetworkIdShort.Trim(), StringComparison.Ordinal))
                continue;

            var existing = await _chats.FindChatByPeerNetworkIdAsync(user.Id, peerId).ConfigureAwait(false);
            ChatEntity chat;
            if (existing == null ||
                !string.Equals(existing.PeerRsaPublicJson, request.PublicKey.Trim(), StringComparison.Ordinal))
            {
                chat = await _chats.AddChatAsync(
                    user.Id,
                    peerId,
                    peerId,
                    request.PublicKey,
                    peerId,
                    user.DataUdpPort).ConfigureAwait(false);
            }
            else
            {
                chat = existing;
            }

            try
            {
                await TrackAsync(connection, () => connection.Api.CreateChatRequestAsync(
                    new ChatRequestCreateRequest
                    {
                        PublicKey = ourPublic,
                        TargetNetworkId = peerId
                    },
                    cancellationToken)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Reply ChatRequest failed for peer {PeerId}", peerId);
            }

            if (existing == null)
            {
                _logger.LogInformation(
                    "Accepted server chat request from {PeerId} into local chat {ChatId}",
                    peerId,
                    chat.Id);
            }
        }
    }

    private async Task ProcessIncomingMessagesAsync(
        MessengerServerConnection connection,
        UserEntity user,
        IReadOnlyList<MessageDto> messages,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
            return;

        var privateKey = _auth.GetCurrentPrivateKey();
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(message.TgtNetworkId.Trim(), user.NetworkIdShort.Trim(), StringComparison.Ordinal))
                continue;

            byte[] wire;
            try
            {
                wire = MessengerServerPayloadCodec.DecryptFromBase64(message.EncryptedDataBase64, privateKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decrypt server message {MessageId}", message.MessageId);
                continue;
            }

            var peerId = message.SrcNetworkId.Trim();

            await _ingestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var chat = await _chats.FindChatByPeerNetworkIdAsync(user.Id, peerId).ConfigureAwait(false);
                if (chat == null)
                {
                    _logger.LogDebug(
                        "Skipping server message {MessageId}: no local chat for peer {PeerId}",
                        message.MessageId,
                        peerId);
                    continue;
                }

                if (_sessions.TryGetSession(chat.Id, out var session) && session != null)
                {
                    await session.IngestIncomingWireFromServerAsync(wire, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await IngestWireIntoRepositoryAsync(chat.Id, wire).ConfigureAwait(false);
                }
            }
            finally
            {
                _ingestGate.Release();
            }

            try
            {
                await TrackAsync(connection, () => connection.Api.SubmitDeliveryReceiptAsync(
                    new DeliveryReceiptRequest
                    {
                        MessageId = message.MessageId,
                        ReceivedAtUtc = DateTime.UtcNow
                    },
                    cancellationToken)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Delivery receipt failed for {MessageId}", message.MessageId);
            }
        }
    }

    private async Task TrackAsync(MessengerServerConnection connection, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            _manager.RecordRequestSuccess(connection.Entity.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _manager.RecordRequestFailure(connection.Entity.Id);
            throw;
        }
    }

    private async Task<T> TrackAsync<T>(MessengerServerConnection connection, Func<Task<T>> action)
    {
        try
        {
            var result = await action().ConfigureAwait(false);
            _manager.RecordRequestSuccess(connection.Entity.Id);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _manager.RecordRequestFailure(connection.Entity.Id);
            throw;
        }
    }

    private async Task IngestWireIntoRepositoryAsync(int chatId, byte[] wire)
    {
        if (ChatWireCodec.TryParse(wire, out var parsed) && parsed != null)
        {
            switch (parsed)
            {
                case ChatWireText t:
                    await _chats.AddMessageAsync(chatId, false, t.Text).ConfigureAwait(false);
                    return;
                case ChatWireImage img:
                    await _chats.AddImageMessageAsync(chatId, false, img.MimeType, img.ImageBytes)
                        .ConfigureAwait(false);
                    return;
                case ChatWireFile f:
                    await _chats.AddFileMessageAsync(chatId, false, f.FileName, f.MimeType, f.FileBytes)
                        .ConfigureAwait(false);
                    return;
                default:
                    await _chats.AddMessageAsync(chatId, false, "[Серверное сообщение: неподдерживаемый тип]")
                        .ConfigureAwait(false);
                    return;
            }
        }

        var text = Encoding.UTF8.GetString(wire);
        await _chats.AddMessageAsync(chatId, false, text).ConfigureAwait(false);
    }

    private static async Task WaitIgnoreCancelAsync(Task? task)
    {
        if (task == null)
            return;
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _ingestGate.Dispose();
    }
}
