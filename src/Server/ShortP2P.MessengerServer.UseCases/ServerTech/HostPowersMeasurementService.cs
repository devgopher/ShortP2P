using Microsoft.Extensions.Logging;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.ServerTech;

/// <summary>Measures TotalPower / FreePowers, persists to LiteDB, logs each measurement.</summary>
public sealed class HostPowersMeasurementService(
    IServerHostPowersRepository repository,
    IHostHardwareInfoProvider hardware,
    IHostLoadInfoProvider load,
    IClock clock,
    ILogger<HostPowersMeasurementService> logger)
{
    public async Task MeasureTotalPowerAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        ServerHostPowers current;
        try
        {
            current = await repository.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "TotalPower: failed to read store; using defaults.");
            current = ServerHostPowers.CreateDefaults(now);
        }

        double value;
        try
        {
            var info = await hardware.TryGetAsync(cancellationToken).ConfigureAwait(false);
            if (info is null || !IsValidHardware(info))
            {
                value = ServerHostPowers.DefaultTotalPower;
                logger.LogWarning(
                    "TotalPower measurement unavailable or malformed; using default {TotalPower:F2} at {MeasuredAtUtc:o}",
                    value,
                    now);
            }
            else
            {
                value = TotalPowerCalculator.Compute(info);
                if (!IsValidTotalPower(value))
                {
                    value = ServerHostPowers.DefaultTotalPower;
                    logger.LogWarning(
                        "TotalPower computed value invalid; using default {TotalPower:F2} at {MeasuredAtUtc:o}",
                        value,
                        now);
                }
                else
                {
                    logger.LogInformation(
                        "TotalPower measured: {TotalPower:F2} (f={Mhz:F0} MHz c={Cores} ramMb={RamMb:F0}) at {MeasuredAtUtc:o}",
                        value,
                        info.MaxClockMhz,
                        info.LogicalCoreCount,
                        info.TotalRamMegabytes,
                        now);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            value = ServerHostPowers.DefaultTotalPower;
            logger.LogWarning(
                ex,
                "TotalPower measurement threw; using default {TotalPower:F2} at {MeasuredAtUtc:o}",
                value,
                now);
        }

        await TryUpsertAsync(
            new ServerHostPowers
            {
                Id = ServerHostPowers.SingletonId,
                TotalPower = value,
                FreePowers = IsValidFreePowers(current.FreePowers)
                    ? current.FreePowers
                    : ServerHostPowers.DefaultFreePowers,
                TotalPowerMeasuredAtUtc = now,
                FreePowersMeasuredAtUtc = current.FreePowersMeasuredAtUtc
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task MeasureFreePowersAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        ServerHostPowers current;
        try
        {
            current = await repository.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "FreePowers: failed to read store; using defaults.");
            current = ServerHostPowers.CreateDefaults(now);
        }

        double value;
        try
        {
            var info = await load.TryGetAsync(cancellationToken).ConfigureAwait(false);
            if (info is null || !IsValidLoad(info))
            {
                value = ServerHostPowers.DefaultFreePowers;
                logger.LogWarning(
                    "FreePowers measurement unavailable or malformed; using default {FreePowers:F2} at {MeasuredAtUtc:o}",
                    value,
                    now);
            }
            else
            {
                value = FreePowersCalculator.Compute(info);
                if (!IsValidFreePowers(value))
                {
                    value = ServerHostPowers.DefaultFreePowers;
                    logger.LogWarning(
                        "FreePowers computed value invalid; using default {FreePowers:F2} at {MeasuredAtUtc:o}",
                        value,
                        now);
                }
                else
                {
                    logger.LogInformation(
                        "FreePowers measured: {FreePowers:F2}% (cpuBusy={CpuBusy:P0} ramAvail={RamAvail:P0}) at {MeasuredAtUtc:o}",
                        value,
                        info.CpuUtilization,
                        info.AvailableRamFraction,
                        now);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            value = ServerHostPowers.DefaultFreePowers;
            logger.LogWarning(
                ex,
                "FreePowers measurement threw; using default {FreePowers:F2} at {MeasuredAtUtc:o}",
                value,
                now);
        }

        await TryUpsertAsync(
            new ServerHostPowers
            {
                Id = ServerHostPowers.SingletonId,
                TotalPower = IsValidTotalPower(current.TotalPower)
                    ? current.TotalPower
                    : ServerHostPowers.DefaultTotalPower,
                FreePowers = value,
                TotalPowerMeasuredAtUtc = current.TotalPowerMeasuredAtUtc,
                FreePowersMeasuredAtUtc = now
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task TryUpsertAsync(ServerHostPowers powers, CancellationToken cancellationToken)
    {
        try
        {
            await repository.UpsertAsync(powers, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to persist host powers snapshot.");
        }
    }

    private static bool IsValidHardware(HostHardwareInfo info) =>
        double.IsFinite(info.MaxClockMhz) && info.MaxClockMhz > 0 &&
        info.LogicalCoreCount > 0 &&
        double.IsFinite(info.TotalRamMegabytes) && info.TotalRamMegabytes > 0;

    private static bool IsValidLoad(HostLoadInfo info) =>
        double.IsFinite(info.CpuUtilization) &&
        info.CpuUtilization is >= 0 and <= 1.0001 &&
        double.IsFinite(info.AvailableRamFraction) &&
        info.AvailableRamFraction is >= 0 and <= 1.0001;

    private static bool IsValidTotalPower(double value) =>
        double.IsFinite(value) && value is >= 1 and <= 100;

    private static bool IsValidFreePowers(double value) =>
        double.IsFinite(value) && value is >= 0 and <= 100;
}
