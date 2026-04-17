using System.Net;
using System.Text;
using ShortP2P.Client;
using System.Threading;
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
/// </summary>
public sealed class ChatP2pSession(
    ChatEntity chat,
    UserEntity user,
    AuthService auth,
    ChatRepository repo,
    SynchronizationContext? uiSynchronizationContext = null,
    P2pRoutingSettings? routingSettings = null)
    : IAsyncDisposable
{
    private const byte FrameHandshake = 0x01;
    private const byte FrameCipher = 0x02;
    public const int MaxMessageChars = 32768;

    private readonly P2pRoutingSettings? _routing = routingSettings;
    private readonly GuaranteedDeliveryPolicy _guaranteedDelivery = new();

    private readonly object _sync = new();
    private readonly SemaphoreSlim _sessionSetup = new(1, 1);
    private int _decryptRecoveryGate;
    private DateTimeOffset _lastDecryptRecoveryUtc = DateTimeOffset.MinValue;
    private static readonly TimeSpan DecryptRecoveryCooldown = TimeSpan.FromSeconds(10);
    private UdpTransport? _udp;
    private Channel<TransportReceiveMessage> _bridge = Channel.CreateUnbounded<TransportReceiveMessage>();
    private PrefixedCipherTransport? _prefixed;
    private MessengerService? _messenger;
    private P2PSession? _session;
    private CancellationTokenSource? _cts;
    private Task? _transportReceiveTask;
    private Task? _incomingTask;
    private bool _incomingStarted;
    private AdaptiveChatTransportLayer? _transportLayer;

    private TransportAddress? _peerAddress;
    private ChatRelayRoute _route = null!;
    private RsaPublicKey? _peerPublicKey;

    public event EventHandler? MessagesChanged;

    private void RaiseMessagesChanged()
    {
        if (uiSynchronizationContext != null)
            uiSynchronizationContext.Post(_ => MessagesChanged?.Invoke(this, EventArgs.Empty), null);
        else
            MessagesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildRouteFromChat()
    {
        _peerPublicKey = RsaKeySerializer.DeserializePublic(chat.PeerRsaPublicJson);
        var primary = PeerHostList.PrimaryHost(chat.PeerHost);
        _peerAddress = UdpTransportAddress.FromIPEndPoint(new IPEndPoint(IPAddress.Parse(primary), chat.PeerPort));
        _route = ChatRelayRoute.FromChat(_peerAddress, chat.RelayRouteBlob);
    }

    /// <summary>Обновляет строку чата из БД (тот же Id), не создавая новую сессию.</summary>
    public void ApplyChatRow(ChatEntity row)
    {
        if (row.Id != chat.Id)
            throw new ArgumentException("Chat id mismatch.", nameof(row));
        chat.PeerNickname = row.PeerNickname;
        chat.PeerNetworkIdShort = row.PeerNetworkIdShort;
        chat.PeerRsaPublicJson = row.PeerRsaPublicJson;
        chat.PeerHost = row.PeerHost;
        chat.PeerPort = row.PeerPort;
        chat.RelayRouteBlob = row.RelayRouteBlob;
        chat.UpdatedUtcTicks = row.UpdatedUtcTicks;
        RebuildRouteFromChat();
    }

    private bool ShouldAcceptIncomingFrom(TransportAddress from)
    {
        try
        {
            var ep = UdpTransportAddress.ToIPEndPoint(from);
            if (ep.Port == chat.PeerPort)
            {
                foreach (var h in PeerHostList.ParseCandidates(chat.PeerHost))
                {
                    if (IPAddress.TryParse(h, out var ip) && ep.Address.Equals(ip))
                        return true;
                }
            }

            if (!string.IsNullOrEmpty(chat.RelayRouteBlob))
                return true;
        }
        catch
        {
            // ignore
        }

        return false;
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);

        _bridge = Channel.CreateUnbounded<TransportReceiveMessage>();
        
        RebuildRouteFromChat();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _udp = new UdpTransport(user.DataUdpPort);
        await _udp.StartAsync(cancellationToken).ConfigureAwait(false);
        _prefixed = new PrefixedCipherTransport(_bridge, async (mem, dest, ct) =>
        {
            await _udp!.SendAsync(mem, dest, ct).ConfigureAwait(false);
        });

        _transportLayer = new AdaptiveChatTransportLayer(
            () => _peerAddress,
            () => _udp,
            ShouldAcceptIncomingFrom);
        await _transportLayer.StartAsync(_cts.Token).ConfigureAwait(false);
        _transportReceiveTask = Task.Run(() => TransportReceiveLoopAsync(_cts.Token), _cts.Token);

        try
        {
            await SendChatInviteWithRetryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // пир офлайн или сеть недоступна
        }

    }

    /// <summary>
    ///     Сохраняет новый IP/порт пира (прямой UDP), убирает цепочку ретрансляции и сбрасывает шифросессию.
    /// </summary>
    public async Task ApplyPeerEndpointAsync(string peerHost, int peerPort, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerHost);
        peerHost = peerHost.Trim();
        _ = IPAddress.Parse(peerHost);
        if (peerPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(peerPort));

        var mergedHost = PeerHostList.WithPrimaryFirst(chat.PeerHost, peerHost);
        await repo.UpdateChatP2pRouteAsync(chat.Id, mergedHost, peerPort, null).ConfigureAwait(false);
        chat.PeerHost = mergedHost;
        chat.PeerPort = peerPort;
        chat.RelayRouteBlob = null;
        RebuildRouteFromChat();
        await ResetCryptoStateAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SendChatInviteWithRetryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }
    }

    private async Task SendChatInviteAsync(CancellationToken cancellationToken)
    {
        var host = LocalEndpointHelper.GetPreferredLanIPv4String();
        var nid = CompressedNetworkId.FromShortString(user.NetworkIdShort);
        var invite = ChatInviteCodec.Build(user.Nickname, nid,
            RsaKeySerializer.SerializePublic(auth.GetCurrentPublicKey()), host, user.DataUdpPort);
        await SendRouteRawAsync(invite, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendChatInviteWithRetryAsync(CancellationToken cancellationToken)
    {
        const int fallbackAttempts = 3;
        var attempts = Math.Max(1, _routing?.SendFailureSearchAttempts ?? fallbackAttempts);
        var delay = _routing?.SendFailureRetryDelay ?? TimeSpan.FromMilliseconds(350);

        for (var i = 0; i < attempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await SendChatInviteAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch when (i < attempts - 1)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
            return;
        if (text.Length > MaxMessageChars)
            throw new ArgumentException($"Message is too long. Max length is {MaxMessageChars} characters.",
                nameof(text));
        var bytes = Encoding.UTF8.GetBytes(text);
        var shouldRetry = false;

        var ackTimeout = (_routing?.LinkTechnology ?? LinkTechnologyPreset.Unlimited).GetMessageAckTimeout();

        await _guaranteedDelivery.ExecuteAsync(
            async ct =>
            {
                await EnsureSessionAsInitiatorAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(chat.RelayRouteBlob))
                {
                    var dests = BuildOrderedDirectPeerAddresses();
                    await _messenger!.SendBinaryAsyncExpectAck(bytes, dests, ackTimeout, ct).ConfigureAwait(false);
                }
                else
                    await _messenger!.SendBinaryAsync(bytes, _peerAddress!, ct).ConfigureAwait(false);
            },
            null,
            shouldRetry,
            _routing,
            cancellationToken).ConfigureAwait(false);

        await repo.AddMessageAsync(chat.Id, true, text).ConfigureAwait(false);
        RaiseMessagesChanged();
    }

    /// <summary>Сброс AES-сессии и повторный обмен ключами после ошибки дешифровки входящего пакета.</summary>
    private async ValueTask OnDecryptFailureAsync()
    {
        if (Interlocked.CompareExchange(ref _decryptRecoveryGate, 1, 0) != 0)
            return;
        try
        {
            var token = _cts?.Token ?? CancellationToken.None;
            if (token.IsCancellationRequested)
                return;

            var now = DateTimeOffset.UtcNow;
            if (now - _lastDecryptRecoveryUtc < DecryptRecoveryCooldown)
                return;
            _lastDecryptRecoveryUtc = now;

            await ResetCryptoStateAsync(token).ConfigureAwait(false);
            await SendChatInviteWithRetryAsync(token).ConfigureAwait(false);
            await EnsureSessionAsInitiatorAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch
        {
            // сеть, таймауты
        }
        finally
        {
            Interlocked.Exchange(ref _decryptRecoveryGate, 0);
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
                // ignore
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

    private async ValueTask SendRouteRawAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
    {
        var layer = _transportLayer ?? throw new InvalidOperationException("Transport layer is not initialized.");
        await layer.SendPacketAsync(packet, cancellationToken).ConfigureAwait(false);
    }

    private List<TransportAddress> BuildOrderedDirectPeerAddresses()
    {
        var list = new List<TransportAddress>();
        foreach (var h in PeerHostList.ParseCandidates(chat.PeerHost))
        {
            try
            {
                list.Add(UdpTransportAddress.FromIPEndPoint(new IPEndPoint(IPAddress.Parse(h), chat.PeerPort)));
            }
            catch
            {
                // skip
            }
        }

        if (list.Count == 0 && _peerAddress != null)
            list.Add(_peerAddress);
        return list;
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
                _messenger = new MessengerService(_prefixed!, _session, null, OnDecryptFailureAsync);
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

    private async Task TransportReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var layer = _transportLayer;
            if (layer == null)
                return;

            await foreach (var msg in layer.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var buf = msg.Payload.ToArray();
                if (buf.Length == 0)
                    continue;

                if (buf[0] == ChatInviteCodec.FrameChatInvite)
                {
                    await IncomingChatInviteHandler.TryAcceptAsync(buf, auth, repo,
                        async (payload, dest, ct) =>
                        {
                            await _udp!.SendAsync(payload, dest, ct).ConfigureAwait(false);
                        }, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (buf[0] == FrameHandshake && buf.Length == 129)
                {
                    var handshake = new byte[128];
                    Buffer.BlockCopy(buf, 1, handshake, 0, 128);
                    await HandleResponderHandshakeAsync(handshake, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (buf[0] != FrameCipher || buf.Length <= 1) 
                    continue;
                
                var inner = new byte[buf.Length - 1];
                Buffer.BlockCopy(buf, 1, inner, 0, inner.Length);
                await _bridge.Writer
                    .WriteAsync(new TransportReceiveMessage(inner, msg.RemoteAddress), cancellationToken)
                    .ConfigureAwait(false);
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
            MessengerService? created;
            lock (_sync)
            {
                if (_session != null)
                    return;

                var localPrivate = auth.GetCurrentPrivateKey();
                _session = P2PCrypto.CreateSession(localPrivate, handshakePacket);
                _messenger = new MessengerService(_prefixed!, _session, null, OnDecryptFailureAsync);
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
                await repo.AddMessageAsync(chat.Id, false, text).ConfigureAwait(false);
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
        if (_cts != null)
            await _cts.CancelAsync();

        if (_transportLayer != null)
            await _transportLayer.StopAsync(cancellationToken).ConfigureAwait(false);

        if (_messenger != null)
            await _messenger.StopAsync(cancellationToken).ConfigureAwait(false);

        if (_transportReceiveTask != null)
        {
            try
            {
                await _transportReceiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
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
                // ignore
            }
        }

        if (_udp != null)
            await _udp.StopAsync(cancellationToken).ConfigureAwait(false);

        _bridge.Writer.TryComplete();
        _cts?.Dispose();
        _cts = null;
        _udp = null;
        _prefixed = null;
        _transportLayer = null;
        _messenger = null;
        _session = null;
        _transportReceiveTask = null;
        _incomingTask = null;
        _incomingStarted = false;
    }

    public ValueTask DisposeAsync() => StopAsync();

    private sealed class PrefixedCipherTransport(
        Channel<TransportReceiveMessage> bridge,
        Func<ReadOnlyMemory<byte>, TransportAddress, CancellationToken, ValueTask> sendRaw)
        : ITransport
    {
        public TransportKind Kind => TransportKind.Udp;

        public ChannelReader<TransportReceiveMessage> Inbound => bridge.Reader;

        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask StopAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
            CancellationToken cancellationToken = default)
        {
            var buf = new byte[payload.Length + 1];
            buf[0] = FrameCipher;
            payload.CopyTo(buf.AsMemory(1));
            await sendRaw(buf.AsMemory(0, buf.Length), destination, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
