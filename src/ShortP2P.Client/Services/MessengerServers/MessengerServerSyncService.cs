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
using ShortP2P.MessengerServer.Http;

namespace ShortP2P.Client.Services.MessengerServers;

/// <summary>
/// Background long-poll inbox for messenger servers, plus outbound server-first message delivery.
/// </summary>
public sealed class MessengerServerSyncService : IAsyncDisposable
{
    /// <summary>Default long-poll wait requested from the server (seconds).</summary>
    public const int LongPollTimeoutSeconds = 25;

    /// <summary>Max concurrent outbound SendMessage calls (and long-poll workers).</summary>
    public const int MaxLongPollWorkers = 3;

    /// <summary>Alias: max parallel fan-out when posting a message to joined servers.</summary>
    public const int MaxSendWorkers = MaxLongPollWorkers;

    private readonly AuthService _auth;
    private readonly ChatRepository _chats;
    private readonly ChatSessionCache _sessions;
    private readonly MessengerServerManager _manager;
    private readonly ILogger<MessengerServerSyncService> _logger;
    private readonly object _startGate = new();
    private readonly SemaphoreSlim _ingestGate = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _longPollLoop;
    private Task? _pingLoop;
    private Task? _ratingLoop;
    private bool _started;

    public MessengerServerSyncService(
        AuthService auth,
        ChatRepository chats,
        ChatSessionCache sessions,
        MessengerServerManager manager,
        ILogger<MessengerServerSyncService>? logger = null)
    {
        _auth = auth ?? throw new global::System.ArgumentNullException(nameof(auth));
        _chats = chats ?? throw new global::System.ArgumentNullException(nameof(chats));
        _sessions = sessions ?? throw new global::System.ArgumentNullException(nameof(sessions));
        _manager = manager ?? throw new global::System.ArgumentNullException(nameof(manager));
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
            _manager.FailoverCompleted -= OnFailoverCompleted;
            _manager.FailoverCompleted += OnFailoverCompleted;
            _longPollLoop = Task.Run(() => LongPollSupervisorAsync(token), token);
            _pingLoop = Task.Run(() => PingLoopAsync(token), token);
            _ratingLoop = Task.Run(() => TrustRatingLoopAsync(token), token);
            _started = true;
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? longPoll;
        Task? pingLoop;
        Task? ratingLoop;
        lock (_startGate)
        {
            if (!_started)
                return;
            _started = false;
            cts = _cts;
            longPoll = _longPollLoop;
            pingLoop = _pingLoop;
            ratingLoop = _ratingLoop;
            _cts = null;
            _longPollLoop = null;
            _pingLoop = null;
            _ratingLoop = null;
            _manager.FailoverCompleted -= OnFailoverCompleted;
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
        await WaitIgnoreCancelAsync(pingLoop).ConfigureAwait(false);
        await WaitIgnoreCancelAsync(ratingLoop).ConfigureAwait(false);
        cts?.Dispose();
    }

    /// <summary>Discovery priority hook: auth + GetClients before UDP/BT.</summary>
    public Task RunDiscoveryRoundAsync(CancellationToken cancellationToken) =>
        ProbeAndListRemoteClientsAsync(cancellationToken);

    private async Task PingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _manager.PingActiveTrustedServersAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Messenger server ping round failed");
            }

            try
            {
                await Task.Delay(MessengerServerManager.PingPeriod, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task TrustRatingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _manager.RefreshPeerTrustRatingsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Messenger server trust-rating round failed");
            }

            try
            {
                await Task.Delay(MessengerServerManager.RatingRefreshPeriod, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

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
            if (!_manager.AllowsTraffic(conn))
                continue;
            try
            {
                LogChatRequest("send", conn, target);
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
                var (host, port) = SplitServerHostPort(conn.Entity.BaseUrl);
                _logger.LogWarning(ex, "CreateChatRequest failed on {Host}:{Port} ({BaseUrl})",
                    host, port, conn.Entity.BaseUrl);
            }
        }
    }

    /// <summary>
    /// One-shot inbox poll (timeout 1s) so ChatRequest replies / keys are applied before send.
    /// </summary>
    public async Task DrainInboxOnceAsync(CancellationToken cancellationToken = default)
    {
        var user = _auth.CurrentUser;
        if (user == null)
            return;

        var ready = await _manager.EnsureAllActiveReadyAsync(cancellationToken).ConfigureAwait(false);
        foreach (var conn in ready)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_manager.AllowsTraffic(conn))
                continue;
            try
            {
                var inbox = await TrackAsync(
                        conn,
                        () => conn.Api.PollEventsAsync(1, cancellationToken))
                    .ConfigureAwait(false);
                if (inbox.ChatRequests.Count > 0)
                    await ProcessChatRequestsAsync(conn, user, inbox.ChatRequests, cancellationToken)
                        .ConfigureAwait(false);
                if (inbox.Messages.Count > 0)
                    await ProcessIncomingMessagesAsync(conn, user, inbox.Messages, cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "DrainInbox failed on {BaseUrl}", conn.Entity.BaseUrl);
            }
        }
    }

    /// <summary>
    /// Delivers an already-built chat wire through trusted messenger servers where the peer is registered.
    /// Returns true if at least one such server accepted the message.
    /// </summary>
    public async Task<bool> TryDeliverWireAsync(
        ChatEntity chat,
        UserEntity user,
        byte[] wire,
        CancellationToken cancellationToken = default)
    {
        Require.NotNull(chat);
        Require.NotNull(user);
        Require.NotNull(wire);

        var latest = await _chats.GetChatAsync(chat.Id).ConfigureAwait(false) ?? chat;
        if (string.IsNullOrWhiteSpace(latest.PeerRsaPublicJson))
        {
            await DrainInboxOnceAsync(cancellationToken).ConfigureAwait(false);
            latest = await _chats.GetChatAsync(chat.Id).ConfigureAwait(false) ?? latest;
        }

        if (string.IsNullOrWhiteSpace(latest.PeerRsaPublicJson))
        {
            _logger.LogInformation("Send skipped: no peer public key for chat {ChatId}", latest.Id);
            return false;
        }

        RsaPublicKey peerKey;
        try
        {
            peerKey = RsaKeySerializer.DeserializePublic(latest.PeerRsaPublicJson);
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
            _logger.LogWarning(ex, "Failed to encrypt server payload for chat {ChatId}", latest.Id);
            return false;
        }

        var peerId = ChatRepository.CanonicalPeerNetworkId(latest.PeerNetworkIdShort);
        if (peerId.Length == 0)
            return false;

        var targets = await CollectPeerServerTargetsAsync(peerId, cancellationToken).ConfigureAwait(false);
        if (targets.Count == 0)
        {
            _logger.LogInformation(
                "Send skipped: no ready trusted messenger server for peer {PeerId}",
                peerId);
            return false;
        }

        var now = DateTime.UtcNow;
        var dto = new MessageDto
        {
            MessageId = Guid.NewGuid().ToString("N"),
            SrcNetworkId = user.NetworkIdShort.Trim(),
            TgtNetworkId = peerId,
            CreatedUtc = now,
            UpdatedUtc = now,
            EncryptedDataBase64 = encrypted
        };

        var degree = Clamp(targets.Count, 1, MaxSendWorkers);
        var successCount = 0;

#if NETFRAMEWORK
        await ParallelFx.ForEachAsync(
#else
        await Parallel.ForEachAsync(
#endif

            targets,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = degree,
                CancellationToken = cancellationToken
            },
            async (conn, ct) =>
            {
                if (!_manager.AllowsTraffic(conn))
                    return;
                try
                {
                    await TrackAsync(conn, () => conn.Api.SendMessageAsync(dto, ct)).ConfigureAwait(false);
                    Interlocked.Increment(ref successCount);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "SendMessage failed on {BaseUrl}", conn.Entity.BaseUrl);
                }
            }).ConfigureAwait(false);

        return successCount > 0;
    }

    /// <summary>
    /// Encrypts an inner chat-wire (image/file frame) and PUTs it to trusted servers where the peer is registered.
    /// Returns true if at least one server accepted the blob.
    /// </summary>
    public async Task<bool> TryUploadBlobAsync(
        ChatEntity chat,
        UserEntity user,
        string blobId,
        byte[] innerWire,
        CancellationToken cancellationToken = default)
    {
        Require.NotNull(chat);
        Require.NotNull(user);
        Require.NotNullOrWhiteSpace(blobId);
        Require.NotNull(innerWire);

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

        byte[] ciphertext;
        try
        {
            ciphertext = MessengerServerPayloadCodec.Encrypt(innerWire, peerKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to encrypt blob for chat {ChatId}", chat.Id);
            return false;
        }

        var peerId = chat.PeerNetworkIdShort.Trim();
        if (peerId.Length == 0)
            return false;

        var targets = await CollectPeerServerTargetsAsync(peerId, cancellationToken).ConfigureAwait(false);
        if (targets.Count == 0)
            return false;

        var degree = Clamp(targets.Count, 1, MaxSendWorkers);
        var successCount = 0;

#if NETFRAMEWORK
        await ParallelFx.ForEachAsync(
#else
        await Parallel.ForEachAsync(
#endif

            targets,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = degree,
                CancellationToken = cancellationToken
            },
            async (conn, ct) =>
            {
                if (!_manager.AllowsTraffic(conn))
                    return;
                try
                {
                    await TrackAsync(conn, () => conn.Api.PutBlobAsync(blobId, peerId, ciphertext, ct))
                        .ConfigureAwait(false);
                    Interlocked.Increment(ref successCount);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "PutBlob failed on {BaseUrl}", conn.Entity.BaseUrl);
                }
            }).ConfigureAwait(false);

        return successCount > 0;
    }

    /// <summary>
    /// GETs an opaque blob from trusted servers. Tries <paramref name="preferredBaseUrl"/> first, then rank order.
    /// Returns null if every server responded 404 or failed.
    /// </summary>
    public async Task<byte[]?> TryDownloadBlobAsync(
        string blobId,
        string? preferredBaseUrl,
        CancellationToken cancellationToken = default)
    {
        Require.NotNullOrWhiteSpace(blobId);

        var ready = await _manager.EnsureAllActiveReadyAsync(cancellationToken).ConfigureAwait(false);
        var ordered = OrderConnectionsForBlobDownload(ready, preferredBaseUrl);
        if (ordered.Count == 0)
            return null;

        foreach (var conn in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_manager.AllowsTraffic(conn))
                continue;

            try
            {
                var bytes = await TrackAsync(conn, () => conn.Api.GetBlobAsync(blobId, cancellationToken))
                    .ConfigureAwait(false);
                if (bytes is { Length: > 0 })
                    return bytes;
            }
            catch (MessengerServerApiException ex) when ((int)ex.StatusCode == 404)
            {
                _logger.LogDebug("Blob {BlobId} not found on {BaseUrl}", blobId, conn.Entity.BaseUrl);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "GetBlob failed on {BaseUrl}", conn.Entity.BaseUrl);
            }
        }

        return null;
    }

    /// <summary>
    /// Removes the blob from trusted servers after the recipient accepted it.
    /// Failures are ignored: TTL retention still applies.
    /// </summary>
    public async Task TryDeleteBlobAsync(string blobId, CancellationToken cancellationToken = default)
    {
        Require.NotNullOrWhiteSpace(blobId);

        var ready = await _manager.EnsureAllActiveReadyAsync(cancellationToken).ConfigureAwait(false);
        var targets = ready.Where(c => _manager.AllowsTraffic(c)).ToArray();
        if (targets.Length == 0)
            return;

        var degree = Clamp(targets.Length, 1, MaxSendWorkers);
#if NETFRAMEWORK
        await ParallelFx.ForEachAsync(
#else
        await Parallel.ForEachAsync(
#endif

            targets,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = degree,
                CancellationToken = cancellationToken
            },
            async (conn, ct) =>
            {
                if (!_manager.AllowsTraffic(conn))
                    return;
                try
                {
                    await TrackAsync(conn, () => conn.Api.DeleteBlobAsync(blobId, ct)).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "DeleteBlob failed on {BaseUrl}", conn.Entity.BaseUrl);
                }
            }).ConfigureAwait(false);
    }

    private async Task<List<MessengerServerConnection>> CollectPeerServerTargetsAsync(
        string peerId,
        CancellationToken cancellationToken)
    {
        var ready = await _manager.EnsureAllActiveReadyAsync(cancellationToken).ConfigureAwait(false);
        var allowed = new List<MessengerServerConnection>(ready.Count);
        foreach (var conn in ready)
        {
            if (_manager.AllowsTraffic(conn))
                allowed.Add(conn);
        }

        if (allowed.Count == 0)
            return allowed;

        var registered = new List<MessengerServerConnection>(allowed.Count);
        foreach (var conn in allowed)
        {
            if (await PeerRegisteredOnTrustedServerAsync(conn, peerId, cancellationToken).ConfigureAwait(false))
                registered.Add(conn);
        }

        // GetClients can fail or use a different id spelling; SendMessage still stores for the target id.
        return registered.Count > 0 ? registered : allowed;
    }

    private List<MessengerServerConnection> OrderConnectionsForBlobDownload(
        IReadOnlyList<MessengerServerConnection> ready,
        string? preferredBaseUrl)
    {
        var ordered = new List<MessengerServerConnection>(ready.Count);
        if (!string.IsNullOrWhiteSpace(preferredBaseUrl))
        {
            var hint = SqliteMessengerServerRepository.NormalizeBaseUrl(preferredBaseUrl);
            var hinted = ready.FirstOrDefault(c =>
                _manager.AllowsTraffic(c) &&
                string.Equals(
                    SqliteMessengerServerRepository.NormalizeBaseUrl(c.Entity.BaseUrl),
                    hint,
                    StringComparison.OrdinalIgnoreCase));
            if (hinted != null)
                ordered.Add(hinted);
        }

        foreach (var conn in _manager.FilterAvailable(ready))
        {
            if (!ordered.Contains(conn))
                ordered.Add(conn);
        }

        foreach (var conn in ready)
        {
            if (_manager.AllowsTraffic(conn) && !ordered.Contains(conn))
                ordered.Add(conn);
        }

        return ordered;
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
            if (!_manager.AllowsTraffic(conn))
                continue;
            IReadOnlyList<ClientPresenceDto> list;
            var sw = Stopwatch.StartNew();
            try
            {
                list = await TrackAsync(conn, () => conn.Api.GetClientsAsync(cancellationToken))
                    .ConfigureAwait(false);
                sw.Stop();
                _manager.RecordProbeSuccess(conn.Entity.Id, sw.Elapsed);
                _manager.ReplaceRegisteredClients(conn.Entity.Id, list.Select(c => c.NetworkId));
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

    private async Task<bool> PeerRegisteredOnTrustedServerAsync(
        MessengerServerConnection connection,
        string peerNetworkId,
        CancellationToken cancellationToken)
    {
        if (!_manager.AllowsTraffic(connection))
            return false;

        if (_manager.IsClientRegisteredOnServer(connection.Entity.Id, peerNetworkId))
            return true;

        try
        {
            var list = await TrackAsync(connection, () => connection.Api.GetClientsAsync(cancellationToken))
                .ConfigureAwait(false);
            _manager.ReplaceRegisteredClients(connection.Entity.Id, list.Select(c => c.NetworkId));
            return _manager.IsClientRegisteredOnServer(connection.Entity.Id, peerNetworkId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "GetClients failed while checking registration on {BaseUrl}", connection.Entity.BaseUrl);
            return false;
        }
    }

    private static bool PreferClient(ClientPresenceDto candidate, ClientPresenceDto existing)
    {
        var candidateOnline = candidate.IsOnline;
        var existingOnline = existing.IsOnline;
        if (candidateOnline != existingOnline)
            return candidateOnline;
        return candidate.LastSeenAtUtc > existing.LastSeenAtUtc;
    }


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

                var degree = Clamp(available.Count, 1, MaxLongPollWorkers);
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
            if (!_manager.AllowsTraffic(connection))
                break;

            try
            {
                var sw = Stopwatch.StartNew();
                var inbox = await TrackAsync(
                        connection,
                        () => connection.Api.PollEventsAsync(LongPollTimeoutSeconds, cancellationToken))
                    .ConfigureAwait(false);
                sw.Stop();

                if (!_manager.AllowsTraffic(connection))
                    break;

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
                if (!_manager.AllowsTraffic(connection))
                    break;

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
        if (!_manager.AllowsTraffic(connection))
            return;

        if (requests.Count == 0)
            return;

        var ourPublic = RsaKeySerializer.SerializePublic(_auth.GetCurrentPublicKey());
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var peerId = request.NetworkId.Trim();
            if (string.IsNullOrEmpty(peerId) ||
                ChatRepository.PeerNetworkIdsEqual(peerId, user.NetworkIdShort))
                continue;

            var blocked = await _chats.IsPeerBlockedAsync(user.Id, peerId, cancellationToken).ConfigureAwait(false);
            var existing = await _chats.FindChatByPeerNetworkIdAsync(user.Id, peerId).ConfigureAwait(false);
            var peerNick = await ResolvePeerNicknameAsync(connection, peerId, cancellationToken).ConfigureAwait(false);
            var source = PeerKeySource.Server(connection.Entity.BaseUrl);
            LogChatRequest("receive", connection, peerId);
            ChatEntity chat;
            if (existing == null ||
                !SafetyNumber.PublicKeyJsonEquals(existing.PeerRsaPublicJson, request.PublicKey.Trim()))
            {
                chat = await _chats.AddChatAsync(
                    user.Id,
                    peerNick,
                    peerId,
                    request.PublicKey,
                    peerId,
                    user.DataUdpPort,
                    remote: true,
                    keySource: source).ConfigureAwait(false);
            }
            else
            {
                chat = existing;
                await _chats.TryUpdatePeerNicknameAsync(chat.Id, peerNick).ConfigureAwait(false);
            }

            if (blocked)
            {
                _logger.LogInformation(
                    "Ignored UI for chat request from blocked peer {PeerId} (chat {ChatId})",
                    peerId,
                    chat.Id);
                continue;
            }

            try
            {
                LogChatRequest("reply", connection, peerId);
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
                var (host, port) = SplitServerHostPort(connection.Entity.BaseUrl);
                _logger.LogWarning(ex, "Reply ChatRequest failed for peer {PeerId} on {Host}:{Port}",
                    peerId, host, port);
            }

            if (existing == null)
            {
                _logger.LogInformation(
                    "Accepted server chat request from {PeerId} into local chat {ChatId}",
                    peerId,
                    chat.Id);
            }

            await _chats.NotifyIncomingChatInviteAsync(chat.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string> ResolvePeerNicknameAsync(
        MessengerServerConnection connection,
        string peerNetworkId,
        CancellationToken cancellationToken)
    {
        try
        {
            var list = await TrackAsync(connection, () => connection.Api.GetClientsAsync(cancellationToken))
                .ConfigureAwait(false);
            _manager.ReplaceRegisteredClients(connection.Entity.Id, list.Select(c => c.NetworkId));
            var hit = list.FirstOrDefault(c =>
                string.Equals(c.NetworkId.Trim(), peerNetworkId, StringComparison.Ordinal));
            var nick = hit?.Nick?.Trim() ?? "";
            if (nick.Length > 0 &&
                !string.Equals(nick, peerNetworkId, StringComparison.OrdinalIgnoreCase))
                return nick;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "GetClients for nickname of {PeerId} failed", peerNetworkId);
        }

        return peerNetworkId;
    }

    private async Task ProcessIncomingMessagesAsync(
        MessengerServerConnection connection,
        UserEntity user,
        IReadOnlyList<MessageDto> messages,
        CancellationToken cancellationToken)
    {
        if (!_manager.AllowsTraffic(connection))
            return;

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

                var blocked = await _chats.IsPeerBlockedAsync(user.Id, peerId, cancellationToken)
                    .ConfigureAwait(false);
                if (blocked)
                {
                    await IngestWireIntoRepositoryAsync(chat.Id, wire, connection.Entity.BaseUrl)
                        .ConfigureAwait(false);
                }
                else if (_sessions.TryGetSession(chat.Id, out var session) && session != null)
                {
                    await session.IngestIncomingWireFromServerAsync(wire, cancellationToken, connection.Entity.BaseUrl)
                        .ConfigureAwait(false);
                }
                else
                {
                    await IngestWireIntoRepositoryAsync(chat.Id, wire, connection.Entity.BaseUrl)
                        .ConfigureAwait(false);
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
        if (!_manager.AllowsTraffic(connection))
            throw new InvalidOperationException("Messenger server is not trusted.");

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
        if (!_manager.AllowsTraffic(connection))
            throw new InvalidOperationException("Messenger server is not trusted.");

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

    private async Task IngestWireIntoRepositoryAsync(int chatId, byte[] wire, string? blobServerBaseUrl = null)
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
                case ChatWireTransferOffer offer:
                    var text = string.IsNullOrWhiteSpace(offer.FileName) ? "[Входящее вложение]" : offer.FileName;
                    var messageId = await _chats.AddMessageAsync(chatId, false, text).ConfigureAwait(false);
                    await _chats.UpdateMessagePayloadAsync(
                            messageId, ChatPayloadKind.TransferOffer, text, offer.MimeType, [])
                        .ConfigureAwait(false);
                    var host = !string.IsNullOrWhiteSpace(offer.Host)
                        ? offer.Host
                        : blobServerBaseUrl?.Trim() ?? "";
                    await _chats.UpdateMessageTransferMetadataAsync(
                            messageId,
                            offer.TransferId,
                            offer.TransferToken,
                            offer.PayloadKind,
                            offer.FileName,
                            offer.SizeBytes,
                            host,
                            offer.Port,
                            offer.ExpiresUtcTicks,
                            ChatTransferState.AwaitingClick)
                        .ConfigureAwait(false);
                    return;
                default:
                    await _chats.AddMessageAsync(chatId, false, "[Серверное сообщение: неподдерживаемый тип]")
                        .ConfigureAwait(false);
                    return;
            }
        }

        var fallback = Encoding.UTF8.GetString(wire);
        await _chats.AddMessageAsync(chatId, false, fallback).ConfigureAwait(false);
    }

    private void OnFailoverCompleted(object? sender, MessengerServerFailoverEventArgs e)
    {
        if (e.SwitchedToMesh || e.FallbackServer == null)
            return;
        var token = _cts?.Token ?? CancellationToken.None;
        _ = RepublishAllChatRequestsAsync(token);
    }

    private async Task RepublishAllChatRequestsAsync(CancellationToken cancellationToken)
    {
        var user = _auth.CurrentUser;
        if (user == null)
            return;
        try
        {
            var chats = await _chats.ListChatsAsync(user.Id).ConfigureAwait(false);
            foreach (var chat in chats)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await PublishChatRequestAsync(chat.PeerNetworkIdShort, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Republished ChatRequest for {Count} chats after messenger-server failover",
                chats.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // expected
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to republish ChatRequest after messenger-server failover");
        }
    }

    private void LogChatRequest(string action, MessengerServerConnection connection, string peerNetworkId)
    {
        var (host, port) = SplitServerHostPort(connection.Entity.BaseUrl);
        _logger.LogInformation(
            "ChatRequest {Action} server {Host}:{Port} ({BaseUrl}) peer={PeerNetworkId}",
            action, host, port, connection.Entity.BaseUrl, peerNetworkId);
    }

    private static int Clamp(int value, int min, int max) =>
        value < min ? min : value > max ? max : value;

    internal static (string Host, int Port) SplitServerHostPort(string? baseUrl)
    {
        if (!Uri.TryCreate((baseUrl ?? "").Trim(), UriKind.Absolute, out var uri))
            return ((baseUrl ?? "").Trim(), 0);
        var port = uri.IsDefaultPort
            ? uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80
            : uri.Port;
        return (uri.Host, port);
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
