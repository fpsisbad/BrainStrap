using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;

namespace BrainStrap.Core.Perf;

internal sealed record FrameStats(
    int Frames, double Seconds, double AvgFps, double Low1, double Low01, double MedianMs, double WorstMs,
    int Stutters, double StuttersPerMin, int Stability)
{
    public string StabilityLabel => Stability >= 85 ? "Rock solid" : Stability >= 70 ? "Smooth" : Stability >= 50 ? "Some stutter" : "Stuttery";

    public static FrameStats? From(IReadOnlyList<float> ms)
    {
        var valid = ms.Where(x => x > 0.2f && x < 2000f).ToArray();
        if (valid.Length < 30) return null;
        var sorted = valid.OrderBy(x => x).ToArray();
        double Pct(double p) => sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
        var total = valid.Sum(x => (double)x);
        var avg = 1000.0 * valid.Length / total;
        var median = Pct(0.5);
        // a stutter: a frame that takes over 2.5x the typical frame and at least 25 ms (a visible hitch)
        var stutters = valid.Count(x => x > Math.Max(25, median * 2.5));
        var minutes = total / 60000.0;
        var perMin = minutes > 0 ? stutters / minutes : 0;
        var low1 = 1000.0 / Pct(0.99);
        var ratio = Math.Clamp(low1 / avg, 0, 1);
        var stability = (int)Math.Clamp(Math.Round(100 * Math.Pow(ratio, 0.7) - Math.Min(35, perMin * 2.5)), 0, 100);
        return new FrameStats(valid.Length, Math.Round(total / 1000.0, 1), Math.Round(avg, 1), Math.Round(low1, 1), Math.Round(1000.0 / Pct(0.999), 1),
            Math.Round(median, 2), Math.Round(sorted[^1], 1), stutters, Math.Round(perMin, 1), stability);
    }

    /// <summary>Shrinks a long frame-time list for a graph while keeping every spike visible (max per bucket).</summary>
    public static float[] Graph(IReadOnlyList<float> ms, int points = 480)
    {
        if (ms.Count <= points) return ms.ToArray();
        var outp = new float[points];
        var per = ms.Count / (double)points;
        for (var i = 0; i < points; i++)
        {
            int a = (int)(i * per), b = Math.Min(ms.Count, (int)((i + 1) * per));
            float mx = 0;
            for (var j = a; j < b; j++) if (ms[j] > mx) mx = ms[j];
            outp[i] = mx;
        }
        return outp;
    }
}

/// <summary>
/// Measures Roblox's real frame times with Intel's PresentMon (MIT licence), which reads the frame "present" events
/// Windows already records (ETW). Like Task Manager, it does not touch the Roblox process.
/// Windows only lets administrators or the "Performance Log Users" group read those events.
/// </summary>
internal sealed class FrameCapture : IDisposable
{
    public const string ReleasesUrl = "https://github.com/GameTechDev/PresentMon/releases";
    const string PerfLogUsersSid = "S-1-5-32-559";

    readonly List<float> frames = new();
    readonly object gate = new();
    Process? pm;
    string? csvPath;
    CancellationTokenSource? cts;
    public string? Error { get; private set; }
    public bool Running => pm != null && !pm.HasExited;

    public static string? FindExe(Settings s)
    {
        var candidates = new List<string?> { s.Perf.PresentMonPath };
        foreach (var dir in new[] { Path.Combine(Paths.Data, "Tools"), AppContext.BaseDirectory })
        {
            try { if (Directory.Exists(dir)) candidates.AddRange(Directory.GetFiles(dir, "PresentMon*.exe")); } catch { }
        }
        return candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
    }

    /// <summary>Copies a PresentMon exe the user downloaded into BrainStrap's Tools folder.</summary>
    public static string Install(string sourceExe, Settings s)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Paths.Data, "Tools")).FullName;
        var dst = Path.Combine(dir, "PresentMon.exe");
        File.Copy(sourceExe, dst, true);
        s.Perf.PresentMonPath = dst;
        return dst;
    }

    public static bool HasPermission()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            var p = new WindowsPrincipal(id);
            return p.IsInRole(WindowsBuiltInRole.Administrator) || p.IsInRole(new SecurityIdentifier(PerfLogUsersSid));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>One Windows admin prompt: adds this Windows user to "Performance Log Users". Takes effect after signing out and in.</summary>
    public static bool RequestPermission()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            var user = id.Name.Replace("'", "''");
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -Command \"Add-LocalGroupMember -SID '{PerfLogUsersSid}' -Member '{user}' -ErrorAction SilentlyContinue\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(30000);
            return p != null && p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.Warn("permission request cancelled or failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>Starts capturing. seconds = null keeps going until Roblox exits or Stop() is called.</summary>
    public bool Start(Settings s, int? seconds = null)
    {
        var exe = FindExe(s);
        if (exe == null) { Error = "PresentMon is not set up yet."; return false; }
        csvPath = Path.Combine(Paths.State, $"frames-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.csv");
        var session = "BrainStrap_" + Environment.ProcessId;
        var args = $"--process_name RobloxPlayerBeta.exe --output_file \"{csvPath}\" --session_name {session} --stop_existing_session --terminate_on_proc_exit";
        if (seconds != null) args += $" --timed {seconds} --terminate_after_timed";
        if (!string.IsNullOrWhiteSpace(s.Perf.PresentMonExtraArgs)) args += " " + s.Perf.PresentMonExtraArgs.Trim();
        try
        {
            pm = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (pm == null) { Error = "PresentMon did not start."; return false; }
            pm.OutputDataReceived += (_, _) => { };
            pm.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Error ??= e.Data.Trim(); };
            pm.BeginOutputReadLine();
            pm.BeginErrorReadLine();
            cts = new CancellationTokenSource();
            _ = TailAsync(cts.Token);
            return true;
        }
        catch (Exception ex)
        {
            Error = "Could not start PresentMon: " + ex.Message;
            return false;
        }
    }

    async Task TailAsync(CancellationToken ct)
    {
        // wait for the CSV to appear
        for (var i = 0; i < 40 && !File.Exists(csvPath) && !ct.IsCancellationRequested; i++) await Task.Delay(250, ct).ContinueWith(_ => { });
        if (!File.Exists(csvPath))
        {
            if (pm != null && pm.HasExited) Error ??= HasPermission() ? "PresentMon stopped early." : "Windows needs permission first (Performance page → Enable FPS measuring).";
            return;
        }
        int col = -1;
        var partial = "";
        using var fs = new FileStream(csvPath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs);
        var buf = new char[16384];
        while (!ct.IsCancellationRequested)
        {
            var n = await reader.ReadAsync(buf, 0, buf.Length);
            if (n == 0)
            {
                if (pm == null || pm.HasExited) { if (reader.EndOfStream) break; }
                await Task.Delay(200, ct).ContinueWith(_ => { });
                continue;
            }
            var chunk = partial + new string(buf, 0, n);
            var lines = chunk.Split('\n');
            partial = lines[^1];
            for (var i = 0; i < lines.Length - 1; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.Length == 0) continue;
                var cells = line.Split(',');
                if (col < 0)
                {
                    // PresentMon 1.x: msBetweenPresents · 2.0-2.2: FrameTime · 2.3+: MsBetweenPresents (case differs between versions)
                    col = Array.FindIndex(cells, c => c.Trim().Equals("msBetweenPresents", StringComparison.OrdinalIgnoreCase));
                    if (col < 0) col = Array.FindIndex(cells, c => c.Trim().Equals("FrameTime", StringComparison.OrdinalIgnoreCase));
                    if (col < 0) { Error = "This PresentMon version writes a format BrainStrap doesn't know."; return; }
                    continue;
                }
                if (col < cells.Length && float.TryParse(cells[col], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) && ms > 0)
                    lock (gate) frames.Add(ms);
            }
        }
    }

    public List<float> Snapshot()
    {
        lock (gate) return new List<float>(frames);
    }

    /// <summary>FPS and 1% low over the last few seconds, for the HUD.</summary>
    public (double fps, double low1) Recent(double seconds = 2)
    {
        lock (gate)
        {
            if (frames.Count == 0) return (0, 0);
            double sum = 0;
            var i = frames.Count - 1;
            var window = new List<float>();
            for (; i >= 0 && sum < seconds * 1000; i--) { sum += frames[i]; window.Add(frames[i]); }
            if (window.Count < 2) return (0, 0);
            window.Sort();
            var p99 = window[Math.Clamp((int)Math.Ceiling(0.99 * window.Count) - 1, 0, window.Count - 1)];
            return (Math.Round(1000.0 * window.Count / sum), Math.Round(1000.0 / p99));
        }
    }

    public async Task WaitAsync(TimeSpan max)
    {
        if (pm == null) return;
        using var t = new CancellationTokenSource(max);
        try { await pm.WaitForExitAsync(t.Token); } catch (OperationCanceledException) { }
        await Task.Delay(400);
    }

    public void Stop()
    {
        try { if (pm != null && !pm.HasExited) pm.Kill(); } catch { }
        try { cts?.Cancel(); } catch { }
    }

    public void Dispose()
    {
        Stop();
        pm?.Dispose();
        try { if (csvPath != null && File.Exists(csvPath)) File.Delete(csvPath); } catch { }
    }
}
