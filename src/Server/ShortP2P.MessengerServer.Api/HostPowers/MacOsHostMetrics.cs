using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ShortP2P.MessengerServer.Api.HostPowers;

/// <summary>macOS sysctl / Mach helpers for TotalPower and FreePowers probes.</summary>
[SupportedOSPlatform("macos")]
internal static class MacOsHostMetrics
{
    /// <summary>
    /// Apple Silicon often omits cpufrequency sysctls; mid-high estimate so cores/RAM still score.
    /// </summary>
    public const double AppleSiliconFallbackMhz = 3200;

    public static double? TryReadMaxClockMhz()
    {
        if (TrySysctlUInt64("hw.cpufrequency_max", out var maxHz) && maxHz > 0)
            return maxHz / 1_000_000.0;
        if (TrySysctlUInt64("hw.cpufrequency", out var hz) && hz > 0)
            return hz / 1_000_000.0;

        if (RuntimeInformation.OSArchitecture is Architecture.Arm64 or Architecture.Arm)
            return AppleSiliconFallbackMhz;

        return null;
    }

    public static double? TryReadTotalRamMb()
    {
        if (!TrySysctlUInt64("hw.memsize", out var bytes) || bytes == 0)
            return null;
        return bytes / (1024.0 * 1024.0);
    }

    public static async Task<double?> TryReadCpuBusyAsync(CancellationToken cancellationToken)
    {
        if (!TryReadCpuTicks(out var a))
            return null;
        await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        if (!TryReadCpuTicks(out var b))
            return null;

        var idle = b.Idle - a.Idle;
        var total = b.Total - a.Total;
        if (total <= 0)
            return 0;
        return Math.Clamp(1.0 - ((double)idle / total), 0, 1);
    }

    public static double? TryReadAvailableRamFraction()
    {
        if (!TrySysctlUInt64("hw.memsize", out var totalBytes) || totalBytes == 0)
            return null;
        if (!TrySysctlInt32("hw.pagesize", out var pageSize) || pageSize <= 0)
            pageSize = 4096;

        if (!TryReadVmPageCounts(out var free, out var inactive, out var speculative))
            return null;

        var availBytes = (free + inactive + speculative) * (ulong)pageSize;
        return Math.Clamp((double)availBytes / totalBytes, 0, 1);
    }

    private static bool TryReadCpuTicks(out (ulong Idle, ulong Total) ticks)
    {
        ticks = default;
        var count = HostCpuLoadInfoCount;
        var byteLen = count * sizeof(int);
        var buffer = Marshal.AllocHGlobal(byteLen);
        try
        {
            var result = host_statistics(mach_host_self(), HostCpuLoadInfo, buffer, ref count);
            if (result != 0 || count < 4)
                return false;

            // CPU_STATE_USER, SYSTEM, IDLE, NICE
            var user = (uint)Marshal.ReadInt32(buffer, 0);
            var system = (uint)Marshal.ReadInt32(buffer, 4);
            var idle = (uint)Marshal.ReadInt32(buffer, 8);
            var nice = (uint)Marshal.ReadInt32(buffer, 12);
            var total = (ulong)user + system + idle + nice;
            ticks = (idle, total);
            return total > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryReadVmPageCounts(out ulong free, out ulong inactive, out ulong speculative)
    {
        free = inactive = speculative = 0;
        var count = HostVmInfoCount;
        var byteLen = count * sizeof(int);
        var buffer = Marshal.AllocHGlobal(byteLen);
        try
        {
            var result = host_statistics(mach_host_self(), HostVmInfo, buffer, ref count);
            if (result != 0 || count < 15)
                return false;

            // vm_statistics: free, active, inactive, wire, ... speculative at index 14 on modern macOS
            free = (uint)Marshal.ReadInt32(buffer, 0 * 4);
            inactive = (uint)Marshal.ReadInt32(buffer, 2 * 4);
            speculative = (uint)Marshal.ReadInt32(buffer, 14 * 4);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TrySysctlUInt64(string name, out ulong value)
    {
        value = 0;
        var len = (UIntPtr)sizeof(ulong);
        var buffer = Marshal.AllocHGlobal(sizeof(ulong));
        try
        {
            if (sysctlbyname(name, buffer, ref len, IntPtr.Zero, UIntPtr.Zero) != 0)
                return false;
            value = unchecked((ulong)Marshal.ReadInt64(buffer));
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TrySysctlInt32(string name, out int value)
    {
        value = 0;
        var len = (UIntPtr)sizeof(int);
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            if (sysctlbyname(name, buffer, ref len, IntPtr.Zero, UIntPtr.Zero) != 0)
                return false;
            value = Marshal.ReadInt32(buffer);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const int HostCpuLoadInfo = 3;
    private const int HostCpuLoadInfoCount = 4;
    private const int HostVmInfo = 2;
    private const int HostVmInfoCount = 38;

    [DllImport("libSystem.dylib", EntryPoint = "sysctlbyname", SetLastError = true)]
    private static extern int sysctlbyname(
        string name,
        IntPtr oldp,
        ref UIntPtr oldlenp,
        IntPtr newp,
        UIntPtr newlen);

    [DllImport("libSystem.dylib")]
    private static extern IntPtr mach_host_self();

    [DllImport("libSystem.dylib")]
    private static extern int host_statistics(
        IntPtr host,
        int flavor,
        IntPtr info,
        ref int count);
}
