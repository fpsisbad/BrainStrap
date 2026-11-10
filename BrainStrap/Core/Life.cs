using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Shell;
using BrainStrap.Core.Perf;

namespace BrainStrap.Core;

/// <summary>
/// Game Mode+: while Roblox runs, switch Windows to the High performance power plan (only when plugged in) and give
/// Roblox "above normal" priority. Both are ordinary Windows settings, and both are put back when Roblox closes.
/// </summary>
internal sealed class GameModePlus : IDisposable
{
    const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    string? previousPlan;

    public void Engage(Process roblox)
    {
        try { roblox.PriorityClass = ProcessPriorityClass.AboveNormal; } catch (Exception ex) { Logger.Warn("priority: " + ex.Message); }
        if (BrainAI.Power.Status().onBattery) return;
        previousPlan = ActivePlan();
        if (previousPlan != null && !previousPlan.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase))
        {
            if (Run("powercfg", $"/setactive {HighPerformance}") == 0) Logger.Info("game mode+: high performance plan on");
            else previousPlan = null;
        }
        else previousPlan = null;
    }

    public void Dispose()
    {
        if (previousPlan != null) Run("powercfg", $"/setactive {previousPlan}");
        previousPlan = null;
    }

    public static string? ActivePlan()
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg", "/getactivescheme") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            var m = Regex.Match(o, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            return m.Success ? m.Value : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsPowerSaver() => ActivePlan()?.Equals("a1841308-3541-4fab-bc81-f71556f20b4a", StringComparison.OrdinalIgnoreCase) == true;

    static int Run(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true })!;
            p.WaitForExit(5000);
            return p.ExitCode;
        }
        catch
        {
            return -1;
        }
    }
}

/// <summary>Companion Apps: start programs you choose with Roblox (Discord, OBS, a music app) and close them politely afterwards.</summary>
internal sealed class Companions : IDisposable
{
    readonly List<(Companion c, Process? p)> started = new();

    public void Start(Settings s)
    {
        foreach (var c in s.Life.Companions)
        {
            if (!File.Exists(c.Path)) continue;
            var name = Path.GetFileNameWithoutExtension(c.Path);
            if (Process.GetProcessesByName(name).Length > 0) { started.Add((c, null)); continue; }   // already running: leave it alone
            try { started.Add((c, Process.Start(new ProcessStartInfo(c.Path, c.Args ?? "") { UseShellExecute = true }))); }
            catch (Exception ex) { Logger.Warn("companion " + name + ": " + ex.Message); }
        }
    }

    public void Dispose()
    {
        foreach (var (c, p) in started)
        {
            if (!c.CloseAfter || p == null) continue;
            try { if (!p.HasExited) p.CloseMainWindow(); } catch { }
            p.Dispose();
        }
        started.Clear();
    }
}

/// <summary>Taskbar Jump List: right-click BrainStrap in the taskbar to jump into recent and favourite games.</summary>
internal static class JumpLists
{
    public static void Update(Settings s)
    {
        try
        {
            var app = System.Windows.Application.Current;
            if (app == null) return;
            if (!s.Life.JumpList) { JumpList.SetJumpList(app, new JumpList()); return; }
            var jl = new JumpList { ShowRecentCategory = false, ShowFrequentCategory = false };
            foreach (var f in s.Servers.Favorites.Where(f => Servers.RobloxApi.IsId(f.PlaceId)).Take(5))
                jl.JumpItems.Add(MakeTask(f.Name, f.PlaceId, f.JobId, "Favourites"));
            foreach (var g in History.Playtime(DateTime.Now.AddDays(-30)).Where(g => g.placeId != null).Take(6))
                jl.JumpItems.Add(MakeTask(g.game, g.placeId!, null, "Recent games"));
            jl.JumpItems.Add(new JumpTask { Title = "BrainAI", Arguments = "--page brainai", ApplicationPath = Paths.SelfExe, IconResourcePath = Paths.SelfExe, Description = "Open BrainAI" });
            JumpList.SetJumpList(app, jl);
            jl.Apply();
        }
        catch (Exception ex)
        {
            Logger.Warn("jump list: " + ex.Message);
        }
    }

    static JumpTask MakeTask(string title, string placeId, string? jobId, string category) => new()
    {
        Title = title.Length > 60 ? title[..60] : title,
        Arguments = $"--play {placeId}" + (jobId != null ? " " + jobId : ""),
        ApplicationPath = Paths.SelfExe,
        IconResourcePath = Paths.SelfExe,
        CustomCategory = category,
        Description = "Play with your BrainStrap settings",
    };
}

/// <summary>Screenshot Gallery: Roblox saves screenshots to Pictures\Roblox.</summary>
internal static class Screenshots
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Roblox");

    public static List<FileInfo> List(int max = 120)
    {
        try
        {
            if (!Directory.Exists(Folder)) return new();
            return new DirectoryInfo(Folder).EnumerateFiles("*.*").Where(f => f.Extension is ".png" or ".jpg" or ".jpeg")
                .OrderByDescending(f => f.LastWriteTime).Take(max).ToList();
        }
        catch
        {
            return new();
        }
    }
}

/// <summary>Backup &amp; Restore: the whole setup (settings, profiles, themes) in one file. Secrets are left out.</summary>
internal static class Backup
{
    public static void Export(Settings s, string file)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(SettingsStore.ToJson(s))!.AsObject();
        if (json["Ai"] is System.Text.Json.Nodes.JsonObject ai) ai.Remove("KeyProtected");
        if (json["Servers"]?["Favorites"] is System.Text.Json.Nodes.JsonArray favs)
            foreach (var f in favs) if (f is System.Text.Json.Nodes.JsonObject o) o.Remove("PrivateLinkProtected");
        json.Remove("OriginalHandlerCommand");
        var wrapper = new System.Text.Json.Nodes.JsonObject
        {
            ["brainstrapBackup"] = 1,
            ["created"] = DateTime.Now.ToString("o"),
            ["settings"] = json,
        };
        File.WriteAllText(file, wrapper.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static Settings Import(string file)
    {
        if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("That file is too large to be a BrainStrap backup.");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file));
        if (node?["brainstrapBackup"] == null || node["settings"] == null) throw new InvalidDataException("That isn't a BrainStrap backup.");
        var incoming = SettingsStore.FromJson(node["settings"]!.ToJsonString());
        var current = AppState.S;
        incoming.Ai.KeyProtected = current.Ai.KeyProtected;
        incoming.OriginalHandlerCommand = current.OriginalHandlerCommand;
        // only keep values the catalog allows, and only valid themes
        var cat = FlagCatalog.Load();
        foreach (var p in incoming.Profiles.Values)
        {
            foreach (var k in p.Values.Keys.ToList())
            {
                var def = cat.Flags.FirstOrDefault(f => f.Id == k);
                var clean = def == null ? null : BrainAI.Engine.Clean(def, p.Values[k]);
                if (clean == null) p.Values.Remove(k); else p.Values[k] = clean;
            }
            foreach (var k in p.Custom.Keys.ToList())
                if (FlagValidator.Check(k, p.Custom[k]).Level == "bad") p.Custom.Remove(k);
        }
        incoming.Studio.CustomThemes = incoming.Studio.CustomThemes
            .Where(t => new[] { t.Bg, t.Panel, t.Panel2, t.Ink, t.Muted, t.Dim, t.Pill, t.PillHover, t.Accent }.All(UI.Themes.IsHex)).ToList();
        incoming.Studio.BackgroundPath = File.Exists(incoming.Studio.BackgroundPath) ? incoming.Studio.BackgroundPath : null;
        incoming.Studio.LaunchSound = File.Exists(incoming.Studio.LaunchSound) ? incoming.Studio.LaunchSound : null;
        incoming.Life.Companions = incoming.Life.Companions.Where(c => File.Exists(c.Path)).ToList();
        Timeline.Record(current, "Before restoring a backup");
        return incoming;
    }
}
