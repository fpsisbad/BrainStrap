using BrainStrap.Core.Perf;
using BrainStrap.Core.Platform;

namespace BrainStrap.Core;

/// <summary>
/// Sample data for the screenshot mode ("--shots"), so the pictures on the website show the app in use rather than
/// empty. It is only ever switched on by that mode, is never saved, and nothing here is presented as a real record.
/// </summary>
internal static class Demo
{
    public static bool On;

    static readonly (string id, string name)[] Games = { ("9000000001", "Tower Tycoon"), ("9000000002", "Obby Rush"), ("9000000003", "Bedwars") };

    public static InstalledVersion Roblox => new(Path.Combine(RobloxDeploy.Root, "version-0000000000000000"), "version-4ea8e85481e34f46", DateTime.UtcNow.AddDays(-2));

    /// <summary>Changes the in-memory settings to look like a PC that has been set up for a while.</summary>
    public static void Dress(Settings s)
    {
        On = true;
        var cat = FlagCatalog.Load();
        s.Profiles["Balanced"] = new FlagProfile { Values = new(cat.Presets.FirstOrDefault(p => p.Id == "balanced")?.Values ?? new()) };
        s.Profiles["Competitive"] = new FlagProfile { Values = new(cat.Presets.FirstOrDefault(p => p.Id == "performance")?.Values ?? new()), ModPack = "pack.competitive-focus" };
        s.ActiveProfile = "Balanced";
        s.GameProfiles[Games[2].id] = "Competitive";
        s.Hud.Enabled = true;
        if (!s.Studio.HiddenWidgets.Contains("start")) s.Studio.HiddenWidgets.Add("start");
        s.Platform.Flows.Clear();
        s.Platform.Flows.Add(new FlowRule { Name = "Launch setup", Trigger = "launch", Actions = { new FlowAction { Type = "profile.switch", Value = "Balanced" }, new FlowAction { Type = "tracking.start" } } });
        s.Platform.Flows.Add(new FlowRule { Name = "Bedwars night", Trigger = "join", TriggerValue = "bedwars", Actions = { new FlowAction { Type = "profile.switch", Value = "Competitive" }, new FlowAction { Type = "hud.on" }, new FlowAction { Type = "app.start", Value = "discord" } } });
        s.Platform.Flows.Add(new FlowRule { Name = "Stream mode", Trigger = "manual", Actions = { new FlowAction { Type = "hud.on" }, new FlowAction { Type = "app.start", Value = "obs" } } });
        s.Platform.Notes["profile:Balanced"] = "Daily driver. Smooth on battery too.";
    }

    static float[] Graph(int seed, double fps)
    {
        var r = new Random(seed);
        var g = new float[120];
        for (var i = 0; i < g.Length; i++)
        {
            var ms = 1000.0 / fps * (0.92 + r.NextDouble() * 0.16);
            if (i % 37 == 21) ms *= 2.6;
            if (i % 53 == 44) ms *= 1.8;
            g[i] = (float)ms;
        }
        return g;
    }

    public static List<SessionRecord> Sessions()
    {
        var list = new List<SessionRecord>();
        var plan = new[] { (0, 0.9, 42.0, 118.0, 96.0, 91), (1, 1.8, 18.0, 74.0, 41.0, 62), (2, 2.1, 56.0, 142.0, 121.0, 94), (0, 3.2, 35.0, 121.0, 99.0, 90), (2, 4.0, 64.0, 139.0, 118.0, 93), (1, 5.3, 22.0, 88.0, 63.0, 78), (0, 6.1, 47.0, 116.0, 94.0, 89) };
        var n = 0;
        foreach (var (g, daysAgo, minutes, fps, low, stability) in plan)
        {
            n++;
            list.Add(new SessionRecord
            {
                Start = DateTime.Now.AddDays(-daysAgo), Minutes = minutes, PlaceId = Games[g].id, GameName = Games[g].name, Profile = g == 2 ? "Competitive" : "Balanced",
                Fps = new FrameStats((int)(fps * minutes * 60), minutes * 60, fps, low, low * 0.8, 1000 / fps, 1000 / fps * 3.1, 9 - stability / 12, 0.4, stability),
                Graph = Graph(n, fps), Ping = new PingStats(600, 30 + n * 3, 3 + n % 3, 0, 26, 58 + n * 4), ExitCode = 0,
                Events = new()
                {
                    new ReplayEvent { T = 7, Kind = "join", Detail = "Joined server" },
                    new ReplayEvent { T = 761, Kind = "stutter", Detail = "212 ms frame" },
                    new ReplayEvent { T = 1865, Kind = "ping", Detail = "Ping spiked to 240 ms" },
                },
            });
        }
        return list;
    }
}
