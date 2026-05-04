using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using ShortP2P.Auth;
using ShortP2P.Auth.Data;
using ShortP2P.Client.ChatMedia;
using ShortP2P.Client.Data;
using ShortP2P.Client.Qr;
using ShortP2P.Client.Routing;
using ShortP2P.Crypto;
using ShortP2P.Discovery;
using ShortP2P.Messenger;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Services;

/// <summary>
///     One chat: UDP transport, RSA handshake (0x01 + 128 bytes), encrypted messenger frames (0x02 + ciphertext).
/// </summary>
public sealed class ChatP2pSession(
    ChatEntity chat,
    UserEntity user,
    AuthService auth,
    ChatRepository repo,
    SynchronizationContext? uiSynchronizationContext = null,
    P2pRoutingSettings? routingSettings = null,
    LocalNetworkScanner? localNetworkScanner = null,
    ChatMediaOptions? chatMediaOptions = null,
    ITransport? bluetoothTransport = null)
    : IAsyncDisposable
{
    private const byte FrameHandshake = 0x01;
    private const byte FrameCipher = 0x02;
    public const int MaxMessageChars = 32768;
    private static readonly TimeSpan DecryptRecoveryCooldown = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _flushPendingSem = new(1, 1);

    private readonly GuaranteedDeliveryPolicy _guaranteedDelivery = new();
    private readonly ChatMediaOptions _media = chatMediaOptions ?? new ChatMediaOptions();
    private readonly List<int> _pendingOutgoing = [];

    private readonly object _pendingSync = new();
    private readonly SemaphoreSlim _sessionSetup = new(1, 1);

    private readonly object _sync = new();
    private Channel<TransportReceiveMessage> _bridge = Channel.CreateUnbounded<TransportReceiveMessage>();
    private CancellationTokenSource? _cts;
    private int _decryptRecoveryGate;
    private bool _incomingStarted;
    private Task? _incomingTask;
    private DateTimeOffset _lastDecryptRecoveryUtc = DateTimeOffset.MinValue;
    private MessengerService? _messenger;

    private TransportAddress? _peerAddress;
    private List<TransportAddress> _peerEndpoints = [];
    private RsaPublicKey? _peerPublicKey;
    private PrefixedCipherTransport? _prefixed;
    private volatile bool _presenceHooked;
    private P2PSession? _session;
    private AdaptiveChatTransportLayer? _transportLayer;
    private Task? _transportReceiveTask;
    private UdpTransport? _udp;

    public ValueTask DisposeAsync()
    {
        return StopAsync();
    }

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
        _peerEndpoints = PeerTransportEndpoints.Parse(chat).ToList();
        if (_peerEndpoints.Count == 0)
        {
            var primary = PeerHostList.PrimaryHost(chat.PeerHost);
            if (IPAddress.TryParse(primary, out var ip))
                _peerEndpoints.Add(UdpTransportAddress.FromIPEndPoint(new IPEndPoint(ip, chat.PeerPort)));
            else if (BluetoothTransportAddress.TryParseMac(primary, out var mac))
                _peerEndpoints.Add(BluetoothTransportAddress.FromMac(mac));
            else
                throw new FormatException(
                    $"Unsupported peer host format: '{primary}'. Expected IPv4/IPv6 or Bluetooth MAC.");
        }

        _peerAddress = _peerEndpoints[0];
        foreach (var ep in _peerEndpoints)
            if (localNetworkScanner != null)
            {
                if (ep.Kind == TransportKind.Bluetooth)
                    localNetworkScanner.RememberBluetoothPeer(ep);
                else if (ep.Kind == TransportKind.Udp)
                    try
                    {
                        var ip = UdpTransportAddress.ToIPEndPoint(ep).Address.ToString();
                        localNetworkScanner.RememberUdpPresenceTarget(ip);
                    }
                    catch
                    {
                        // invalid UDP endpoint payload in storage
                    }
            }
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
        if (!IsTransportEnabled(from.Kind))
            return false;
        if (_peerEndpoints.Any(x => x.Kind == from.Kind && x.Data.AsSpan().SequenceEqual(from.Data)))
            return true;
        try
        {
            if (from.Kind == TransportKind.Udp)
            {
                var ep = UdpTransportAddress.ToIPEndPoint(from);
                foreach (var h in PeerHostList.ParseCandidates(chat.PeerHost))
                {
                    if (!IPAddress.TryParse(h, out var ip))
                        continue;
                    if (ep.Address.Equals(ip))
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

        if (IsTransportEnabled(TransportKind.Udp))
        {
            _udp = UdpTransport.CreateUdpTransport(IPAddress.Any, user.DataUdpPort);
            await _udp.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _udp = null;
        }

        if (bluetoothTransport != null && IsTransportEnabled(TransportKind.Bluetooth))
            await bluetoothTransport.StartAsync(cancellationToken).ConfigureAwait(false);
        _prefixed = new PrefixedCipherTransport(_bridge,
            async (mem, dest, ct) =>
            {
                await ResolveTransportForAddress(dest).SendAsync(mem, dest, ct).ConfigureAwait(false);
            });

        _transportLayer = new AdaptiveChatTransportLayer(
            () => _peerEndpoints.ToArray(),
            ResolveTransportForAddressOrNull,
            GetInboundTransports,
            IsTransportEnabled,
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

        HookPresenceForPendingFlush();
    }

    /// <summary>
    ///     Сохраняет новый IP/порт пира (прямой UDP), убирает цепочку ретрансляции и сбрасывает шифросессию.
    /// </summary>
    public async Task ApplyPeerEndpointAsync(string peerHost, int peerPort,
        CancellationToken cancellationToken = default)
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
        var host = BuildInviteHosts();
        var nid = CompressedNetworkId.FromShortString(user.NetworkIdShort);
        var invite = ChatInviteCodec.Build(user.Nickname, nid,
            RsaKeySerializer.SerializePublic(auth.GetCurrentPublicKey()), host, ChatInviteCodec.InviteUdpPort);
        await SendRouteRawAsync(invite, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildInviteHosts()
    {
        return LocalIPv4Resolver.GetInviteHostsCommaSeparated(TimeSpan.FromSeconds(2));
    }

    private async Task SendChatInviteWithRetryAsync(CancellationToken cancellationToken)
    {
        const int fallbackAttempts = 3;
        var attempts = Math.Max(1, routingSettings?.SendFailureSearchAttempts ?? fallbackAttempts);
        var delay = routingSettings?.SendFailureRetryDelay ?? TimeSpan.FromMilliseconds(350);

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

    private static bool IsDeferrableSendFailure(Exception ex)
    {
        return ex switch
        {
            IOException => true,
            TimeoutException => true,
            SocketException => true,
            TaskCanceledException => true,
            _ => ex.InnerException != null && IsDeferrableSendFailure(ex.InnerException)
        };
    }

    private bool CanQueueUntilPeerSeenOnLan()
    {
        return localNetworkScanner != null && !string.IsNullOrWhiteSpace(chat.PeerNetworkIdShort);
    }

    private void HookPresenceForPendingFlush()
    {
        if (localNetworkScanner == null || _presenceHooked)
            return;
        localNetworkScanner.ClientsChanged += OnLanClientsChangedForPendingFlush;
        localNetworkScanner.DiscoveryPingReceived += OnDiscoveryPingForPendingFlush;
        _presenceHooked = true;
    }

    private void UnhookPresenceAndClearPending()
    {
        lock (_pendingSync)
        {
            _pendingOutgoing.Clear();
        }

        if (!_presenceHooked || localNetworkScanner == null)
            return;
        localNetworkScanner.ClientsChanged -= OnLanClientsChangedForPendingFlush;
        localNetworkScanner.DiscoveryPingReceived -= OnDiscoveryPingForPendingFlush;
        _presenceHooked = false;
    }

    private bool HasPendingOutgoing()
    {
        lock (_pendingSync)
        {
            return _pendingOutgoing.Count > 0;
        }
    }

    private void OnLanClientsChangedForPendingFlush(object? sender, EventArgs e)
    {
        if (!HasPendingOutgoing())
            return;
        if (!localNetworkScanner!.IsPeerSeenRecentlyOnLan(chat.PeerNetworkIdShort))
            return;
        StartFlushPendingInBackground();
    }

    private void OnDiscoveryPingForPendingFlush(object? sender, DiscoveryPingReceivedEventArgs e)
    {
        if (!HasPendingOutgoing())
            return;
        if (string.IsNullOrWhiteSpace(chat.PeerNetworkIdShort))
            return;
        string peerShort;
        try
        {
            peerShort = CompressedNetworkId.FromGuid(e.Peer.NetworkId).ToShortString();
        }
        catch (FormatException)
        {
            return;
        }

        if (!string.Equals(peerShort, chat.PeerNetworkIdShort.Trim(), StringComparison.OrdinalIgnoreCase))
            return;
        StartFlushPendingInBackground();
    }

    private void StartFlushPendingInBackground()
    {
        var cts = _cts;
        if (cts == null || cts.IsCancellationRequested)
            return;
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await TryFlushPendingOutgoingAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
            catch
            {
                // сеть / таймауты
            }
        }, token);
    }

    private async Task TryFlushPendingOutgoingAsync(CancellationToken cancellationToken)
    {
        if (_udp == null || _transportLayer == null)
            return;

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

                var row = await repo.GetMessageAsync(nextId).ConfigureAwait(false);
                if (row == null || row.ChatId != chat.Id || !row.Outgoing)
                {
                    lock (_pendingSync)
                    {
                        if (_pendingOutgoing.Count > 0 && _pendingOutgoing[0] == nextId)
                            _pendingOutgoing.RemoveAt(0);
                    }

                    continue;
                }

                try
                {
                    var wire = BuildOutgoingWire(row);
                    await DeliverOutgoingWireAsync(nextId, wire, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    return;
                }

                lock (_pendingSync)
                {
                    if (_pendingOutgoing.Count > 0 && _pendingOutgoing[0] == nextId)
                        _pendingOutgoing.RemoveAt(0);
                }
            }
        }
        finally
        {
            _flushPendingSem.Release();
        }
    }

    private static byte[] BuildOutgoingWire(ChatMessageEntity row)
    {
        return row.PayloadKind switch
        {
            (int)ChatPayloadKind.File when row.ImageBlob == null || row.ImageBlob.Length == 0 =>
                throw new InvalidOperationException("File message has no payload."),
            (int)ChatPayloadKind.File => ChatWireCodec.EncodeFile(row.Text, row.MimeType, row.ImageBlob),
            (int)ChatPayloadKind.Image when row.ImageBlob == null || row.ImageBlob.Length == 0 =>
                throw new InvalidOperationException("Image message has no payload."),
            (int)ChatPayloadKind.Image => ChatWireCodec.EncodeImage(row.MimeType, row.ImageBlob),
            _ => ChatWireCodec.EncodeText(row.Text)
        };
    }

    private async Task DeliverOutgoingWireAsync(int messageId, byte[] wire, CancellationToken cancellationToken)
    {
        var ackTimeout = (routingSettings?.LinkTechnology ?? LinkTechnologyPreset.Unlimited).GetMessageAckTimeout();
        await _guaranteedDelivery.ExecuteAsync(
            async ct =>
            {
                await EnsureSessionAsInitiatorAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(chat.RelayRouteBlob))
                {
                    var dests = BuildOrderedDirectPeerAddresses();
                    await _messenger!.SendBinaryAsyncExpectAck(wire, dests, ackTimeout, ct).ConfigureAwait(false);
                }
                else
                {
                    await _messenger!.SendBinaryAsync(wire, _peerAddress!, ct).ConfigureAwait(false);
                }
            },
            null,
            false,
            routingSettings,
            cancellationToken).ConfigureAwait(false);

        await repo.UpdateMessageDeliveryStatusAsync(messageId, MessageDeliveryStatus.Delivered).ConfigureAwait(false);
        RaiseMessagesChanged();
    }

    public async ValueTask SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
            return;
        if (text.Length > MaxMessageChars)
            throw new ArgumentException($"Message is too long. Max length is {MaxMessageChars} characters.",
                nameof(text));

        var messageId = await repo.AddMessageAsync(chat.Id, true, text, MessageDeliveryStatus.Pending)
            .ConfigureAwait(false);
        RaiseMessagesChanged();

        try
        {
            var wire = ChatWireCodec.EncodeText(text);
            await DeliverOutgoingWireAsync(messageId, wire, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (CanQueueUntilPeerSeenOnLan() && !cancellationToken.IsCancellationRequested &&
                                   (ex is OperationCanceledException || IsDeferrableSendFailure(ex)))
        {
            lock (_pendingSync)
            {
                _pendingOutgoing.Add(messageId);
            }

            throw new OutboundMessageQueuedException();
        }
        catch (Exception)
        {
            await repo.UpdateMessageDeliveryStatusAsync(messageId, MessageDeliveryStatus.Failed).ConfigureAwait(false);
            RaiseMessagesChanged();
            throw;
        }
    }

    public async ValueTask SendImageAsync(ReadOnlyMemory<byte> imageBytes, string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (imageBytes.Length == 0)
            throw new ArgumentException("Image is empty.", nameof(imageBytes));
        _media.ValidateMime(mimeType);
        _media.ValidateSize(imageBytes.Length);

        var bytes = imageBytes.ToArray();
        var messageId = await repo.AddImageMessageAsync(chat.Id, true, mimeType, bytes, MessageDeliveryStatus.Pending)
            .ConfigureAwait(false);
        RaiseMessagesChanged();

        var wire = ChatWireCodec.EncodeImage(mimeType, bytes);
        try
        {
            await DeliverOutgoingWireAsync(messageId, wire, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (CanQueueUntilPeerSeenOnLan() && !cancellationToken.IsCancellationRequested &&
                                   (ex is OperationCanceledException || IsDeferrableSendFailure(ex)))
        {
            lock (_pendingSync)
            {
                _pendingOutgoing.Add(messageId);
            }

            throw new OutboundMessageQueuedException();
        }
        catch (Exception)
        {
            await repo.UpdateMessageDeliveryStatusAsync(messageId, MessageDeliveryStatus.Failed).ConfigureAwait(false);
            RaiseMessagesChanged();
            throw;
        }
    }

    public async ValueTask SendFileAsync(string fileName, ReadOnlyMemory<byte> fileBytes, string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (fileBytes.Length == 0)
            throw new ArgumentException("File is empty.", nameof(fileBytes));
        _media.ValidateDocumentMime(mimeType);
        _media.ValidateDocumentSize(fileBytes.Length);

        var bytes = fileBytes.ToArray();
        var safeName = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrEmpty(safeName))
            safeName = "file";

        var messageId = await repo.AddFileMessageAsync(chat.Id, true, safeName, mimeType, bytes,
                MessageDeliveryStatus.Pending)
            .ConfigureAwait(false);
        RaiseMessagesChanged();

        var wire = ChatWireCodec.EncodeFile(safeName, mimeType, bytes);
        try
        {
            await DeliverOutgoingWireAsync(messageId, wire, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (CanQueueUntilPeerSeenOnLan() && !cancellationToken.IsCancellationRequested &&
                                   (ex is OperationCanceledException || IsDeferrableSendFailure(ex)))
        {
            lock (_pendingSync)
            {
                _pendingOutgoing.Add(messageId);
            }

            throw new OutboundMessageQueuedException();
        }
        catch (Exception)
        {
            await repo.UpdateMessageDeliveryStatusAsync(messageId, MessageDeliveryStatus.Failed).ConfigureAwait(false);
            RaiseMessagesChanged();
            throw;
        }
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
        if (_peerAddress != null && IsTransportEnabled(_peerAddress.Kind))
            list.Add(_peerAddress);
        foreach (var ep in _peerEndpoints)
        {
            if (!IsTransportEnabled(ep.Kind))
                continue;
            if (list.Any(x => x.Kind == ep.Kind && x.Data.AsSpan().SequenceEqual(ep.Data)))
                continue;
            list.Add(ep);
        }

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
                _messenger =
                    new MessengerService(_prefixed!, _session, CreateMessengerOptions(), OnDecryptFailureAsync);
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

    private MessengerOptions CreateMessengerOptions()
    {
        return new MessengerOptions { MaxBinaryMessageBytes = _media.MaxMessengerBinaryBytes };
    }

    private async Task TransportReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var layer = _transportLayer;
        if (layer == null)
            return;

        await foreach (var msg in layer.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            try
            {
                var buf = msg.Payload.ToArray();
                if (buf.Length == 0)
                    continue;

                switch (buf[0])
                {
                    case ChatInviteCodec.FrameChatInvite:
                        await IncomingChatInviteHandler.TryAcceptAsync(buf, auth, repo,
                            async (payload, dest, ct) =>
                            {
                                await ResolveTransportForAddress(dest).SendAsync(payload, dest, ct)
                                    .ConfigureAwait(false);
                            }, msg.RemoteAddress, cancellationToken).ConfigureAwait(false);
                        continue;
                    case FrameHandshake when buf.Length == 129:
                    {
                        var handshake = new byte[128];
                        Buffer.BlockCopy(buf, 1, handshake, 0, 128);
                        await HandleResponderHandshakeAsync(handshake, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                if (buf[0] != FrameCipher || buf.Length <= 1)
                    continue;

                var inner = new byte[buf.Length - 1];
                Buffer.BlockCopy(buf, 1, inner, 0, inner.Length);
                await _bridge.Writer
                    .WriteAsync(new TransportReceiveMessage(inner, msg.RemoteAddress), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore 
                continue;
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
                _messenger =
                    new MessengerService(_prefixed!, _session, CreateMessengerOptions(), OnDecryptFailureAsync);
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
                var payload = incoming.Payload.ToArray();
                if (ChatWireCodec.TryParse(payload, out var wire) && wire != null)
                {
                    switch (wire)
                    {
                        case ChatWireText t:
                            _ = await repo.AddMessageAsync(chat.Id, false, t.Text).ConfigureAwait(false);
                            break;
                        case ChatWireImage img:
                            _ = await repo.AddImageMessageAsync(chat.Id, false, img.MimeType, img.ImageBytes)
                                .ConfigureAwait(false);
                            break;
                        case ChatWireFile f:
                            try
                            {
                                _media.ValidateDocumentMime(f.MimeType);
                                _media.ValidateDocumentSize(f.FileBytes.Length);
                                _ = await repo.AddFileMessageAsync(chat.Id, false, f.FileName, f.MimeType, f.FileBytes)
                                    .ConfigureAwait(false);
                            }
                            catch
                            {
                                _ = await repo.AddMessageAsync(chat.Id, false,
                                        "[Входящий файл отклонён: неподдерживаемый тип или размер.]")
                                    .ConfigureAwait(false);
                            }

                            break;
                    }
                }
                else if (ChatWireCodec.LooksLikeFramedWire(payload))
                {
                    _ = await repo.AddMessageAsync(chat.Id, false,
                            "[Входящее сообщение не распознано. Обновите клиент.]")
                        .ConfigureAwait(false);
                }
                else
                {
                    var text = Encoding.UTF8.GetString(payload);
                    _ = await repo.AddMessageAsync(chat.Id, false, text).ConfigureAwait(false);
                }

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
        UnhookPresenceAndClearPending();

        if (_cts != null)
            await _cts.CancelAsync();

        if (_transportLayer != null)
            await _transportLayer.StopAsync(cancellationToken).ConfigureAwait(false);

        if (_messenger != null)
            await _messenger.StopAsync(cancellationToken).ConfigureAwait(false);

        if (_transportReceiveTask != null)
            try
            {
                await _transportReceiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }

        if (_incomingTask != null)
            try
            {
                await _incomingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }

        if (_udp != null)
            await _udp.StopAsync(cancellationToken).ConfigureAwait(false);
        if (bluetoothTransport != null)
            await bluetoothTransport.StopAsync(cancellationToken).ConfigureAwait(false);

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

    private IReadOnlyList<ITransport> GetInboundTransports()
    {
        var list = new List<ITransport>();
        if (_udp != null && IsTransportEnabled(TransportKind.Udp))
            list.Add(_udp);
        if (bluetoothTransport != null && IsTransportEnabled(TransportKind.Bluetooth))
            list.Add(bluetoothTransport);
        return list;
    }

    private ITransport ResolveTransportForAddress(TransportAddress destination)
    {
        return ResolveTransportForAddressOrNull(destination) ??
               throw new InvalidOperationException($"Transport is not started for {destination.Kind}.");
    }

    private ITransport? ResolveTransportForAddressOrNull(TransportAddress destination)
    {
        return destination.Kind switch
        {
            TransportKind.Udp when IsTransportEnabled(TransportKind.Udp) => _udp,
            TransportKind.Bluetooth when IsTransportEnabled(TransportKind.Bluetooth) => bluetoothTransport,
            _ => null
        };
    }

    private bool IsTransportEnabled(TransportKind kind)
    {
        return kind switch
        {
            TransportKind.Udp => routingSettings?.EnableUdpTransport ?? true,
            TransportKind.Bluetooth => routingSettings?.EnableBluetoothTransport ?? true,
            _ => false
        };
    }

    private sealed class PrefixedCipherTransport(
        Channel<TransportReceiveMessage> bridge,
        Func<ReadOnlyMemory<byte>, TransportAddress, CancellationToken, ValueTask> sendRaw)
        : ITransport
    {
        public TransportKind Kind => TransportKind.Udp;

        public ChannelReader<TransportReceiveMessage> Inbound => bridge.Reader;

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
            CancellationToken cancellationToken = default)
        {
            var buf = new byte[payload.Length + 1];
            buf[0] = FrameCipher;
            payload.CopyTo(buf.AsMemory(1));
            await sendRaw(buf.AsMemory(0, buf.Length), destination, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}