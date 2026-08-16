using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.ServerTech;

/// <summary>TotalPower = 1 + 99 * geo-mean of log-normalized clock / cores / RAM.</summary>
public static class TotalPowerCalculator
{
    public const double MinMhz = 400;
    public const double MaxMhz = 4300;
    public const int MinCores = 1;
    public const int MaxCores = 16;
    public const double MinRamMb = 256;
    public const double MaxRamMb = 65536; // 64 GiB

    public static double Compute(HostHardwareInfo info)
    {
        var sf = NormLog(info.MaxClockMhz, MinMhz, MaxMhz);
        var sc = NormLog(info.LogicalCoreCount, MinCores, MaxCores);
        var sr = NormLog(info.TotalRamMegabytes, MinRamMb, MaxRamMb);
        var g = Math.Cbrt(sf * sc * sr);
        return Clamp(1 + 99 * g, 1, 100);
    }

    private static double NormLog(double value, double min, double max)
    {
        var v = Math.Clamp(value, min, max);
        if (v <= min)
            return 0;
        if (v >= max)
            return 1;
        return Math.Log(v / min) / Math.Log(max / min);
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Min(max, Math.Max(min, value));
}
