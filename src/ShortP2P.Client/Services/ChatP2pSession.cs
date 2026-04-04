using System.Net;
using System.Text;
using System.Threading.Channels;
using ShortP2P.Client.Data;
using ShortP2P.Crypto;
using ShortP2P.Messenger;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Services;

/// <summary>
/// One chat: UDP transport, RSA handshake (0x01 + 128 bytes), encrypted messenger frames (0x02 + ciphertext).
/// </summary>
public sealed class ChatP2pSession : IAsyncDisposable
{
    public const byte FrameHandshake = 0x01;
    public const byte FrameCipher = 0x02;

    private readonly ChatEntity _chat;
    private readonly UserEntity _user;
    private readonly AuthService _auth;
    private readonly ChatRepository _repo;
    private readonly SynchronizationContext? _uiSync;

    private readonly object _sync = new();
    private readonly SemaphoreSlim _sessionSetup = new(1, 1);
    private UdpTransport? _udp;
    private readonly Channel<TransportReceiveMessage> _bridge = Channel.CreateUnbounded<TransportReceiveMessage>();
    private PrefixedCipherTransport? _prefixed;
    private MessengerService? _messenger;
    private P2PSession? _session;
    private CancellationTokenSource? _cts;
    private Task? _pumpTask;
    private Task? _incomingTask;
    private bool _incomingStarted;

    private TransportAddress? _peerAddress;
    private RsaPublicKey? _peerPublicKey;

    public ChatP2pSession(ChatEntity chat, UserEntity user, AuthService auth, ChatRepository repo,
        SynchronizationContext? uiSynchronizationContext = null)
    {
        _chat = chat;
        _user = user;
        _auth = auth;
        _repo = repo;
        _uiSync = uiSynchronizationContext;
    }

    public event EventHandler? MessagesChanged;

    private void RaiseMessagesChanged()
    {
        if (_uiSync != null)
            _uiSync.Post(_ => MessagesChanged?.Invoke(this, EventArgs.Empty), null);
        else
            MessagesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);

        _peerPublicKey = RsaKeySerializer.DeserializePublic(_chat.PeerRsaPublicJson);
        _peerAddress = UdpTransportAddress.FromIPEndPoint(new IPEndPoint(IPAddress.Parse(_chat.PeerHost), _chat.PeerPort));

        _udp = new UdpTransport(_user.DataUdpPort);
        await _udp.StartAsync(cancellationToken).ConfigureAwait(false);
        _prefixed = new PrefixedCipherTransport(_udp, _bridge);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pumpTask = Task.Run(() => PumpAsync(_cts.Token), _cts.Token);
    }

    public async ValueTask SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
            return;

        await EnsureSessionAsInitiatorAsync(cancellationToken).ConfigureAwait(false);

        var bytes = Encoding.UTF8.GetBytes(text);
        await _messenger!.SendBinaryAsync(bytes, _peerAddress!, cancellationToken).ConfigureAwait(false);
        await _repo.AddMessageAsync(_chat.Id, true, text).ConfigureAwait(false);
        RaiseMessagesChanged();
    }

    private async Task EnsureSessionAsInitiatorAsync(CancellationToken cancellationToken)
    {
        await _sessionSetup.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                if (_session != null && _messenger != null)
                    return;
            }

            var hs = P2PCrypto.CreateHandshakeInitiation(_peerPublicKey!);
            var packet = new byte[129];
            packet[0] = FrameHandshake;
            Buffer.BlockCopy(hs.HandshakePacket, 0, packet, 1, hs.HandshakePacket.Length);
            await _udp!.SendAsync(packet, _peerAddress!, cancellationToken).ConfigureAwait(false);

            MessengerService ms;
            lock (_sync)
            {
                if (_session != null && _messenger != null)
                    return;
                _session = hs.Session;
                _messenger = new MessengerService(_prefixed!, _session);
                ms = _messenger;
            }

            await ms.StartAsync(cancellationToken).ConfigureAwait(false);
            StartIncomingReaderIfNeeded();
        }
        finally
        {
            _sessionSetup.Release();
        }
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var msg in _udp!.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var buf = msg.Payload.ToArray();
                if (buf.Length == 0)
                    continue;

                if (buf[0] == FrameHandshake && buf.Length == 129)
                {
                    var handshake = new byte[128];
                    Buffer.BlockCopy(buf, 1, handshake, 0, 128);
                    await HandleResponderHandshakeAsync(handshake, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (buf[0] == FrameCipher && buf.Length > 1)
                {
                    var inner = new byte[buf.Length - 1];
                    Buffer.BlockCopy(buf, 1, inner, 0, inner.Length);
                    await _bridge.Writer
                        .WriteAsync(new TransportReceiveMessage(inner, msg.RemoteAddress), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }

    private async Task HandleResponderHandshakeAsync(byte[] handshakePacket, CancellationToken cancellationToken)
    {
        await _sessionSetup.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            MessengerService? created = null;
            lock (_sync)
            {
                if (_session != null)
                    return;

                var localPrivate = _auth.GetCurrentPrivateKey();
                _session = P2PCrypto.CreateSession(localPrivate, handshakePacket);
                _messenger = new MessengerService(_prefixed!, _session);
                created = _messenger;
            }

            if (created != null)
            {
                await created.StartAsync(cancellationToken).ConfigureAwait(false);
                StartIncomingReaderIfNeeded();
            }
        }
        finally
        {
            _sessionSetup.Release();
        }
    }

    private void StartIncomingReaderIfNeeded()
    {
        lock (_sync)
        {
            if (_incomingStarted || _messenger == null)
                return;
            _incomingStarted = true;
        }

        _incomingTask = Task.Run(() => IncomingLoopAsync(_cts!.Token));
    }

    private async Task IncomingLoopAsync(CancellationToken cancellationToken)
    {
        MessengerService? m;
        lock (_sync)
        {
            m = _messenger;
        }

        if (m == null)
            return;

        try
        {
            await foreach (var incoming in m.Incoming.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var text = Encoding.UTF8.GetString(incoming.Payload.ToArray());
                await _repo.AddMessageAsync(_chat.Id, false, text).ConfigureAwait(false);
                RaiseMessagesChanged();
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        _cts?.Cancel();

        if (_messenger != null)
            await _messenger.StopAsync(cancellationToken).ConfigureAwait(false);

        if (_pumpTask != null)
        {
            try
            {
                await _pumpTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_incomingTask != null)
        {
            try
            {
                await _incomingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_udp != null)
            await _udp.StopAsync(cancellationToken).ConfigureAwait(false);

        _bridge.Writer.TryComplete();
        _cts?.Dispose();
        _cts = null;
        _udp = null;
        _prefixed = null;
        _messenger = null;
        _session = null;
        _pumpTask = null;
        _incomingTask = null;
        _incomingStarted = false;
    }

    public ValueTask DisposeAsync() => StopAsync();

    private sealed class PrefixedCipherTransport : ITransport
    {
        private readonly UdpTransport _udp;
        private readonly Channel<TransportReceiveMessage> _bridge;

        public PrefixedCipherTransport(UdpTransport udp, Channel<TransportReceiveMessage> bridge)
        {
            _udp = udp;
            _bridge = bridge;
        }

        public TransportKind Kind => TransportKind.Udp;

        public ChannelReader<TransportReceiveMessage> Inbound => _bridge.Reader;

        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask StopAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
            CancellationToken cancellationToken = default)
        {
            var buf = new byte[payload.Length + 1];
            buf[0] = FrameCipher;
            payload.CopyTo(buf.AsMemory(1));
            await _udp.SendAsync(buf, destination, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
