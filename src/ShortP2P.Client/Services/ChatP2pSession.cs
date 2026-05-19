using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Diagnostics.CodeAnalysis;
using ShortP2P.Auth;
using ShortP2P.Auth.Data;
using ShortP2P.Client.ChatMedia;
using ShortP2P.Client.Data;
using ShortP2P.Client.Qr;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Transceivers;
using ShortP2P.Client.Transport;
using ShortP2P.Crypto;
using ShortP2P.Discovery;
using ShortP2P.Discovery.Transceivers;
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
    private readonly UserP2pRuntime _runtime;
    private readonly P2pCryptoSessionCache _cryptoSessionCache;
    private ITransport? bluetoothTransport => _runtime.BluetoothTransport;

    private const byte FrameHandshake = 0x01;
    private const byte FrameCipher = 0x02;
    /// <summary>Запрос на установку сессии: только от пира с большим NetworkId; тело — 16 байт Guid отправителя.</summary>
    private const byte FrameSessionSetupRequest = 0x04;
    public const int MaxMessageChars = 32768;
    private static readonly TimeSpan DecryptRecoveryCooldown = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _flushPendingSem = new(1, 1);

    private readonly GuaranteedDeliveryPolicy _guaranteedDelivery = new();
    private readonly ChatMediaOptions _media;
    private readonly TcpTransferService _tcpTransfer = new();
    private readonly List<int> _pendingOutgoing = [];

    private readonly object _pendingSync = new();
    private readonly SemaphoreSlim _sessionSetup = new(1, 1);

    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _outboundCts;
    private int _decryptRecoveryGate;
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
    private volatile bool _presenceHooked;
    private bool _transceiverSubscribed;

    private ChatP2pSession(
        ChatEntity chat,
        UserEntity user,
        AuthService auth,
        ChatRepository repo,
        UserP2pRuntime runtime,
        SynchronizationContext? uiSynchronizationContext = null,
        P2pRoutingSettings? routingSettings = null,
        LocalNetworkScanner? localNetworkScanner = null,
        ChatMediaOptions? chatMediaOptions = null,
        P2pCryptoSessionCache? cryptoSessionCache = null)
    {
        this.chat = chat;
        this.user = user;
        this.auth = auth;
        this.repo = repo;
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.uiSynchronizationContext = uiSynchronizationContext;
        this.routingSettings = routingSettings;
        this.localNetworkScanner = localNetworkScanner;
        _cryptoSessionCache = cryptoSessionCache ?? new P2pCryptoSessionCache();
        _media = chatMediaOptions ?? new ChatMediaOptions();
    }

    public static ChatP2pSession Create(
        ChatEntity chat,
        UserEntity user,
        AuthService auth,
        ChatRepository repo,
        UserP2pRuntime runtime,
        SynchronizationContext? uiSynchronizationContext = null,
        P2pRoutingSettings? routingSettings = null,
        LocalNetworkScanner? localNetworkScanner = null,
        ChatMediaOptions? chatMediaOptions = null,
        P2pCryptoSessionCache? cryptoSessionCache = null)
    {
        return new ChatP2pSession(chat, user, auth, repo, runtime, uiSynchronizationContext, routingSettings,
            localNetworkScanner, chatMediaOptions, cryptoSessionCache);
    }

    private bool TryGetCryptoSession([NotNullWhen(true)] out P2PSession? session) =>
        _cryptoSessionCache.TryGetSession(chat.Id, out session);
    private void ClearCryptoSession() => _cryptoSessionCache.TryRemove(chat.Id, out _);

    /// <summary>Follower (больший NetworkId): сессия и messenger готовы к обмену cipher.</summary>
    private bool IsFollowerCryptoReady()
    {
        lock (_sync)
            return IsFollowerCryptoReadyCore();
    }

    private bool IsFollowerCryptoReadyCore() =>
        !IsCryptoSessionLeader() && TryGetCryptoSession(out _) && _messenger != null;

    /// <summary>Успешное завершение ожидания handshake у follower (0x01 от лидера).</summary>
    private void SignalFollowerHandshakeSuccess(TaskCompletionSource<bool>? waiter = null)
    {
        CompleteFollowerHandshakeWait(success: true, waiter: waiter);
    }

    /// <summary>Ошибка ожидания (таймаут, сеть, сбой отправки 0x04).</summary>
    private void SignalFollowerHandshakeFailure(Exception ex, TaskCompletionSource<bool>? waiter = null)
    {
        CompleteFollowerHandshakeWait(success: false, failure: ex, waiter: waiter);
    }

    /// <summary>Отмена ожидания (стоп чата, сброс крипты).</summary>
    private void CancelFollowerHandshakeWait()
    {
        CompleteFollowerHandshakeWait(success: false, failure: null, waiter: null, canceled: true);
    }

    private void CompleteFollowerHandshakeWait(bool success, Exception? failure = null,
        TaskCompletionSource<bool>? waiter = null, bool canceled = false)
    {
        lock (_sync)
        {
            var tcs = waiter ?? _followerHandshakeTcs;
            if (tcs is not { Task.IsCompleted: false })
                return;

            if (success)
                tcs.TrySetResult(true);
            else if (canceled)
                tcs.TrySetCanceled();
            else if (failure != null)
                tcs.TrySetException(failure);

            if (ReferenceEquals(_followerHandshakeTcs, tcs))
                _followerHandshakeTcs = null;
        }
    }
    private async ValueTask<P2PSession> WaitForCryptoSessionAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (TryGetCryptoSession(out var session))
                return session;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        throw new OperationCanceledException(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return StopAsync();
    }

    public event EventHandler? MessagesChanged;
    public event EventHandler<int>? TransferStateChanged;

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
                foreach (var h in PeerHostList.ParseIpCandidates(chat.PeerHost))
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
        RebuildRouteFromChat();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ResetOutboundCts();

        SubscribeToTransceivers();

        try
        {
            await SendChatInviteWithRetryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // пир офлайн или сеть недоступна
        }

        await EnsureSessionAsInitiatorAsync(cancellationToken).ConfigureAwait(false);

        _ = TryConfirmCryptoSessionAsync(cancellationToken);

        HookPresenceForPendingFlush();
    }

    private void SubscribeToTransceivers()
    {
        if (_transceiverSubscribed)
            return;
        var handshake = _runtime.Handshake;
        if (handshake != null)
            handshake.GotData += OnHandshakeReceived;

        var message = _runtime.Message;
        if (message != null)
            message.GotData += OnCipherReceived;

        var invite = _runtime.Invite;
        if (invite != null)
            invite.GotData += OnInviteReceived;

        _transceiverSubscribed = true;
    }

    private void UnsubscribeFromTransceivers()
    {
        if (!_transceiverSubscribed)
            return;
        var handshake = _runtime.Handshake;
        if (handshake != null)
            handshake.GotData -= OnHandshakeReceived;

        var message = _runtime.Message;
        if (message != null)
            message.GotData -= OnCipherReceived;

        var invite = _runtime.Invite;
        if (invite != null)
            invite.GotData -= OnInviteReceived;

        _transceiverSubscribed = false;
    }

    private void OnHandshakeReceived(object? sender, HandshakeMessage msg)
    {
        if (!ShouldAcceptIncomingFrom(msg.RemoteAddress))
            return;
        var token = _cts?.Token ?? CancellationToken.None;
        _ = Task.Run(() => HandleHandshakeAsync(msg, token), token);
    }

    private async Task HandleHandshakeAsync(HandshakeMessage msg, CancellationToken cancellationToken)
    {
        try
        {
            switch (msg.Kind)
            {
                case HandshakeKind.Handshake:
                    await ProcessHandshakePacketAsync(msg.Body, msg.RemoteAddress, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                case HandshakeKind.SessionSetupRequest:
                    await ProcessSessionSetupRequestAsync(msg.Body, msg.RemoteAddress, cancellationToken)
                        .ConfigureAwait(false);
                    return;
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch
        {
            // safety: подписчик не должен ронять цикл приёма транспивера
        }
    }

    private void OnCipherReceived(object? sender, TransportReceiveMessage msg)
    {
        if (!ShouldAcceptIncomingFrom(msg.RemoteAddress))
            return;
        var messenger = _messenger;
        messenger?.TryAcceptCipher(msg);
    }

    private async void OnMessengerGotData(object? sender, IncomingBinaryMessage incoming)
    {
        try
        {
            var shouldNotify = false;
            var payload = incoming.Payload.ToArray();
            var cancellationToken = _cts?.Token ?? CancellationToken.None;
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
                    case ChatWireTransferOffer offer:
                        await HandleTransferOfferAsync(offer).ConfigureAwait(false);
                        shouldNotify = true;
                        break;
                    case ChatWireTransferControl control:
                        await HandleTransferControlAsync(control, cancellationToken).ConfigureAwait(false);
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
        catch
        {
            // ignore to avoid breaking messenger callbacks
        }
    }

    private void OnInviteReceived(object? sender, InviteMessage msg)
    {
        if (!ShouldAcceptIncomingFrom(msg.RemoteAddress))
            return;
        var token = _cts?.Token ?? CancellationToken.None;
        _ = Task.Run(async () =>
        {
            try
            {
                await IncomingChatInviteHandler.TryAcceptAsync(msg.RawPayload, auth, repo,
                    SendInviteRawAsync, msg.RemoteAddress, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }, token);
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
        if (_cts == null)
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
                    var udp = ResolveOutbound(dest);
                    if (udp != null)
                        await udp.SendAsync(ping, dest, cancellationToken).ConfigureAwait(false);
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
            var udp = ResolveOutbound(dest);
            if (udp != null)
                await udp.SendAsync(ping, dest, cancellationToken).ConfigureAwait(false);
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
        await SendSessionNegotiationAfterInviteAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SendSessionNegotiationAfterInviteAsync(CancellationToken cancellationToken)
    {
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

        await SendSessionSetupRequestPacketAsync(cancellationToken).ConfigureAwait(false);
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

    private void ClearPendingOutgoing()
    {
        lock (_pendingSync)
        {
            _pendingOutgoing.Clear();
        }
    }

    private void UnhookPresenceAndClearPending()
    {
        ClearPendingOutgoing();

        if (!_presenceHooked || localNetworkScanner == null)
            return;
        localNetworkScanner.ClientsChanged -= OnLanClientsChangedForPendingFlush;
        localNetworkScanner.DiscoveryPingReceived -= OnDiscoveryPingForPendingFlush;
        _presenceHooked = false;
    }

    private void ResetOutboundCts()
    {
        _outboundCts?.Dispose();
        _outboundCts = _cts is { IsCancellationRequested: false }
            ? CancellationTokenSource.CreateLinkedTokenSource(_cts.Token)
            : null;
    }

    private async ValueTask CancelOutboundDeliveryAsync()
    {
        if (_outboundCts == null)
            return;
        try
        {
            await _outboundCts.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            _outboundCts.Cancel();
        }

        ResetOutboundCts();
    }

    private static CancellationTokenSource? CreateOutboundLinkedCts(CancellationToken cancellationToken,
        CancellationTokenSource? outboundCts)
    {
        if (outboundCts == null)
            return null;
        return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, outboundCts.Token);
    }

    /// <summary>Удаляет все сообщения чата из БД и отменяет недоставленные исходящие отправки.</summary>
    public async ValueTask<bool> ClearMessagesAsync(CancellationToken cancellationToken = default)
    {
        await CancelOutboundDeliveryAsync().ConfigureAwait(false);
        ClearPendingOutgoing();

        await _flushPendingSem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ClearPendingOutgoing();
            var ok = await repo.ClearMessagesAsync(chat.Id, user.Id, cancellationToken).ConfigureAwait(false);
            if (ok)
                RaiseMessagesChanged();
            return ok;
        }
        finally
        {
            _flushPendingSem.Release();
        }
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
        if (_runtime.Message == null)
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
            (int)ChatPayloadKind.TransferOffer => ChatWireCodec.EncodeTransferOffer(new ChatWireTransferOffer(
                row.TransferId,
                row.TransferToken,
                row.TransferPayloadKind,
                row.TransferFileName,
                row.MimeType,
                row.TransferSizeBytes,
                row.TransferHost,
                row.TransferPort,
                row.TransferExpiresUtcTicks)),
            _ => ChatWireCodec.EncodeText(row.Text)
        };
    }

    private async Task DeliverOutgoingWireAsync(int messageId, byte[] wire, CancellationToken cancellationToken)
    {
        using var linkedCts = CreateOutboundLinkedCts(cancellationToken, _outboundCts);
        var deliveryToken = linkedCts?.Token ?? cancellationToken;

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
            deliveryToken).ConfigureAwait(false);

        await repo.UpdateMessageDeliveryStatusAsync(messageId, MessageDeliveryStatus.Delivered).ConfigureAwait(false);
        RaiseMessagesChanged();
    }

    private async Task SendWireAsync(byte[] wire, CancellationToken cancellationToken)
    {
        using var linkedCts = CreateOutboundLinkedCts(cancellationToken, _outboundCts);
        var deliveryToken = linkedCts?.Token ?? cancellationToken;

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
            deliveryToken).ConfigureAwait(false);
    }

    public async ValueTask RetryFailedMessageAsync(int messageId, CancellationToken cancellationToken = default)
    {
        var row = await repo.GetMessageAsync(messageId).ConfigureAwait(false);
        if (row == null || row.ChatId != chat.Id || !row.Outgoing)
            return;

        await repo.UpdateMessageDeliveryStatusAsync(messageId, MessageDeliveryStatus.Pending).ConfigureAwait(false);
        RaiseMessagesChanged();
        try
        {
            var wire = BuildOutgoingWire(row);
            await DeliverOutgoingWireAsync(messageId, wire, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await repo.UpdateMessageDeliveryStatusAsync(messageId, MessageDeliveryStatus.Failed).ConfigureAwait(false);
            RaiseMessagesChanged();
            throw;
        }
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
        catch (OperationCanceledException)
        {
            throw;
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
        await CreateAndSendTransferOfferAsync("image", "image", mimeType, bytes, cancellationToken).ConfigureAwait(false);
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

        var payloadKind = mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
            ? "voice"
            : mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                ? "video"
                : "document";
        await CreateAndSendTransferOfferAsync(payloadKind, safeName, mimeType, bytes, cancellationToken).ConfigureAwait(false);
    }

    public async Task RequestBinaryDownloadAsync(int messageId, CancellationToken cancellationToken = default)
    {
        var row = await repo.GetMessageAsync(messageId).ConfigureAwait(false);
        if (row == null || row.ChatId != chat.Id || row.Outgoing || string.IsNullOrWhiteSpace(row.TransferId))
            return;
        if ((ChatTransferState)row.TransferState is ChatTransferState.Received or ChatTransferState.Transferring)
            return;

        await repo.UpdateTransferStateAsync(messageId, ChatTransferState.Transferring).ConfigureAwait(false);
        TransferStateChanged?.Invoke(this, messageId);
        RaiseMessagesChanged();

        var localHost = TryResolveLocalHost() ?? "127.0.0.1";
        var lease = await _tcpTransfer.CreateListenerAsync(row.TransferId, row.TransferToken, TimeSpan.FromSeconds(45),
            cancellationToken).ConfigureAwait(false);
        try
        {
            var ack = new ChatWireTransferControl("tcp-ack", row.TransferId, row.TransferToken, localHost, lease.Port,
                lease.ExpiresAtUtc.UtcTicks, "");
            await SendWireAsync(ChatWireCodec.EncodeTransferControl(ack), cancellationToken)
                .ConfigureAwait(false);
            var bytes = await _tcpTransfer.AcceptAndReceiveAsync(lease, row.TransferSizeBytes, cancellationToken)
                .ConfigureAwait(false);
            var targetKind = row.TransferPayloadKind.Equals("image", StringComparison.OrdinalIgnoreCase)
                ? ChatPayloadKind.Image
                : ChatPayloadKind.File;
            var fileName = string.IsNullOrWhiteSpace(row.TransferFileName) ? row.Text : row.TransferFileName;
            await repo.UpdateMessagePayloadAsync(messageId, targetKind, fileName, row.MimeType, bytes).ConfigureAwait(false);
            await repo.UpdateMessageTransferMetadataAsync(messageId, row.TransferId, row.TransferToken,
                row.TransferPayloadKind, row.TransferFileName, row.TransferSizeBytes, "", 0, 0, ChatTransferState.Received)
                .ConfigureAwait(false);
            TransferStateChanged?.Invoke(this, messageId);
            RaiseMessagesChanged();
        }
        catch
        {
            await repo.UpdateTransferStateAsync(messageId, ChatTransferState.Failed).ConfigureAwait(false);
            TransferStateChanged?.Invoke(this, messageId);
            RaiseMessagesChanged();
            throw;
        }
        finally
        {
            lease.Dispose();
        }
    }

    private async Task CreateAndSendTransferOfferAsync(string payloadKind, string fileName, string mimeType, byte[] bytes,
        CancellationToken cancellationToken)
    {
        var messageId = await repo.AddFileMessageAsync(chat.Id, true, fileName, mimeType, bytes, MessageDeliveryStatus.Pending)
            .ConfigureAwait(false);
        await repo.UpdateMessagePayloadAsync(messageId, ChatPayloadKind.TransferOffer, fileName, mimeType, bytes)
            .ConfigureAwait(false);
        var transferId = Guid.NewGuid().ToString("N");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var expires = DateTimeOffset.UtcNow.AddMinutes(2);
        await repo.UpdateMessageTransferMetadataAsync(messageId, transferId, token, payloadKind, fileName, bytes.Length, "",
                0, expires.UtcTicks, ChatTransferState.Offered)
            .ConfigureAwait(false);
        RaiseMessagesChanged();

        var offer = new ChatWireTransferOffer(transferId, token, payloadKind, fileName, mimeType, bytes.Length, "", 0,
            expires.UtcTicks);
        try
        {
            await DeliverOutgoingWireAsync(messageId, ChatWireCodec.EncodeTransferOffer(offer), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await repo.UpdateMessageDeliveryStatusAsync(messageId, MessageDeliveryStatus.Failed).ConfigureAwait(false);
            await repo.UpdateTransferStateAsync(messageId, ChatTransferState.Failed).ConfigureAwait(false);
            RaiseMessagesChanged();
            throw;
        }
    }

    private async Task HandleTransferOfferAsync(ChatWireTransferOffer offer)
    {
        var text = string.IsNullOrWhiteSpace(offer.FileName) ? "[Входящее вложение]" : offer.FileName;
        var payloadKind = offer.PayloadKind.Equals("image", StringComparison.OrdinalIgnoreCase)
            ? ChatPayloadKind.TransferOffer
            : ChatPayloadKind.TransferOffer;
        var messageId = await repo.AddMessageAsync(chat.Id, false, text).ConfigureAwait(false);
        await repo.UpdateMessagePayloadAsync(messageId, payloadKind, text, offer.MimeType, [])
            .ConfigureAwait(false);
        await repo.UpdateMessageTransferMetadataAsync(messageId, offer.TransferId, offer.TransferToken, offer.PayloadKind,
            offer.FileName, offer.SizeBytes, offer.Host, offer.Port, offer.ExpiresUtcTicks, ChatTransferState.AwaitingClick)
            .ConfigureAwait(false);
    }

    private async Task HandleTransferControlAsync(ChatWireTransferControl control, CancellationToken cancellationToken)
    {
        if (!string.Equals(control.Command, "tcp-ack", StringComparison.OrdinalIgnoreCase))
            return;
        var rows = await repo.ListMessagesAsync(chat.Id).ConfigureAwait(false);
        var row = rows.LastOrDefault(m => m.Outgoing && m.TransferId == control.TransferId);
        if (row?.ImageBlob is not { Length: > 0 })
            return;
        if (!string.Equals(row.TransferToken, control.TransferToken, StringComparison.Ordinal))
            return;
        if (string.IsNullOrWhiteSpace(control.Host) || control.Port is < 1 or > 65535)
            return;
        await repo.UpdateTransferStateAsync(row.Id, ChatTransferState.Transferring).ConfigureAwait(false);
        TransferStateChanged?.Invoke(this, row.Id);
        try
        {
            await _tcpTransfer.SendAsync(control.Host, control.Port, row.TransferId, row.TransferToken, row.ImageBlob,
                cancellationToken).ConfigureAwait(false);
            await repo.UpdateTransferStateAsync(row.Id, ChatTransferState.Received).ConfigureAwait(false);
        }
        catch
        {
            await repo.UpdateTransferStateAsync(row.Id, ChatTransferState.Failed).ConfigureAwait(false);
            throw;
        }
        finally
        {
            TransferStateChanged?.Invoke(this, row.Id);
        }
    }

    private string? TryResolveLocalHost()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                        return ua.Address.ToString();
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
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
        {
            _messenger.GotData -= OnMessengerGotData;
            await _messenger.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        lock (_sync)
        {
            _cryptoProbeOkAwaiter?.TrySetCanceled();
            _cryptoProbeOkAwaiter = null;
            CancelFollowerHandshakeWait();
            _handshakeWeInitiated = false;
            _cryptoProbeRoundTripOk = false;
            _messenger = null;
        }
        ClearCryptoSession();
    }

    /// <summary>
    ///     Отправка «сырых» пакетов на пира: invite (0x30) и handshake-фреймы (0x01/0x04). Перебирает peer endpoints
    ///     в порядке предпочтения (свежий <c>_peerAddress</c> первым), останавливается на первом успешном.
    ///     Маршрут UDP идёт через общий data-сокет в <see cref="UserP2pRuntime" />, BT — через общий BT-транспорт.
    /// </summary>
    private async ValueTask SendRouteRawAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
    {
        Exception? last = null;
        foreach (var dest in BuildOrderedDirectPeerAddresses())
        {
            var transport = ResolveOutbound(dest);
            if (transport == null)
                continue;
            try
            {
                await transport.SendAsync(packet, dest, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
            }
        }

        if (last != null)
            throw last;
    }

    private async Task SendInviteRawAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
        CancellationToken cancellationToken)
    {
        var transport = ResolveOutbound(destination);
        if (transport == null)
            return;
        await transport.SendAsync(payload, destination, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Отправка cipher payload (без префикса 0x02) через глобальный <see cref="MessageTransceiver" />.
    ///     Делегируется в <see cref="MessengerService" /> при каждом исходящем сообщении/чанке.
    /// </summary>
    private async ValueTask SendCipherAsync(ReadOnlyMemory<byte> cipherPayload, TransportAddress destination,
        CancellationToken cancellationToken)
    {
        var message = _runtime.Message
                      ?? throw new InvalidOperationException("Message transceiver is not initialized.");
        await message.SendAsync(new TransportReceiveMessage(cipherPayload, destination), destination,
            cancellationToken).ConfigureAwait(false);
    }

    private ITransport? ResolveOutbound(TransportAddress destination)
    {
        return destination.Kind switch
        {
            TransportKind.Udp when IsTransportEnabled(TransportKind.Udp) => _runtime.DataUdp,
            TransportKind.Bluetooth when IsTransportEnabled(TransportKind.Bluetooth) => bluetoothTransport,
            _ => null
        };
    }

    private async Task ProcessHandshakePacketAsync(ReadOnlyMemory<byte> body, TransportAddress remoteAddress,
        CancellationToken cancellationToken)
    {
        if (body.Length != 128)
            return;
        await HandleResponderHandshakeAsync(body.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessSessionSetupRequestAsync(ReadOnlyMemory<byte> body, TransportAddress remoteAddress,
        CancellationToken cancellationToken)
    {
        if (!IsCryptoSessionLeader())
            return;
        if (body.Length != 16)
            return;
        var peerGuid = new Guid(body.Span);
        var expected = CompressedNetworkId.FromShortString(chat.PeerNetworkIdShort.Trim()).Value;
        if (peerGuid != expected)
            return;

        await _sessionSetup.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLeaderCryptoSessionCoreAsync(cancellationToken, forceSendHandshake: true)
                .ConfigureAwait(false);
        }
        finally
        {
            _sessionSetup.Release();
        }
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
        var endpoints = LanBroadcastHelper.GetIpv4BroadcastEndpoints(17501);// EnumerateBroadcastAddresses

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
    /// <param name="forceSendHandshake">Ответ на 0x04 от follower — всегда шлём 0x01, даже если сессия в кэше.</param>
    private async Task EnsureLeaderCryptoSessionCoreAsync(CancellationToken cancellationToken,
        bool forceSendHandshake = false)
    {
        if (!forceSendHandshake)
        {
            lock (_sync)
            {
                if (TryGetCryptoSession(out _) && _messenger != null)
                    return;
            }
        }

        var hs = P2PCrypto.CreateHandshakeInitiation(_peerPublicKey!);
        var packet = new byte[129];
        packet[0] = FrameHandshake;
        Buffer.BlockCopy(hs.HandshakePacket, 0, packet, 1, hs.HandshakePacket.Length);
        await SendRouteRawAsync(packet, cancellationToken).ConfigureAwait(false);

        MessengerService? ms = null;
        lock (_sync)
        {
            if (!forceSendHandshake && TryGetCryptoSession(out _) && _messenger != null)
                return;

            if (forceSendHandshake)
                ClearCryptoSession();

            _ = _cryptoSessionCache.GetSession(chat.Id, () => hs.Session);
            if (_messenger == null)
            {
                _messenger =
                    new MessengerService(SendCipherAsync, WaitForCryptoSessionAsync, CreateMessengerOptions(),
                        OnDecryptFailureAsync);
                _messenger.GotData += OnMessengerGotData;
                ms = _messenger;
            }

            _handshakeWeInitiated = true;
        }

        if (ms != null)
        {
            await ms.StartAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine("messenger started (leader)");
        }
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

        if (IsFollowerCryptoReady())
            return;

        TaskCompletionSource<bool> waitHandshake;
        var shouldSendSessionRequest = false;
        lock (_sync)
        {
            if (IsFollowerCryptoReadyCore())
                return;

            if (_followerHandshakeTcs is { Task.IsCompleted: false })
            {
                // Single-flight: повторные вызовы просто ждут уже существующий handshake.
                waitHandshake = _followerHandshakeTcs;
            }
            else
            {
                waitHandshake = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _followerHandshakeTcs = waitHandshake;
                shouldSendSessionRequest = true;
            }
        }

        if (IsFollowerCryptoReady())
        {
            SignalFollowerHandshakeSuccess(waitHandshake);
            return;
        }

        if (shouldSendSessionRequest)
        {
            try
            {
                await SendSessionSetupRequestPacketAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SignalFollowerHandshakeFailure(ex, waitHandshake);
                throw;
            }
        }

        if (IsFollowerCryptoReady())
        {
            SignalFollowerHandshakeSuccess(waitHandshake);
            return;
        }

        try
        {
            await waitHandshake.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            SignalFollowerHandshakeFailure(ex, waitHandshake);
            throw;
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
            if (!TryGetCryptoSession(out _) || _messenger != null)
                return;
            _messenger =
                new MessengerService(SendCipherAsync, WaitForCryptoSessionAsync, CreateMessengerOptions(),
                    OnDecryptFailureAsync);
            _messenger.GotData += OnMessengerGotData;
            created = _messenger;
        }

        await created.StartAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"messenger started ({(_handshakeWeInitiated ? "leader" : "follower")})");
    }

    private MessengerOptions CreateMessengerOptions()
    {
        return new MessengerOptions { MaxBinaryMessageBytes = _media.MaxMessengerBinaryBytes };
    }

    private async Task HandleResponderHandshakeAsync(byte[] handshakePacket, CancellationToken cancellationToken)
    {
        await _sessionSetup.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsCryptoSessionLeader())
                return;

            MessengerService? created = null;
            lock (_sync)
            {
                // Follower must rebuild crypto session from latest leader handshake.
                // Otherwise stale cached session may survive retries and break decrypt.
                ClearCryptoSession();
                var localPrivate = auth.GetCurrentPrivateKey();
                _ = _cryptoSessionCache.GetSession(chat.Id,
                    () => P2PCrypto.CreateSession(localPrivate, handshakePacket));

                _handshakeWeInitiated = false;
                if (_messenger == null)
                {
                    _messenger =
                        new MessengerService(SendCipherAsync, WaitForCryptoSessionAsync, CreateMessengerOptions(),
                            OnDecryptFailureAsync);
                    _messenger.GotData += OnMessengerGotData;
                    created = _messenger;
                }
            }

            if (created != null)
            {
                await created.StartAsync(cancellationToken).ConfigureAwait(false);
                Console.WriteLine("messenger started (follower)");
            }

            SignalFollowerHandshakeSuccess();
        }
        finally
        {
            _sessionSetup.Release();
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        UnhookPresenceAndClearPending();
        UnsubscribeFromTransceivers();

        if (_cts != null)
            await _cts.CancelAsync();

        if (_messenger != null)
        {
            _messenger.GotData -= OnMessengerGotData;
            await _messenger.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        _cts?.Dispose();
        _cts = null;
        _outboundCts?.Dispose();
        _outboundCts = null;
        lock (_sync)
        {
            _cryptoProbeOkAwaiter?.TrySetCanceled();
            _cryptoProbeOkAwaiter = null;
            CancelFollowerHandshakeWait();
            _handshakeWeInitiated = false;
            _cryptoProbeRoundTripOk = false;
        }

        _messenger = null;
        ClearCryptoSession();
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

}