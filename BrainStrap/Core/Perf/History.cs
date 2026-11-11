using System.Net.NetworkInformation;
using System.Text.Json;

namespace BrainStrap.Core.Perf;

internal sealed record PingStats(int Samples, double Avg, double Jitter, double LossPct, double Best, double Worst)
{
    /// <summary>Connection Quality Score: 100 is a perfect, stable, low-ping connection.</summary>
    public int Quality => Samples == 0 ? 0 : (int)Math.Clamp(Math.Round(100 - Math.Max(0, Avg - 30) * 0.35 - Jitter * 1.2 - LossPct * 6), 0, 100);
    public string QualityLabel => Samples == 0 ? "No data" : Quality >= 85 ? "Excellent" : Quality >= 70 ? "Good" : Quality >= 50 ? "Fair" : "Poor";

    public static PingStats From(IReadOnlyList<double?> rtts)
    {
        if (rtts.Count == 0) return new PingStats(0, 0, 0, 0, 0, 0);
        var ok = rtts.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        var loss = 100.0 * (rtts.Count - ok.Count) / rtts.Count;
        if (ok.Count == 0) return new PingStats(rtts.Count, 0, 0, Math.Round(loss, 1), 0, 0);
        double jitter = 0;
        for (var i = 1; i < ok.Count; i++) jitter += Math.Abs(ok[i] - ok[i - 1]);
        jitter = ok.Count > 1 ? jitter / (ok.Count - 1) : 0;
        return new PingStats(rtts.Count, Math.Round(ok.Average(), 1), Math.Round(jitter, 1), Math.Round(loss, 1), ok.Min(), ok.Max());
    }
}

/// <summary>Pings one address once a second and keeps the recent results. Some servers ignore ping; then the samples show loss.</summary>
internal sealed class Pinger : IDisposable
{
    readonly string host;
    readonly List<double?> samples = new();
    readonly object gate = new();
    readonly CancellationTokenSource cts = new();
    public string Host => host;

    public Pinger(string host, int intervalMs = 1000)
    {
        this.host = host;
        _ = Task.Run(async () =>
        {
            using var ping = new Ping();
            while (!cts.IsCancellationRequested)
            {
                double? rtt = null;
                try
                {
                    var r = await ping.SendPingAsync(host, 1000);
                    if (r.Status == IPStatus.Success) rtt = r.RoundtripTime;
                }
                catch
                {
                }
                lock (gate)
                {
                    samples.Add(rtt);
                    if (samples.Count > 7200) samples.RemoveRange(0, 3600);
                }
                try { await Task.Delay(intervalMs, cts.Token); } catch { }
            }
        });
    }

    public PingStats Stats(int lastN = 0)
    {
        lock (gate)
        {
            var src = lastN > 0 && samples.Count > lastN ? samples.Skip(samples.Count - lastN).ToList() : new List<double?>(samples);
            return PingStats.From(src);
        }
    }

    public List<double?> Recent(int n)
    {
        lock (gate) return samples.Skip(Math.Max(0, samples.Count - n)).ToList();
    }

    public void Dispose() => cts.Cancel();

    public static async Task<PingStats> BurstAsync(string host, int count = 10, int gapMs = 200)
    {
        var list = new List<double?>();
        using var ping = new Ping();
        for (var i = 0; i < count; i++)
        {
            try
            {
                var r = await ping.SendPingAsync(host, 1000);
                list.Add(r.Status == IPStatus.Success ? r.RoundtripTime : null);
            }
            catch
            {
                list.Add(null);
            }
            await Task.Delay(gapMs);
        }
        return PingStats.From(list);
    }
}

internal sealed class SessionRecord
{
    public DateTime Start { get; set; }
    public double Minutes { get; set; }
    public string? PlaceId { get; set; }
    public string? GameName { get; set; }
    public string? JobId { get; set; }
    public string Profile { get; set; } = "Default";
    public FrameStats? Fps { get; set; }
    public float[]? Graph { get; set; }
    public PingStats? Ping { get; set; }
    public string? ServerIp { get; set; }
    public string? Region { get; set; }
    public int? ExitCode { get; set; }
    public string? Disconnect { get; set; }
    public string? Problem { get; set; }
    public double? PeakRamMb { get; set; }
    public double? PeakCpuTemp { get; set; }
    public double? PeakGpuTemp { get; set; }
    public List<ReplayEvent> Events { get; set; } = new();
}

/// <summary>BrainReplay: one moment in a session (seconds from the start).</summary>
internal sealed class ReplayEvent
{
    public double T { get; set; }
    public string Kind { get; set; } = "";       // stutter | ping | disconnect | join | crash
    public string Detail { get; set; } = "";
}

internal sealed class BenchRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime When { get; set; } = DateTime.Now;
    public string Label { get; set; } = "";
    public string Profile { get; set; } = "";
    public Dictionary<string, string> Values { get; set; } = new();
    public string? PlaceId { get; set; }
    public string? GameName { get; set; }
    public FrameStats Stats { get; set; } = null!;
    public float[] Graph { get; set; } = Array.Empty<float>();
}

/// <summary>Live numbers the running session shares with the BrainStrap window (a small file, refreshed every 2 s).</summary>
internal sealed class LiveState
{
    public DateTime Updated { get; set; }
    public DateTime Started { get; set; }
    public string? PlaceId { get; set; }
    public string? GameName { get; set; }
    public string? JobId { get; set; }
    public string? ServerIp { get; set; }
    public string? Region { get; set; }
    public string Profile { get; set; } = "";
    public double Fps { get; set; }
    public double Low1 { get; set; }
    public PingStats? Ping { get; set; }
    public LiveSample? Hw { get; set; }
    public bool Measuring { get; set; }
}

internal static class History
{
    static readonly JsonSerializerOptions Opt = new() { PropertyNameCaseInsensitive = true };
    static string SessionsFile => Path.Combine(Paths.State, "sessions.jsonl");
    static string BenchFile => Path.Combine(Paths.State, "benchmarks.json");
    static string LiveFile => Path.Combine(Paths.State, "live.json");
    static readonly object Gate = new();

    public static void AddSession(SessionRecord r)
    {
        try
        {
            lock (Gate) File.AppendAllText(SessionsFile, JsonSerializer.Serialize(r) + Environment.NewLine);
            // keep the file small: the newest 1000 sessions
            var lines = File.ReadAllLines(SessionsFile);
            if (lines.Length > 1200) lock (Gate) File.WriteAllLines(SessionsFile, lines.Skip(lines.Length - 1000));
        }
        catch (Exception ex)
        {
            Logger.Warn("could not save session: " + ex.Message);
        }
    }

    public static List<SessionRecord> Sessions()
    {
        if (Demo.On) return Demo.Sessions();
        var list = new List<SessionRecord>();
        try
        {
            if (!File.Exists(SessionsFile)) return list;
            foreach (var line in File.ReadLines(SessionsFile))
            {
                try { if (JsonSerializer.Deserialize<SessionRecord>(line, Opt) is { } r) list.Add(r); } catch { }
            }
        }
        catch
        {
        }
        return list.OrderByDescending(r => r.Start).ToList();
    }

    public static List<BenchRecord> Benchmarks()
    {
        try
        {
            return File.Exists(BenchFile)
                ? (JsonSerializer.Deserialize<List<BenchRecord>>(File.ReadAllText(BenchFile), Opt) ?? new()).OrderByDescending(b => b.When).ToList()
                : new List<BenchRecord>();
        }
        catch
        {
            return new List<BenchRecord>();
        }
    }

    public static void AddBenchmark(BenchRecord b)
    {
        var all = Benchmarks();
        all.Insert(0, b);
        try { File.WriteAllText(BenchFile, JsonSerializer.Serialize(all.Take(60).ToList())); } catch { }
    }

    public static void DeleteBenchmark(string id)
    {
        var all = Benchmarks().Where(b => b.Id != id).ToList();
        try { File.WriteAllText(BenchFile, JsonSerializer.Serialize(all)); } catch { }
    }

    public static void WriteLive(LiveState s)
    {
        try
        {
            var tmp = LiveFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s));
            File.Move(tmp, LiveFile, true);
        }
        catch
        {
        }
    }

    /// <summary>The running session's numbers, or null when no session is active.</summary>
    public static LiveState? ReadLive()
    {
        if (Demo.On) return new LiveState { Started = DateTime.UtcNow.AddMinutes(-37), Updated = DateTime.UtcNow, GameName = "Tower Tycoon", Fps = 141, Measuring = true, Ping = new PingStats(600, 31, 3, 0, 26, 58), Profile = "Competitive" };
        try
        {
            if (!File.Exists(LiveFile)) return null;
            var s = JsonSerializer.Deserialize<LiveState>(File.ReadAllText(LiveFile), Opt);
            return s != null && DateTime.UtcNow - s.Updated < TimeSpan.FromSeconds(10) ? s : null;
        }
        catch
        {
            return null;
        }
    }

    public static void ClearLive()
    {
        try { if (File.Exists(LiveFile)) File.Delete(LiveFile); } catch { }
    }

    /// <summary>Playtime Journal: minutes per game.</summary>
    public static List<(string game, string? placeId, double minutes, int sessions, DateTime last)> Playtime(DateTime? since = null) =>
        Sessions().Where(s => since == null || s.Start >= since)
            .GroupBy(s => s.PlaceId ?? "?")
            .Select(g => (g.First().GameName ?? (g.Key == "?" ? "Unknown game" : "Place " + g.Key), g.Key == "?" ? null : g.Key,
                          Math.Round(g.Sum(x => x.Minutes)), g.Count(), g.Max(x => x.Start)))
            .OrderByDescending(x => x.Item3)
            .ToList();
}
