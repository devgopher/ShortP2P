namespace ShortP2P.Discovery;

/// <summary>Пресет «радиоканала» для симуляции минимальной практической скорости (бит/с).</summary>
public enum LinkTechnologyPreset
{
    /// <summary>Без искусственного ограничения скорости.</summary>
    Unlimited = 0,

    /// <summary>2G (GSM), ~9.6 kbit/s.</summary>
    Gsm2G = 1,

    /// <summary>EDGE (2.75G), ~50 kbit/s.</summary>
    Edge275G = 2,

    /// <summary>LoRa / LoRaWAN, ~0.3 kbit/s.</summary>
    Lora = 3,

    /// <summary>3G (UMTS), ~384 kbit/s.</summary>
    Umts3G = 4,

    /// <summary>4G (LTE), ~1 Mbit/s (нижняя граница в плохих условиях).</summary>
    Lte4G = 5,

    /// <summary>Infrared (IrDA), нижняя граница ~9.6 kbit/s.</summary>
    InfraredIrda = 6,

    /// <summary>Bluetooth Classic (BR/EDR), ~200 kbit/s практический минимум.</summary>
    BluetoothClassic = 7,

    /// <summary>Bluetooth Low Energy, ~125 kbit/s.</summary>
    BluetoothLe = 8
}

public static class LinkTechnologyPresetExtensions
{
    public static readonly LinkTechnologyPreset[] AllPresets =
    [
        LinkTechnologyPreset.Unlimited,
        LinkTechnologyPreset.Gsm2G,
        LinkTechnologyPreset.Edge275G,
        LinkTechnologyPreset.Lora,
        LinkTechnologyPreset.Umts3G,
        LinkTechnologyPreset.Lte4G,
        LinkTechnologyPreset.InfraredIrda,
        LinkTechnologyPreset.BluetoothClassic,
        LinkTechnologyPreset.BluetoothLe
    ];

    /// <summary>Минимальная моделируемая скорость в бит/с; 0 — без ограничения.</summary>
    public static long GetSimulatedMinBitsPerSecond(this LinkTechnologyPreset preset)
    {
        return preset switch
        {
            LinkTechnologyPreset.Unlimited => 0,
            LinkTechnologyPreset.Gsm2G => 9_600,
            LinkTechnologyPreset.Edge275G => 50_000,
            LinkTechnologyPreset.Lora => 10_000,
            LinkTechnologyPreset.Umts3G => 384_000,
            LinkTechnologyPreset.Lte4G => 1_000_000,
            LinkTechnologyPreset.InfraredIrda => 9_600,
            LinkTechnologyPreset.BluetoothClassic => 200_000,
            LinkTechnologyPreset.BluetoothLe => 125_000,
            _ => 0
        };
    }

    public static string GetDisplayLabel(this LinkTechnologyPreset preset)
    {
        return preset switch
        {
            LinkTechnologyPreset.Unlimited => "Unlimited (no simulation)",
            LinkTechnologyPreset.Gsm2G => "2G (GSM) — min ~9.6 kbit/s",
            LinkTechnologyPreset.Edge275G => "EDGE (2.75G) — min ~50 kbit/s",
            LinkTechnologyPreset.Lora => "LoRa (LoRaWAN) — min ~10 kbit/s",
            LinkTechnologyPreset.Umts3G => "3G (UMTS) — min ~384 kbit/s",
            LinkTechnologyPreset.Lte4G => "4G (LTE) — min ~1 Mbit/s",
            LinkTechnologyPreset.InfraredIrda => "Infrared (IrDA) — min ~9.6 kbit/s",
            LinkTechnologyPreset.BluetoothClassic => "Bluetooth Classic — min ~200 kbit/s",
            LinkTechnologyPreset.BluetoothLe => "Bluetooth LE — min ~125 kbit/s",
            _ => preset.ToString()
        };
    }

    /// <summary>
    ///     Период UDP/BT presence/discovery-пинга: 1 с; при экономии / ультраэкономии — 10 с.
    /// </summary>
    public static TimeSpan GetPresencePingPeriod(this LinkTechnologyPreset preset,
        TrafficQualityMode trafficQuality = TrafficQualityMode.Normal)
    {
        if (trafficQuality.UsesReducedPresencePing())
            return TimeSpan.FromSeconds(10);
        return TimeSpan.FromSeconds(1);
    }

    /// <summary>Ожидание квитанции доставки сообщения: канал ≤32 kbit/s — 10 с, иначе 3 с; безлимит — 3 с.</summary>
    public static TimeSpan GetMessageAckTimeout(this LinkTechnologyPreset preset)
    {
        var bps = preset.GetSimulatedMinBitsPerSecond();
        return TimeSpan.FromSeconds(bps is > 0 and <= 32_000 ? 10 : 3);
    }
}