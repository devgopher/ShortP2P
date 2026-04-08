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
public sealed class ChatP2pSession(
    ChatEntity chat,
    UserEntity user,
    AuthService auth,
    ChatRepository repo,
    SynchronizationContext? uiSynchronizationContext = null,
    SharedUserUdpGateway? sharedGateway = null,
    P2pRoutingSettings? routingSettings = null)
    : IAsyncDisposable
{
    private const byte FrameHandshake = 0x01;
    private const byte FrameCipher = 0x02;
    public const int MaxMessageChars = 32768;

    private readonly P2pRoutingSettings? _routing = routingSettings ?? (sharedGateway != null ? new P2pRoutingSettings() : null);
    private readonly GuaranteedDeliveryPolicy _guaranteedDelivery = new();
    private readonly Guid _peerNetworkId = CompressedNetworkId.FromShortString(chat.PeerNetworkIdShort).Value;

    private readonly object _sync = new();
    private readonly SemaphoreSlim _sessionSetup = new(1, 1);
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
        _peerAddress = UdpTransportAddress.FromIPEndPoint(new IPEndPoint(IPAddress.Parse(chat.PeerHost), chat.PeerPort));
        _route = ChatRelayRoute.FromChat(_peerAddress, chat.RelayRouteBlob);
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);

        _bridge = Channel.CreateUnbounded<TransportReceiveMessage>();
        
        RebuildRouteFromChat();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (sharedGateway != null)
        {
            await sharedGateway.EnsureStartedAsync(user, cancellationToken).ConfigureAwait(false);
            _prefixed = new PrefixedCipherTransport(_bridge, async (mem, ct) =>
            {
                await SendRouteRawAsync(mem, ct).ConfigureAwait(false);
            });
        }
        else
        {
            _udp = new UdpTransport(user.DataUdpPort);
            await _udp.StartAsync(cancellationToken).ConfigureAwait(false);
            _prefixed = new PrefixedCipherTransport(_bridge, async (mem, ct) =>
            {
                await _udp!.SendAsync(mem, _peerAddress!, ct).ConfigureAwait(false);
            });
        }

        _transportLayer = new AdaptiveChatTransportLayer(
            sharedGateway,
            () => _route,
            () => _peerAddress,
            () => _udp,
            _peerNetworkId);
        await _transportLayer.StartAsync(_cts.Token).ConfigureAwait(false);
        _transportReceiveTask = Task.Run(() => TransportReceiveLoopAsync(_cts.Token), _cts.Token);

        try
        {
            await SendChatInviteAsync(cancellationToken).ConfigureAwait(false);
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

        await repo.UpdateChatP2pRouteAsync(chat.Id, peerHost, peerPort, null).ConfigureAwait(false);
        chat.PeerHost = peerHost;
        chat.PeerPort = peerPort;
        chat.RelayRouteBlob = null;
        RebuildRouteFromChat();
        await ResetCryptoStateAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SendChatInviteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // ignored
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

    public async ValueTask SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
            return;
        if (text.Length > MaxMessageChars)
            throw new ArgumentException($"Message is too long. Max length is {MaxMessageChars} characters.",
                nameof(text));
        var bytes = Encoding.UTF8.GetBytes(text);
        var shouldRetry = sharedGateway != null && _routing != null;

        await _guaranteedDelivery.ExecuteAsync(
            async ct =>
            {
                await EnsureSessionAsInitiatorAsync(ct).ConfigureAwait(false);
                await _messenger!.SendBinaryAsync(bytes, _peerAddress!, ct).ConfigureAwait(false);
            },
            async ct =>
            {
                await TryRefreshRouteViaSearchAsync(ct).ConfigureAwait(false);
                await ResetCryptoStateAsync(ct).ConfigureAwait(false);
            },
            shouldRetry,
            _routing,
            cancellationToken).ConfigureAwait(false);

        await repo.AddMessageAsync(chat.Id, true, text).ConfigureAwait(false);
        RaiseMessagesChanged();
    }

    private async Task TryRefreshRouteViaSearchAsync(CancellationToken cancellationToken)
    {
        if (sharedGateway == null) return;
        var targetId = CompressedNetworkId.FromShortString(chat.PeerNetworkIdShort).Value;
        var found = await sharedGateway.SearchPeerAsync(targetId, chat.PeerNickname, cancellationToken)
            .ConfigureAwait(false);
        if (found == null)
            return;

        var direct = UdpTransportAddress.FromIPEndPoint(new IPEndPoint(IPAddress.Parse(found.PeerHost), found.PeerPort));
        string? blob = null;
        if (found is { FirstRelayHop: not null, RelayStrip.Count: > 0 })
        {
            blob = ChatRelayRoute.SerializeBlob(new ChatRelayRoute
            {
                Direct = direct,
                FirstHop = found.FirstRelayHop,
                RelayStrip = found.RelayStrip,
            });
        }

        await repo.UpdateChatP2pRouteAsync(chat.Id, found.PeerHost, found.PeerPort, blob).ConfigureAwait(false);
        var fresh = await repo.GetChatAsync(chat.Id).ConfigureAwait(false);
        if (fresh != null)
        {
            chat.PeerHost = fresh.PeerHost;
            chat.PeerPort = fresh.PeerPort;
            chat.RelayRouteBlob = fresh.RelayRouteBlob;
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
                    await IncomingChatInviteHandler.TryAcceptAsync(buf, auth, repo, cancellationToken)
                        .ConfigureAwait(false);
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
        if (sharedGateway != null)
            sharedGateway.SetChatSink(null);

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
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> sendRaw)
        : ITransport
    {
        public TransportKind Kind => TransportKind.Udp;

        public ChannelReader<TransportReceiveMessage> Inbound => bridge.Reader;

        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask StopAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
            CancellationToken cancellationToken = default)
        {
            _ = destination;
            var buf = new byte[payload.Length + 1];
            buf[0] = FrameCipher;
            payload.CopyTo(buf.AsMemory(1));
            await sendRaw(buf, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
