using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShortP2P.Auth;
using ShortP2P.Auth.Data;
using ShortP2P.Client.ChatMedia;
using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Transceivers;
using ShortP2P.Client.Transport;
using ShortP2P.Crypto;
using ShortP2P.Discovery;
using ShortP2P.Discovery.Transceivers;
using ShortP2P.Messenger;
using ShortP2P.Client.Services.MessengerServers;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Services;

/// <summary>
///     Чат: UDP/Bluetooth, RSA-handshake (0x01) только у пира с меньшим network id (сравнение Guid);
///     второй шлёт запрос 0x04+16 байт id и ждёт handshake. Шифрокадры 0x02.
///     Leader и follower обмениваются сообщениями; BLE ConnectGatt — только у узла с большим MAC
///     (ответы — через GATT notify на уже открытой сессии, см. Bluetooth-транспорт).
/// </summary>
public sealed class ChatP2PSession : IAsyncDisposable
{
    private const byte FrameHandshake = 0x01;
    private const byte FrameCipher = 0x02;

    /// <summary>Запрос на установку сессии: только от пира с большим NetworkId; тело — 12 байт wire id отправителя.</summary>
    private const byte FrameSessionSetupRequest = 0x04;

    public const int MaxMessageChars = 32768;
    private static readonly TimeSpan DecryptRecoveryCooldown = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _cryptoProbeLoopLock = new(1, 1);
    private readonly P2pCryptoSessionCache _cryptoSessionCache;
    private readonly SemaphoreSlim _flushPendingSem = new(1, 1);

    private readonly GuaranteedDeliveryPolicy _guaranteedDelivery = new();
    private readonly ILogger<ChatP2PSession> _logger;
    private readonly ChatMediaOptions _media;
    private readonly List<int> _pendingOutgoing = [];

    private readonly Lock _pendingSync = new();
    private readonly UserP2pRuntime _runtime;
    private readonly SemaphoreSlim _sessionSetup = new(1, 1);

    private readonly Lock _sync = new();
    private readonly TcpTransferService _tcpTransfer = new();
    private readonly AuthService _auth;
    private readonly ChatEntity _chat;
    private readonly LocalNetworkScanner? _localNetworkScanner;
    private readonly ChatRepository _repo;
    private readonly P2pRoutingSettings? _routingSettings;
    private readonly SynchronizationContext? _uiSynchronizationContext;
    private readonly UserEntity _user;
    private TaskCompletionSource<bool>? _cryptoProbeOkAwaiter;
    private volatile bool _cryptoProbeRoundTripOk;
    private CancellationTokenSource? _cts;
    private int _decryptRecoveryGate;
    private TaskCompletionSource<bool>? _followerHandshakeTcs;

    /// <summary>True только у лидера (меньший NetworkId): отправлен свой RSA-handshake; зонд ACK/OK только с лидера.</summary>
    private bool _handshakeWeInitiated;

    private DateTimeOffset _lastDecryptRecoveryUtc = DateTimeOffset.MinValue;
    private MessengerService? _messenger;
    private CancellationTokenSource? _outboundCts;

    private TransportAddress? _peerAddress;
    private List<TransportAddress> _peerEndpoints = [];
    private RsaPublicKey? _peerPublicKey;
    private volatile bool _presenceHooked;
    private bool _transceiverSubscribed;

    private ChatP2PSession(
        ChatEntity chat,
        UserEntity user,
        AuthService auth,
        ChatRepository repo,
        UserP2pRuntime runtime,
        SynchronizationContext? uiSynchronizationContext = null,
        P2pRoutingSettings? routingSettings = null,
        LocalNetworkScanner? localNetworkScanner = null,
        ChatMediaOptions? chatMediaOptions = null,
        P2pCryptoSessionCache? cryptoSessionCache = null,
        ILogger<ChatP2PSession>? logger = null)
    {
        _chat = chat;
        _user = user;
        _auth = auth;
        _repo = repo;
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _uiSynchronizationContext = uiSynchronizationContext;
        _routingSettings = routingSettings;
        _localNetworkScanner = localNetworkScanner;
        _cryptoSessionCache = cryptoSessionCache ?? new P2pCryptoSessionCache();
        _media = chatMediaOptions ?? new ChatMediaOptions();
        _logger = logger ?? NullLogger<ChatP2PSession>.Instance;
    }

    private ITransport? BluetoothTransport => _runtime.BluetoothTransport;

    public ValueTask DisposeAsync()
    {
        return StopAsync();
    }

    public static ChatP2PSession Create(
        ChatEntity chat,
        UserEntity user,
        AuthService auth,
        ChatRepository repo,
        UserP2pRuntime runtime,
        SynchronizationContext? uiSynchronizationContext = null,
        P2pRoutingSettings? routingSettings = null,
        LocalNetworkScanner? localNetworkScanner = null,
        ChatMediaOptions? chatMediaOptions = null,
        P2pCryptoSessionCache? cryptoSessionCache = null,
        ILogger<ChatP2PSession>? logger = null)
    {
        return new ChatP2PSession(chat, user, auth, repo, runtime, uiSynchronizationContext, routingSettings,
            localNetworkScanner, chatMediaOptions, cryptoSessionCache, logger);
    }

    /// <summary>Меньший NetworkId — единственный, кто высылает RSA-handshake; больший — только 0x04-запрос.</summary>
    private bool IsCryptoSessionLeader()
    {
        var ours = CompressedNetworkId.FromShortString(_user.NetworkIdShort.Trim());
        var peer = CompressedNetworkId.FromShortString(_chat.PeerNetworkIdShort.Trim());
        return ours.CompareTo(peer) < 0;
    }

    private string SessionRoleLabel() => IsCryptoSessionLeader() ? "leader" : "follower";

    private string PeerSessionRoleLabel() => IsCryptoSessionLeader() ? "follower" : "leader";

    private (string LocalRole, string PeerRole) GetCryptoSessionRoles() =>
        (SessionRoleLabel(), PeerSessionRoleLabel());

    private bool TryGetLocalBluetoothMac(out byte[] mac)
    {
        mac = [];
        if (_routingSettings?.EnableBluetoothTransport == false)
            return false;
        var macText = _routingSettings?.SelectedBluetoothAdapterMac;
        return !string.IsNullOrWhiteSpace(macText)
               && BluetoothTransportAddress.TryParseMac(macText.Trim(), out mac);
    }

    private bool TryGetPeerBluetoothMac(out byte[] mac)
    {
        mac = [];
        foreach (var ep in _peerEndpoints)
        {
            if (ep.Kind != TransportKind.Bluetooth || ep.Data.Length != BluetoothTransportAddress.MacLength)
                continue;
            mac = ep.Data.ToArray();
            return true;
        }

        foreach (var token in PeerHostList.ParseEndpointCandidates(_chat.PeerHost))
        {
            if (!BluetoothTransportAddress.TryParseMac(token, out mac))
                continue;
            return true;
        }

        return false;
    }

    /// <summary>Больший MAC — BLE leader (инициирует ConnectGatt); меньший — BLE follower (ждёт входящее).</summary>
    private bool IsBleConnectionLeader()
    {
        if (!TryGetLocalBluetoothMac(out var local) || !TryGetPeerBluetoothMac(out var peer))
            return false;
        return BluetoothTransportAddress.ShouldInitiateBleConnection(local, peer);
    }

    private string? BleSessionRoleLabel() =>
        TryGetLocalBluetoothMac(out _) && TryGetPeerBluetoothMac(out _)
            ? IsBleConnectionLeader() ? "leader" : "follower"
            : null;

    private string? PeerBleSessionRoleLabel()
    {
        var local = BleSessionRoleLabel();
        return local switch
        {
            "leader" => "follower",
            "follower" => "leader",
            _ => null
        };
    }

    private (string? LocalMac, string? PeerMac, string? LocalRole, string? PeerRole) GetBleSessionRoles()
    {
        if (!TryGetLocalBluetoothMac(out var localBytes) || !TryGetPeerBluetoothMac(out var peerBytes))
            return (null, null, null, null);

        var localMac = BluetoothTransportAddress.ToMacString(localBytes);
        var peerMac = BluetoothTransportAddress.ToMacString(peerBytes);
        var localRole = BluetoothTransportAddress.ShouldInitiateBleConnection(localBytes, peerBytes)
            ? "leader"
            : "follower";
        var peerRole = localRole == "leader" ? "follower" : "leader";
        return (localMac, peerMac, localRole, peerRole);
    }

    private void LogSessionRoleContext(string eventName, LogLevel level = LogLevel.Information)
    {
        var (cryptoLocal, cryptoPeer) = GetCryptoSessionRoles();
        var (bleLocalMac, blePeerMac, bleLocalRole, blePeerRole) = GetBleSessionRoles();
        if (bleLocalRole != null)
        {
            _logger.Log(level,
                "Chat {ChatId}: {Event} — crypto local={Role} peer={PeerRole}; BLE local={BleRole} (mac={LocalBleMac}) peer={BlePeerRole} (mac={PeerBleMac})",
                _chat.Id, eventName, cryptoLocal, cryptoPeer, bleLocalRole, bleLocalMac, blePeerRole, blePeerMac);
            return;
        }

        _logger.Log(level,
            "Chat {ChatId}: {Event} — crypto local={Role} peer={PeerRole}; BLE roles unavailable (mac unknown)",
            _chat.Id, eventName, cryptoLocal, cryptoPeer);
    }

    private static string FormatTransportAddress(TransportAddress address)
    {
        try
        {
            return address.Kind switch
            {
                TransportKind.Udp => UdpTransportAddress.ToIPEndPoint(address).ToString(),
                TransportKind.Bluetooth => BluetoothTransportAddress.ToMacString(address.Data),
                _ => $"{address.Kind}:{Convert.ToHexString(address.Data)}"
            };
        }
        catch
        {
            return $"{address.Kind}:{Convert.ToHexString(address.Data)}";
        }
    }

    private bool TryGetCryptoSession([NotNullWhen(true)] out P2PSession? session)
    {
        return _cryptoSessionCache.TryGetSession(_chat.Id, out session);
    }

    private void ClearCryptoSession()
    {
        if (_cryptoSessionCache.TryRemove(_chat.Id, out _))
            _logger.LogInformation("Chat {ChatId}: crypto session cleared from cache (role={Role})", _chat.Id,
                SessionRoleLabel());
    }

    /// <summary>Follower (больший NetworkId): сессия и messenger готовы к обмену cipher.</summary>
    private bool IsFollowerCryptoReady()
    {
        lock (_sync)
        {
            return IsFollowerCryptoReadyCore();
        }
    }

    private bool IsFollowerCryptoReadyCore()
    {
        return !IsCryptoSessionLeader() && TryGetCryptoSession(out _) && _messenger != null;
    }

    /// <summary>Успешное завершение ожидания handshake у follower (0x01 от лидера).</summary>
    private void SignalFollowerHandshakeSuccess(TaskCompletionSource<bool>? waiter = null)
    {
        CompleteFollowerHandshakeWait(true, waiter: waiter);
    }

    /// <summary>Ошибка ожидания (таймаут, сеть, сбой отправки 0x04).</summary>
    private void SignalFollowerHandshakeFailure(Exception ex, TaskCompletionSource<bool>? waiter = null)
    {
        CompleteFollowerHandshakeWait(false, ex, waiter);
    }

    /// <summary>Отмена ожидания (стоп чата, сброс крипты).</summary>
    private void CancelFollowerHandshakeWait()
    {
        CompleteFollowerHandshakeWait(false, null, null, true);
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
            {
                _logger.LogInformation("Chat {ChatId}: follower handshake wait completed", _chat.Id);
                tcs.TrySetResult(true);
            }
            else if (canceled)
            {
                _logger.LogDebug("Chat {ChatId}: follower handshake wait canceled", _chat.Id);
                tcs.TrySetCanceled();
            }
            else if (failure != null)
            {
                _logger.LogWarning(failure, "Chat {ChatId}: follower handshake wait failed", _chat.Id);
                tcs.TrySetException(failure);
            }

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

    public event EventHandler? MessagesChanged;
    public event EventHandler<int>? TransferStateChanged;

    private void RaiseMessagesChanged()
    {
        if (_uiSynchronizationContext != null)
            _uiSynchronizationContext.Post(_ => MessagesChanged?.Invoke(this, EventArgs.Empty), null);
        else
            MessagesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildRouteFromChat()
    {
        _peerPublicKey = RsaKeySerializer.DeserializePublic(_chat.PeerRsaPublicJson);
        _peerEndpoints = PeerTransportEndpoints.Parse(_chat).ToList();
        if (_peerEndpoints.Count == 0)
        {
            var primary = PeerHostList.PrimaryHost(_chat.PeerHost);
            if (IPAddress.TryParse(primary, out var ip))
                _peerEndpoints.Add(UdpTransportAddress.FromIPEndPoint(new IPEndPoint(ip, _chat.PeerPort)));
            else if (BluetoothTransportAddress.TryParseMac(primary, out var mac))
                _peerEndpoints.Add(BluetoothTransportAddress.FromMac(mac));
            else if (!CompressedNetworkId.TryParseShortString(primary, out _))
                throw new FormatException(
                    $"Unsupported peer host format: '{primary}'. Expected IPv4/IPv6, network id, or Bluetooth MAC.");
        }

        _peerAddress = _peerEndpoints.Count > 0 ? _peerEndpoints[0] : null;
        _logger.LogDebug(
            "Chat {ChatId}: route rebuilt, peer endpoints={EndpointCount}, primary={PrimaryEndpoint}, relay={HasRelay}",
            _chat.Id,
            _peerEndpoints.Count,
            _peerAddress != null ? FormatTransportAddress(_peerAddress) : "(none)",
            !string.IsNullOrEmpty(_chat.RelayRouteBlob));
        foreach (var ep in _peerEndpoints)
            if (_localNetworkScanner != null)
            {
                if (ep.Kind == TransportKind.Bluetooth)
                    _localNetworkScanner.RememberBluetoothPeer(ep);
                else if (ep.Kind == TransportKind.Udp)
                    try
                    {
                        var ip = UdpTransportAddress.ToIPEndPoint(ep).Address.ToString();
                        _localNetworkScanner.RememberUdpPresenceTarget(ip);
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
        if (row.Id != _chat.Id)
            throw new ArgumentException("Chat id mismatch.", nameof(row));
        var previousKey = _chat.PeerRsaPublicJson;
        _chat.PeerNickname = row.PeerNickname;
        _chat.PeerNetworkIdShort = row.PeerNetworkIdShort;
        _chat.PeerRsaPublicJson = row.PeerRsaPublicJson;
        _chat.PeerHost = row.PeerHost;
        _chat.PeerPort = row.PeerPort;
        _chat.PeerEndpointsJson = row.PeerEndpointsJson;
        _chat.PeerKeySourceKind = row.PeerKeySourceKind;
        _chat.PeerKeySourceDetail = row.PeerKeySourceDetail;
        _chat.RelayRouteBlob = row.RelayRouteBlob;
        _chat.UpdatedUtcTicks = row.UpdatedUtcTicks;
        _logger.LogInformation(
            "Chat {ChatId}: chat row applied (peer={PeerNetworkId}, host={PeerHost}, port={PeerPort})",
            _chat.Id, _chat.PeerNetworkIdShort, _chat.PeerHost, _chat.PeerPort);
        RebuildRouteFromChat();
        if (!string.IsNullOrWhiteSpace(previousKey) &&
            !SafetyNumber.PublicKeyJsonEquals(previousKey, row.PeerRsaPublicJson))
        {
            _logger.LogWarning("Chat {ChatId}: peer public key changed, clearing crypto session", _chat.Id);
            ClearCryptoSession();
        }
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
                foreach (var h in PeerHostList.ParseIpCandidates(_chat.PeerHost))
                {
                    if (!IPAddress.TryParse(h, out var ip))
                        continue;
                    if (ep.Address.Equals(ip))
                        return true;
                }
            }

            if (from.Kind == TransportKind.Bluetooth) return true;

            if (!string.IsNullOrEmpty(_chat.RelayRouteBlob))
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
        LogSessionRoleContext("P2P session starting");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ResetOutboundCts();

        SubscribeToTransceivers();

        try
        {
            var servers = _runtime.MessengerServers;
            if (servers != null)
                await servers.PublishChatRequestAsync(_chat.PeerNetworkIdShort, cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chat {ChatId}: server ChatRequest publish failed during session start", _chat.Id);
        }

        try
        {
            await SendChatInviteWithRetryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chat {ChatId}: invite send failed during session start", _chat.Id);
        }

        try
        {
            await EnsureSessionAsInitiatorAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chat {ChatId}: session setup failed during session start", _chat.Id);
        }

        _ = TryConfirmCryptoSessionAsync(cancellationToken);

        HookPresenceForPendingFlush();
        LogSessionRoleContext("P2P session start completed");
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
        {
            _logger.LogDebug(
                "Chat {ChatId}: ignored handshake frame {Kind} from {Remote}",
                _chat.Id, msg.Kind, FormatTransportAddress(msg.RemoteAddress));
            return;
        }

        _logger.LogDebug(
            "Chat {ChatId}: received handshake frame {Kind} from {Remote}",
            _chat.Id, msg.Kind, FormatTransportAddress(msg.RemoteAddress));
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
                    _logger.LogInformation(
                        "Chat {ChatId}: processing RSA handshake (0x01) from {Remote}",
                        _chat.Id, FormatTransportAddress(msg.RemoteAddress));
                    await ProcessHandshakePacketAsync(msg.Body, msg.RemoteAddress, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                case HandshakeKind.SessionSetupRequest:
                    _logger.LogInformation(
                        "Chat {ChatId}: processing session setup request (0x04) from {Remote}",
                        _chat.Id, FormatTransportAddress(msg.RemoteAddress));
                    await ProcessSessionSetupRequestAsync(msg.Body, msg.RemoteAddress, cancellationToken)
                        .ConfigureAwait(false);
                    return;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Chat {ChatId}: handshake handling canceled ({Kind})", _chat.Id, msg.Kind);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chat {ChatId}: handshake handling failed ({Kind})", _chat.Id, msg.Kind);
        }
    }

    private void OnCipherReceived(object? sender, TransportReceiveMessage msg)
    {
        if (!ShouldAcceptIncomingFrom(msg.RemoteAddress))
            return;
        var messenger = _messenger;
        messenger?.TryAcceptCipher(msg);
    }

    /// <summary>Ingest a decrypted chat wire delivered via messenger server (servers-first path).</summary>
    public async Task IngestIncomingWireFromServerAsync(
        byte[] payload,
        CancellationToken cancellationToken = default,
        string? blobServerBaseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (await ProcessIncomingPayloadAsync(payload, cancellationToken, blobServerBaseUrl).ConfigureAwait(false))
            RaiseMessagesChanged();
    }

    private async void OnMessengerGotData(object? sender, IncomingBinaryMessage incoming)
    {
        try
        {
            var payload = incoming.Payload.ToArray();
            var cancellationToken = _cts?.Token ?? CancellationToken.None;
            if (await ProcessIncomingPayloadAsync(payload, cancellationToken).ConfigureAwait(false))
                RaiseMessagesChanged();
        }
        catch
        {
            // ignore to avoid breaking messenger callbacks
        }
    }

    /// <returns>True if UI should refresh the message list.</returns>
    private async Task<bool> ProcessIncomingPayloadAsync(
        byte[] payload,
        CancellationToken cancellationToken,
        string? blobServerBaseUrl = null)
    {
        var shouldNotify = false;
        if (ChatWireCodec.TryParse(payload, out var wire) && wire != null)
        {
            switch (wire)
            {
                case ChatWireText t:
                    if (!TryHandleSessionCryptoProbeText(t.Text, cancellationToken))
                    {
                        _ = await _repo.AddMessageAsync(_chat.Id, false, t.Text).ConfigureAwait(false);
                        shouldNotify = true;
                    }

                    break;
                case ChatWireImage img:
                    _ = await _repo.AddImageMessageAsync(_chat.Id, false, img.MimeType, img.ImageBytes)
                        .ConfigureAwait(false);
                    shouldNotify = true;
                    break;
                case ChatWireFile f:
                    try
                    {
                        _media.ValidateDocumentMime(f.MimeType);
                        _media.ValidateDocumentSize(f.FileBytes.Length);
                        _ = await _repo.AddFileMessageAsync(_chat.Id, false, f.FileName, f.MimeType, f.FileBytes)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        _ = await _repo.AddMessageAsync(_chat.Id, false,
                                "[Входящий файл отклонён: неподдерживаемый тип или размер.]")
                            .ConfigureAwait(false);
                    }

                    shouldNotify = true;
                    break;
                case ChatWireTransferOffer offer:
                    await HandleTransferOfferAsync(offer, blobServerBaseUrl).ConfigureAwait(false);
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
            _ = await _repo.AddMessageAsync(_chat.Id, false,
                    "[Входящее сообщение не распознано. Обновите клиент.]")
                .ConfigureAwait(false);
            shouldNotify = true;
        }
        else
        {
            var text = Encoding.UTF8.GetString(payload);
            if (!TryHandleSessionCryptoProbeText(text, cancellationToken))
            {
                _ = await _repo.AddMessageAsync(_chat.Id, false, text).ConfigureAwait(false);
                shouldNotify = true;
            }
        }

        return shouldNotify;
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
                await IncomingChatInviteHandler.TryAcceptAsync(msg.RawPayload, _auth, _repo,
                    SendInviteRawAsync, msg.RemoteAddress, _routingSettings,
                    _routingSettings?.EnableBluetoothTransport == false
                        ? null
                        : _routingSettings?.SelectedBluetoothAdapterMac,
                    CancellationToken.None).ConfigureAwait(false);
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

        var mergedHost = PeerHostList.WithPrimaryFirst(_chat.PeerHost, peerHost);
        await _repo.UpdateChatP2pRouteAsync(_chat.Id, mergedHost, peerPort, null).ConfigureAwait(false);
        _chat.PeerHost = mergedHost;
        _chat.PeerPort = peerPort;
        _chat.RelayRouteBlob = null;
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

        _logger.LogInformation("Chat {ChatId}: manual crypto reset and handshake requested", _chat.Id);
        await ResetCryptoStateAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSessionAsInitiatorAsync(cancellationToken).ConfigureAwait(false);
        _ = TryConfirmCryptoSessionAsync(cancellationToken);
    }

    /// <summary>Временная отладка UI: chat invite (frame 0x30) на адреса пира.</summary>
    public async Task TechSendInviteAsync(CancellationToken cancellationToken = default)
    {
        if (_cts == null)
            throw new InvalidOperationException("Сессия чата не запущена.");

        await SendChatInviteWithRetryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Временная отладка UI: presence ping (порт discovery) на адреса пира.</summary>
    public async Task TechSendPresencePingAsync(CancellationToken cancellationToken = default)
    {
        if (_cts == null)
            throw new InvalidOperationException("Сессия чата не запущена.");

        var nid = CompressedNetworkId.FromShortString(_user.NetworkIdShort);
        var link = _routingSettings?.LinkTechnology ?? LinkTechnologyPreset.Unlimited;
        var ping = PresencePingCodec.Build(nid, _user.Nickname, _user.DataUdpPort, link);
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
                case TransportKind.Bluetooth when BluetoothTransport != null:
                    await BluetoothTransport.SendAsync(ping, addr, cancellationToken).ConfigureAwait(false);
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
        var nid = CompressedNetworkId.FromShortString(_user.NetworkIdShort);
        var invite = ChatInviteCodec.Build(_user.Nickname, nid,
            RsaKeySerializer.SerializePublic(_auth.GetCurrentPublicKey()), host, ChatInviteCodec.InviteUdpPort);
        await SendInviteRouteRawAsync(invite, cancellationToken).ConfigureAwait(false);
        await SendSessionNegotiationAfterInviteAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SendSessionNegotiationAfterInviteAsync(CancellationToken cancellationToken)
    {
        if (IsCryptoSessionLeader())
        {
            _logger.LogInformation(
                "Chat {ChatId}: post-invite session negotiation as leader (crypto peer role={PeerRole}, BLE role={BleRole})",
                _chat.Id, PeerSessionRoleLabel(), BleSessionRoleLabel() ?? "n/a");
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

        _logger.LogInformation(
            "Chat {ChatId}: post-invite session negotiation as follower (crypto peer role={PeerRole}, BLE role={BleRole}, send 0x04)",
            _chat.Id, PeerSessionRoleLabel(), BleSessionRoleLabel() ?? "n/a");
        await SendSessionSetupRequestPacketAsync(cancellationToken).ConfigureAwait(false);
    }

    private string BuildInviteHosts()
    {
        return InviteHostsBuilder.BuildCommaSeparated(
            _routingSettings,
            _routingSettings?.EnableBluetoothTransport == false ? null : _routingSettings?.SelectedBluetoothAdapterMac,
            _user.NetworkIdShort,
            TimeSpan.FromSeconds(2));
    }

    private async Task SendChatInviteWithRetryAsync(CancellationToken cancellationToken)
    {
        const int fallbackAttempts = 3;
        var attempts = Math.Max(1, _routingSettings?.SendFailureSearchAttempts ?? fallbackAttempts);
        var delay = _routingSettings?.SendFailureRetryDelay ?? TimeSpan.FromMilliseconds(350);

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
        return _localNetworkScanner != null && !string.IsNullOrWhiteSpace(_chat.PeerNetworkIdShort);
    }

    private void HookPresenceForPendingFlush()
    {
        if (_localNetworkScanner == null || _presenceHooked)
            return;
        _localNetworkScanner.ClientsChanged += OnLanClientsChangedForPendingFlush;
        _localNetworkScanner.DiscoveryPingReceived += OnDiscoveryPingForPendingFlush;
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

        if (!_presenceHooked || _localNetworkScanner == null)
            return;
        _localNetworkScanner.ClientsChanged -= OnLanClientsChangedForPendingFlush;
        _localNetworkScanner.DiscoveryPingReceived -= OnDiscoveryPingForPendingFlush;
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
            await _outboundCts.CancelAsync();
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
            var ok = await _repo.ClearMessagesAsync(_chat.Id, _user.Id, cancellationToken).ConfigureAwait(false);
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
        if (!_localNetworkScanner!.IsPeerSeenRecentlyOnLan(_chat.PeerNetworkIdShort))
            return;
        StartFlushPendingInBackground();
    }

    private void OnDiscoveryPingForPendingFlush(object? sender, DiscoveryPingReceivedEventArgs e)
    {
        if (!HasPendingOutgoing())
            return;
        if (string.IsNullOrWhiteSpace(_chat.PeerNetworkIdShort))
            return;
        string peerShort;
        try
        {
            peerShort = e.Peer.NetworkId.ToShortString();
        }
        catch (FormatException)
        {
            return;
        }

        if (!string.Equals(peerShort, _chat.PeerNetworkIdShort.Trim(), StringComparison.OrdinalIgnoreCase))
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

                var row = await _repo.GetMessageAsync(nextId).ConfigureAwait(false);
                if (row == null || row.ChatId != _chat.Id || !row.Outgoing)
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
                row.TransferExpiresUtcTicks,
                string.IsNullOrWhiteSpace(row.TransferId) ? null : row.TransferId)),
            _ => ChatWireCodec.EncodeText(row.Text)
        };
    }

    private async Task DeliverOutgoingWireAsync(int messageId, byte[] wire, CancellationToken cancellationToken)
    {
        using var linkedCts = CreateOutboundLinkedCts(cancellationToken, _outboundCts);
        var deliveryToken = linkedCts?.Token ?? cancellationToken;
        
        await _guaranteedDelivery.ExecuteAsync(
            async ct =>
            {
                var servers = _runtime.MessengerServers;
                if (servers != null &&
                    await servers.TryDeliverWireAsync(_chat, _user, wire, ct).ConfigureAwait(false))
                    return;

                await EnsureSessionAsInitiatorAsync(ct).ConfigureAwait(false);
                if (_handshakeWeInitiated && !_cryptoProbeRoundTripOk)
                    _ = TryConfirmCryptoSessionAsync(ct);

                if (string.IsNullOrEmpty(_chat.RelayRouteBlob))
                {
                    var dests = BuildOrderedDirectPeerAddresses();
                    await _messenger!.SendBinaryAsyncExpectAck(wire, dests, ct).ConfigureAwait(false);
                }
                else
                {
                    await _messenger!.SendBinaryAsync(wire, _peerAddress!, ct).ConfigureAwait(false);
                }
            },
            null,
            false,
            _routingSettings,
            deliveryToken).ConfigureAwait(false);

        await _repo.UpdateMessageDeliveryStatusAsync(messageId, MessageDeliveryStatus.Delivered).ConfigureAwait(false);
        RaiseMessagesChanged();
    }

    private async Task SendWireAsync(byte[] wire, CancellationToken cancellationToken)
    {
        using var linkedCts = CreateOutboundLinkedCts(cancellationToken, _outboundCts);
        var deliveryToken = linkedCts?.Token ?? cancellationToken;

        var ackTimeout = (_routingSettings?.LinkTechnology ?? LinkTechnologyPreset.Unlimited).GetMessageAckTimeout();
        await _guaranteedDelivery.ExecuteAsync(
            async ct =>
            {
                var servers = _runtime.MessengerServers;
                if (servers != null &&
                    await servers.TryDeliverWireAsync(_chat, _user, wire, ct).ConfigureAwait(false))
                    return;

                await EnsureSessionAsInitiatorAsync(ct).ConfigureAwait(false);
                if (_handshakeWeInitiated && !_cryptoProbeRoundTripOk)
                    _ = TryConfirmCryptoSessionAsync(ct);

                if (string.IsNullOrEmpty(_chat.RelayRouteBlob))
                {
                    var dests = BuildOrderedDirectPeerAddresses();
                    await _messenger!.SendBinaryAsyncExpectAck(wire, dests, ct).ConfigureAwait(false);
                }
                else
                {
                    await _messenger!.SendBinaryAsync(wire, _peerAddress!, ct).ConfigureAwait(false);
                }
            },
            null,
            false,
            _routingSettings,
            deliveryToken).ConfigureAwait(false);
    }

    public async ValueTask RetryFailedMessageAsync(int messageId, CancellationToken cancellationToken = default)
    {
        var row = await _repo.GetMessageAsync(messageId).ConfigureAwait(false);
        if (row == null || row.ChatId != _chat.Id || !row.Outgoing)
            return;

        await _repo.UpdateMessageDeliveryStatusAsync(messageId, MessageDeliveryStatus.Pending).ConfigureAwait(false);
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
            await _repo.UpdateMessageDeliveryStatusAsync(messageId, MessageDeliveryStatus.Failed).ConfigureAwait(false);
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

        var messageId = await _repo.AddMessageAsync(_chat.Id, true, text, MessageDeliveryStatus.Pending)
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
            await _repo.UpdateMessageDeliveryStatusAsync(messageId, MessageDeliveryStatus.Failed).ConfigureAwait(false);
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
        await CreateAndSendTransferOfferAsync("image", "image", mimeType, bytes, cancellationToken)
            .ConfigureAwait(false);
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
        await CreateAndSendTransferOfferAsync(payloadKind, safeName, mimeType, bytes, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RequestBinaryDownloadAsync(int messageId, CancellationToken cancellationToken = default)
    {
        var row = await _repo.GetMessageAsync(messageId).ConfigureAwait(false);
        if (row == null || row.ChatId != _chat.Id || row.Outgoing || string.IsNullOrWhiteSpace(row.TransferId))
            return;
        // if ((ChatTransferState)row.TransferState is ChatTransferState.Received or ChatTransferState.Transferring)
        //     return;

        await _repo.UpdateTransferStateAsync(messageId, ChatTransferState.Transferring).ConfigureAwait(false);
        TransferStateChanged?.Invoke(this, messageId);
        RaiseMessagesChanged();

        try
        {
            if (await TryReceiveBlobFromMessengerServersAsync(row, cancellationToken).ConfigureAwait(false))
                return;

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
                await _repo.UpdateMessagePayloadAsync(messageId, targetKind, fileName, row.MimeType, bytes)
                    .ConfigureAwait(false);
                await _repo.UpdateMessageTransferMetadataAsync(messageId, row.TransferId, row.TransferToken,
                        row.TransferPayloadKind, row.TransferFileName, row.TransferSizeBytes, "", 0, 0,
                        ChatTransferState.Received)
                    .ConfigureAwait(false);
                TransferStateChanged?.Invoke(this, messageId);
                RaiseMessagesChanged();
            }
            finally
            {
                lease.Dispose();
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
        var servers = _runtime.MessengerServers;
        if (servers == null)
            return false;

        var blobId = row.TransferId.Trim();
        if (blobId.Length == 0)
            return false;

        var hint = LooksLikeHttpBaseUrl(row.TransferHost) ? row.TransferHost : null;
        var ciphertext = await servers.TryDownloadBlobAsync(blobId, hint, cancellationToken).ConfigureAwait(false);
        if (ciphertext == null || ciphertext.Length == 0)
            return false;

        byte[] wire;
        try
        {
            wire = MessengerServerPayloadCodec.Decrypt(ciphertext, _auth.GetCurrentPrivateKey());
        }
        catch
        {
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
                _media.ValidateMime(img.MimeType);
                _media.ValidateSize(img.ImageBytes.Length);
                bytes = img.ImageBytes;
                mimeType = img.MimeType;
                fileName = string.IsNullOrWhiteSpace(row.TransferFileName) ? row.Text : row.TransferFileName;
                kind = ChatPayloadKind.Image;
                break;
            case ChatWireFile f:
                _media.ValidateDocumentMime(f.MimeType);
                _media.ValidateDocumentSize(f.FileBytes.Length);
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
            await servers.TryDeleteBlobAsync(blobId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Local receive already succeeded; TTL will drop leftovers.
        }

        return true;
    }

    private static bool LooksLikeHttpBaseUrl(string? host) =>
        !string.IsNullOrWhiteSpace(host) &&
        (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
         host.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

    private async Task CreateAndSendTransferOfferAsync(string payloadKind, string fileName, string mimeType,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var messageId = await _repo
            .AddFileMessageAsync(_chat.Id, true, fileName, mimeType, bytes, MessageDeliveryStatus.Pending)
            .ConfigureAwait(false);
        await _repo.UpdateMessagePayloadAsync(messageId, ChatPayloadKind.TransferOffer, fileName, mimeType, bytes)
            .ConfigureAwait(false);
        var transferId = Guid.NewGuid().ToString("N");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var expires = DateTimeOffset.UtcNow.AddMinutes(2);
        await _repo.UpdateMessageTransferMetadataAsync(messageId, transferId, token, payloadKind, fileName, bytes.Length,
                "",
                0, expires.UtcTicks, ChatTransferState.Offered)
            .ConfigureAwait(false);
        RaiseMessagesChanged();

        var innerWire = payloadKind.Equals("image", StringComparison.OrdinalIgnoreCase)
            ? ChatWireCodec.EncodeImage(mimeType, bytes)
            : ChatWireCodec.EncodeFile(fileName, mimeType, bytes);
        var servers = _runtime.MessengerServers;
        if (servers != null)
        {
            try
            {
                await servers.TryUploadBlobAsync(_chat, _user, transferId, innerWire, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // TCP remains the fallback when no server accepted the blob.
            }
        }

        var offer = new ChatWireTransferOffer(transferId, token, payloadKind, fileName, mimeType, bytes.Length, "", 0,
            expires.UtcTicks, transferId);
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
            await _repo.UpdateMessageDeliveryStatusAsync(messageId, MessageDeliveryStatus.Failed).ConfigureAwait(false);
            await _repo.UpdateTransferStateAsync(messageId, ChatTransferState.Failed).ConfigureAwait(false);
            RaiseMessagesChanged();
            throw;
        }
    }

    private async Task HandleTransferOfferAsync(ChatWireTransferOffer offer, string? blobServerBaseUrl = null)
    {
        var text = string.IsNullOrWhiteSpace(offer.FileName) ? "[Входящее вложение]" : offer.FileName;
        var payloadKind = ChatPayloadKind.TransferOffer;
        var messageId = await _repo.AddMessageAsync(_chat.Id, false, text).ConfigureAwait(false);
        await _repo.UpdateMessagePayloadAsync(messageId, payloadKind, text, offer.MimeType, [])
            .ConfigureAwait(false);
        var host = !string.IsNullOrWhiteSpace(offer.Host) ? offer.Host : blobServerBaseUrl?.Trim() ?? "";
        await _repo.UpdateMessageTransferMetadataAsync(messageId, offer.TransferId, offer.TransferToken,
                offer.PayloadKind,
                offer.FileName, offer.SizeBytes, host, offer.Port, offer.ExpiresUtcTicks,
                ChatTransferState.AwaitingClick)
            .ConfigureAwait(false);
    }

    private async Task HandleTransferControlAsync(ChatWireTransferControl control, CancellationToken cancellationToken)
    {
        if (!string.Equals(control.Command, "tcp-ack", StringComparison.OrdinalIgnoreCase))
            return;
        var rows = await _repo.ListMessagesAsync(_chat.Id).ConfigureAwait(false);
        var row = rows.LastOrDefault(m => m.Outgoing && m.TransferId == control.TransferId);
        if (row?.ImageBlob is not { Length: > 0 })
            return;
        if (!string.Equals(row.TransferToken, control.TransferToken, StringComparison.Ordinal))
            return;
        if (string.IsNullOrWhiteSpace(control.Host) || control.Port is < 1 or > 65535)
            return;
        await _repo.UpdateTransferStateAsync(row.Id, ChatTransferState.Transferring).ConfigureAwait(false);
        TransferStateChanged?.Invoke(this, row.Id);
        try
        {
            await _tcpTransfer.SendAsync(control.Host, control.Port, row.TransferId, row.TransferToken, row.ImageBlob,
                cancellationToken).ConfigureAwait(false);
            await _repo.UpdateTransferStateAsync(row.Id, ChatTransferState.Received).ConfigureAwait(false);
        }
        catch
        {
            await _repo.UpdateTransferStateAsync(row.Id, ChatTransferState.Failed).ConfigureAwait(false);
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
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                        return ua.Address.ToString();
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
            _logger.LogWarning("Chat {ChatId}: decrypt failure, starting crypto recovery", _chat.Id);

            await ResetCryptoStateAsync(token).ConfigureAwait(false);
            await SendChatInviteWithRetryAsync(token).ConfigureAwait(false);
            await EnsureSessionAsInitiatorAsync(token).ConfigureAwait(false);
            _ = TryConfirmCryptoSessionAsync(token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Chat {ChatId}: decrypt recovery canceled", _chat.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chat {ChatId}: decrypt recovery failed", _chat.Id);
        }
        finally
        {
            Interlocked.Exchange(ref _decryptRecoveryGate, 0);
        }
    }

    private async Task ResetCryptoStateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Chat {ChatId}: resetting crypto state (role={Role})", _chat.Id, SessionRoleLabel());
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
    ///     Invite (0x30) на все адреса пира по каждому включённому виду транспорта (UDP invite-порт, Bluetooth).
    ///     Ошибка только если не удалось отправить ни по одному транспорту.
    /// </summary>
    private async ValueTask SendInviteRouteRawAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
    {
        var destinations = BuildOrderedDirectPeerAddresses();
        if (destinations.Count == 0)
            throw new InvalidOperationException("Нет адресов пира для отправки invite.");

        var hadAttempt = false;
        var anySuccess = false;
        Exception? last = null;

        foreach (var dest in destinations)
        {
            if (!IsTransportEnabled(dest.Kind))
                continue;

            hadAttempt = true;
            try
            {
                await SendInviteOnPeerAddressAsync(packet, dest, cancellationToken).ConfigureAwait(false);
                anySuccess = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
            }
        }

        if (!hadAttempt)
            throw new InvalidOperationException("Нет включённых транспортов для отправки invite.");

        if (!anySuccess)
            throw new InvalidOperationException("Все транспорты недоступны: не удалось отправить invite.", last);
    }

    private async ValueTask SendInviteOnPeerAddressAsync(ReadOnlyMemory<byte> packet, TransportAddress destination,
        CancellationToken cancellationToken)
    {
        switch (destination.Kind)
        {
            case TransportKind.Udp:
                if (!IsTransportEnabled(TransportKind.Udp))
                    throw new InvalidOperationException("UDP-транспорт отключён в настройках.");
                var inviteTx = _runtime.Invite
                               ?? throw new InvalidOperationException(
                                   "UDP invite-транспорт недоступен (слушатель не запущен).");
                var inviteDest = ToInviteUdpDestination(destination);
                var nid = CompressedNetworkId.FromShortString(_user.NetworkIdShort);
                var inviteMsg = new InviteMessage(nid, _user.Nickname, "", "", 0, packet, inviteDest);
                await inviteTx.SendAsync(inviteMsg, inviteDest, cancellationToken).ConfigureAwait(false);
                return;

            case TransportKind.Bluetooth:
                if (!IsTransportEnabled(TransportKind.Bluetooth))
                    throw new InvalidOperationException("Bluetooth-транспорт отключён в настройках.");
                var bt = ResolveOutbound(destination)
                         ?? throw new InvalidOperationException("Bluetooth-транспорт недоступен.");
                await bt.SendAsync(packet, destination, cancellationToken).ConfigureAwait(false);
                return;

            default:
                throw new InvalidOperationException($"Транспорт {destination.Kind} не поддерживается для invite.");
        }
    }

    private static TransportAddress ToInviteUdpDestination(TransportAddress destination)
    {
        var ep = UdpTransportAddress.ToIPEndPoint(destination);
        return UdpTransportAddress.FromIPEndPoint(new IPEndPoint(ep.Address, ChatInviteCodec.InviteUdpPort));
    }

    /// <summary>
    ///     Отправка «сырых» пакетов handshake (0x01/0x04) на пира. Перебирает peer endpoints
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

    private Task SendInviteRawAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
        CancellationToken cancellationToken)
    {
        return SendInviteOnPeerAddressAsync(payload, destination, cancellationToken).AsTask();
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
            TransportKind.Bluetooth when IsTransportEnabled(TransportKind.Bluetooth) => BluetoothTransport,
            _ => null
        };
    }

    private async Task ProcessHandshakePacketAsync(ReadOnlyMemory<byte> body, TransportAddress remoteAddress,
        CancellationToken cancellationToken)
    {
        if (body.Length != 128)
        {
            _logger.LogWarning(
                "Chat {ChatId}: ignored RSA handshake from {Remote}, invalid body length {Length}",
                _chat.Id, FormatTransportAddress(remoteAddress), body.Length);
            return;
        }

        await HandleResponderHandshakeAsync(body.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessSessionSetupRequestAsync(ReadOnlyMemory<byte> body, TransportAddress remoteAddress,
        CancellationToken cancellationToken)
    {
        if (!IsCryptoSessionLeader())
        {
            _logger.LogDebug(
                "Chat {ChatId}: ignored session setup request from {Remote} (not leader)",
                _chat.Id, FormatTransportAddress(remoteAddress));
            return;
        }

        if (body.Length != 1 + CompressedNetworkId.WireLength)
        {
            _logger.LogWarning(
                "Chat {ChatId}: ignored session setup request from {Remote}, invalid body length {Length}",
                _chat.Id, FormatTransportAddress(remoteAddress), body.Length);
            return;
        }

        var peerId = CompressedNetworkId.FromWireBytes(body.Span.Slice(1, CompressedNetworkId.WireLength));
        var expected = CompressedNetworkId.FromShortString(_chat.PeerNetworkIdShort.Trim());
        if (peerId != expected)
        {
            _logger.LogWarning(
                "Chat {ChatId}: ignored session setup request from {Remote}, network id mismatch (got {Got}, expected {Expected})",
                _chat.Id, FormatTransportAddress(remoteAddress), peerId.ToShortString(), expected.ToShortString());
            return;
        }

        _logger.LogInformation(
            "Chat {ChatId}: accepted session setup request from {Remote}, sending RSA handshake",
            _chat.Id, FormatTransportAddress(remoteAddress));
        await _sessionSetup.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLeaderCryptoSessionCoreAsync(cancellationToken, true)
                .ConfigureAwait(false);
        }
        finally
        {
            _sessionSetup.Release();
        }
    }

    private List<TransportAddress> BuildOrderedDirectPeerAddresses()
    {
        //в _peerAddress должен записываться адрес, который приходит в пакете с networkId
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
        var endpoints = LanBroadcastHelper.GetIpv4BroadcastEndpoints(17501); // EnumerateBroadcastAddresses

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

            _logger.LogInformation(
                "Chat {ChatId}: starting crypto probe round-trip confirmation (role={Role})",
                _chat.Id, SessionRoleLabel());
            var okWait = TimeSpan.FromSeconds(60);

            while (!cancellationToken.IsCancellationRequested && !_cryptoProbeRoundTripOk)
            {
                MessengerService? ms;
                lock (_sync)
                {
                    ms = _messenger;
                }

                var canProbe = ms != null && _handshakeWeInitiated;
                if (canProbe)
                {
                    var my = _user.NetworkIdShort.Trim();
                    var peer = _chat.PeerNetworkIdShort.Trim();
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
                        _logger.LogDebug("Chat {ChatId}: sending crypto probe ACK", _chat.Id);
                        await SendEncryptedProbeWireAsync(ackWire, cancellationToken).ConfigureAwait(false);
                        await tcs.Task.WaitAsync(okWait, cancellationToken).ConfigureAwait(false);
                        _cryptoProbeRoundTripOk = true;
                        _logger.LogInformation("Chat {ChatId}: crypto probe round-trip confirmed", _chat.Id);
                        return;
                    }
                    catch (TimeoutException)
                    {
                        _logger.LogWarning(
                            "Chat {ChatId}: crypto probe OK not received within {TimeoutSeconds}s",
                            _chat.Id, okWait.TotalSeconds);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogDebug("Chat {ChatId}: crypto probe canceled", _chat.Id);
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Chat {ChatId}: crypto probe send/wait failed", _chat.Id);
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
                _logger.LogInformation(
                    "Chat {ChatId}: crypto probe failed, retrying session setup after {PauseSeconds}s",
                    _chat.Id, pauseSec);
                await Task.Delay(TimeSpan.FromSeconds(pauseSec), cancellationToken).ConfigureAwait(false);

                try
                {
                    await ResetCryptoStateAsync(cancellationToken).ConfigureAwait(false);
                    await SendChatInviteWithRetryAsync(cancellationToken).ConfigureAwait(false);
                    await EnsureSessionAsInitiatorAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Chat {ChatId}: crypto probe recovery cycle failed", _chat.Id);
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
        if (string.IsNullOrEmpty(_chat.RelayRouteBlob))
        {
            var dests = BuildOrderedDirectPeerAddresses();
            Exception? last = null;
            foreach (var d in dests)
                try
                {
                    await m.SendBinaryAsync(wire, d, cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
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
            var my = _user.NetworkIdShort.Trim();
            var peer = _chat.PeerNetworkIdShort.Trim();
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
        var my = _user.NetworkIdShort.Trim();
        var peer = _chat.PeerNetworkIdShort.Trim();
        if (!string.Equals(tgt, my, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(src, peer, StringComparison.OrdinalIgnoreCase))
            return false;

        bool weInitiated;
        lock (_sync)
        {
            weInitiated = _handshakeWeInitiated;
        }

        if (kind == SessionCryptoProbeKind.Ack)
        {
            if (weInitiated)
                return false;
            _logger.LogInformation("Chat {ChatId}: received crypto probe ACK, sending OK", _chat.Id);
            _ = SendCryptoProbeOkAsync(cancellationToken);
            return true;
        }

        if (kind == SessionCryptoProbeKind.Ok)
        {
            if (!weInitiated)
                return false;
            _logger.LogInformation("Chat {ChatId}: received crypto probe OK", _chat.Id);
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

    private async Task SendSessionSetupRequestPacketAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Chat {ChatId}: sending session setup request (0x04), crypto role={Role}, BLE role={BleRole}",
            _chat.Id, SessionRoleLabel(), BleSessionRoleLabel() ?? "n/a");
        var id = CompressedNetworkId.FromShortString(_user.NetworkIdShort.Trim());
        var buf = new byte[1 + CompressedNetworkId.WireLength];
        buf[0] = FrameSessionSetupRequest;
        if (!id.TryWriteBytes(buf.AsSpan(1, CompressedNetworkId.WireLength)))
            throw new InvalidOperationException("Failed to write network id.");
        await SendRouteRawAsync(buf, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Вызывается только под захватом <see cref="_sessionSetup" /> (лидер).</summary>
    /// <param name="forceSendHandshake">Ответ на 0x04 от follower — всегда шлём 0x01, даже если сессия в кэше.</param>
    private async Task EnsureLeaderCryptoSessionCoreAsync(CancellationToken cancellationToken,
        bool forceSendHandshake = false)
    {
        if (!forceSendHandshake)
            lock (_sync)
            {
                if (TryGetCryptoSession(out _) && _messenger != null)
                {
                    _logger.LogDebug("Chat {ChatId}: leader crypto session already active, skip handshake", _chat.Id);
                    return;
                }
            }

        _logger.LogInformation(
            "Chat {ChatId}: leader sending RSA handshake (0x01), crypto role={Role}, BLE role={BleRole}, force={ForceSendHandshake}",
            _chat.Id, SessionRoleLabel(), BleSessionRoleLabel() ?? "n/a", forceSendHandshake);
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

            _ = _cryptoSessionCache.GetSession(_chat.Id, () => hs.Session);
            _logger.LogInformation("Chat {ChatId}: leader crypto session created in cache (role={Role})", _chat.Id,
                SessionRoleLabel());
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
            _logger.LogInformation("Chat {ChatId}: messenger started (crypto role={Role}, BLE role={BleRole})",
                _chat.Id, SessionRoleLabel(), BleSessionRoleLabel() ?? "n/a");
        }
    }

    private async Task EnsureSessionAsInitiatorAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Chat {ChatId}: ensure session as initiator ({Role})", _chat.Id, SessionRoleLabel());
        await EnsureMessengerStartedForExistingSessionAsync(cancellationToken).ConfigureAwait(false);

        if (IsCryptoSessionLeader())
        {
            _logger.LogDebug("Chat {ChatId}: leader session setup begin", _chat.Id);
            await _sessionSetup.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureLeaderCryptoSessionCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sessionSetup.Release();
            }

            _logger.LogInformation("Chat {ChatId}: leader session setup completed (role={Role})", _chat.Id,
                SessionRoleLabel());
        }

        // if (IsFollowerCryptoReady())
        // {
        //     _logger.LogDebug("Chat {ChatId}: follower crypto already ready", _chat.Id);
        //     return;
        // }
        //
        // TaskCompletionSource<bool> waitHandshake;
        // var shouldSendSessionRequest = false;
        // lock (_sync)
        // {
        //     if (IsFollowerCryptoReadyCore())
        //         return;
        //
        //     if (_followerHandshakeTcs is { Task.IsCompleted: false })
        //     {
        //         _logger.LogDebug("Chat {ChatId}: follower waiting on existing handshake flight", _chat.Id);
        //         waitHandshake = _followerHandshakeTcs;
        //     }
        //     else
        //     {
        //         waitHandshake = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        //         _followerHandshakeTcs = waitHandshake;
        //         shouldSendSessionRequest = true;
        //     }
        // }
        //
        // if (IsFollowerCryptoReady())
        // {
        //     SignalFollowerHandshakeSuccess(waitHandshake);
        //     return;
        // }
        //
        // if (shouldSendSessionRequest)
        //     try
        //     {
        //         await SendSessionSetupRequestPacketAsync(cancellationToken).ConfigureAwait(false);
        //     }
        //     catch (Exception ex)
        //     {
        //         SignalFollowerHandshakeFailure(ex, waitHandshake);
        //         throw;
        //     }
        //
        // if (IsFollowerCryptoReady())
        // {
        //     SignalFollowerHandshakeSuccess(waitHandshake);
        //     return;
        // }
        //
        // try
        // {
        //     _logger.LogDebug("Chat {ChatId}: follower waiting for RSA handshake (timeout 60s)", _chat.Id);
        //     await waitHandshake.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
        //     _logger.LogInformation("Chat {ChatId}: follower session setup completed", _chat.Id);
        // }
        // catch (TimeoutException ex)
        // {
        //     _logger.LogWarning(ex, "Chat {ChatId}: follower handshake wait timed out", _chat.Id);
        //     SignalFollowerHandshakeFailure(ex, waitHandshake);
        //     throw;
        // }
        // finally
        // {
        //     lock (_sync)
        //     {
        //         if (ReferenceEquals(_followerHandshakeTcs, waitHandshake))
        //             _followerHandshakeTcs = null;
        //     }
        // }
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
        _logger.LogInformation(
            "Chat {ChatId}: messenger started (crypto role={Role}, BLE role={BleRole}) from cached crypto session",
            _chat.Id, SessionRoleLabel(), BleSessionRoleLabel() ?? "n/a");
    }

    private MessengerOptions CreateMessengerOptions()
    {
        return new MessengerOptions { MaxBinaryMessageBytes = _media.MaxMessengerBinaryBytes };
    }

    private async Task HandleResponderHandshakeAsync(byte[] handshakePacket, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Chat {ChatId}: processing RSA handshake packet (crypto role={Role}, BLE role={BleRole})",
            _chat.Id, SessionRoleLabel(), BleSessionRoleLabel() ?? "n/a");
        await _sessionSetup.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsCryptoSessionLeader())
            {
                _logger.LogDebug("Chat {ChatId}: ignored RSA handshake on leader side", _chat.Id);
                return;
            }

            MessengerService? created = null;
            lock (_sync)
            {
                ClearCryptoSession();
                var localPrivate = _auth.GetCurrentPrivateKey();
                _ = _cryptoSessionCache.GetSession(_chat.Id,
                    () => P2PCrypto.CreateSession(localPrivate, handshakePacket));
                _logger.LogInformation(
                    "Chat {ChatId}: crypto session created from handshake (role={Role})",
                    _chat.Id, SessionRoleLabel());

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
                _logger.LogInformation("Chat {ChatId}: messenger started (crypto role={Role}, BLE role={BleRole})",
                _chat.Id, SessionRoleLabel(), BleSessionRoleLabel() ?? "n/a");
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
        LogSessionRoleContext("P2P session stopping");
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
        LogSessionRoleContext("P2P session stopped");
    }

    private bool IsTransportEnabled(TransportKind kind)
    {
        return kind switch
        {
            TransportKind.Udp => _routingSettings?.EnableUdpTransport ?? true,
            TransportKind.Bluetooth => _routingSettings?.EnableBluetoothTransport ?? true,
            _ => false
        };
    }
}