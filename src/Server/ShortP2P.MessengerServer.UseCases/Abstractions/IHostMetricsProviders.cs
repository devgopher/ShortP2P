namespace ShortP2P.MessengerServer.UseCases.Abstractions;

public sealed record HostHardwareInfo(
    double MaxClockMhz,
    int LogicalCoreCount,
    double TotalRamMegabytes);

public sealed record HostLoadInfo(
    double CpuUtilization,
    double AvailableRamFraction);

public interface IHostHardwareInfoProvider
{
    Task<HostHardwareInfo?> TryGetAsync(CancellationToken cancellationToken = default);
}

public interface IHostLoadInfoProvider
{
    Task<HostLoadInfo?> TryGetAsync(CancellationToken cancellationToken = default);
}
