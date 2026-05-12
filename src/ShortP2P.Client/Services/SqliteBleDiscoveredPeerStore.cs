using ShortP2P.Auth.Data;
using ShortP2P.Client.Data;
using ShortP2P.Discovery.Ble;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Services;

public sealed class SqliteBleDiscoveredPeerStore(AppDatabase db) : IBleDiscoveredPeerStore
{
    public async ValueTask RecordScanSeenAsync(TransportAddress bluetoothMac,
        CancellationToken cancellationToken = default)
    {
        if (bluetoothMac.Kind != TransportKind.Bluetooth || bluetoothMac.Data.Length != BluetoothTransportAddress.MacLength)
            return;

        var mac = BluetoothTransportAddress.ToMacString(bluetoothMac.Data);
        var conn = await db.GetConnectionAsync().ConfigureAwait(false);
        var now = DateTime.UtcNow.Ticks;
        cancellationToken.ThrowIfCancellationRequested();
        var row = await conn.FindAsync<BleDiscoveredPeerEntity>(mac).ConfigureAwait(false);
        if (row == null)
        {
            await conn.InsertAsync(new BleDiscoveredPeerEntity { MacNormalized = mac, LastSeenScanUtcTicks = now })
                .ConfigureAwait(false);
        }
        else
        {
            row.LastSeenScanUtcTicks = now;
            await conn.UpdateAsync(row).ConfigureAwait(false);
        }
    }

    public async ValueTask RecordPingAsync(TransportAddress bluetoothMac, Guid peerNetworkId, string nickname,
        CancellationToken cancellationToken = default)
    {
        if (bluetoothMac.Kind != TransportKind.Bluetooth || bluetoothMac.Data.Length != BluetoothTransportAddress.MacLength)
            return;

        var mac = BluetoothTransportAddress.ToMacString(bluetoothMac.Data);
        var idShort = CompressedNetworkId.FromGuid(peerNetworkId).ToShortString();
        var nick = string.IsNullOrWhiteSpace(nickname) ? "?" : nickname.Trim();

        var conn = await db.GetConnectionAsync().ConfigureAwait(false);
        var now = DateTime.UtcNow.Ticks;
        cancellationToken.ThrowIfCancellationRequested();
        var row = await conn.FindAsync<BleDiscoveredPeerEntity>(mac).ConfigureAwait(false);
        if (row == null)
        {
            await conn.InsertAsync(new BleDiscoveredPeerEntity
            {
                MacNormalized = mac,
                LastSeenScanUtcTicks = now,
                LastPingUtcTicks = now,
                PeerNetworkIdShort = idShort,
                PeerNickname = nick,
            }).ConfigureAwait(false);
        }
        else
        {
            row.LastPingUtcTicks = now;
            row.PeerNetworkIdShort = idShort;
            row.PeerNickname = nick;
            await conn.UpdateAsync(row).ConfigureAwait(false);
        }
    }
}
