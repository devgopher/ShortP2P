using ShortP2P.Auth;
using ShortP2P.Client.Data;
using ShortP2P.Discovery;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Services;

/// <summary>
///     Собирает BLE-цели для presence: endpoint'ы из чатов + строки из <c>ble_discovered_peers</c>.
/// </summary>
public sealed class BluetoothPresencePingTargetsProvider(AuthService auth, ChatRepository chats, AppDatabase db)
    : IBluetoothPresencePingTargetsProvider
{
    public async ValueTask<IReadOnlyList<TransportAddress>> GetBluetoothPingTargetsAsync(
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
                if (ep.Kind != TransportKind.Bluetooth || ep.Data.Length != BluetoothTransportAddress.MacLength)
                    continue;
                dedup[Convert.ToBase64String(ep.Data)] = ep;
            }
        }

        var conn = await db.GetConnectionAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var bleRows = await conn.Table<BleDiscoveredPeerEntity>().ToListAsync().ConfigureAwait(false);
        foreach (var row in bleRows)
        {
            if (string.IsNullOrWhiteSpace(row.MacNormalized))
                continue;
            if (!BluetoothTransportAddress.TryParseMac(row.MacNormalized, out var mac))
                continue;
            var ep = BluetoothTransportAddress.FromMac(mac);
            dedup[Convert.ToBase64String(ep.Data)] = ep;
        }

        return dedup.Values.ToList();
    }
}