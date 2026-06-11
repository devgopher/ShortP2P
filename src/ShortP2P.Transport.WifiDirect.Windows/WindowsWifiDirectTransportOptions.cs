using Microsoft.Extensions.Logging;
using ShortP2P.Auth.Data;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport.WifiDirect.Windows;

/// <param name="LocalNetworkId">Локальный NetworkId для vendor IE в Wi-Fi Direct рекламе.</param>
/// <param name="OnPeerNetworkIdReceived">Вызывается после приёма NetworkId из IE пира.</param>
/// <param name="Logger">Опциональный логгер.</param>
public readonly record struct WindowsWifiDirectTransportOptions(
    CompressedNetworkId? LocalNetworkId = null,
    Action<TransportAddress, CompressedNetworkId>? OnPeerNetworkIdReceived = null,
    ILogger? Logger = null);
