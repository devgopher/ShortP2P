using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Channels;
using ShortP2P.Crypto;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Messenger;

/// <summary>
///     Бэкенд мессенджера на устройстве пользователя: шифрует бинарные сообщения (с фрагментацией под лимит
///     <see cref="P2PSession" />)
///     и собирает входящие фрагменты обратно. Поддерживает квитанции доставки и дедупликацию по id сообщения.
/// </summary>
public sealed class MessengerService(ITransport transport, P2PSession session, MessengerOptions? options = null,
    Func<ValueTask>? onDecryptFailure = null)
    : IAsyncDisposable
{
    private const int MaxTrackedDeliveredIds = 8192;

    private readonly Channel<IncomingBinaryMessage> _incoming = Channel.CreateUnbounded<IncomingBinaryMessage>();
    private readonly MessengerOptions _options = options ?? new MessengerOptions();
    private readonly Dictionary<Guid, Reassembly> _pending = new();
    private readonly P2PSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly object _sync = new();
    private readonly ITransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _ackWaiters = new();
    private readonly HashSet<Guid> _deliveredMessageIds = [];

    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    public ChannelReader<IncomingBinaryMessage> Incoming => _incoming.Reader;

    public ValueTask DisposeAsync()
    {
        return StopAsync();
    }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (_receiveTask != null) return ValueTask.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
        return ValueTask.CompletedTask;
    }

    public async ValueTask SendBinaryAsync(byte[] data, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var messageId = Guid.NewGuid();
        await SendChunksForMessageAsync(data, messageId, destination, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Отправляет по очереди на каждый адрес, пока не придёт квитанция <paramref name="ackTimeout" /> или не
    ///     закончатся адреса.
    /// </summary>
    public async ValueTask SendBinaryAsyncExpectAck(byte[] data, IReadOnlyList<TransportAddress> destinationsInOrder,
        TimeSpan ackTimeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (destinationsInOrder.Count == 0)
            throw new ArgumentException("At least one destination is required.", nameof(destinationsInOrder));

        if (data.Length > _options.MaxBinaryMessageBytes)
            throw new ArgumentException($"Message exceeds MaxBinaryMessageBytes ({_options.MaxBinaryMessageBytes}).",
                nameof(data));

        Exception? last = null;
        foreach (var dest in destinationsInOrder)
        {
            var messageId = Guid.NewGuid();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ackTimeout);
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ackWaiters[messageId] = tcs;
            try
            {
                await SendChunksForMessageAsync(data, messageId, dest, cancellationToken).ConfigureAwait(false);
                await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested &&
                                                         !cancellationToken.IsCancellationRequested)
            {
                last = ex;
                _ackWaiters.TryRemove(messageId, out _);
            }
            catch
            {
                _ackWaiters.TryRemove(messageId, out _);
                throw;
            }
        }

        throw new IOException("Peer did not acknowledge message delivery on any address.", last);
    }

    private async ValueTask SendChunksForMessageAsync(byte[] data, Guid messageId, TransportAddress destination,
        CancellationToken cancellationToken)
    {
        if (data.Length > _options.MaxBinaryMessageBytes)
            throw new ArgumentException($"Message exceeds MaxBinaryMessageBytes ({_options.MaxBinaryMessageBytes}).",
                nameof(data));

        var maxPayload = ChunkCodec.MaxPayloadPerChunk(_session);
        var totalChunks = data.Length == 0 ? 1 : (data.Length + maxPayload - 1) / maxPayload;

        for (var i = 0; i < totalChunks; i++)
        {
            var offset = i * maxPayload;
            var len = Math.Min(maxPayload, data.Length - offset);
            var sliceBytes = len == 0 ? Array.Empty<byte>() : new byte[len];
            if (len > 0)
                Buffer.BlockCopy(data, offset, sliceBytes, 0, len);
            var plain = ChunkCodec.BuildChunk(messageId, i, totalChunks, sliceBytes);
            var encrypted = _session.Encrypt(plain);
            await _transport.SendAsync(encrypted, destination, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var msg in _transport.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                ProcessIncomingPacket(msg);
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        finally
        {
            _incoming.Writer.TryComplete();
        }
    }

    private void ProcessIncomingPacket(TransportReceiveMessage msg)
    {
        byte[] decrypted;
        try
        {
            decrypted = _session.Decrypt(msg.Payload.ToArray());
        }
        catch (CryptographicException)
        {
            ScheduleDecryptRecovery();
            return;
        }

        if (DeliveryAckCodec.TryParse(decrypted, out var ackId))
        {
            if (_ackWaiters.TryRemove(ackId, out var w))
                w.TrySetResult();
            return;
        }

        Guid messageId;
        int chunkIndex;
        int totalChunks;
        byte[] payloadBytes;
        try
        {
            ChunkCodec.ParseChunk(decrypted, out messageId, out chunkIndex, out totalChunks, out var payload);
            payloadBytes = payload.ToArray();
        }
        catch (CryptographicException)
        {
            return;
        }

        bool shouldEnqueue;
        lock (_sync)
        {
            if (!_pending.TryGetValue(messageId, out var state))
            {
                state = new Reassembly(totalChunks);
                _pending[messageId] = state;
            }

            if (state.TotalChunks != totalChunks)
            {
                _pending.Remove(messageId);
                return;
            }

            if (state.Parts[chunkIndex] != null)
                return;

            state.Parts[chunkIndex] = payloadBytes;
            state.Received++;

            if (state.Received != state.TotalChunks)
                return;

            _pending.Remove(messageId);

            var fullLen = 0;
            for (var i = 0; i < state.TotalChunks; i++)
                fullLen += state.Parts[i]!.Length;

            var buffer = new byte[fullLen];
            var pos = 0;
            for (var i = 0; i < state.TotalChunks; i++)
            {
                var part = state.Parts[i]!;
                Buffer.BlockCopy(part, 0, buffer, pos, part.Length);
                pos += part.Length;
            }

            if (buffer.Length > _options.MaxBinaryMessageBytes)
                return;

            shouldEnqueue = TrackDeliverOnce(messageId);
            if (shouldEnqueue)
                _incoming.Writer.TryWrite(new IncomingBinaryMessage(buffer, msg.RemoteAddress));
        }

        _ = SendDeliveryAckAsync(messageId, msg.RemoteAddress);
    }

    private bool TrackDeliverOnce(Guid messageId)
    {
        if (_deliveredMessageIds.Contains(messageId))
            return false;
        if (_deliveredMessageIds.Count >= MaxTrackedDeliveredIds)
            _deliveredMessageIds.Clear();
        _deliveredMessageIds.Add(messageId);
        return true;
    }

    private async Task SendDeliveryAckAsync(Guid messageId, TransportAddress replyTo)
    {
        try
        {
            var plain = DeliveryAckCodec.ToBytes(messageId);
            var encrypted = _session.Encrypt(plain);
            await _transport.SendAsync(encrypted, replyTo, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // ignore: доставка ack best-effort
        }
    }

    /// <summary>Вне потока приёма: иначе StopAsync мессенджера может взаимно заблокироваться с ReceiveLoop.</summary>
    private void ScheduleDecryptRecovery()
    {
        var cb = onDecryptFailure;
        if (cb == null)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                await cb().ConfigureAwait(false);
            }
            catch
            {
                // сбой восстановления — игнорируем
            }
        });
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts == null) return;
        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            _cts.Cancel();
        }

        if (_receiveTask != null)
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }

        _receiveTask = null;
        _cts.Dispose();
        _cts = null;

        foreach (var kv in _ackWaiters.ToArray())
            kv.Value.TrySetCanceled();
        _ackWaiters.Clear();
    }

    private sealed class Reassembly(int totalChunks)
    {
        public int TotalChunks { get; } = totalChunks;

        public byte[][] Parts { get; } = new byte[totalChunks][];

        public int Received { get; set; }
    }
}
