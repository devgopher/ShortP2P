using System.Net;
using System.Text.Json;
using ShortP2P.Client.Data;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client;

/// <summary>Хранение/чтение набора peer endpoint (UDP/Bluetooth) в чате.</summary>
public static class PeerTransportEndpoints
{
    public static IReadOnlyList<TransportAddress> Parse(ChatEntity chat)
    {
        if (!string.IsNullOrWhiteSpace(chat.PeerEndpointsJson))
            try
            {
                var arr = JsonSerializer.Deserialize<List<EndpointDto>>(chat.PeerEndpointsJson!) ?? [];
                var list = new List<TransportAddress>(arr.Count);
                foreach (var x in arr)
                {
                    if (!Enum.IsDefined(typeof(TransportKind), (byte)x.K))
                        continue;
                    var data = Convert.FromBase64String(x.D);
                    list.Add(new TransportAddress((TransportKind)x.K, data));
                }

                if (list.Count > 0)
                    return list;
            }
            catch
            {
                // fallback to legacy fields
            }

        var legacy = new List<TransportAddress>();
        foreach (var host in (chat.PeerHost ?? string.Empty).Split([',', ';', '|', ' ', '\n', '\r', '\t'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (IPAddress.TryParse(host, out var ip))
                legacy.Add(UdpTransportAddress.FromIPEndPoint(new IPEndPoint(ip, chat.PeerPort)));
            else if (BluetoothTransportAddress.TryParseMac(host, out var mac))
                legacy.Add(BluetoothTransportAddress.FromMac(mac));

        return legacy;
    }

    public static string ReplaceBluetooth(IEnumerable<TransportAddress> endpoints, TransportAddress bluetoothEndpoint)
    {
        var list = endpoints.Where(e => e.Kind != TransportKind.Bluetooth).ToList();
        list.Add(bluetoothEndpoint);
        return Serialize(list);
    }

    public static string Serialize(IEnumerable<TransportAddress> endpoints)
    {
        var arr = endpoints
            .Select(e => new EndpointDto { K = (int)e.Kind, D = Convert.ToBase64String(e.Data) })
            .ToList();
        return JsonSerializer.Serialize(arr);
    }

    private sealed class EndpointDto
    {
        public int K { get; set; }
        public string D { get; set; } = "";
    }
}