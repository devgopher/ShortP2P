using ShortP2P.Auth.Data;
using ShortP2P.Client.Data;
using ShortP2P.Discovery.Ble;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Services;

public sealed class SqliteBleDiscoveredPeerStore(AppDatabase db) : IBleDiscoveredPeerStore
{
    public async ValueTask RecordScanSeenAsync(TransportAddress bluetoothMac, BleAdScanResult scanResult = default,
        CancellationToken cancellationToken = default)
    {
        if (bluetoothMac.Kind != TransportKind.Bluetooth || bluetoothMac.Data.Length != BluetoothTransportAddress.MacLength)
            return;

        var mac = BluetoothTransportAddress.ToMacString(bluetoothMac.Data);
        string? hintHex = scanResult.HasHint ? Convert.ToHexString(scanResult.NetworkIdHint.Span) : null;
        string? idShort = null;
        if (scanResult.LegacyFullNetworkId is Guid legacy && legacy != Guid.Empty)
            idShort = CompressedNetworkId.FromGuid(legacy).ToShortString();

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
                PeerNetworkIdShort = idShort,
                PeerNetworkIdHintHex = hintHex,
            }).ConfigureAwait(false);
        }
        else
        {
            row.LastSeenScanUtcTicks = now;
            if (hintHex != null)
                row.PeerNetworkIdHintHex = hintHex;
            if (idShort != null)
                row.PeerNetworkIdShort = idShort;
            await conn.UpdateAsync(row).ConfigureAwait(false);
        }
    }

    public async ValueTask RecordPingAsync(TransportAddress bluetoothMac, Guid peerNetworkId, string nickname,
        CancellationToken cancellationToken = default)
    {
        if (bluetoothMac.Kind != TransportKind.Bluetooth || bluetoothMac.Data.Length != BluetoothTransportAddress.MacLength)
            return;
        if (peerNetworkId == Guid.Empty)
            return;

        var mac = BluetoothTransportAddress.ToMacString(bluetoothMac.Data);
        var idShort = CompressedNetworkId.FromGuid(peerNetworkId).ToShortString();
        var nick = string.IsNullOrWhiteSpace(nickname) ? "?" : nickname.Trim();
        var hintHex = Convert.ToHexString(GetHintBytes(peerNetworkId));

        var conn = await db.GetConnectionAsync().ConfigureAwait(false);
        var now = DateTime.UtcNow.Ticks;
        cancellationToken.ThrowIfCancellationRequested();
        var row = await conn.FindAsync<BleDiscoveredPeerEntity>(mac).ConfigureAwait(false);
        if (row != null
            && !string.IsNullOrEmpty(row.PeerNetworkIdHintHex)
            && !string.Equals(row.PeerNetworkIdHintHex, hintHex, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (row == null)
        {
            await conn.InsertAsync(new BleDiscoveredPeerEntity
            {
                MacNormalized = mac,
                LastSeenScanUtcTicks = now,
                LastPingUtcTicks = now,
                PeerNetworkIdShort = idShort,
                PeerNetworkIdHintHex = hintHex,
                PeerNickname = nick,
            }).ConfigureAwait(false);
        }
        else
        {
            row.LastPingUtcTicks = now;
            row.PeerNetworkIdShort = idShort;
            row.PeerNetworkIdHintHex = hintHex;
            row.PeerNickname = nick;
            await conn.UpdateAsync(row).ConfigureAwait(false);
        }
    }

    private static byte[] GetHintBytes(Guid networkId)
    {
        var hint = new byte[BleAdScanResult.NetworkIdHintLength];
        BleShortP2PGattProtocol.TryWriteNetworkIdHint(networkId, hint);
        return hint;
    }
}
