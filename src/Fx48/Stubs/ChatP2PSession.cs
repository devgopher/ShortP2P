using System.Threading;
using Microsoft.Extensions.Logging;
using ShortP2P.Auth.Data;
using ShortP2P.Client.ChatMedia;
using ShortP2P.Client.Data;
using ShortP2P.Client.Services.MessengerServers;
using ShortP2P.Crypto;

namespace ShortP2P.Client.Services;

/// <summary>
/// Fx48 / Iskra.WinForms session: no UDP/BLE P2P.
/// Outbound delivery is a single per-chat background flush worker that reads Pending rows from
/// SQLite, posts via <see cref="MessengerServerSyncService.TryDeliverWireAsync"/>, and updates
/// Sent / Failed in the DB. Incoming wires stay on the repository path
/// (<see cref="IsReadyForServerReceive"/> is always false).
/// </summary>
public sealed class ChatP2PSession
{
    public const int MaxMessageChars = 32768;

    private readonly object _pendingSync = new();
    private readonly List<int> _pendingOutgoing = [];
    private readonly SemaphoreSlim _flushPendingSem = new(1, 1);
    private readonly ChatRepository _repo;
    private readonly MessengerServerSyncService _servers;
    private readonly UserEntity _user;
    private readonly SynchronizationContext? _uiSynchronizationContext;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _outboundCts = new();

    private ChatEntity _chat;
    private int _flushWorkerRunning;

    public ChatP2PSession(
        ChatEntity chat,
        UserEntity user,
        ChatRepository repo,
        MessengerServerSyncService servers,
        SynchronizationContext? uiSynchronizationContext = null,
        ILogger? logger = null)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _servers = servers ?? throw new ArgumentNullException(nameof(servers));
        _uiSynchronizationContext = uiSynchronizationContext;
        _logger = logger;
    }

    /// <summary>
    /// Keep false so <c>PersistIncomingServerWireAsync</c> always writes via the repository
    /// (this stub must not swallow inbox wires).
    /// </summary>
    public bool IsReadyForServerReceive => false;

    public event EventHandler? MessagesChanged;

    public event EventHandler<int>? TransferStateChanged;

    /// <summary>
    /// Downloads an incoming transfer blob from messenger servers (no TCP P2P on Fx48).
    /// Mirrors the server path of the full <c>ChatP2PSession.RequestBinaryDownloadAsync</c>.
    /// </summary>
    public async Task RequestBinaryDownloadAsync(int messageId, CancellationToken cancellationToken = default)
    {
        var row = await _repo.GetMessageAsync(messageId).ConfigureAwait(false);
        if (row == null || row.ChatId != _chat.Id || row.Outgoing || string.IsNullOrWhiteSpace(row.TransferId))
            return;

        var state = (ChatTransferState)row.TransferState;
        if (state == ChatTransferState.Received && row.ImageBlob is { Length: > 0 })
            return;
        if (state == ChatTransferState.Transferring)
            return;

        await _repo.UpdateTransferStateAsync(messageId, ChatTransferState.Transferring).ConfigureAwait(false);
        TransferStateChanged?.Invoke(this, messageId);
        RaiseMessagesChanged();

        try
        {
            if (!await TryReceiveBlobFromMessengerServersAsync(row, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "Не удалось скачать файл с сервера сообщений. Проверьте подключение и повторите.");
            }
        }
        catch
        {
            await _repo.UpdateTransferStateAsync(messageId, ChatTransferState.Failed).ConfigureAwait(false);
            TransferStateChanged?.Invoke(this, messageId);
            RaiseMessagesChanged();
            throw;
        }
    }

    private async Task<bool> TryReceiveBlobFromMessengerServersAsync(
        ChatMessageEntity row,
        CancellationToken cancellationToken)
    {
        var blobId = row.TransferId.Trim();
        if (blobId.Length == 0)
            return false;

        var hint = LooksLikeHttpBaseUrl(row.TransferHost) ? row.TransferHost : null;
        var ciphertext = await _servers.TryDownloadBlobAsync(blobId, hint, cancellationToken).ConfigureAwait(false);
        if (ciphertext == null || ciphertext.Length == 0)
            return false;

        if (string.IsNullOrWhiteSpace(_user.RsaPrivateJson))
            return false;

        byte[] wire;
        try
        {
            var privateKey = RsaKeySerializer.DeserializePrivate(_user.RsaPrivateJson);
            wire = MessengerServerPayloadCodec.Decrypt(ciphertext, privateKey);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Chat {ChatId}: blob decrypt failed for message {MessageId}", _chat.Id, row.Id);
            return false;
        }

        if (!ChatWireCodec.TryParse(wire, out var parsed) || parsed == null)
            return false;

        byte[] bytes;
        string mimeType;
        string fileName;
        ChatPayloadKind kind;
        switch (parsed)
        {
            case ChatWireImage img:
                bytes = img.ImageBytes;
                mimeType = img.MimeType;
                fileName = string.IsNullOrWhiteSpace(row.TransferFileName) ? row.Text : row.TransferFileName;
                kind = ChatPayloadKind.Image;
                break;
            case ChatWireFile f:
                bytes = f.FileBytes;
                mimeType = f.MimeType;
                fileName = string.IsNullOrWhiteSpace(f.FileName)
                    ? (string.IsNullOrWhiteSpace(row.TransferFileName) ? row.Text : row.TransferFileName)
                    : f.FileName;
                kind = ChatPayloadKind.File;
                break;
            default:
                return false;
        }

        await _repo.UpdateMessagePayloadAsync(row.Id, kind, fileName, mimeType, bytes).ConfigureAwait(false);
        await _repo.UpdateMessageTransferMetadataAsync(
                row.Id, row.TransferId, row.TransferToken, row.TransferPayloadKind, row.TransferFileName,
                bytes.Length, "", 0, 0, ChatTransferState.Received)
            .ConfigureAwait(false);
        TransferStateChanged?.Invoke(this, row.Id);
        RaiseMessagesChanged();

        try
        {
            await _servers.TryDeleteBlobAsync(blobId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Chat {ChatId}: blob delete after receive failed", _chat.Id);
        }

        return true;
    }

    private static bool LooksLikeHttpBaseUrl(string? host) =>
        !string.IsNullOrWhiteSpace(host) &&
        (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
         host.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

    /// <summary>Updates the cached chat row from DB (same Id) without creating a new session.</summary>
    public void ApplyChatRow(ChatEntity row)
    {
        if (row == null)
            throw new ArgumentNullException(nameof(row));
        if (row.Id != _chat.Id)
            throw new ArgumentException("Chat id mismatch.", nameof(row));

        var hadPeerKey = !string.IsNullOrWhiteSpace(_chat.PeerRsaPublicJson);
        _chat = row;
        // Peer key often lands via long-poll after ChatRequest — re-queue Pending sends.
        if (!hadPeerKey && !string.IsNullOrWhiteSpace(_chat.PeerRsaPublicJson))
            _ = EnqueueExistingPendingAsync(CancellationToken.None);
    }

    /// <summary>
    /// UI refresh when delivery status changes outside this session (e.g. server delivery receipt).
    /// </summary>
    public void NotifyMessagesChangedFromExternal() => RaiseMessagesChanged();

    public Task IngestIncomingWireFromServerAsync(
        byte[] wire,
        CancellationToken cancellationToken,
        string? serverBaseUrl = null) =>
        Task.CompletedTask;

    /// <summary>
    /// Ensures a ChatRequest is published so PeerRsaPublicJson can land via long-poll,
    /// then re-queues any leftover Pending outgoing rows for this chat.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _servers.PublishChatRequestAsync(_chat.PeerNetworkIdShort, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogDebug(ex, "Chat {ChatId}: PublishChatRequest on start", _chat.Id);
        }

        await EnqueueExistingPendingAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
            return;
        if (text.Length > MaxMessageChars)
            throw new ArgumentException(
                $"Message is too long. Max length is {MaxMessageChars} characters.",
                nameof(text));

        var messageId = await _repo
            .AddMessageAsync(_chat.Id, true, text, MessageDeliveryStatus.Pending)
            .ConfigureAwait(false);
        RaiseMessagesChanged();

        QueueOutgoingDelivery(messageId);
    }

    public async ValueTask SendImageAsync(ReadOnlyMemory<byte> imageBytes, string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (imageBytes.Length == 0)
            throw new ArgumentException("Image is empty.", nameof(imageBytes));
        if (string.IsNullOrWhiteSpace(mimeType))
            throw new ArgumentException("MIME type is required.", nameof(mimeType));

        var bytes = imageBytes.ToArray();
        var messageId = await _repo
            .AddImageMessageAsync(_chat.Id, true, mimeType.Trim(), bytes, MessageDeliveryStatus.Pending)
            .ConfigureAwait(false);
        RaiseMessagesChanged();
        QueueOutgoingDelivery(messageId);
    }

    public async ValueTask SendFileAsync(string fileName, ReadOnlyMemory<byte> fileBytes, string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (fileBytes.Length == 0)
            throw new ArgumentException("File is empty.", nameof(fileBytes));
        if (string.IsNullOrWhiteSpace(mimeType))
            throw new ArgumentException("MIME type is required.", nameof(mimeType));

        var bytes = fileBytes.ToArray();
        var safeName = Path.GetFileName((fileName ?? "").Trim());
        if (string.IsNullOrEmpty(safeName))
            safeName = "file";

        var payloadKind = mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
            ? "voice"
            : mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                ? "video"
                : "document";

        var messageId = await _repo
            .AddFileMessageAsync(_chat.Id, true, safeName, mimeType.Trim(), bytes, MessageDeliveryStatus.Pending)
            .ConfigureAwait(false);
        await _repo.UpdateMessageTransferMetadataAsync(
                messageId, "", "", payloadKind, safeName, bytes.Length, "", 0, 0, ChatTransferState.None)
            .ConfigureAwait(false);
        RaiseMessagesChanged();
        QueueOutgoingDelivery(messageId);
    }

    public async ValueTask RetryFailedMessageAsync(int messageId, CancellationToken cancellationToken = default)
    {
        var row = await _repo.GetMessageAsync(messageId, includePayloadBlob: false).ConfigureAwait(false);
        if (row == null || row.ChatId != _chat.Id || !row.Outgoing)
            return;

        await _repo.UpdateMessageDeliveryStatusAsync(messageId, MessageDeliveryStatus.Pending)
            .ConfigureAwait(false);
        RaiseMessagesChanged();
        QueueOutgoingDelivery(messageId);
    }

    /// <summary>
    /// Enqueue for the single per-chat outbound worker. Wire is rebuilt from DB in the flush
    /// worker so retries stay consistent with the stored payload.
    /// </summary>
    public void QueueOutgoingDelivery(int messageId)
    {
        lock (_pendingSync)
        {
            if (!_pendingOutgoing.Contains(messageId))
                _pendingOutgoing.Add(messageId);
        }

        StartFlushPendingInBackground();
    }

    private async Task EnqueueExistingPendingAsync(CancellationToken cancellationToken)
    {
        var rows = await _repo.ListMessagesAsync(_chat.Id).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var row in rows)
        {
            if (!row.Outgoing || row.DeliveryStatus != (int)MessageDeliveryStatus.Pending)
                continue;
            QueueOutgoingDelivery(row.Id);
        }
    }

    private void StartFlushPendingInBackground()
    {
        if (Interlocked.CompareExchange(ref _flushWorkerRunning, 1, 0) != 0)
            return;

        var token = _outboundCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await TryFlushPendingOutgoingAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Chat {ChatId}: outbound flush worker stopped", _chat.Id);
            }
            finally
            {
                Interlocked.Exchange(ref _flushWorkerRunning, 0);
                // More items may have been queued while we were finishing.
                lock (_pendingSync)
                {
                    if (_pendingOutgoing.Count > 0 && !_outboundCts.IsCancellationRequested)
                        StartFlushPendingInBackground();
                }
            }
        }, CancellationToken.None);
    }

    private async Task TryFlushPendingOutgoingAsync(CancellationToken cancellationToken)
    {
        await _flushPendingSem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int nextId;
                lock (_pendingSync)
                {
                    if (_pendingOutgoing.Count == 0)
                        return;
                    nextId = _pendingOutgoing[0];
                }

                var row = await _repo.GetMessageAsync(nextId, includePayloadBlob: false).ConfigureAwait(false);
                if (row == null || row.ChatId != _chat.Id || !row.Outgoing)
                {
                    DequeuePendingOutgoingHead(nextId);
                    continue;
                }

                if (row.DeliveryStatus is (int)MessageDeliveryStatus.Sent
                    or (int)MessageDeliveryStatus.Delivered)
                {
                    DequeuePendingOutgoingHead(nextId);
                    continue;
                }

                try
                {
                    var wire = BuildOutgoingWire(row);
                    await DeliverOutgoingWireAsync(nextId, wire, cancellationToken).ConfigureAwait(false);
                    DequeuePendingOutgoingHead(nextId);
                }
                catch (OperationCanceledException)
                {
                    // Keep Pending at head for a later retry.
                    throw;
                }
                catch (OutboundNotReadyException ex)
                {
                    // No peer key / no ready server yet — leave Pending in DB, drop from the
                    // in-memory queue so we do not spin. ApplyChatRow / StartAsync re-enqueue.
                    _logger?.LogDebug(ex,
                        "Chat {ChatId}: deferring outbound message {MessageId}",
                        _chat.Id, nextId);
                    DequeuePendingOutgoingHead(nextId);
                    return;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex,
                        "Chat {ChatId}: outbound delivery failed for message {MessageId}",
                        _chat.Id, nextId);
                    try
                    {
                        await _repo
                            .UpdateMessageDeliveryStatusAsync(nextId, MessageDeliveryStatus.Failed)
                            .ConfigureAwait(false);
                        RaiseMessagesChanged();
                    }
                    catch (Exception updateEx)
                    {
                        _logger?.LogDebug(updateEx,
                            "Chat {ChatId}: failed to mark message {MessageId} as failed",
                            _chat.Id, nextId);
                    }

                    DequeuePendingOutgoingHead(nextId);
                }
            }
        }
        finally
        {
            _flushPendingSem.Release();
        }
    }

    private void DequeuePendingOutgoingHead(int messageId)
    {
        lock (_pendingSync)
        {
            if (_pendingOutgoing.Count > 0 && _pendingOutgoing[0] == messageId)
                _pendingOutgoing.RemoveAt(0);
        }
    }

    private static byte[] BuildOutgoingWire(ChatMessageEntity row) =>
        row.PayloadKind switch
        {
            (int)ChatPayloadKind.Image when row.ImageBlob is { Length: > 0 } =>
                ChatWireCodec.EncodeImage(row.MimeType, row.ImageBlob),
            (int)ChatPayloadKind.File when row.ImageBlob is { Length: > 0 } =>
                ChatWireCodec.EncodeFile(row.Text, row.MimeType, row.ImageBlob),
            _ => ChatWireCodec.EncodeText(row.Text)
        };

    private async Task DeliverOutgoingWireAsync(int messageId, byte[] wire, CancellationToken cancellationToken)
    {
        // Refresh chat row so PeerRsaPublicJson from long-poll is visible to TryDeliverWireAsync.
        var latest = await _repo.GetChatAsync(_chat.Id).ConfigureAwait(false);
        if (latest != null)
            ApplyChatRow(latest);

        var acceptedId = await _servers
            .TryDeliverWireAsync(_chat, _user, wire, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(acceptedId))
        {
            throw new OutboundNotReadyException(
                "Messenger server did not accept the message (no peer key or no ready server).");
        }

        await _repo.RegisterOutgoingServerMessageAsync(acceptedId!, messageId, _chat.Id)
            .ConfigureAwait(false);
        await _repo.UpdateMessageDeliveryStatusAsync(messageId, MessageDeliveryStatus.Sent)
            .ConfigureAwait(false);
        RaiseMessagesChanged();
    }

    private void RaiseMessagesChanged()
    {
        if (_uiSynchronizationContext != null)
            _uiSynchronizationContext.Post(_ => MessagesChanged?.Invoke(this, EventArgs.Empty), null);
        else
            MessagesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Temporary: peer key or messenger target not ready; keep DB status Pending.</summary>
    private sealed class OutboundNotReadyException(string message) : Exception(message);
}
