using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BrainStrap.Core.Perf;

internal sealed record LiveSample(
    double CpuPct, double GpuPct, long RamBytes, long GpuMemBytes,
    double? CpuTemp, double? GpuTemp, long SysAvailBytes, double SysCpuPct);

/// <summary>
/// Reads the same numbers Task Manager shows (Windows performance counters), for the Roblox process and the system.
/// Nothing is injected into Roblox; GPU temperature comes from the NVIDIA driver's NVML library when it exists.
/// </summary>
internal sealed class RobloxMonitor : IDisposable
{
    readonly Process proc;
    TimeSpan lastCpu;
    DateTime lastAt;
    readonly Dictionary<string, PerformanceCounter> gpuCounters = new();
    PerformanceCounter? sysCpu, availMem;
    DateTime nextInstanceScan = DateTime.MinValue;

    public RobloxMonitor(Process p)
    {
        proc = p;
        try { lastCpu = p.TotalProcessorTime; } catch { }
        lastAt = DateTime.UtcNow;
        try { sysCpu = new PerformanceCounter("Processor", "% Processor Time", "_Total"); sysCpu.NextValue(); } catch { sysCpu = null; }
        try { availMem = new PerformanceCounter("Memory", "Available Bytes"); } catch { availMem = null; }
    }

    public LiveSample Sample()
    {
        double cpu = 0;
        long ram = 0;
        try
        {
            proc.Refresh();
            var now = DateTime.UtcNow;
            var t = proc.TotalProcessorTime;
            var dt = (now - lastAt).TotalMilliseconds;
            if (dt > 0) cpu = (t - lastCpu).TotalMilliseconds / dt / Environment.ProcessorCount * 100;
            lastCpu = t;
            lastAt = now;
            ram = proc.PrivateMemorySize64;
        }
        catch
        {
        }

        double gpu = 0;
        long gpuMem = 0;
        try
        {
            var tag = $"pid_{proc.Id}_";
            if (DateTime.UtcNow >= nextInstanceScan)
            {
                nextInstanceScan = DateTime.UtcNow.AddSeconds(10);
                foreach (var inst in new PerformanceCounterCategory("GPU Engine").GetInstanceNames())
                    if (inst.Contains(tag) && inst.EndsWith("engtype_3D") && !gpuCounters.ContainsKey(inst))
                        gpuCounters[inst] = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst);
            }
            foreach (var kv in gpuCounters.ToList())
            {
                try { gpu += kv.Value.NextValue(); }
                catch { kv.Value.Dispose(); gpuCounters.Remove(kv.Key); }
            }
            foreach (var inst in new PerformanceCounterCategory("GPU Process Memory").GetInstanceNames().Where(i => i.Contains(tag)))
                using (var c = new PerformanceCounter("GPU Process Memory", "Dedicated Usage", inst)) gpuMem += (long)c.NextValue();
        }
        catch
        {
            // GPU counters exist on Windows 10 1709 and later with a WDDM 2 driver
        }

        double sys = 0;
        long avail = 0;
        try { sys = sysCpu?.NextValue() ?? 0; } catch { }
        try { avail = (long)(availMem?.NextValue() ?? 0); } catch { }
        return new LiveSample(Math.Round(cpu, 1), Math.Round(Math.Min(100, gpu), 1), ram, gpuMem, Thermals.CpuC(), Thermals.GpuC(), avail, Math.Round(sys, 1));
    }

    public void Dispose()
    {
        foreach (var c in gpuCounters.Values) c.Dispose();
        sysCpu?.Dispose();
        availMem?.Dispose();
    }
}

/// <summary>Temperatures where Windows or the GPU driver exposes them. Returns null when not supported.</summary>
internal static class Thermals
{
    public static double? CpuC()
    {
        try
        {
            var cat = new PerformanceCounterCategory("Thermal Zone Information");
            double best = double.NaN;
            foreach (var inst in cat.GetInstanceNames())
            {
                using var c = new PerformanceCounter("Thermal Zone Information", "High Precision Temperature", inst);
                var k = c.NextValue() / 10.0;          // tenths of a kelvin
                var cel = k - 273.15;
                if (cel > 5 && cel < 120 && (double.IsNaN(best) || cel > best)) best = cel;
            }
            return double.IsNaN(best) ? null : Math.Round(best);
        }
        catch
        {
            return null;
        }
    }

    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")] static extern int NvmlInit();
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] static extern int NvmlHandle(uint index, out IntPtr device);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")] static extern int NvmlTemp(IntPtr device, int sensor, out uint temp);

    static bool? nvmlOk;
    static IntPtr nvmlDevice;

    public static double? GpuC()
    {
        try
        {
            if (nvmlOk == null)
            {
                nvmlOk = NvmlInit() == 0 && NvmlHandle(0, out nvmlDevice) == 0;
            }
            if (nvmlOk != true) return null;
            return NvmlTemp(nvmlDevice, 0, out var t) == 0 ? t : null;
        }
        catch
        {
            nvmlOk = false;    // no NVIDIA driver: GPU temperature is not available
            return null;
        }
    }
}

internal sealed record HeavyApp(string Name, int Pid, double CpuPct, long RamBytes, string Hint);

/// <summary>Background App Advisor: finds programs that compete with Roblox. It only advises; closing is always your click.</summary>
internal static class BackgroundAdvisor
{
    static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "explorer",
        "fontdrvhost", "Memory Compression", "RobloxPlayerBeta", "BrainStrap", "audiodg", "ctfmon", "sihost", "taskhostw",
        "RuntimeBroker", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "TextInputHost", "SecurityHealthService",
        "MsMpEng", "NisSrv", "spoolsv", "conhost", "dllhost", "WmiPrvSE", "LsaIso", "SgrmBroker", "smartscreen",
    };

    static readonly (string match, string hint)[] Known =
    {
        ("chrome", "Browser tabs use RAM and GPU time, especially video."),
        ("msedge", "Browser tabs use RAM and GPU time, especially video."),
        ("firefox", "Browser tabs use RAM and GPU time, especially video."),
        ("opera", "Browser tabs use RAM and GPU time."),
        ("brave", "Browser tabs use RAM and GPU time."),
        ("obs64", "Recording or streaming uses the GPU encoder and CPU."),
        ("discord", "Turn off Discord's hardware acceleration and overlay if FPS dips."),
        ("spotify", "Small, but its hardware acceleration can use the GPU."),
        ("steamwebhelper", "Steam's browser windows use RAM."),
        ("epicgameslauncher", "The Epic launcher keeps running in the background."),
        ("onedrive", "Syncing files uses disk and CPU."),
        ("teams", "Teams is heavy on RAM and CPU."),
        ("zoom", "Video calls use CPU and GPU."),
        ("wallpaper", "Animated wallpapers render all the time."),
        ("icue", "RGB software polls hardware constantly."),
        ("razer", "RGB software polls hardware constantly."),
        ("armourycrate", "Armoury Crate runs several background services."),
        ("msiafterburner", "Overlays can add stutter."),
        ("adobe", "Adobe background services use CPU."),
    };

    public static async Task<List<HeavyApp>> ScanAsync(int sampleMs = 1200)
    {
        var first = new Dictionary<int, (string name, TimeSpan cpu)>();
        foreach (var p in Process.GetProcesses())
        {
            try { if (!Skip.Contains(p.ProcessName) && p.SessionId != 0) first[p.Id] = (p.ProcessName, p.TotalProcessorTime); }
            catch { }
            finally { p.Dispose(); }
        }
        await Task.Delay(sampleMs);
        var list = new List<HeavyApp>();
        foreach (var (pid, (name, cpu0)) in first)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                var cpu = (p.TotalProcessorTime - cpu0).TotalMilliseconds / sampleMs / Environment.ProcessorCount * 100;
                var ram = p.WorkingSet64;
                var hint = Known.FirstOrDefault(k => name.Contains(k.match, StringComparison.OrdinalIgnoreCase)).hint ?? "";
                if (cpu >= 3 || ram >= 400L * 1024 * 1024 || hint.Length > 0)
                    list.Add(new HeavyApp(name, pid, Math.Round(cpu, 1), ram, hint));
            }
            catch
            {
            }
        }
        // group helper processes (a browser has many) by name
        return list.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new HeavyApp(g.Key, g.First().Pid, Math.Round(g.Sum(a => a.CpuPct), 1), g.Sum(a => a.RamBytes), g.First().Hint))
            .Where(a => a.CpuPct >= 3 || a.RamBytes >= 500L * 1024 * 1024)
            .OrderByDescending(a => a.CpuPct * 40 + a.RamBytes / 1048576.0)
            .Take(12)
            .ToList();
    }

    /// <summary>Asks a program to close normally (like clicking its X). Never force-kills.</summary>
    public static int CloseGracefully(string name)
    {
        var n = 0;
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow()) n++; }
            catch { }
            finally { p.Dispose(); }
        }
        return n;
    }
}
