using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Transceivers;

/// <summary>
///     Приёмопередатчик одного типа фреймов поверх UDP/Bluetooth.
///     Только unicast: отправка на конкретный <see cref="TransportAddress" />, broadcast недопустим.
/// </summary>
public interface IUnicastTransceiver<TMessage> : IAsyncDisposable
{
    /// <summary>Триггерится для каждого распознанного входящего сообщения этого типа.</summary>
    event EventHandler<TMessage> GotData;

    ValueTask StartAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Отправка одного сообщения по конкретному адресу пира.</summary>
    ValueTask SendAsync(TMessage message, TransportAddress destination,
        CancellationToken cancellationToken = default);
}