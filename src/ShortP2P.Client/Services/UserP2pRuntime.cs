using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Discovery;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Services;

/// <summary>Discovery + настройки маршрутизации + <see cref="SharedUserUdpGateway"/> для сессии пользователя.</summary>
public sealed class UserP2pRuntime : IAsyncDisposable
{
    private readonly ChatRepository _chats;
    private readonly P2pRoutingSettingsStore _store;
    private UdpPeerDiscoveryService? _discovery;

    public P2pRoutingSettings Settings { get; } = new();

    public SharedUserUdpGateway Gateway { get; }

    public event EventHandler<PeerPresenceChangedEventArgs>? PeerPresenceChanged
    {
        add => Gateway.PeerPresenceChanged += value;
        remove => Gateway.PeerPresenceChanged -= value;
    }

    public UserP2pRuntime(AuthService auth, ChatRepository chats, P2pRoutingSettingsStore store,
        ITransport? bluetoothTransport = null)
    {
        _chats = chats;
        _store = store;
        Gateway = new SharedUserUdpGateway(auth, chats, Settings, bluetoothTransport);
    }

    public async Task EnsureStartedAsync(UserEntity user, CancellationToken cancellationToken = default)
    {
        var persisted = await _store.LoadAsync().ConfigureAwait(false);
        Settings.MaxSearchHops = persisted.MaxSearchHops;
        Settings.SendFailureSearchAttempts = persisted.SendFailureSearchAttempts;
        Settings.SendFailureRetryDelay = persisted.SendFailureRetryDelay;
        Settings.SearchWaitTimeout = persisted.SearchWaitTimeout;

        await Gateway.EnsureStartedAsync(user, cancellationToken).ConfigureAwait(false);

        if (_discovery != null)
        {
            Gateway.SetDiscovery(_discovery);
            return;
        }

        var nid = CompressedNetworkId.FromShortString(user.NetworkIdShort);
        var peer = new PeerIdentity(user.Nickname, nid, user.DataUdpPort);
        _discovery = new UdpPeerDiscoveryService(peer);
        await _discovery.StartAsync(cancellationToken).ConfigureAwait(false);
        Gateway.SetDiscovery(_discovery);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Gateway.SetDiscovery(null);
        Gateway.SetChatSink(null);
        if (_discovery != null)
        {
            try
            {
                await _discovery.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            _discovery = null;
        }

        await Gateway.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    public bool IsPeerOnline(string peerNetworkIdShort)
    {
        var id = CompressedNetworkId.FromShortString(peerNetworkIdShort).Value;
        return Gateway.IsPeerOnline(id);
    }
}
