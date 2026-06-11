using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ShortP2P.Auth.Data;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;
using Windows.Devices.Enumeration;
using Windows.Devices.WiFiDirect;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace ShortP2P.Transport.WifiDirect.Windows;

/// <summary>
///     Wi-Fi Direct на Windows: vendor IE discovery + framed stream socket для presence/invite/discovery/data.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsWifiDirectTransport(WindowsWifiDirectTransportOptions options) : ITransport
{
    private const int DefaultChannelCapacity = 256;

    private static readonly string[] DeviceProperties =
    [
        "System.Devices.WiFiDirect.InformationElements"
    ];

    private readonly ConcurrentDictionary<string, PeerSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _reportedPeers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _knownDeviceIds = new(StringComparer.Ordinal);
    private readonly ILogger? _logger = options.Logger;

    private readonly Channel<TransportReceiveMessage> _inbound =
        Channel.CreateBounded<TransportReceiveMessage>(new BoundedChannelOptions(DefaultChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    private WiFiDirectAdvertisementPublisher? _publisher;
    private WiFiDirectConnectionListener? _connectionListener;
    private DeviceWatcher? _watcher;
    private StreamSocketListener? _socketListener;
    private CancellationTokenSource? _runCts;
    private bool _disposed;

    public TransportKind Kind => TransportKind.WifiDirect;

    public ChannelReader<TransportReceiveMessage> Inbound => _inbound.Reader;

    public bool IsRunning => _publisher != null;

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_publisher != null)
            return;

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _runCts.Token;
        try
        {
            StartPublisher();
            StartWatcher();
            StartConnectionListener();
            await StartSocketListenerAsync(ct).ConfigureAwait(false);
            _logger?.LogInformation("Wi-Fi Direct transport started (IE discovery + port {Port})",
                WifiDirectShortP2PProtocol.ServicePort);
        }
        catch (Exception ex)
        {
            StopCore();
            _logger?.LogWarning(ex, "Wi-Fi Direct transport failed to start");
        }
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        StopCore();
        return ValueTask.CompletedTask;
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (payload.IsEmpty)
            return;

        if (destination is { Kind: TransportKind.WifiDirect, Data.Length: > 0 } &&
            WifiDirectTransportAddress.TryParseAddress(destination.Data, out var deviceId))
        {
            await SendToDeviceAsync(deviceId, payload, cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var id in _knownDeviceIds.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await SendToDeviceAsync(id, payload, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore single peer
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _inbound.Writer.TryComplete();
    }

    private void StartPublisher()
    {
        var publisher = new WiFiDirectAdvertisementPublisher();
        var advertisement = publisher.Advertisement;
        advertisement.IsAutonomousGroupOwnerEnabled = true;
        advertisement.ListenStateDiscoverability = WiFiDirectAdvertisementListenStateDiscoverability.Normal;

        if (options.LocalNetworkId is { IsEmpty: false } local)
        {
            var payload = WifiDirectShortP2PProtocol.BuildNetworkIdPayload(local);
            advertisement.InformationElements.Add(CreateInformationElement(payload));
        }

        publisher.Start();
        _publisher = publisher;
    }

    private void StartWatcher()
    {
        var watcher = DeviceInformation.CreateWatcher(
            WiFiDirectDevice.GetDeviceSelector(),
            DeviceProperties,
            DeviceInformationKind.AssociationEndpoint);
        watcher.Added += OnDeviceAdded;
        watcher.Updated += OnDeviceUpdated;
        watcher.Start();
        _watcher = watcher;
    }

    private void StartConnectionListener()
    {
        var listener = new WiFiDirectConnectionListener();
        listener.ConnectionRequested += OnConnectionRequested;
        _connectionListener = listener;
    }

    private async Task StartSocketListenerAsync(CancellationToken ct)
    {
        var listener = new StreamSocketListener();
        listener.ConnectionReceived += OnSocketConnectionReceived;
        await listener.BindServiceNameAsync(WifiDirectShortP2PProtocol.ServicePort).AsTask(ct).ConfigureAwait(false);
        _socketListener = listener;
    }

    private void OnConnectionRequested(WiFiDirectConnectionListener sender,
        WiFiDirectConnectionRequestedEventArgs args)
    {
        try
        {
            var request = args.GetConnectionRequest();
            var deviceId = request.DeviceInformation.Id;
            if (string.IsNullOrWhiteSpace(deviceId))
                return;
            RememberDevice(deviceId);
            _logger?.LogDebug("Wi-Fi Direct incoming connection from {DeviceId}", deviceId);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Wi-Fi Direct connection request failed");
        }
    }

    private void OnSocketConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
    {
        var socket = args.Socket;
        if (socket == null)
            return;
        var remote = socket.Information.RemoteAddress?.DisplayName ?? socket.Information.RemoteHostName?.DisplayName
                     ?? Guid.NewGuid().ToString("N");
        _ = Task.Run(() => ReadSocketLoopAsync(socket, WifiDirectTransportAddress.FromAddress(remote)));
    }

    private async Task ReadSocketLoopAsync(StreamSocket socket, TransportAddress remote)
    {
        try
        {
            using var socketRef = socket;
            var reader = new DataReader(socket.InputStream);
            reader.InputStreamOptions = InputStreamOptions.Partial;
            while (true)
            {
                await ReadExactAsync(reader, 4).ConfigureAwait(false);
                var len = reader.ReadUInt32();
                if (len == 0 || len > WifiDirectShortP2PProtocol.MaxFrameLength)
                    break;
                await ReadExactAsync(reader, len).ConfigureAwait(false);
                var payload = new byte[len];
                reader.ReadBytes(payload);
                await _inbound.Writer.WriteAsync(new TransportReceiveMessage(payload, remote)).ConfigureAwait(false);
            }
        }
        catch
        {
            // connection closed
        }
    }

    private static async Task ReadExactAsync(DataReader reader, uint count)
    {
        while (reader.UnconsumedBufferLength < count)
        {
            var loaded = await reader.LoadAsync(count).AsTask().ConfigureAwait(false);
            if (loaded == 0)
                throw new EndOfStreamException();
        }
    }

    private async Task SendToDeviceAsync(string deviceId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        RememberDevice(deviceId);
        var session = _sessions.GetOrAdd(deviceId, _ => new PeerSession(deviceId));
        await session.SendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (session.Socket == null)
                await ConnectSocketAsync(session, cancellationToken).ConfigureAwait(false);
            if (session.Socket == null || session.Writer == null)
                return;

            var frame = new byte[4 + payload.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)payload.Length);
            payload.CopyTo(frame.AsMemory(4));
            session.Writer.WriteBytes(frame);
            await session.Writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(false);
            await session.Writer.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            session.SendLock.Release();
        }
    }

    private async Task ConnectSocketAsync(PeerSession session, CancellationToken cancellationToken)
    {
        try
        {
            var connectionParams = new WiFiDirectConnectionParameters
            {
                GroupOwnerIntent = 15
            };
            var device = await WiFiDirectDevice.FromIdAsync(session.DeviceId, connectionParams)
                .AsTask(cancellationToken).ConfigureAwait(false);
            if (device == null)
                return;

            if (device.ConnectionStatus != WiFiDirectConnectionStatus.Connected)
                return;

            foreach (var pair in device.GetConnectionEndpointPairs())
            {
                try
                {
                    var socket = new StreamSocket();
                    await socket.ConnectAsync(pair.RemoteHostName, WifiDirectShortP2PProtocol.ServicePort)
                        .AsTask(cancellationToken).ConfigureAwait(false);
                    session.Socket = socket;
                    session.Writer = new DataWriter(socket.OutputStream);
                    return;
                }
                catch
                {
                    // try next endpoint
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Wi-Fi Direct connect to {DeviceId} failed", session.DeviceId);
        }
    }

    private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation args)
    {
        ProcessDevice(args);
    }

    private async void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        try
        {
            var info = await DeviceInformation.CreateFromIdAsync(args.Id, DeviceProperties).AsTask()
                .ConfigureAwait(false);
            if (info != null)
                ProcessDevice(info);
        }
        catch
        {
            // ignore
        }
    }

    private void ProcessDevice(DeviceInformation info)
    {
        if (string.IsNullOrWhiteSpace(info.Id))
            return;

        RememberDevice(info.Id);
        try
        {
            var elements = WiFiDirectInformationElement.CreateFromDeviceInformation(info);
            foreach (var element in elements)
            {
                var oui = BufferToBytes(element.Oui);
                if (!WifiDirectShortP2PProtocol.MatchesInformationElement(oui, element.OuiType))
                    continue;

                var payload = BufferToBytes(element.Value);
                if (!WifiDirectShortP2PProtocol.TryParseNetworkIdPayload(payload, out var peerNetworkId))
                    continue;

                if (options.LocalNetworkId is { } local && local == peerNetworkId)
                    return;

                var dedupeKey = $"{info.Id}|{peerNetworkId.ToShortString()}";
                if (!_reportedPeers.TryAdd(dedupeKey, 0))
                    return;

                var addr = WifiDirectTransportAddress.FromAddress(info.Id);
                _logger?.LogInformation("Wi-Fi Direct peer discovered: {DeviceId} networkId={NetworkId}",
                    info.Id, peerNetworkId.ToShortString());
                options.OnPeerNetworkIdReceived?.Invoke(addr, peerNetworkId);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Wi-Fi Direct device parse failed: {DeviceId}", info.Id);
        }
    }

    private void RememberDevice(string deviceId)
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
            _knownDeviceIds.TryAdd(deviceId, 0);
    }

    private static WiFiDirectInformationElement CreateInformationElement(byte[] payload)
    {
        var ouiWriter = new DataWriter();
        ouiWriter.WriteBytes(WifiDirectShortP2PProtocol.VendorOui);
        var valueWriter = new DataWriter();
        valueWriter.WriteBytes(payload);
        return new WiFiDirectInformationElement
        {
            Oui = ouiWriter.DetachBuffer(),
            OuiType = WifiDirectShortP2PProtocol.OuiType,
            Value = valueWriter.DetachBuffer()
        };
    }

    private static byte[] BufferToBytes(IBuffer buffer)
    {
        if (buffer.Length == 0)
            return [];
        var reader = DataReader.FromBuffer(buffer);
        var data = new byte[buffer.Length];
        reader.ReadBytes(data);
        return data;
    }

    private void StopCore()
    {
        try
        {
            _runCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        var watcher = _watcher;
        _watcher = null;
        if (watcher != null)
        {
            try
            {
                watcher.Added -= OnDeviceAdded;
                watcher.Updated -= OnDeviceUpdated;
                watcher.Stop();
            }
            catch
            {
                // ignore
            }
        }

        _connectionListener = null;
        var socketListener = _socketListener;
        _socketListener = null;
        socketListener?.Dispose();

        var publisher = _publisher;
        _publisher = null;
        if (publisher != null)
        {
            try
            {
                publisher.Stop();
            }
            catch
            {
                // ignore
            }
        }

        foreach (var session in _sessions.Values)
            session.Dispose();
        _sessions.Clear();
        _reportedPeers.Clear();
        _knownDeviceIds.Clear();

        _runCts?.Dispose();
        _runCts = null;
    }

    private sealed class PeerSession(string deviceId) : IDisposable
    {
        public string DeviceId { get; } = deviceId;
        public StreamSocket? Socket { get; set; }
        public DataWriter? Writer { get; set; }
        public SemaphoreSlim SendLock { get; } = new(1, 1);

        public void Dispose()
        {
            try
            {
                Writer?.DetachStream();
            }
            catch
            {
                // ignore
            }

            Writer?.Dispose();
            Socket?.Dispose();
            SendLock.Dispose();
        }
    }
}
