using System.Threading.Channels;

namespace ShortP2P.Transport.Abstractions;

/// <summary>
///     Транспортный слой: отправка сырых байт и приём в канал (для последующей расшифровки в мессенджере).
/// </summary>
public interface ITransport : IAsyncDisposable
{
    TransportKind Kind { get; }

    /// <summary>
    ///     Входящие пакеты. Заполняется после <see cref="StartAsync" />.
    /// </summary>
    ChannelReader<TransportReceiveMessage> Inbound { get; }

    ValueTask StartAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Отправка одного блока данных (для UDP — один датаграмма; для serial — с внутренним кадрированием).
    /// </summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
        CancellationToken cancellationToken = default);
}