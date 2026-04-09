using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using ShortP2P.Transport;

namespace ShortP2P.Discovery;

/// <summary>
///     Поиск абонентов в локальной сети по UDP broadcast: периодически объявляет локальный <see cref="PeerIdentity" />
///     и принимает объявления других, сопоставляя их с адресом <see cref="UdpTransportAddress" />.
/// </summary>
public sealed class UdpPeerDiscoveryService : IPeerDiscoveryService
{
    private readonly Channel<DiscoveryNotification> _events =
        Channel.CreateUnbounded<DiscoveryNotification>(new UnboundedChannelOptions());
    private readonly UdpPeerDiscoveryOptions _options;
    private readonly ConcurrentDictionary<Guid, DiscoveredPeer> _peers = new();
    private readonly UdpClient _udp;
    private Task? _announceTask;
    private const int AnnouncePauseMs = 1000;
    private const int ReceivePauseMs = 10;
    
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _staleTask;

    public UdpPeerDiscoveryService(PeerIdentity localPeer, UdpPeerDiscoveryOptions? options = null)
    {
        LocalPeer = localPeer ?? throw new ArgumentNullException(nameof(localPeer));
        _options = options ?? new UdpPeerDiscoveryOptions();
        _udp = new UdpClient(_options.DiscoveryPort)
        {
            EnableBroadcast = true
        };
    }

    public PeerIdentity LocalPeer { get; }

    public ChannelReader<DiscoveryNotification> Notifications => _events.Reader;

    public IReadOnlyCollection<DiscoveredPeer> GetPeersSnapshot()
    {
        return _peers.Values.ToArray();
    }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (_cts != null) return ValueTask.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;
        _receiveTask = Task.Run(() => ReceiveLoopAsync(token), token);
        _announceTask = Task.Run(() => AnnounceLoopAsync(token), token);
        _staleTask = Task.Run(() => StaleLoopAsync(token), token);
        return ValueTask.CompletedTask;
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

        _udp.Close();

        var tasks = new[] { _receiveTask, _announceTask, _staleTask }.Where(t => t != null).ToArray();
        try
        {
            await Task.WhenAll(tasks!).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }

        _receiveTask = _announceTask = _staleTask = null;
        _cts.Dispose();
        _cts = null;
        _events.Writer.TryComplete();
    }

    public ValueTask DisposeAsync()
    {
        return StopAsync();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (!DiscoveryBeaconCodec.TryParseAnnounce(result.Buffer, _options.MaxNicknameUtf8Bytes,
                        out var remoteIdentity) || remoteIdentity == null)
                {
                    await Task.Delay(ReceivePauseMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (remoteIdentity.NetworkId.Value == LocalPeer.NetworkId.Value)
                {
                    await Task.Delay(ReceivePauseMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var addr = UdpTransportAddress.FromIPEndPoint(result.RemoteEndPoint);
                var dataAddr = UdpTransportAddress.WithUdpPort(addr, remoteIdentity.DataUdpPort);
                var discovered = new DiscoveredPeer
                {
                    Identity = remoteIdentity,
                    ReachableAt = addr,
                    DataReachableAt = dataAddr,
                    LastSeenUtc = DateTimeOffset.UtcNow
                };

                _peers[remoteIdentity.NetworkId.Value] = discovered;
                await _events.Writer.WriteAsync(new PeerSeenNotification(discovered), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            try
            {
                await Task.Delay(ReceivePauseMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task AnnounceLoopAsync(CancellationToken cancellationToken)
    {
        var payload = DiscoveryBeaconCodec.EncodeAnnounce(LocalPeer, _options.MaxNicknameUtf8Bytes);
        var broadcastEp = new IPEndPoint(IPAddress.Broadcast, _options.DiscoveryPort);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _udp.SendAsync(payload, broadcastEp, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            try
            {
                await Task.Delay(_options.AnnounceInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await Task.Delay(AnnouncePauseMs, cancellationToken);
        }
    }

    private async Task StaleLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.StaleCheckInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var kv in _peers.ToArray())
            {
                if (now - kv.Value.LastSeenUtc <= _options.PeerStaleTimeout)
                    continue;

                if (!_peers.TryRemove(kv.Key, out _))
                    continue;

                var lost = new CompressedNetworkId(kv.Key);
                await _events.Writer.WriteAsync(new PeerLostNotification(lost), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}