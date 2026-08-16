using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.HostPowers;

public sealed class OsHostLoadInfoProvider : IHostLoadInfoProvider
{
    public async Task<HostLoadInfo?> TryGetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            double? cpu;
            double? ramFrac;

            if (OperatingSystem.IsWindows())
            {
                cpu = await TryReadWindowsCpuBusyAsync(cancellationToken).ConfigureAwait(false);
                ramFrac = TryReadWindowsAvailableRamFraction();
            }
            else if (OperatingSystem.IsLinux())
            {
                cpu = await TryReadLinuxCpuBusyAsync(cancellationToken).ConfigureAwait(false);
                ramFrac = TryReadLinuxAvailableRamFraction();
            }
            else if (OperatingSystem.IsMacOS())
            {
                cpu = await MacOsHostMetrics.TryReadCpuBusyAsync(cancellationToken).ConfigureAwait(false);
                ramFrac = MacOsHostMetrics.TryReadAvailableRamFraction();
            }
            else
            {
                return null;
            }

            if (cpu is null || ramFrac is null)
                return null;

            return new HostLoadInfo(
                Math.Clamp(cpu.Value, 0, 1),
                Math.Clamp(ramFrac.Value, 0, 1));
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<double?> TryReadWindowsCpuBusyAsync(CancellationToken cancellationToken)
    {
        if (!GetSystemTimes(out var idle1, out var kernel1, out var user1))
            return null;

        await Task.Delay(300, cancellationToken).ConfigureAwait(false);

        if (!GetSystemTimes(out var idle2, out var kernel2, out var user2))
            return null;

        var idle = SubFileTime(idle2, idle1);
        var kernel = SubFileTime(kernel2, kernel1);
        var user = SubFileTime(user2, user1);
        var total = kernel + user;
        if (total <= 0)
            return 0;
        // kernel includes idle on Windows
        var busy = total - idle;
        if (busy < 0)
            busy = 0;
        return (double)busy / total;
    }

    [SupportedOSPlatform("windows")]
    private static double? TryReadWindowsAvailableRamFraction()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
            return null;
        return (double)status.AvailPhys / status.TotalPhys;
    }

    private static async Task<double?> TryReadLinuxCpuBusyAsync(CancellationToken cancellationToken)
    {
        var a = TryReadLinuxCpuTimes();
        if (a is null)
            return null;
        await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        var b = TryReadLinuxCpuTimes();
        if (b is null)
            return null;

        var idleDelta = b.Value.Idle - a.Value.Idle;
        var totalDelta = b.Value.Total - a.Value.Total;
        if (totalDelta <= 0)
            return 0;
        var busy = 1.0 - ((double)idleDelta / totalDelta);
        return Math.Clamp(busy, 0, 1);
    }

    private static (ulong Idle, ulong Total)? TryReadLinuxCpuTimes()
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/stat"))
            {
                if (!line.StartsWith("cpu ", StringComparison.Ordinal))
                    continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // cpu user nice system idle iowait irq softirq steal guest guest_nice
                if (parts.Length < 5)
                    return null;
                ulong sum = 0;
                for (var i = 1; i < parts.Length; i++)
                {
                    if (!ulong.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                        return null;
                    sum += v;
                }

                if (!ulong.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idle))
                    return null;
                ulong idleAll = idle;
                if (parts.Length > 5 &&
                    ulong.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var iowait))
                    idleAll += iowait;

                return (idleAll, sum);
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static double? TryReadLinuxAvailableRamFraction()
    {
        try
        {
            double? total = null;
            double? available = null;
            double? free = null;
            double? buffers = null;
            double? cached = null;

            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                    total = ParseKb(line);
                else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                    available = ParseKb(line);
                else if (line.StartsWith("MemFree:", StringComparison.Ordinal))
                    free = ParseKb(line);
                else if (line.StartsWith("Buffers:", StringComparison.Ordinal))
                    buffers = ParseKb(line);
                else if (line.StartsWith("Cached:", StringComparison.Ordinal))
                    cached = ParseKb(line);
            }

            if (total is null or <= 0)
                return null;

            var avail = available ?? ((free ?? 0) + (buffers ?? 0) + (cached ?? 0));
            return avail / total.Value;
        }
        catch
        {
            return null;
        }
    }

    private static double? ParseKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var kb))
            return kb;
        return null;
    }

    private static long SubFileTime(FileTime a, FileTime b)
    {
        var av = ((long)a.DwHighDateTime << 32) | (uint)a.DwLowDateTime;
        var bv = ((long)b.DwHighDateTime << 32) | (uint)b.DwLowDateTime;
        return av - bv;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint DwLowDateTime;
        public uint DwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}
