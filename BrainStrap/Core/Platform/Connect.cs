using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;

namespace BrainStrap.Core.Platform;

/// <summary>
/// BrainConnect: integrations that need no passwords. Local apps are found on this PC, webhooks only go to addresses
/// you add (stored encrypted), and every integration can be switched off, which also forgets its data.
/// </summary>
internal static class Connect
{
    static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(8), DefaultRequestHeaders = { { "User-Agent", "BrainStrap/2.0" } } };

    // ── brainstrap:// links (RBLX.BRAIN website, Stream Deck, BrainAPI) ──
    const string ProtoKey = @"Software\Classes\brainstrap";

    public static bool ProtocolRegistered()
    {
        using var k = Registry.CurrentUser.OpenSubKey(ProtoKey + @"\shell\open\command");
        return (k?.GetValue(null) as string)?.Contains("BrainStrap.exe", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static void RegisterProtocol(bool on)
    {
        if (!on) { Registry.CurrentUser.DeleteSubKeyTree(ProtoKey, false); return; }
        var exe = Installer.IsInstalled ? Installer.InstalledExe : Paths.SelfExe;
        using var root = Registry.CurrentUser.CreateSubKey(ProtoKey);
        root.SetValue(null, "URL:BrainStrap");
        root.SetValue("URL Protocol", "");
        using var cmd = root.CreateSubKey(@"shell\open\command");
        cmd.SetValue(null, $"\"{exe}\" \"%1\"");
    }

    public static readonly (string example, string what)[] Commands =
    {
        ("brainstrap://play/920587237", "Play a game with your BrainStrap setup"),
        ("brainstrap://profile/Competitive", "Switch graphics profile"),
        ("brainstrap://macro/Stream%20mode", "Run one of your macros"),
        ("brainstrap://open/servers", "Open a BrainStrap page"),
        ("brainstrap://pack/pack.midnight-arcade", "Apply a BrainMods pack"),
        ("brainstrap://theme/BST1.…", "Import a theme (asks first)"),
        ("brainstrap://flags/<base64 JSON>", "Import a flag preset (asks first)"),
    };

    /// <summary>Handles a brainstrap:// link. Anything that changes settings asks first. Returns a page to open, if any.</summary>
    public static string? HandleUri(Settings s, string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || u.Scheme != "brainstrap") return null;
        var kind = u.Host.ToLowerInvariant();
        var arg = Uri.UnescapeDataString(u.AbsolutePath.Trim('/'));
        bool Ask(string q) => MessageBox.Show(q, "BrainStrap", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        switch (kind)
        {
            case "play":
                var parts = arg.Split('/');
                if (Servers.RobloxApi.IsId(parts[0])) Servers.JoinLinks.Join(parts[0], parts.Length > 1 && Servers.RobloxApi.IsJobId(parts[1]) ? parts[1] : null);
                return null;
            case "profile":
                var p = s.Profiles.Keys.FirstOrDefault(k => k.Equals(arg, StringComparison.OrdinalIgnoreCase));
                if (p != null) { s.ActiveProfile = p; AppState.Save("Profile " + p + " (link)"); Notify("BrainStrap", "Profile: " + p); }
                return null;
            case "macro":
                var m = Flow.AllRules(s).FirstOrDefault(r => r.Trigger == "manual" && r.Name.Equals(arg, StringComparison.OrdinalIgnoreCase));
                if (m != null) Flow.Run(s, m.Actions);
                return null;
            case "open":
                return Regex.IsMatch(arg, "^[a-z]{2,20}$") ? arg : "home";
            case "pack":
                var pack = ModPacks.All(s).FirstOrDefault(x => x.Id == arg);
                if (pack != null && Ask($"Apply the '{pack.Name}' mod pack?")) ModPacks.Apply(s, new List<ModPack> { pack });
                return null;
            case "theme":
                var t = UI.Themes.FromCode(arg);
                if (t != null && Ask($"Add the theme '{t.Name}' from a link?")) { s.Studio.CustomThemes.Add(t); s.Theme = t.Id; AppState.Save("Theme from link: " + t.Name); }
                return null;
            case "flags":
                try
                {
                    var b64 = arg.Replace('-', '+').Replace('_', '/');
                    b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
                    var vals = JsonSerializer.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(Convert.FromBase64String(b64))) ?? new();
                    var cat = FlagCatalog.Load();
                    var clean = vals.Select(kv => (kv.Key, v: cat.Flags.FirstOrDefault(f => f.Id == kv.Key) is { } d ? BrainAI.Engine.Clean(d, kv.Value) : null))
                        .Where(x => x.v != null).ToDictionary(x => x.Key, x => x.v!);
                    if (clean.Count > 0 && Ask($"Import {clean.Count} BrainFlags setting(s) into a new profile?"))
                    {
                        var name = "Imported";
                        for (var i = 2; s.Profiles.ContainsKey(name); i++) name = "Imported " + i;
                        s.Profiles[name] = new FlagProfile { Values = clean };
                        s.ActiveProfile = name;
                        AppState.Save("Flags imported from a link");
                    }
                }
                catch { }
                return null;
        }
        return null;
    }

    // ── local apps (Discord, OBS, Spotify, Steam) ──
    public static string? FindApp(string? key)
    {
        string L(params string[] parts) => Path.Combine(parts);
        var local = Paths.Local;
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string? First(params string[] c) => c.FirstOrDefault(File.Exists);
        return key switch
        {
            "discord" => First(L(local, "Discord", "Update.exe")),
            "obs" => First(L(pf, "obs-studio", "bin", "64bit", "obs64.exe")),
            "spotify" => First(L(roaming, "Spotify", "Spotify.exe")) ?? "spotify:",
            "steam" => First(L(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe")) ?? "steam://open/main",
            _ => null,
        };
    }

    public static void StartApp(string? key)
    {
        var target = FindApp(key);
        if (target == null) return;
        var name = key switch { "discord" => "Discord", "obs" => "obs64", "spotify" => "Spotify", "steam" => "steam", _ => "" };
        if (name.Length > 0 && Process.GetProcessesByName(name).Length > 0) return;
        var psi = target.EndsWith("Update.exe") ? new ProcessStartInfo(target, "--processStart Discord.exe") : new ProcessStartInfo(target);
        psi.UseShellExecute = true;
        if (key == "obs") psi.WorkingDirectory = Path.GetDirectoryName(target)!;
        try { Process.Start(psi); } catch (Exception ex) { Logger.Warn("start app: " + ex.Message); }
    }

    public static void CloseApp(string? key)
    {
        var name = key switch { "discord" => "Discord", "obs" => "obs64", "spotify" => "Spotify", "steam" => "steam", _ => null };
        if (name != null) BrainStrap.Core.Perf.BackgroundAdvisor.CloseGracefully(name);
    }

    // ── Windows notifications ──
    public static void Notify(string title, string text)
    {
        if (!AppState.S.Platform.Notifications) return;
        Application.Current?.Dispatcher.Invoke(() => new UI.Toast(title, text).Show());
    }

    // ── Spotify now playing (read from the Spotify window title; no account needed) ──
    public static string? SpotifyNowPlaying()
    {
        foreach (var p in Process.GetProcessesByName("Spotify"))
        {
            try
            {
                var t = p.MainWindowTitle;
                if (t.Contains(" - ") && !t.StartsWith("Spotify")) return t;
            }
            catch { }
            finally { p.Dispose(); }
        }
        return null;
    }

    // ── OBS Studio: a text file OBS can show with a "Text (GDI+) → Read from file" source ──
    public static string ObsFile => Path.Combine(Paths.Data, "obs-stats.txt");

    public static void WriteObs(Settings s, BrainStrap.Core.Perf.LiveState live)
    {
        if (!s.Platform.ObsStats) return;
        try
        {
            var ping = live.Ping is { Samples: > 0 } p && p.LossPct < 100 ? $"{p.Avg:0} ms" : "—";
            File.WriteAllText(ObsFile, $"{(live.Fps > 0 ? live.Fps.ToString("0") : "—")} FPS · {ping}");
        }
        catch { }
    }

    // ── Webhooks (Discord launch notifications too): only to addresses you added, only game + performance facts ──
    public static async Task SendWebhooksAsync(Settings s, string ev, JsonObject data)
    {
        foreach (var w in s.Platform.Webhooks.Where(w => w.Events.Contains(ev) || ev == "flow"))
        {
            var url = Secret.Unprotect(w.UrlProtected);
            if (url == null || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
            var payload = new JsonObject { ["event"] = ev, ["app"] = "BrainStrap", ["version"] = Installer.Version, ["time"] = DateTime.UtcNow.ToString("o") };
            foreach (var kv in data) payload[kv.Key] = kv.Value?.DeepClone();
            // Discord webhooks want a "content" line
            if (url.Contains("discord.com/api/webhooks", StringComparison.OrdinalIgnoreCase))
                payload["content"] = ev switch
                {
                    "start" => $"🎮 Started playing {data["game"] ?? "Roblox"} with BrainStrap",
                    "end" => $"🏁 Played {data["game"] ?? "Roblox"} for {data["minutes"]} min{(data["fps"] != null ? $" · {data["fps"]} FPS avg" : "")}",
                    "crash" => $"⚠️ Roblox closed unexpectedly: {data["problem"]}",
                    _ => data["message"]?.ToString() ?? "BrainStrap",
                };
            try { using var r = await Http.PostAsync(url, new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")); }
            catch (Exception ex) { Logger.Warn("webhook " + w.Name + ": " + ex.GetType().Name); }
        }
    }

    // ── GitHub: updates and issue reports for your BrainStrap repo ──
    public static async Task<(string tag, string url)?> LatestReleaseAsync(string repo)
    {
        if (!Regex.IsMatch(repo ?? "", @"^[\w.-]+/[\w.-]+$")) return null;
        try
        {
            using var d = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest"));
            return (d.RootElement.GetProperty("tag_name").GetString() ?? "", d.RootElement.GetProperty("html_url").GetString() ?? "");
        }
        catch { return null; }
    }

    public static string IssueUrl(string repo, string diagnostics) =>
        $"https://github.com/{repo}/issues/new?title={Uri.EscapeDataString("Bug: ")}&body={Uri.EscapeDataString("**What happened?**\n\n\n**Diagnostics** (no account data)\n```\n" + diagnostics + "\n```")}";

    // ── Roblox Studio ──
    public static string? FindStudio()
    {
        foreach (var root in new[] { Paths.RobloxVersions, Paths.RobloxVersionsMachine })
        {
            if (!Directory.Exists(root)) continue;
            var f = Directory.EnumerateDirectories(root, "version-*").Select(d => Path.Combine(d, "RobloxStudioBeta.exe")).Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (f != null) return f;
        }
        return null;
    }
}
