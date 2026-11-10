using System.Windows;
using System.Windows.Controls;
using BrainStrap.Core;
using BrainStrap.Core.Perf;
using BrainStrap.Core.Servers;

namespace BrainStrap.UI.Pages;

/// <summary>Home: the widgets you picked in Studio → Layout Builder, in your order.</summary>
internal static class HomePage
{
    static readonly string[] Tips =
    {
        "Press Ctrl+K to jump anywhere in BrainStrap.",
        "Run a benchmark before and after changing settings: Performance → Benchmark Comparison shows the real difference.",
        "Laptop? Turn on Battery-Aware Profiles in BrainLife so Roblox runs cooler unplugged.",
        "Lag? The Lag Detective in BrainAI tells you whether it's your FPS, your Wi-Fi or the server.",
        "Every change is on the Timeline in Tools. Roll back anything with one click.",
        "Give each profile its own theme in Studio, so you always know which one is active.",
        "Share your theme: Studio → Copy share code. Friends paste it to get the exact look.",
        "Safe mode on the Launch page starts Roblox with no flags or mods, to rule them out.",
    };

    public static FrameworkElement Build(MainWindow host)
    {
        var s = AppState.S;
        var hour = DateTime.Now.Hour;
        var greet = hour < 5 ? "Late night session?" : hour < 12 ? "Good morning." : hour < 18 ? "Good afternoon." : "Good evening.";
        var page = Ui.Page(greet, "Your Roblox, set up exactly the way you like it.", out var body);

        foreach (var w in s.Studio.HomeWidgets.Where(w => !s.Studio.HiddenWidgets.Contains(w)))
        {
            UIElement? card = w switch
            {
                "play" => Play(host),
                "status" => Status(host, s),
                "boost" => Boost(host),
                "session" => SessionCard(),
                "server" => Server(host),
                "suggest" => Suggest(host, s),
                "tip" => Ui.Card(Ui.Lbl("Tip"), Ui.Text(Tips[DateTime.Now.DayOfYear % Tips.Length], 14, "Ink")),
                "clock" => Ui.Card(Ui.Lbl("Clock"), Ui.Big(DateTime.Now.ToString("HH:mm")), Ui.Small(DateTime.Now.ToString("dddd d MMMM"), 0)),
                "stats" => Stats(),
                "fpschart" => FpsChart(),
                "spotify" => Spotify(s),
                "notes" => NotesCard(host, s),
                _ => null,
            };
            if (card != null && (w is not ("clock" or "stats" or "fpschart" or "spotify" or "notes") || BrainStrap.Core.Platform.Extensions.On(s, "brainwidgets"))) body.Children.Add(card);
        }
        body.Children.Add(Ui.Small("Change what's on this page in Studio → Layout Builder.", 4));
        return page;
    }

    // ── BrainWidgets ──
    static UIElement? Stats()
    {
        var week = History.Sessions().Where(r => r.Start > DateTime.Now.AddDays(-7)).ToList();
        if (week.Count == 0) return null;
        var fps = week.Where(r => r.Fps != null).Select(r => r.Fps!.AvgFps).ToList();
        return Ui.Card(Ui.Lbl("This week"), Ui.Cols(3,
            Ui.Stat($"{week.Sum(r => r.Minutes) / 60:0.0} h", "played"),
            Ui.Stat(week.Count.ToString(), "sessions"),
            Ui.Stat(fps.Count > 0 ? $"{fps.Average():0}" : "—", "average FPS")));
    }

    static UIElement? FpsChart()
    {
        var fps = History.Sessions().Where(r => r.Fps != null).Take(20).Reverse().Select(r => r.Fps!.AvgFps).ToList();
        return fps.Count < 2 ? null : Ui.Card(Ui.Lbl("FPS · last sessions"), Ui.Spark(fps, 54));
    }

    static UIElement? Spotify(Settings s)
    {
        if (!s.Platform.SpotifyWidget) return null;
        var now = BrainStrap.Core.Platform.Connect.SpotifyNowPlaying();
        return now == null ? null : Ui.Card(Ui.Lbl("Now playing · Spotify"), Ui.Title(now, 15));
    }

    static UIElement? NotesCard(MainWindow host, Settings s)
    {
        var note = BrainStrap.Core.Platform.Notes.Profile(s, s.ActiveProfile);
        return string.IsNullOrWhiteSpace(note) ? null : Ui.Card(Ui.Lbl("Note · " + s.ActiveProfile + " profile"), Ui.Text(note, 14), Ui.Buttons(Ui.Ghost("Edit notes", () => host.Go("extensions"))));
    }

    static UIElement Play(MainWindow host)
    {
        var lib = new WrapPanel();
        foreach (var g in History.Playtime(DateTime.Now.AddDays(-60)).Where(g => g.placeId != null).Take(6))
            lib.Children.Add(LifePage.GameTile(host, g.placeId!, g.game, $"{g.minutes:0} min played"));
        foreach (var f in AppState.S.Servers.Favorites.Where(f => f.PrivateLinkProtected == null && RobloxApi.IsId(f.PlaceId)).Take(Math.Max(0, 6 - lib.Children.Count)))
            lib.Children.Add(LifePage.GameTile(host, f.PlaceId, f.Name, "favourite"));
        if (lib.Children.Count == 0)
            return Ui.Card(Ui.Lbl("Quick Play"), Ui.Title("Press Play on roblox.com, as always", 15),
                Ui.Desc("Games you play through BrainStrap appear here for one-click launching, with your settings applied."),
                Ui.Buttons(Ui.Pill("Open roblox.com", () => Ui.Open("https://www.roblox.com/discover"))));
        return Ui.Card(Ui.Lbl("Quick Play"), lib);
    }

    static UIElement Status(MainWindow host, Settings s)
    {
        var registered = ProtocolHandler.IsRegisteredToUs();
        var installed = RobloxInstall.Newest();
        var sp = new StackPanel();
        sp.Children.Add(Ui.Lbl("Status"));
        sp.Children.Add(Ui.Status(registered ? "ok" : "warn", registered ? "BrainStrap opens when you press Play" : "BrainStrap isn't handling the Play button yet", "",
            registered ? null : Ui.Pill("Set up", () => { Installer.Install(s); host.Refresh(); })));
        sp.Children.Add(Ui.Status(installed != null ? "ok" : "bad", installed != null ? "Roblox installed" : "Roblox not installed", installed?.Name ?? ""));
        sp.Children.Add(Ui.Status("info", $"Profile: {s.ActiveProfile}", $"{FlagCatalog.Resolve(FlagCatalog.Load(), s.Current, s.FlagFormat).Count} flag(s) · {s.GameProfiles.Count} game profile(s){(s.SafeMode ? " · SAFE MODE ON" : "")}",
            Ui.Ghost("BrainFlags", () => host.Go("flags"))));
        return Ui.Card(sp);
    }

    static UIElement Boost(MainWindow host) => Ui.Card(
        Ui.Lbl("BrainBoost"),
        Ui.Title("One click: the best setup for this PC", 16),
        Ui.Desc("BrainAI reads your hardware and builds a tested, safe performance setup. You see every change before it's applied."),
        Ui.Buttons(
            Ui.Pill("Run BrainBoost", () => { BrainAiPage.PendingRequest = "smoother while keeping the graphics looking good"; host.Go("brainai"); }),
            Ui.Ghost("Ask BrainAI something", () => host.Go("brainai"))));

    static UIElement SessionCard()
    {
        var live = History.ReadLive();
        if (live != null)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Lbl("Playing now"));
            sp.Children.Add(Ui.Title(live.GameName ?? "Roblox", 16));
            sp.Children.Add(Ui.Cols(3,
                Ui.Stat(live.Measuring && live.Fps > 0 ? $"{live.Fps:0}" : "—", "FPS"),
                Ui.Stat(live.Ping is { Samples: > 0 } p && p.LossPct < 100 ? $"{p.Avg:0} ms" : "—", "ping"),
                Ui.Stat($"{(DateTime.UtcNow - live.Started).TotalMinutes:0} min", "session")));
            return Ui.Card(sp);
        }
        var last = History.Sessions().FirstOrDefault();
        if (last == null) return Ui.Card(Ui.Lbl("Last session"), Ui.Small("Nothing played through BrainStrap yet.", 0));
        var box = new StackPanel();
        box.Children.Add(Ui.Lbl("Last session"));
        box.Children.Add(Ui.Title($"{last.GameName ?? "Roblox"} · {last.Minutes:0} min · {last.Start:ddd d MMM, HH:mm}", 15));
        box.Children.Add(new Border { Height = 8 });
        box.Children.Add(Ui.Cols(4,
            Ui.Stat(last.Fps != null ? $"{last.Fps.AvgFps:0}" : "—", "average FPS"),
            Ui.Stat(last.Fps != null ? $"{last.Fps.Low1:0}" : "—", "1% low"),
            Ui.Stat(last.Fps != null ? $"{last.Fps.Stability}" : "—", "stability"),
            Ui.Stat(last.Ping is { Samples: > 0 } p2 && p2.LossPct < 100 ? $"{p2.Avg:0}" : "—", "ping (ms)")));
        if (last.Graph is { Length: > 2 } g) box.Children.Add(Ui.Graph(g, 90));
        if (last.Problem != null) box.Children.Add(Ui.Status("warn", last.Problem, "See BrainAI → Troubleshooter."));
        return Ui.Card(box);
    }

    static UIElement? Server(MainWindow host)
    {
        var live = History.ReadLive();
        var last = History.Sessions().FirstOrDefault(r => r.ServerIp != null);
        var ip = live?.ServerIp ?? last?.ServerIp;
        if (ip == null) return null;
        var ping = live?.Ping ?? last?.Ping;
        return Ui.Card(
            Ui.Lbl(live != null ? "Your server" : "Last server"),
            Ui.KV("Game", live?.GameName ?? last?.GameName ?? "Roblox"),
            Ui.KV("Region", live?.Region ?? last?.Region ?? "turn on Region Viewer in Servers"),
            Ui.KV("Connection", ping is { Samples: > 0 } p && p.LossPct < 100 ? $"{p.Avg:0} ms · quality {p.Quality}/100 ({p.QualityLabel})" : "no ping data"),
            Ui.Buttons(Ui.Ghost("Find a better server", () => host.Go("servers"))));
    }

    static UIElement? Suggest(MainWindow host, Settings s)
    {
        var items = new List<UIElement>();
        // Profile Auto Selection: games that ran badly and have no profile yet
        var slow = History.Sessions().Where(r => r.PlaceId != null && r.Fps != null && !s.GameProfiles.ContainsKey(r.PlaceId!))
            .GroupBy(r => r.PlaceId).Select(g => g.First()).Where(r => r.Fps!.AvgFps < 45).Take(2);
        foreach (var r in slow)
            items.Add(Ui.Status("warn", $"{r.GameName ?? "Place " + r.PlaceId} ran at {r.Fps!.AvgFps:0} FPS", "BrainAI can build a profile just for this game.",
                Ui.Ghost("Build profile", () => host.Go("brainai"))));
        var compat = BrainStrap.Core.BrainAI.Doctor.Compatibility(s).Where(f => f.Level == "high").ToList();
        if (compat.Count > 0) items.Add(Ui.Status("bad", $"{compat.Count} setting problem(s) in '{s.ActiveProfile}'", compat[0].Title, Ui.Ghost("Check", () => host.Go("brainai"))));
        if (BrainStrap.Core.BrainAI.Power.Status().onBattery) items.Add(Ui.Status("warn", "On battery", "Windows slows the PC down unplugged.", Ui.Ghost("Battery profiles", () => host.Go("life"))));
        if (FrameCapture.FindExe(s) == null) items.Add(Ui.Status("info", "Turn on FPS measuring", "Real FPS numbers make BrainAI much smarter.", Ui.Ghost("Set up", () => host.Go("performance"))));
        if (items.Count == 0) return null;
        var sp = new StackPanel();
        sp.Children.Add(Ui.Lbl("Suggestions"));
        foreach (var i in items) sp.Children.Add(i);
        return Ui.Card(sp);
    }
}
