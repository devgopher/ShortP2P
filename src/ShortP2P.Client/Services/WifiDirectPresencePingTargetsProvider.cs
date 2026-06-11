using ShortP2P.Auth;
using ShortP2P.Client.Data;
using ShortP2P.Discovery;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Services;

public sealed class WifiDirectPresencePingTargetsProvider(AuthService auth, ChatRepository chats, AppDatabase db)
    : IWifiDirectPresencePingTargetsProvider
{
    public async ValueTask<IReadOnlyList<TransportAddress>> GetWifiDirectPingTargetsAsync(
        CancellationToken cancellationToken = default)
    {
        var dedup = new Dictionary<string, TransportAddress>(StringComparer.Ordinal);
        var user = auth.CurrentUser;
        if (user != null)
        {
            var list = await chats.ListChatsAsync(user.Id).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var chat in list)
            foreach (var ep in PeerTransportEndpoints.Parse(chat))
            {
                if (ep.Kind != TransportKind.WifiDirect)
                    continue;
                if (!WifiDirectTransportAddress.TryParseAddress(ep.Data, out _))
                    continue;
                dedup[Convert.ToBase64String(ep.Data)] = ep;
            }
        }

        var conn = await db.GetConnectionAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await conn.Table<BleDiscoveredPeerEntity>().ToListAsync().ConfigureAwait(false);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.MacNormalized))
                continue;
            if (BluetoothTransportAddress.TryParseMac(row.MacNormalized, out _))
                continue;
            var ep = WifiDirectTransportAddress.FromAddress(row.MacNormalized);
            dedup[Convert.ToBase64String(ep.Data)] = ep;
        }

        return dedup.Values.ToList();
    }
}
