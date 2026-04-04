using System.Threading.Channels;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport;

/// <summary>
///     Объединяет входящие потоки нескольких транспортов в один канал (прототип mesh-приёма).
///     Отправка по-прежнему выполняется через выбранный <see cref="ITransport" />.
/// </summary>
public sealed class MeshInboundMerge : IAsyncDisposable
{
    private readonly List<Task> _forwarders = new();
    private readonly Channel<TransportReceiveMessage> _merged = Channel.CreateUnbounded<TransportReceiveMessage>();
    private readonly List<ITransport> _transports = new();
    private CancellationTokenSource? _cts;

    public ChannelReader<TransportReceiveMessage> Inbound => _merged.Reader;

    public ValueTask DisposeAsync()
    {
        return StopAsync();
    }

    public void AddTransport(ITransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transports.Add(transport);
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (_cts != null) return;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;

        foreach (var t in _transports)
        {
            await t.StartAsync(token).ConfigureAwait(false);
            var tr = t;
            _forwarders.Add(Task.Run(() => ForwardAsync(tr, token), token));
        }
    }

    private async Task ForwardAsync(ITransport transport, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var msg in transport.Inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                await _merged.Writer.WriteAsync(msg, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts == null) return;
        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            _cts.Cancel();
        }

        foreach (var t in _transports)
            try
            {
                await t.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }

        await Task.WhenAll(_forwarders).ConfigureAwait(false);
        _forwarders.Clear();
        _cts.Dispose();
        _cts = null;
        _merged.Writer.TryComplete();
    }
}