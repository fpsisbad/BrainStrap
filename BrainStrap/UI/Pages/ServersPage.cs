using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BrainStrap.Core;
using BrainStrap.Core.Perf;
using BrainStrap.Core.Servers;

namespace BrainStrap.UI.Pages;

/// <summary>Server Intelligence: current server, regions, ping and quality, a server browser with suggestions, favourites and diagnostics.</summary>
internal static class ServersPage
{
    public static FrameworkElement Build(MainWindow host)
    {
        var s = AppState.S;
        var page = Ui.Page("Servers", "Where you're connected, how good the connection is, and better servers to join. Everything comes from Roblox's public information and its own logs.", out var body);

        // ── current / last server ──
        var cur = new StackPanel();
        body.Children.Add(Ui.Card(Ui.Lbl("BrainServers · your server"), cur));
        var pingHistory = new List<double>();
        void DrawCurrent()
        {
            cur.Children.Clear();
            var live = History.ReadLive();
            var last = History.Sessions().FirstOrDefault();
            string? place = live?.PlaceId ?? last?.PlaceId, job = live?.JobId ?? last?.JobId, ip = live?.ServerIp ?? last?.ServerIp;
            string? game = live?.GameName ?? last?.GameName, region = live?.Region ?? last?.Region;
            var ping = live?.Ping ?? last?.Ping;
            if (place == null) { cur.Children.Add(Ui.Small("Join a game through BrainStrap and its server shows up here.", 0)); return; }
            cur.Children.Add(Ui.Title((live != null ? "Playing now: " : "Last played: ") + (game ?? "Place " + place), 16));
            cur.Children.Add(new Border { Height = 10 });
            cur.Children.Add(Ui.KV("Server address", ip ?? "not seen in the log yet"));
            cur.Children.Add(Ui.KV("Region", region ?? (s.Servers.RegionLookup ? "looking up…" : "turn on region lookup below")));
            if (ping is { Samples: > 0 } p && p.LossPct < 100)
            {
                cur.Children.Add(Ui.KV("Ping", $"{p.Avg:0} ms · jitter {p.Jitter:0} ms · loss {p.LossPct:0.#}%"));
                cur.Children.Add(Ui.KV("Connection quality", $"{p.Quality}/100 · {p.QualityLabel}", p.Quality >= 70 ? "Ok" : p.Quality >= 50 ? "Warn" : "Bad"));
                if (live != null) { pingHistory.Add(p.Avg); if (pingHistory.Count > 60) pingHistory.RemoveAt(0); }
                if (pingHistory.Count > 2) cur.Children.Add(Ui.Spark(pingHistory, 40));
            }
            else if (ping != null) cur.Children.Add(Ui.KV("Ping", "this server doesn't answer ping"));
            if (job != null) cur.Children.Add(Ui.KV("Server ID", job));
            cur.Children.Add(Ui.Buttons(
                Ui.Pill("Rejoin this server", () => JoinLinks.Join(place, job)),
                Ui.Ghost("Copy join link", () => Ui.Copy(JoinLinks.Web(place, job))),
                Ui.Ghost("Add to favourites", () =>
                {
                    s.Servers.Favorites.Add(new FavoriteServer { Name = game ?? "Place " + place, PlaceId = place, JobId = job });
                    AppState.Save("Favourite added");
                    JumpLists.Update(s);
                    host.Refresh();
                })));
        }
        DrawCurrent();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => { if (History.ReadLive() != null) DrawCurrent(); };
        page.Loaded += (_, _) => timer.Start();
        page.Unloaded += (_, _) => timer.Stop();

        body.Children.Add(Ui.Card(
            Ui.ToggleRow("Server Region Viewer", "Shows where the game server is. Off by default because it sends the Roblox server's IP address (not yours) to ipinfo.io to look up its location.",
                s.Servers.RegionLookup, on => { s.Servers.RegionLookup = on; AppState.Save(); }),
            Ui.ToggleRow("Track ping during sessions", "Pings the game server once a second for Ping History and the Connection Quality Score. Some Roblox servers ignore ping.",
                s.Servers.TrackPing, on => { s.Servers.TrackPing = on; AppState.Save(); })));

        // ── server browser ──
        var lastPlace = History.ReadLive()?.PlaceId ?? History.Sessions().FirstOrDefault(r => r.PlaceId != null)?.PlaceId ?? "";
        var placeBox = Ui.Field(lastPlace, 200, true);
        var list = new StackPanel();
        async Task Load()
        {
            list.Children.Clear();
            var id = new string(placeBox.Text.Where(char.IsAsciiDigit).ToArray());
            if (!RobloxApi.IsId(id)) { list.Children.Add(Ui.Small("Enter a place ID.")); return; }
            list.Children.Add(Ui.Small("Loading servers…"));
            var game = await RobloxApi.GameAsync(id);
            var servers = await RobloxApi.PublicServersAsync(id);
            list.Children.Clear();
            list.Children.Add(Ui.Title(game?.Name ?? "Place " + id, 15));
            list.Children.Add(Ui.Small($"{servers.Count} public server(s) with free space{(game != null ? $" · {game.Playing:N0} playing now" : "")}. Ping and FPS are what Roblox reports for each server's current players.", 2));
            if (servers.Count == 0) { list.Children.Add(Ui.Small("None found (or Roblox limited the request; try again in a minute).", 8)); return; }
            list.Children.Add(Ui.Lbl("Smart Server Suggestions", 14));
            foreach (var sug in RobloxApi.Suggest(servers))
            {
                var sv = sug.Server;
                list.Children.Add(Ui.Status("ok", $"{sv.Playing}/{sv.Max} players · {(sv.Ping > 0 ? sv.Ping + " ms" : "ping ?")}", sug.Why,
                    Ui.Row(Ui.Pill("Join", () => JoinLinks.Join(id, sv.Id)), Ui.Ghost("Copy link", () => Ui.Copy(JoinLinks.Web(id, sv.Id))))));
            }
            list.Children.Add(Ui.Lbl("Server Population Viewer", 10));
            foreach (var sv in servers.OrderBy(x => x.Ping <= 0 ? 999 : x.Ping).Take(40))
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var players = Ui.Text($"{sv.Playing}/{sv.Max}", 13, "Ink", FontWeights.SemiBold, true);
                var bar = Ui.Meter(sv.Max > 0 ? (double)sv.Playing / sv.Max : 0);
                bar.Margin = new Thickness(0, 6, 18, 0);
                var pingT = Ui.Text(sv.Ping > 0 ? $"{sv.Ping:0} ms" : "—", 13, sv.Ping > 150 ? "Warn" : "Muted", mono: true);
                var fpsT = Ui.Text($"{sv.Fps:0} fps", 13, (sv.Fps is > 0 and < 50) ? "Warn" : "Muted", mono: true);
                var jobId = sv.Id;
                var join = Ui.Ghost("Join", () => JoinLinks.Join(id, jobId));
                join.Margin = new Thickness(0);
                join.Height = 30;
                Grid.SetColumn(bar, 1); Grid.SetColumn(pingT, 2); Grid.SetColumn(fpsT, 3); Grid.SetColumn(join, 4);
                foreach (var el in new UIElement[] { players, bar, pingT, fpsT, join }) g.Children.Add(el);
                list.Children.Add(g);
            }
        }
        body.Children.Add(Ui.Card(
            Ui.Lbl("Server browser"),
            Ui.Row(Ui.Text("Place ID  ", 13, "Muted"), placeBox, new Border { Width = 10 }, Ui.AsyncPill("Load servers", Load)),
            list));

        // ── favourites + private servers ──
        var fav = new StackPanel();
        if (s.Servers.Favorites.Count == 0) fav.Children.Add(Ui.Small("No favourites yet. Add the server you're on, or a private server link below.", 0));
        foreach (var f in s.Servers.Favorites.ToList())
        {
            var item = f;
            var priv = Secret.Unprotect(item.PrivateLinkProtected);
            fav.Children.Add(Ui.Status("info", item.Name, priv != null ? "Private server" : item.JobId != null ? "A specific server" : "Game · place " + item.PlaceId,
                Ui.Row(
                    Ui.Pill("Join", () => JoinLinks.Join(item.PlaceId, item.JobId, priv)),
                    Ui.Ghost("Remove", () => { s.Servers.Favorites.Remove(item); AppState.Save("Favourite removed"); JumpLists.Update(s); host.Refresh(); }))));
        }
        var favName = Ui.Field("", 200);
        var favLink = Ui.Field("", 360, true);
        var favMsg = Ui.Small("");
        body.Children.Add(Ui.Card(
            Ui.Lbl("Server Favorites · Private Server Shortcuts"),
            fav,
            Ui.Lbl("Add a game or a private server", 10),
            Ui.Row(Ui.Text("Name  ", 13, "Muted"), favName),
            new Border { Height = 8 },
            Ui.Row(Ui.Text("Place ID or private server link  ", 13, "Muted"), favLink),
            Ui.Buttons(Ui.Pill("Add", () =>
            {
                var raw = favLink.Text.Trim();
                var name = favName.Text.Trim();
                if (RobloxApi.IsId(raw))
                    s.Servers.Favorites.Add(new FavoriteServer { Name = name.Length > 0 ? name : "Place " + raw, PlaceId = raw });
                else if (JoinLinks.CleanPrivateLink(raw) is { } link)
                {
                    var placeId = System.Text.RegularExpressions.Regex.Match(link, @"/games/(\d+)").Groups[1].Value;
                    s.Servers.Favorites.Add(new FavoriteServer { Name = name.Length > 0 ? name : "Private server", PlaceId = placeId, PrivateLinkProtected = Secret.Protect(link) });
                }
                else { Ui.Msg(favMsg, "Paste a place ID or a roblox.com private server link.", "Warn"); return; }
                AppState.Save("Favourite added");
                JumpLists.Update(s);
                host.Refresh();
            })),
            Ui.Desc("Private server links are stored encrypted for your Windows user, because anyone with the link can join that server."),
            favMsg));

        // ── join link generator ──
        var lp = Ui.Field(lastPlace, 200, true);
        var lj = Ui.Field(History.ReadLive()?.JobId ?? "", 330, true);
        var outApp = Ui.Text("", 12, "Muted", mono: true);
        var outWeb = Ui.Text("", 12, "Muted", mono: true);
        void Gen()
        {
            var p = new string(lp.Text.Where(char.IsAsciiDigit).ToArray());
            if (!RobloxApi.IsId(p)) { outApp.Text = outWeb.Text = ""; return; }
            var j = lj.Text.Trim();
            outApp.Text = JoinLinks.App(p, j.Length > 0 ? j : null);
            outWeb.Text = JoinLinks.Web(p, j.Length > 0 ? j : null);
        }
        lp.TextChanged += (_, _) => Gen();
        lj.TextChanged += (_, _) => Gen();
        Gen();
        body.Children.Add(Ui.Card(
            Ui.Lbl("Join Link Generator"),
            Ui.Row(Ui.Text("Place ID  ", 13, "Muted"), lp),
            new Border { Height = 8 },
            Ui.Row(Ui.Text("Server ID (optional)  ", 13, "Muted"), lj),
            Ui.Lbl("Share link (opens in a browser)", 12), outWeb,
            Ui.Lbl("App link (opens Roblox directly)", 10), outApp,
            Ui.Buttons(Ui.Pill("Copy share link", () => Ui.Copy(outWeb.Text)), Ui.Ghost("Copy app link", () => Ui.Copy(outApp.Text)))));

        // ── ping history ──
        var ph = new StackPanel();
        var withPing = History.Sessions().Where(r => r.Ping is { Samples: > 0 } p && p.LossPct < 100).Take(30).Reverse().ToList();
        if (withPing.Count < 2) ph.Children.Add(Ui.Small("Ping history appears after a couple of sessions with ping tracking on.", 0));
        else
        {
            ph.Children.Add(Ui.Spark(withPing.Select(r => r.Ping!.Avg).ToList(), 46));
            foreach (var r in withPing.AsEnumerable().Reverse().Take(8))
                ph.Children.Add(Ui.Status(r.Ping!.Quality >= 70 ? "ok" : "warn", $"{r.Start:d MMM HH:mm} · {r.GameName ?? "Place " + r.PlaceId}",
                    $"{r.Ping.Avg:0} ms · jitter {r.Ping.Jitter:0} · quality {r.Ping.Quality}/100{(r.Region != null ? " · " + r.Region : "")}"));
        }
        body.Children.Add(Ui.Card(Ui.Lbl("Ping History"), ph));

        // ── diagnostics ──
        var diag = new StackPanel();
        body.Children.Add(Ui.Card(
            Ui.Lbl("Connection Diagnostics"),
            Ui.Desc("Tests your Wi-Fi/router, your internet, DNS, Roblox's website and (while playing) the game server, so you know which part is the problem.", 0),
            Ui.Buttons(Ui.AsyncPill("Run diagnostics", async () =>
            {
                diag.Children.Clear();
                diag.Children.Add(Ui.Small("Testing (about 10 seconds)…"));
                var results = await NetDiag.RunAsync(History.ReadLive()?.ServerIp);
                diag.Children.Clear();
                foreach (var r in results) diag.Children.Add(Ui.Status(r.Level, $"{r.Name}: {r.Value}", r.Help));
            })),
            diag));
        return page;
    }
}
