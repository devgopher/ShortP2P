using ShortP2P.Auth.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.WifiDirect;

/// <summary>Создаёт и пересоздаёт платформенный Wi-Fi Direct транспорт по настройкам Routing.</summary>
public interface IWifiDirectTransportProvider
{
    ITransport? Current { get; }

    void SetLocalNetworkId(CompressedNetworkId? networkId);

    void ApplySettings(P2pRoutingSettings settings);
}
