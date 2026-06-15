using ShortP2P.Auth.Data;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Transceivers;

/// <summary>
///     Распознанный BLE-кадр NetworkId с адресом отправителя.
/// </summary>
public sealed class BleNetworkIdMessage(CompressedNetworkId networkId, TransportAddress remoteAddress)
{
    public CompressedNetworkId NetworkId { get; } = networkId;

    public TransportAddress RemoteAddress { get; } = remoteAddress;
}
