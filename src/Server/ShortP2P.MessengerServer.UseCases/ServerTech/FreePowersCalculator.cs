using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.ServerTech;

/// <summary>FreePowers = 100 * sqrt((1 - cpuBusy) * ramAvailableFraction).</summary>
public static class FreePowersCalculator
{
    public static double Compute(HostLoadInfo info)
    {
        var freeCpu = Math.Clamp(1 - info.CpuUtilization, 0, 1);
        var freeRam = Math.Clamp(info.AvailableRamFraction, 0, 1);
        return 100 * Math.Sqrt(freeCpu * freeRam);
    }
}
