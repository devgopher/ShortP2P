using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Diagnostics.CodeAnalysis;
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
///     Чат: UDP/Bluetooth, RSA-handshake (0x01) только у пира с меньшим network id (сравнение Guid);
///     второй шлёт запрос 0x04+16 байт id и ждёт handshake. Шифрокадры 0x02.
/// </summary>
public sealed class ChatP2pSession : IAsyncDisposable
{
    private readonly ChatEntity chat;
    private readonly UserEntity user;
    private readonly AuthService auth;
    private readonly ChatRepository repo;
    private readonly SynchronizationContext? uiSynchronizationContext;
    private readonly P2pRoutingSettings? routingSettings;
    private readonly LocalNetworkScanner? localNetworkScanner;
    private readonly ITransport? bluetoothTransport;
    private readonly P2pCryptoSessionCache _cryptoSessionCache;

    private const byte FrameHandshake = 0x01;
    private const byte FrameCipher = 0x02;
    /// <summary>Запрос на установку сессии: только от пира с большим NetworkId; тело — 16 байт Guid отправителя.</summary>
    private const byte FrameSessionSetupRequest = 0x04;
    public const int MaxMessageChars = 32768;
    private static readonly TimeSpan DecryptRecoveryCooldown = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _flushPendingSem = new(1, 1);

    private readonly GuaranteedDeliveryPolicy _guaranteedDelivery = new();
    private readonly ChatMediaOptions _media;
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
    /// <summary>True только у лидера (меньший NetworkId): отправлен свой RSA-handshake; зонд ACK/OK только с лидера.</summary>
    private bool _handshakeWeInitiated;
    private TaskCompletionSource<bool>? _followerHandshakeTcs;
    private TaskCompletionSource<bool>? _cryptoProbeOkAwaiter;
    private volatile bool _cryptoProbeRoundTripOk;
    private readonly SemaphoreSlim _cryptoProbeLoopLock = new(1, 1);

    private TransportAddress? _peerAddress;
    private List<TransportAddress> _peerEndpoints = [];
    private RsaPublicKey? _peerPublicKey;
    private PrefixedCipherTransport? _prefixed;
    private volatile bool _presenceHooked;
    private AdaptiveChatTransportLayer? _transportLayer;
    private Task? _transportReceiveTask;
    private UdpTransport? _udp;

    private ChatP2pSession(
        ChatEntity chat,
        UserEntity user,
        AuthService auth,
        ChatRepository repo,
        SynchronizationContext? uiSynchronizationContext = null,
        P2pRoutingSettings? routingSettings = null,
        LocalNetworkScanner? localNetworkScanner = null,
        ChatMediaOptions? chatMediaOptions = null,
        ITransport? bluetoothTransport = null,
        P2pCryptoSessionCache? cryptoSessionCache = null)
    {
        this.chat = chat;
        this.user = user;
        this.auth = auth;
        this.repo = repo;
        this.uiSynchronizationContext = uiSynchronizationContext;
        this.routingSettings = routingSettings;
        this.localNetworkScanner = localNetworkScanner;
        this.bluetoothTransport = bluetoothTransport;
        _cryptoSessionCache = cryptoSessionCache ?? new P2pCryptoSessionCache();
        _media = chatMediaOptions ?? new ChatMediaOptions();
    }

    public static ChatP2pSession Create(
        ChatEntity chat,
        UserEntity user,
        AuthService auth,
        ChatRepository repo,
        SynchronizationContext? uiSynchronizationContext = null,
        P2pRoutingSettings? routingSettings = null,
        LocalNetworkScanner? localNetworkScanner = null,
        ChatMediaOptions? chatMediaOptions = null,
        ITransport? bluetoothTransport = null,
        P2pCryptoSessionCache? cryptoSessionCache = null)
    {
        return new ChatP2pSession(chat, user, auth, repo, uiSynchronizationContext, routingSettings,
            localNetworkScanner, chatMediaOptions, bluetoothTransport, cryptoSessionCache);
    }

    private bool TryGetCryptoSession([NotNullWhen(true)] out P2PSession? session) =>
        _cryptoSessionCache.TryGetSession(chat.Id, out session);
    private void ClearCryptoSession() => _cryptoSessionCache.TryRemove(chat.Id, out _);

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

        try
        {
            await EnsureSessionAsInitiatorAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // пир ещё не готов — сессия поднимется при первой отправке
        }

        _ = TryConfirmCryptoSessionAsync(cancellationToken);

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

        try
        {
            await EnsureSessionAsInitiatorAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        _ = TryConfirmCryptoSessionAsync(cancellationToken);
    }

    /// <summary>Временная отладка UI: сброс AES и повторная установка сессии по правилам лидера/подписчика.</summary>
    public async Task TechSendHandshakeAsync(CancellationToken cancellationToken = default)
    {
        if (_cts == null || _transportLayer == null)
            throw new InvalidOperationException("Сессия чата не запущена.");

        await ResetCryptoStateAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSessionAsInitiatorAsync(cancellationToken).ConfigureAwait(false);
        _ = TryConfirmCryptoSessionAsync(cancellationToken);
    }

    /// <summary>Временная отладка UI: presence ping (порт discovery) на адреса пира.</summary>
    public async Task TechSendPresencePingAsync(CancellationToken cancellationToken = default)
    {
        if (_cts == null)
            throw new InvalidOperationException("Сессия чата не запущена.");

        var nid = CompressedNetworkId.FromShortString(user.NetworkIdShort).Value;
        var link = routingSettings?.LinkTechnology ?? LinkTechnologyPreset.Unlimited;
        var ping = PresencePingCodec.Build(nid, user.Nickname, user.DataUdpPort, link);
        var sentUdpTargets = new HashSet<string>(StringComparer.Ordinal);

        var peers = BuildOrderedDirectPeerAddresses();
        peers.AddRange(BuildOrderedBroadcastAddresses());
        foreach (var addr in peers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (addr.Kind)
            {
                case TransportKind.Udp:
                {
                    var ipEp = UdpTransportAddress.ToIPEndPoint(addr);
                    var dest = UdpTransportAddress.FromIPEndPoint(
                        new IPEndPoint(ipEp.Address, PresencePingCodec.UdpPort));
                    var key = $"{ipEp.Address}:{PresencePingCodec.UdpPort}";
                    if (!sentUdpTargets.Add(key))
                        break;
                    await ResolveTransportForAddress(dest).SendAsync(ping, dest, cancellationToken).ConfigureAwait(false);
                    break;
                }
                case TransportKind.Bluetooth when bluetoothTransport != null:
                    await bluetoothTransport.SendAsync(ping, addr, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        foreach (var ep in EnumerateIpv4BroadcastEndpoints(PresencePingCodec.UdpPort))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = $"{ep.Address}:{ep.Port}";
            if (!sentUdpTargets.Add(key))
                continue;
            var dest = UdpTransportAddress.FromIPEndPoint(ep);
            await ResolveTransportForAddress(dest).SendAsync(ping, dest, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IEnumerable<IPEndPoint> EnumerateIpv4BroadcastEndpoints(int port)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ua.Address))
                    continue;
                var mask = ua.IPv4Mask;
                if (mask == null)
                    continue;
                var ep = new IPEndPoint(ComputeBroadcastAddress(ua.Address, mask), port);
                var key = $"{ep.Address}:{ep.Port}";
                if (seen.Add(key))
                    yield return ep;
            }
        }

        var limited = new IPEndPoint(IPAddress.Broadcast, port);
        var limitedKey = $"{limited.Address}:{limited.Port}";
        if (seen.Add(limitedKey))
            yield return limited;
    }

    private static IPAddress ComputeBroadcastAddress(IPAddress address, IPAddress mask)
    {
        var a = address.GetAddressBytes();
        var m = mask.GetAddressBytes();
        if (a.Length != 4 || m.Length != 4)
            throw new ArgumentException("IPv4 address and mask are required.");
        var b = new byte[4];
        for (var i = 0; i < 4; i++)
            b[i] = (byte)(a[i] | ~m[i]);
        return new IPAddress(b);
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
                if (_handshakeWeInitiated && !_cryptoProbeRoundTripOk)
                    _ = TryConfirmCryptoSessionAsync(ct);

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
            _ = TryConfirmCryptoSessionAsync(token);
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
            _cryptoProbeOkAwaiter?.TrySetCanceled();
            _cryptoProbeOkAwaiter = null;
            _followerHandshakeTcs?.TrySetCanceled();
            _followerHandshakeTcs = null;
            _handshakeWeInitiated = false;
            _cryptoProbeRoundTripOk = false;
            _messenger = null;
            _incomingStarted = false;
        }
        ClearCryptoSession();
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
    
    
    private List<TransportAddress> BuildOrderedBroadcastAddresses()
    {
        var endpoints = LanBroadcastHelper.GetIpv4BroadcastEndpoints(50101);// EnumerateBroadcastAddresses

        return endpoints.Select(UdpTransportAddress.FromIPEndPoint).ToList();
    }
    
    /// <summary>
    ///     Инициатор: ACK → ждём OK; без ответа — пауза 5 с ± 2 с (равномерно), сброс крипты, инвайт, handshake, снова.
    ///     Ответчик на ACK шлёт OK. Не пишется в БД. Один активный цикл на сессию.
    /// </summary>
    private async Task TryConfirmCryptoSessionAsync(CancellationToken cancellationToken)
    {
        if (_cryptoProbeRoundTripOk)
            return;

        if (!await _cryptoProbeLoopLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            if (!_handshakeWeInitiated)
                return;

            var okWait = TimeSpan.FromSeconds(10);

            while (!cancellationToken.IsCancellationRequested && !_cryptoProbeRoundTripOk)
            {
                MessengerService? ms;
                lock (_sync)
                    ms = _messenger;

                var canProbe = ms != null && _handshakeWeInitiated;
                if (canProbe)
                {
                    var my = user.NetworkIdShort.Trim();
                    var peer = chat.PeerNetworkIdShort.Trim();
                    var ackWire = ChatWireCodec.EncodeText(SessionCryptoProbe.FormatAck(my, peer));

                    TaskCompletionSource<bool> tcs;
                    lock (_sync)
                    {
                        _cryptoProbeOkAwaiter?.TrySetCanceled();
                        _cryptoProbeOkAwaiter = tcs =
                            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    }

                    try
                    {
                        await SendEncryptedProbeWireAsync(ackWire, cancellationToken).ConfigureAwait(false);
                        await tcs.Task.WaitAsync(okWait, cancellationToken).ConfigureAwait(false);
                        _cryptoProbeRoundTripOk = true;
                        return;
                    }
                    catch (TimeoutException)
                    {
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch
                    {
                        // сеть / отправка
                    }
                    finally
                    {
                        lock (_sync)
                        {
                            if (ReferenceEquals(_cryptoProbeOkAwaiter, tcs))
                                _cryptoProbeOkAwaiter = null;
                        }
                    }
                }

                if (_cryptoProbeRoundTripOk)
                    return;

                var pauseSec = 5 + Random.Shared.Next(-2, 3);
                await Task.Delay(TimeSpan.FromSeconds(pauseSec), cancellationToken).ConfigureAwait(false);

                try
                {
                    await ResetCryptoStateAsync(cancellationToken).ConfigureAwait(false);
                    await SendChatInviteWithRetryAsync(cancellationToken).ConfigureAwait(false);
                    await EnsureSessionAsInitiatorAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }
        finally
        {
            _cryptoProbeLoopLock.Release();
        }
    }

    private async Task SendEncryptedProbeWireAsync(byte[] wire, CancellationToken cancellationToken)
    {
        var m = _messenger ?? throw new InvalidOperationException("Messenger is not initialized.");
        if (string.IsNullOrEmpty(chat.RelayRouteBlob))
        {
            var dests = BuildOrderedDirectPeerAddresses();
            Exception? last = null;
            foreach (var d in dests)
            {
                try
                {
                    await m.SendBinaryAsync(wire, d, cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }

            if (last != null)
                throw last;
            throw new IOException("No direct peer address for crypto probe.");
        }

        await m.SendBinaryAsync(wire, _peerAddress!, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendCryptoProbeOkAsync(CancellationToken cancellationToken)
    {
        try
        {
            var m = _messenger;
            if (m == null)
                return;
            var my = user.NetworkIdShort.Trim();
            var peer = chat.PeerNetworkIdShort.Trim();
            var wire = ChatWireCodec.EncodeText(SessionCryptoProbe.FormatOk(my, peer));
            await SendEncryptedProbeWireAsync(wire, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }
    }

    private bool TryHandleSessionCryptoProbeText(string text, CancellationToken cancellationToken)
    {
        if (!SessionCryptoProbe.TryParse(text, out var kind, out var src, out var tgt))
            return false;
        var my = user.NetworkIdShort.Trim();
        var peer = chat.PeerNetworkIdShort.Trim();
        if (!string.Equals(tgt, my, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(src, peer, StringComparison.OrdinalIgnoreCase))
            return false;

        bool weInitiated;
        lock (_sync)
            weInitiated = _handshakeWeInitiated;

        if (kind == SessionCryptoProbeKind.Ack)
        {
            if (weInitiated)
                return false;
            _ = SendCryptoProbeOkAsync(cancellationToken);
            return true;
        }

        if (kind == SessionCryptoProbeKind.Ok)
        {
            if (!weInitiated)
                return false;
            TaskCompletionSource<bool>? w;
            lock (_sync)
            {
                w = _cryptoProbeOkAwaiter;
                _cryptoProbeOkAwaiter = null;
            }

            w?.TrySetResult(true);
            return true;
        }

        return false;
    }

    /// <summary>Меньший NetworkId (Guid) — единственный, кто высылает RSA-handshake; больший — только 0x04-запрос.</summary>
    private bool IsCryptoSessionLeader()
    {
        var ours = CompressedNetworkId.FromShortString(user.NetworkIdShort.Trim()).Value;
        var peer = CompressedNetworkId.FromShortString(chat.PeerNetworkIdShort.Trim()).Value;
        return ours.CompareTo(peer) < 0;
    }

    private async Task SendSessionSetupRequestPacketAsync(CancellationToken cancellationToken)
    {
        var id = CompressedNetworkId.FromShortString(user.NetworkIdShort.Trim());
        var buf = new byte[17];
        buf[0] = FrameSessionSetupRequest;
        if (!id.Value.TryWriteBytes(buf.AsSpan(1)))
            throw new InvalidOperationException("Failed to write network id.");
        await SendRouteRawAsync(buf, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Вызывается только под захватом <see cref="_sessionSetup" /> (лидер).</summary>
    private async Task EnsureLeaderCryptoSessionCoreAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (TryGetCryptoSession(out _) && _messenger != null)
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
            if (TryGetCryptoSession(out _) && _messenger != null)
                return;
            var cryptoSession = _cryptoSessionCache.GetSession(chat.Id, () => hs.Session);
            _messenger =
                new MessengerService(_prefixed!, cryptoSession, CreateMessengerOptions(), OnDecryptFailureAsync);
            _handshakeWeInitiated = true;
            ms = _messenger;
        }

        await ms.StartAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine("messenger started (leader)");
        StartIncomingReaderIfNeeded();
    }

    private async Task EnsureSessionAsInitiatorAsync(CancellationToken cancellationToken)
    {
        await EnsureMessengerStartedForExistingSessionAsync(cancellationToken).ConfigureAwait(false);

        if (IsCryptoSessionLeader())
        {
            await _sessionSetup.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureLeaderCryptoSessionCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sessionSetup.Release();
            }

            return;
        }

        lock (_sync)
        {
            if (TryGetCryptoSession(out _) && _messenger != null)
                return;
        }

        TaskCompletionSource<bool>? waitHandshake = null;
        await _sessionSetup.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                if (TryGetCryptoSession(out _) && _messenger != null)
                    return;
                _followerHandshakeTcs?.TrySetCanceled();
                _followerHandshakeTcs = waitHandshake =
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            await SendSessionSetupRequestPacketAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (_sync)
            {
                if (waitHandshake != null && ReferenceEquals(_followerHandshakeTcs, waitHandshake))
                {
                    _followerHandshakeTcs.TrySetCanceled();
                    _followerHandshakeTcs = null;
                }
            }

            throw;
        }
        finally
        {
            _sessionSetup.Release();
        }

        if (waitHandshake == null)
            return;

        try
        {
            await waitHandshake.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_followerHandshakeTcs, waitHandshake))
                    _followerHandshakeTcs = null;
            }
        }
    }

    private async Task EnsureMessengerStartedForExistingSessionAsync(CancellationToken cancellationToken)
    {
        MessengerService? created = null;
        lock (_sync)
        {
            if (!TryGetCryptoSession(out var cryptoSession) || _messenger != null)
                return;
            _messenger = new MessengerService(_prefixed!, cryptoSession, CreateMessengerOptions(), OnDecryptFailureAsync);
            created = _messenger;
        }

        await created.StartAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"messenger started ({(_handshakeWeInitiated ? "leader" : "follower")})");
        StartIncomingReaderIfNeeded();
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
                    case FrameSessionSetupRequest when buf.Length == 17:
                    {
                        if (!IsCryptoSessionLeader())
                            continue;
                        var peerGuid = new Guid(buf.AsSpan(1, 16));
                        var expected = CompressedNetworkId.FromShortString(chat.PeerNetworkIdShort.Trim()).Value;
                        if (peerGuid != expected)
                            continue;
                        await _sessionSetup.WaitAsync(cancellationToken).ConfigureAwait(false);
                        try
                        {
                            await EnsureLeaderCryptoSessionCoreAsync(cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            _sessionSetup.Release();
                        }

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
            if (IsCryptoSessionLeader())
                return;

            MessengerService? created;
            TaskCompletionSource<bool>? followerSignal;
            lock (_sync)
            {
                if (TryGetCryptoSession(out _) && _messenger != null)
                    return;

                // Follower must rebuild crypto session from latest leader handshake.
                // Otherwise stale cached session may survive retries and break decrypt.
                ClearCryptoSession();
                var localPrivate = auth.GetCurrentPrivateKey();
                var cryptoSession = _cryptoSessionCache.GetSession(chat.Id,
                    () => P2PCrypto.CreateSession(localPrivate, handshakePacket));

                _messenger ??=
                    new MessengerService(_prefixed!, cryptoSession, CreateMessengerOptions(), OnDecryptFailureAsync);
                _handshakeWeInitiated = false;
                followerSignal = _followerHandshakeTcs;
                _followerHandshakeTcs = null;
                created = _messenger;
            }

            if (created != null)
            {
                await created.StartAsync(cancellationToken).ConfigureAwait(false);
                Console.WriteLine("messenger started (follower)");
                StartIncomingReaderIfNeeded();
            }

            followerSignal?.TrySetResult(true);
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
                var shouldNotify = false;
                var payload = incoming.Payload.ToArray();
                if (ChatWireCodec.TryParse(payload, out var wire) && wire != null)
                {
                    switch (wire)
                    {
                        case ChatWireText t:
                            if (!TryHandleSessionCryptoProbeText(t.Text, cancellationToken))
                            {
                                _ = await repo.AddMessageAsync(chat.Id, false, t.Text).ConfigureAwait(false);
                                shouldNotify = true;
                            }

                            break;
                        case ChatWireImage img:
                            _ = await repo.AddImageMessageAsync(chat.Id, false, img.MimeType, img.ImageBytes)
                                .ConfigureAwait(false);
                            shouldNotify = true;
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

                            shouldNotify = true;
                            break;
                    }
                }
                else if (ChatWireCodec.LooksLikeFramedWire(payload))
                {
                    _ = await repo.AddMessageAsync(chat.Id, false,
                            "[Входящее сообщение не распознано. Обновите клиент.]")
                        .ConfigureAwait(false);
                    shouldNotify = true;
                }
                else
                {
                    var text = Encoding.UTF8.GetString(payload);
                    if (!TryHandleSessionCryptoProbeText(text, cancellationToken))
                    {
                        _ = await repo.AddMessageAsync(chat.Id, false, text).ConfigureAwait(false);
                        shouldNotify = true;
                    }
                }

                if (shouldNotify)
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
        lock (_sync)
        {
            _cryptoProbeOkAwaiter?.TrySetCanceled();
            _cryptoProbeOkAwaiter = null;
            _followerHandshakeTcs?.TrySetCanceled();
            _followerHandshakeTcs = null;
            _handshakeWeInitiated = false;
            _cryptoProbeRoundTripOk = false;
        }

        _messenger = null;
        ClearCryptoSession();
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