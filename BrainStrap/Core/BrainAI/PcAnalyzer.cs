using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BrainStrap.Core.BrainAI;

internal sealed record PcInfo(
    string Cpu, int Cores, int Threads, int CpuMhz,
    string Gpu, long VramBytes, string GpuDriver,
    long RamBytes, bool Laptop, bool OnBattery, int BatteryPercent, int Refresh,
    int Tier, int Score, List<string> Notes)
{
    public string TierName => Tier switch { 0 => "Entry", 1 => "Everyday", 2 => "Gaming", _ => "High-end" };
    public string Summary =>
        $"{Cpu} ({Cores} cores) · {Gpu}{(VramBytes > 0 ? $" {VramBytes / 1073741824.0:0.#} GB" : "")} · {RamBytes / 1073741824.0:0} GB RAM · {(Laptop ? "laptop" : "desktop")} · {Refresh} Hz";
}

internal static class Power
{
    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

    /// <summary>(hasBattery, onBattery, percent)</summary>
    public static (bool hasBattery, bool onBattery, int percent) Status()
    {
        try
        {
            if (!GetSystemPowerStatus(out var s)) return (false, false, -1);
            var has = s.BatteryFlag != 128 && s.BatteryFlag != 255;
            return (has, has && s.ACLineStatus == 0, s.BatteryLifePercent == 255 ? -1 : s.BatteryLifePercent);
        }
        catch
        {
            return (false, false, -1);
        }
    }
}

/// <summary>AI PC Analyzer: reads hardware through Windows (WMI and the registry). Nothing is sent anywhere.</summary>
internal static class PcAnalyzer
{
    static PcInfo? cached;
    static DateTime cachedAt;

    public static PcInfo Detect(bool fresh = false)
    {
        if (!fresh && cached != null && DateTime.UtcNow - cachedAt < TimeSpan.FromMinutes(5)) return cached;
        var notes = new List<string>();

        string cpu = "Unknown CPU";
        int cores = 0, threads = Environment.ProcessorCount, mhz = 0;
        Wmi("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor", o =>
        {
            cpu = Clean(o["Name"]?.ToString() ?? cpu);
            cores += Convert.ToInt32(o["NumberOfCores"] ?? 0);
            threads = Math.Max(threads, Convert.ToInt32(o["NumberOfLogicalProcessors"] ?? 0));
            mhz = Math.Max(mhz, Convert.ToInt32(o["MaxClockSpeed"] ?? 0));
        });
        if (cores == 0) cores = Math.Max(1, threads / 2);

        var gpus = new List<(string name, string driver, int refresh)>();
        Wmi("SELECT Name, DriverVersion, CurrentRefreshRate FROM Win32_VideoController", o =>
            gpus.Add((Clean(o["Name"]?.ToString() ?? ""), o["DriverVersion"]?.ToString() ?? "", Convert.ToInt32(o["CurrentRefreshRate"] ?? 0))));
        // prefer a dedicated GPU over the integrated one
        var best = gpus.OrderByDescending(g => GpuTier(g.name)).ThenByDescending(g => g.refresh).FirstOrDefault();
        var gpu = string.IsNullOrWhiteSpace(best.name) ? "Unknown GPU" : best.name;
        var refresh = gpus.Select(g => g.refresh).Where(r => r > 1).DefaultIfEmpty(60).Max();
        var vram = VramFor(gpu);

        long ram = 0;
        Wmi("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem", o => ram = Convert.ToInt64(o["TotalPhysicalMemory"] ?? 0L));

        var (hasBattery, onBattery, pct) = Power.Status();
        var laptop = hasBattery;
        Wmi("SELECT ChassisTypes FROM Win32_SystemEnclosure", o =>
        {
            if (o["ChassisTypes"] is ushort[] types && types.Any(t => t is 8 or 9 or 10 or 11 or 12 or 14 or 18 or 21 or 30 or 31 or 32))
                laptop = true;
        });

        var tier = GpuTier(gpu);
        if (laptop && Regex.IsMatch(gpu, "laptop|max-q|mobile", RegexOptions.IgnoreCase) && !Regex.IsMatch(gpu, "4080|4090|5080|5090"))
            tier = Math.Max(1, tier - 1);
        var ramGb = ram / 1073741824.0;
        if (ramGb > 0 && ramGb < 6) { tier = 0; notes.Add("Less than 6 GB of RAM: memory is the main limit."); }
        else if (ramGb > 0 && ramGb < 7.5) { tier = Math.Min(tier, 1); notes.Add("8 GB of RAM or less: busy servers and browser tabs compete for memory."); }
        if (threads < 4) { tier = Math.Min(tier, 1); notes.Add("Fewer than 4 CPU threads: big servers will be CPU-limited."); }
        if (onBattery) notes.Add("Running on battery: Windows lowers CPU and GPU speed to save power.");
        if (gpus.Count > 1 && laptop) notes.Add("Two graphics chips: make sure Windows runs Roblox on the dedicated one (Settings → Display → Graphics).");

        var score = tier switch { 0 => 15, 1 => 40, 2 => 66, _ => 88 };
        if (ramGb >= 16) score += 4; else if (ramGb > 0 && ramGb < 8) score -= 10;
        if (threads >= 12) score += 4; else if (threads < 4) score -= 10;
        if (mhz >= 3500) score += 3;
        score = Math.Clamp(score, 5, 99);

        cached = new PcInfo(cpu, cores, threads, mhz, gpu, vram, best.driver ?? "", ram, laptop, onBattery, pct, refresh, tier, score, notes);
        cachedAt = DateTime.UtcNow;
        Logger.Info($"pc: tier {tier}, score {score}, {(laptop ? "laptop" : "desktop")}, {refresh} Hz");
        return cached;
    }

    /// <summary>0 entry · 1 everyday · 2 gaming · 3 high-end, from the graphics chip's name.</summary>
    public static int GpuTier(string name)
    {
        var n = (name ?? "").ToLowerInvariant();
        if (Regex.IsMatch(n, @"rtx\s*(2080|3070|3080|3090|4070|4080|4090|5070|5080|5090)|rx\s*(6800|6900|6950|7800|7900|9070)")) return 3;
        if (Regex.IsMatch(n, @"rtx\s*(2060|2070|3050|3060|4050|4060|5050|5060)|gtx\s*(1070|1080|1660|1650 super|titan)|rx\s*(580|590|5500|5600|5700|6600|6650|6700|6750|7600|7700|9060)|arc\(tm\)\s*[ab]\d{3}|arc\s*[ab]\d{3}|vega 56|vega 64|radeon vii"))
            return 2;
        if (Regex.IsMatch(n, @"gtx\s*(9[5-8]0|1050|1060|1630|1650)|\bmx\s*\d{3}|rx\s*(460|470|480|550|560|570|6400|6500)|radeon\s*(\(tm\)\s*)?(6[0-9]0m|7[0-9]0m|8[0-9]0m|8[0-9]0s)|radeon\s*vega|iris\s*xe|arc\(tm\)\s*graphics|arc graphics|quadro|rtx a\d{3,4}"))
            return 1;
        return 0;
    }

    static string Clean(string s) => Regex.Replace(s.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", ""), @"\s+", " ").Trim();

    static void Wmi(string query, Action<ManagementBaseObject> each)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();
            foreach (var o in results)
                using (o) each(o);
        }
        catch (Exception ex)
        {
            Logger.Warn("wmi query failed: " + ex.GetType().Name);
        }
    }

    /// <summary>Win32_VideoController.AdapterRAM stops at 4 GB, so read the driver's 64-bit memory size instead.</summary>
    static long VramFor(string gpuName)
    {
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls == null) return 0;
            foreach (var sub in cls.GetSubKeyNames().Where(k => k.Length == 4 && k.All(char.IsAsciiDigit)))
            {
                try
                {
                    using var k = cls.OpenSubKey(sub);
                    var desc = Clean(k?.GetValue("DriverDesc") as string ?? "");
                    if (!desc.Equals(gpuName, StringComparison.OrdinalIgnoreCase)) continue;
                    var v = k!.GetValue("HardwareInformation.qwMemorySize");
                    if (v is long l) return l;
                    if (v is byte[] b && b.Length >= 8) return BitConverter.ToInt64(b, 0);
                    if (k.GetValue("HardwareInformation.MemorySize") is int i) return (uint)i;
                }
                catch (System.Security.SecurityException)
                {
                    // some adapter keys are admin-only; skip them
                }
            }
        }
        catch
        {
        }
        return 0;
    }
}
