using System.Net;
using System.Text;
using System.Threading.Channels;
using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Crypto;
using ShortP2P.Discovery;
using ShortP2P.Messenger;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Services;

/// <summary>
/// One chat: UDP transport, RSA handshake (0x01 + 128 bytes), encrypted messenger frames (0x02 + ciphertext).
/// С опциональным <see cref="SharedUserUdpGateway"/> — поиск пира по графу (≤3 рёбер), ретрансляция и повторы при ошибке.
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
    private readonly SharedUserUdpGateway? _gateway;
    private readonly P2pRoutingSettings? _routing;

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
    private ChatRelayRoute _route = null!;
    private RsaPublicKey? _peerPublicKey;

    public ChatP2pSession(ChatEntity chat, UserEntity user, AuthService auth, ChatRepository repo,
        SynchronizationContext? uiSynchronizationContext = null, SharedUserUdpGateway? sharedGateway = null,
        P2pRoutingSettings? routingSettings = null)
    {
        _chat = chat;
        _user = user;
        _auth = auth;
        _repo = repo;
        _uiSync = uiSynchronizationContext;
        _gateway = sharedGateway;
        _routing = routingSettings ?? (sharedGateway != null ? new P2pRoutingSettings() : null);
    }

    public event EventHandler? MessagesChanged;

    private void RaiseMessagesChanged()
    {
        if (_uiSync != null)
            _uiSync.Post(_ => MessagesChanged?.Invoke(this, EventArgs.Empty), null);
        else
            MessagesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildRouteFromChat()
    {
        _peerPublicKey = RsaKeySerializer.DeserializePublic(_chat.PeerRsaPublicJson);
        _peerAddress = UdpTransportAddress.FromIPEndPoint(new IPEndPoint(IPAddress.Parse(_chat.PeerHost), _chat.PeerPort));
        _route = ChatRelayRoute.FromChat(_peerAddress, _chat.RelayRouteBlob);
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);

        RebuildRouteFromChat();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (_gateway != null)
        {
            await _gateway.EnsureStartedAsync(_user, cancellationToken).ConfigureAwait(false);
            _prefixed = new PrefixedCipherTransport(_bridge, async (mem, ct) =>
            {
                await SendRouteRawAsync(mem, ct).ConfigureAwait(false);
            });
            _gateway.SetChatSink(ProcessGatewayDatagramAsync);
        }
        else
        {
            _udp = new UdpTransport(_user.DataUdpPort);
            await _udp.StartAsync(cancellationToken).ConfigureAwait(false);
            _prefixed = new PrefixedCipherTransport(_bridge, async (mem, ct) =>
            {
                await _udp!.SendAsync(mem, _peerAddress!, ct).ConfigureAwait(false);
            });
            _pumpTask = Task.Run(() => PumpAsync(_cts.Token), _cts.Token);
        }
    }

    public async ValueTask SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var maxAttempts = _gateway != null && _routing != null ? Math.Max(1, _routing.SendFailureSearchAttempts) : 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await EnsureSessionAsInitiatorAsync(cancellationToken).ConfigureAwait(false);

                var bytes = Encoding.UTF8.GetBytes(text);
                await _messenger!.SendBinaryAsync(bytes, _peerAddress!, cancellationToken).ConfigureAwait(false);
                await _repo.AddMessageAsync(_chat.Id, true, text).ConfigureAwait(false);
                RaiseMessagesChanged();
                return;
            }
            catch (Exception)
            {
                var canRetry = attempt < maxAttempts && _gateway != null && _routing != null;
                if (!canRetry)
                    throw;

                await Task.Delay(_routing!.SendFailureRetryDelay, cancellationToken).ConfigureAwait(false);
                await TryRefreshRouteViaSearchAsync(cancellationToken).ConfigureAwait(false);
                await ResetCryptoStateAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task TryRefreshRouteViaSearchAsync(CancellationToken cancellationToken)
    {
        if (_gateway == null) return;
        var targetId = CompressedNetworkId.FromShortString(_chat.PeerNetworkIdShort).Value;
        var found = await _gateway.SearchPeerAsync(targetId, _chat.PeerNickname, cancellationToken)
            .ConfigureAwait(false);
        if (found == null)
            return;

        var direct = UdpTransportAddress.FromIPEndPoint(new IPEndPoint(IPAddress.Parse(found.PeerHost), found.PeerPort));
        string? blob = null;
        if (found.FirstRelayHop != null && found.RelayStrip.Count > 0)
        {
            blob = ChatRelayRoute.SerializeBlob(new ChatRelayRoute
            {
                Direct = direct,
                FirstHop = found.FirstRelayHop,
                RelayStrip = found.RelayStrip,
            });
        }

        await _repo.UpdateChatP2pRouteAsync(_chat.Id, found.PeerHost, found.PeerPort, blob).ConfigureAwait(false);
        var fresh = await _repo.GetChatAsync(_chat.Id).ConfigureAwait(false);
        if (fresh != null)
        {
            _chat.PeerHost = fresh.PeerHost;
            _chat.PeerPort = fresh.PeerPort;
            _chat.RelayRouteBlob = fresh.RelayRouteBlob;
            RebuildRouteFromChat();
        }
    }

    private async Task ResetCryptoStateAsync(CancellationToken cancellationToken)
    {
        if (_messenger != null)
            await _messenger.StopAsync(cancellationToken).ConfigureAwait(false);

        if (_incomingTask != null)
        {
            try
            {
                await _incomingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            _incomingTask = null;
        }

        lock (_sync)
        {
            _messenger = null;
            _session = null;
            _incomingStarted = false;
        }
    }

    private async Task ProcessGatewayDatagramAsync(ReadOnlyMemory<byte> bufMem, TransportAddress from)
    {
        var token = _cts!.Token;
        var buf = bufMem.ToArray();
        if (buf.Length == 0)
            return;

        if (buf[0] == FrameHandshake && buf.Length == 129)
        {
            var handshake = new byte[128];
            Buffer.BlockCopy(buf, 1, handshake, 0, 128);
            await HandleResponderHandshakeAsync(handshake, token).ConfigureAwait(false);
            return;
        }

        if (buf[0] == FrameCipher && buf.Length > 1)
        {
            var inner = new byte[buf.Length - 1];
            Buffer.BlockCopy(buf, 1, inner, 0, inner.Length);
            await _bridge.Writer.WriteAsync(new TransportReceiveMessage(inner, from), token).ConfigureAwait(false);
        }
    }

    private async ValueTask SendRouteRawAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
    {
        if (_gateway != null)
            await _gateway.SendP2pPayloadAsync(packet, _route, cancellationToken).ConfigureAwait(false);
        else
            await _udp!.SendAsync(packet, _peerAddress!, cancellationToken).ConfigureAwait(false);
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
            await SendRouteRawAsync(packet, cancellationToken).ConfigureAwait(false);

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
        if (_gateway != null)
            _gateway.SetChatSink(null);

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
        private readonly Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> _sendRaw;
        private readonly Channel<TransportReceiveMessage> _bridge;

        public PrefixedCipherTransport(Channel<TransportReceiveMessage> bridge,
            Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> sendRaw)
        {
            _bridge = bridge;
            _sendRaw = sendRaw;
        }

        public TransportKind Kind => TransportKind.Udp;

        public ChannelReader<TransportReceiveMessage> Inbound => _bridge.Reader;

        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask StopAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
            CancellationToken cancellationToken = default)
        {
            _ = destination;
            var buf = new byte[payload.Length + 1];
            buf[0] = FrameCipher;
            payload.CopyTo(buf.AsMemory(1));
            await _sendRaw(buf, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
