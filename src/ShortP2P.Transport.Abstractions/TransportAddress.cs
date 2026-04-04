namespace ShortP2P.Transport.Abstractions;

/// <summary>
///     Адрес пира в рамках конкретного транспорта. Содержимое <see cref="Data" /> задаёт реализация транспорта.
/// </summary>
public sealed class TransportAddress(TransportKind kind, byte[] data)
{
    public TransportKind Kind { get; } = kind;

    public byte[] Data { get; } = data ?? throw new ArgumentNullException(nameof(data));
}