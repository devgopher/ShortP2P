using System.Security.Cryptography;
using System.Threading.Channels;
using ShortP2P.Crypto;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Messenger;

/// <summary>
///     Бэкенд мессенджера на устройстве пользователя: шифрует бинарные сообщения (с фрагментацией под лимит
///     <see cref="P2PSession" />)
///     и собирает входящие фрагменты обратно.
/// </summary>
public sealed class MessengerService(ITransport transport, P2PSession session, MessengerOptions? options = null)
    : IAsyncDisposable
{
    private readonly Channel<IncomingBinaryMessage> _incoming = Channel.CreateUnbounded<IncomingBinaryMessage>();
    private readonly MessengerOptions _options = options ?? new MessengerOptions();
    private readonly Dictionary<Guid, Reassembly> _pending = new();
    private readonly P2PSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly object _sync = new();
    private readonly ITransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));

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
        if (data.Length > _options.MaxBinaryMessageBytes)
            throw new ArgumentException($"Message exceeds MaxBinaryMessageBytes ({_options.MaxBinaryMessageBytes}).",
                nameof(data));

        var maxPayload = ChunkCodec.MaxPayloadPerChunk(_session);
        var messageId = Guid.NewGuid();
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
                try
                {
                    ProcessIncomingPacket(msg);
                }
                catch (CryptographicException)
                {
                    // повреждённый или чужой пакет — отбрасываем
                }
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
        var decrypted = _session.Decrypt(msg.Payload.ToArray());
        ChunkCodec.ParseChunk(decrypted, out var messageId, out var chunkIndex, out var totalChunks, out var payload);

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

            state.Parts[chunkIndex] = payload.ToArray();
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

            _incoming.Writer.TryWrite(new IncomingBinaryMessage(buffer, msg.RemoteAddress));
        }
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
            }

        _receiveTask = null;
        _cts.Dispose();
        _cts = null;
    }

    private sealed class Reassembly(int totalChunks)
    {
        public int TotalChunks { get; } = totalChunks;

        public byte[][] Parts { get; } = new byte[totalChunks][];

        public int Received { get; set; }
    }
}