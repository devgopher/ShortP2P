using ShortP2P.Auth.Data;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport.Tests;

public sealed class BleAdvertisementIdentityParserTests
{
    [Fact]
    public void NetworkIdAnnouncePacket_roundtrips()
    {
        var networkId = CompressedNetworkId.New();
        var packet = BleShortP2PGattProtocol.BuildNetworkIdAnnouncePacket(networkId);

        Assert.Equal(BleShortP2PGattProtocol.NetworkIdAnnouncePacketLength, packet.Length);
        Assert.Equal(BleShortP2PGattProtocol.FrameNetworkIdAnnounce, packet[0]);
        Assert.True(BleShortP2PGattProtocol.TryParseNetworkIdAnnouncePacket(packet, out var parsed));
        Assert.Equal(networkId, parsed);
    }

    [Fact]
    public void BuildManufacturerNetworkIdPayload_roundtrips()
    {
        var networkId = CompressedNetworkId.New();
        var payload = BleShortP2PGattProtocol.BuildManufacturerNetworkIdPayload(networkId);

        Assert.Equal(BleShortP2PGattProtocol.ManufacturerNetworkIdPayloadLength, payload.Length);
        Assert.Equal(BleShortP2PGattProtocol.ManufacturerPayloadTypeNetworkId, payload[0]);

        var parsed = BleAdvertisementIdentityParser.ParseManufacturerData(
            BleShortP2PGattProtocol.ManufacturerCompanyId, payload);

        Assert.True(parsed.HasNetworkId);
        Assert.Equal(networkId, parsed.NetworkId);
    }

    [Fact]
    public void ParseManufacturerData_rejects_wrong_company_id()
    {
        var payload = BleShortP2PGattProtocol.BuildManufacturerNetworkIdPayload(CompressedNetworkId.New());

        var parsed = BleAdvertisementIdentityParser.ParseManufacturerData(0xFFFF, payload);

        Assert.False(parsed.HasIdentity);
    }

    [Fact]
    public void ParseManufacturerData_parses_legacy_sp2n_format()
    {
        var networkId = CompressedNetworkId.New();
        var payload = BleShortP2PGattProtocol.BuildManufacturerLegacyNetworkIdPayload(networkId);

        var parsed = BleAdvertisementIdentityParser.ParseManufacturerData(
            BleShortP2PGattProtocol.ManufacturerCompanyId, payload);

        Assert.Equal(networkId, parsed.NetworkId);
    }

    [Fact]
    public void ParseManufacturerEntries_merges_first_valid_entry()
    {
        var expected = CompressedNetworkId.New();
        var valid = BleShortP2PGattProtocol.BuildManufacturerNetworkIdPayload(expected);
        var entries = new[]
        {
            new BleManufacturerDataEntry(0x0001, [0x01, 0x02]),
            new BleManufacturerDataEntry(BleShortP2PGattProtocol.ManufacturerCompanyId, valid),
        };

        var parsed = BleAdvertisementIdentityParser.ParseManufacturerEntries(entries);

        Assert.Equal(expected, parsed.NetworkId);
    }

    [Fact]
    public void IsShortP2P_detects_service_uuid_or_manufacturer_company()
    {
        Assert.True(BleAdvertisementIdentityParser.IsShortP2P(advertisesShortP2PServiceUuid: true, null));
        Assert.True(BleAdvertisementIdentityParser.IsShortP2P(false,
            [BleShortP2PGattProtocol.ManufacturerCompanyId]));
        Assert.False(BleAdvertisementIdentityParser.IsShortP2P(false, [0x1234]));
        Assert.False(BleAdvertisementIdentityParser.IsShortP2P(false, null));
    }

    [Fact]
    public void Merge_prefers_existing_network_id()
    {
        var first = new BleAdScanResult { NetworkId = CompressedNetworkId.New() };
        var second = new BleAdScanResult { NetworkId = CompressedNetworkId.New() };

        var merged = BleAdvertisementIdentityParser.Merge(first, second);

        Assert.Equal(first.NetworkId, merged.NetworkId);
    }
}
