namespace ShortP2P.Transport;

/// <summary>Перечень локальных Bluetooth-адаптеров (платформенная реализация).</summary>
public interface IBluetoothRadioCatalog
{
    ValueTask<IReadOnlyList<BluetoothRadioInfo>> ListRadiosAsync(CancellationToken cancellationToken = default);

    /// <summary>MAC для «мои транспорты» по сохранённому <paramref name="deviceId" /> (пусто = default).</summary>
    ValueTask<string?> ResolveMacStringAsync(string? deviceId, CancellationToken cancellationToken = default);
}