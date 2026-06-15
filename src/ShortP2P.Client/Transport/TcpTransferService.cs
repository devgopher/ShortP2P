using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ShortP2P.Client.Transport;

public sealed class TcpTransferService
{
    private const int HeaderMaxBytes = 8 * 1024;

    public Task<TcpListenerLease> CreateListenerAsync(string transferId, string token, TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();
        var ep = (IPEndPoint)listener.LocalEndpoint;
        return Task.FromResult(new TcpListenerLease(listener, transferId, token, DateTimeOffset.UtcNow.Add(ttl),
            ep.Port));
    }

    public async Task<byte[]> AcceptAndReceiveAsync(TcpListenerLease lease, long expectedSize,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ttlLeft = lease.ExpiresAtUtc - DateTimeOffset.UtcNow;
        if (ttlLeft <= TimeSpan.Zero)
            throw new TimeoutException("TCP listener expired.");
        timeoutCts.CancelAfter(ttlLeft);

        using var client = await lease.Listener.AcceptTcpClientAsync(timeoutCts.Token).ConfigureAwait(false);
        await using var stream = client.GetStream();
        var header = await ReadHeaderAsync(stream, timeoutCts.Token).ConfigureAwait(false);
        ValidateHeader(header, lease.TransferId, lease.Token);
        if (expectedSize > 0 && header.SizeBytes != expectedSize)
            throw new InvalidDataException("Unexpected transfer size.");
        var payload = new byte[header.SizeBytes];
        await ReadExactlyAsync(stream, payload, timeoutCts.Token).ConfigureAwait(false);
        return payload;
    }

    public async Task SendAsync(string host, int port, string transferId, string token, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        await using var stream = client.GetStream();
        var header = new TcpTransferHeader(transferId, token, payload.Length);
        var headerJson = JsonSerializer.Serialize(header);
        var headerUtf8 = Encoding.UTF8.GetBytes(headerJson);
        if (headerUtf8.Length > HeaderMaxBytes)
            throw new InvalidDataException("Header too large.");
        var lenBuf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenBuf, headerUtf8.Length);
        await stream.WriteAsync(lenBuf, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(headerUtf8, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TcpTransferHeader> ReadHeaderAsync(NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var lenBuf = new byte[4];
        await ReadExactlyAsync(stream, lenBuf, cancellationToken).ConfigureAwait(false);
        var len = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
        if (len <= 0 || len > HeaderMaxBytes)
            throw new InvalidDataException("Invalid TCP header length.");
        var headerBuf = new byte[len];
        await ReadExactlyAsync(stream, headerBuf, cancellationToken).ConfigureAwait(false);
        var header = JsonSerializer.Deserialize<TcpTransferHeader>(headerBuf);
        return header ?? throw new InvalidDataException("Invalid TCP header.");
    }

    private static void ValidateHeader(TcpTransferHeader header, string transferId, string token)
    {
        if (!string.Equals(header.TransferId, transferId, StringComparison.Ordinal))
            throw new InvalidDataException("Transfer id mismatch.");
        if (!string.Equals(header.Token, token, StringComparison.Ordinal))
            throw new InvalidDataException("Transfer token mismatch.");
        if (header.SizeBytes < 0)
            throw new InvalidDataException("Invalid transfer size.");
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                throw new EndOfStreamException("Unexpected EOF.");
            offset += read;
        }
    }
}

public sealed class TcpListenerLease(
    TcpListener listener,
    string transferId,
    string token,
    DateTimeOffset expiresAtUtc,
    int port)
    : IDisposable
{
    public TcpListener Listener { get; } = listener;
    public string TransferId { get; } = transferId;
    public string Token { get; } = token;
    public DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;
    public int Port { get; } = port;

    public void Dispose()
    {
        Listener.Stop();
    }
}

public sealed record TcpTransferHeader(string TransferId, string Token, int SizeBytes);