using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace ShortP2P.Transport;

/// <summary>
///     Singleton-фабрика UDP: для каждой тройки (bind IP, порт, broadcast) один экземпляр
///     <see cref="UdpTransport" />; освобождение через <see cref="ReleaseAsync" /> со счётчиком ссылок.
///     Порты с несколькими независимыми читателями одного канала (например data UDP чата при нескольких сессиях)
///     по-прежнему создают отдельные сокеты через <see cref="UdpTransport.CreateUdpTransport" />.
/// </summary>
public interface IUdpTransportFactory
{
    UdpTransport Acquire(IPAddress ip, int port, bool enableBroadcast = false);

    ValueTask ReleaseAsync(UdpTransport transport, CancellationToken cancellationToken = default);
}

public sealed class UdpTransportFactory : IUdpTransportFactory
{
    private readonly ConcurrentDictionary<UdpTransportCacheKey, CacheEntry> _entries = new();

    public UdpTransport Acquire(IPAddress ip, int port, bool enableBroadcast = false)
    {
        ArgumentNullException.ThrowIfNull(ip);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        var key = new UdpTransportCacheKey(ip, port, enableBroadcast);
        var entry = _entries.GetOrAdd(key, static k =>
            new CacheEntry(UdpTransport.CreateUdpTransport(k.Ip, k.Port, k.EnableBroadcast)));
        lock (entry.Gate)
        {
            entry.RefCount++;
        }

        return entry.Transport;
    }

    public async ValueTask ReleaseAsync(UdpTransport transport, CancellationToken cancellationToken = default)
    {
        CacheEntry? found = null;
        UdpTransportCacheKey? foundKey = null;
        foreach (var kv in _entries)
            if (ReferenceEquals(kv.Value.Transport, transport))
            {
                found = kv.Value;
                foundKey = kv.Key;
                break;
            }

        if (found == null || foundKey is not { } key)
            return;

        var shouldDispose = false;
        lock (found.Gate)
        {
            found.RefCount--;
            if (found.RefCount < 0)
                throw new InvalidOperationException("UDP transport release underflow.");
            if (found.RefCount == 0)
                shouldDispose = true;
        }

        if (!shouldDispose)
            return;

        _entries.TryRemove(key, out _);
        try
        {
            await transport.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // expected if socket already closed
        }
        catch (SocketException)
        {
            // ignore
        }
    }

    private sealed class CacheEntry(UdpTransport transport)
    {
        public int RefCount;
        public UdpTransport Transport { get; } = transport;
        public object Gate { get; } = new();
    }

    private readonly struct UdpTransportCacheKey : IEquatable<UdpTransportCacheKey>
    {
        public UdpTransportCacheKey(IPAddress ip, int port, bool enableBroadcast)
        {
            Ip = ip;
            Port = port;
            EnableBroadcast = enableBroadcast;
        }

        public IPAddress Ip { get; }
        public int Port { get; }
        public bool EnableBroadcast { get; }

        public bool Equals(UdpTransportCacheKey other)
        {
            return Port == other.Port && EnableBroadcast == other.EnableBroadcast && Ip.Equals(other.Ip);
        }

        public override bool Equals(object? obj)
        {
            return obj is UdpTransportCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Ip.GetHashCode(), Port, EnableBroadcast);
        }
    }
}