using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BrainStrap.Core.Platform;

// ─────────────────────────────── models ───────────────────────────────

internal sealed class FlowAction
{
    public string Type { get; set; } = "";
    public string? Value { get; set; }
}

internal sealed class FlowRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Trigger { get; set; } = "launch";        // launch | join | exit | crash | battery | manual
    public string? TriggerValue { get; set; }               // place id or game name for "join"
    public List<FlowAction> Actions { get; set; } = new();
    public string? Source { get; set; }                     // extension id that added it, if any
}

internal sealed class ExtCommand
{
    public string Title { get; set; } = "";
    public List<FlowAction> Actions { get; set; } = new();
}

internal sealed class ExtWidget
{
    public string Type { get; set; } = "text";              // clock | stats | fpschart | spotify | notes | text
    public string Title { get; set; } = "";
    public string? Text { get; set; }
}

internal sealed class ExtVersion
{
    public string Version { get; set; } = "";
    public string Date { get; set; } = "";
    public string Notes { get; set; } = "";
}

/// <summary>An extension is data only: commands, automations, widgets, themes, flag presets and mod packs built from BrainStrap's own safe actions. No code ever runs.</summary>
internal sealed class ExtensionManifest
{
    public int Format { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Publisher { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Permissions { get; set; } = new();
    public List<ExtCommand> Commands { get; set; } = new();
    public List<FlowRule> Flows { get; set; } = new();
    public List<ExtWidget> Widgets { get; set; } = new();
    public List<string> Themes { get; set; } = new();
    public List<ModPack> Packs { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> FlagPresets { get; set; } = new();
    public List<ExtVersion> Changelog { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
}

internal sealed class ModPack
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Creator { get; set; } = "RBLX.BRAIN";
    public string Summary { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public string? ThemeCode { get; set; }
    public string? Accent { get; set; }
    public string? Font { get; set; }
    public bool? Minimal { get; set; }
    public string? Background { get; set; }                 // none | silk (packs never carry files)
    public string? Splash { get; set; }
    public string? HudCorner { get; set; }
    public double? HudOpacity { get; set; }
    public List<string>? HomeWidgets { get; set; }
}

internal sealed class Webhook
{
    public string Name { get; set; } = "";
    public string UrlProtected { get; set; } = "";
    public List<string> Events { get; set; } = new() { "start", "end", "crash" };
}

internal sealed class PlatformSettings
{
    public HashSet<string> BuiltinsOff { get; set; } = new();
    public List<FlowRule> Flows { get; set; } = new();
    public Dictionary<string, string> Notes { get; set; } = new();          // "game:<placeId>" / "profile:<name>"
    public List<string> Following { get; set; } = new();
    public string StoreUrl { get; set; } = "";
    public List<string> ActivePacks { get; set; } = new();
    public List<ModPack> InstalledPacks { get; set; } = new();
    public Dictionary<string, List<ModPack>> PackHistory { get; set; } = new();
    public StudioSettings? StudioBeforePacks { get; set; }
    public List<Webhook> Webhooks { get; set; } = new();
    public bool ObsStats { get; set; }
    public bool SpotifyWidget { get; set; } = true;
    public bool Notifications { get; set; } = true;
    public bool WriteIgnoredFlags { get; set; }
    public string GitHubRepo { get; set; } = "";
    public Dictionary<string, Dictionary<string, string>> FlagPresets { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> KnownGood { get; set; } = new();
}

// ─────────────────────────────── grammar (builder.json) ───────────────────────────────

internal sealed class GrammarItem
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("p")] public List<string> P { get; set; } = new();
    [JsonPropertyName("arg")] public string? Arg { get; set; }
    [JsonPropertyName("perm")] public string? Perm { get; set; }
}

internal sealed class Grammar
{
    [JsonPropertyName("triggers")] public List<GrammarItem> Triggers { get; set; } = new();
    [JsonPropertyName("actions")] public List<GrammarItem> Actions { get; set; } = new();
    [JsonPropertyName("apps")] public Dictionary<string, string> Apps { get; set; } = new();
    [JsonPropertyName("permissions")] public Dictionary<string, string[]> Permissions { get; set; } = new();

    static Grammar? cached;
    public static Grammar Load()
    {
        if (cached != null) return cached;
        using var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("builder.json")!;
        return cached = JsonSerializer.Deserialize<Grammar>(st) ?? new Grammar();
    }

    public string ActionLabel(FlowAction a) =>
        (Actions.FirstOrDefault(x => x.Type == a.Type)?.Label ?? a.Type) + (a.Value != null ? $": {a.Value}" : "");

    public string TriggerLabel(FlowRule r) =>
        (Triggers.FirstOrDefault(x => x.Type == r.Trigger)?.Label ?? r.Trigger) + (r.TriggerValue != null ? $" ({r.TriggerValue})" : "");

    public List<string> PermissionsFor(IEnumerable<FlowAction> actions) =>
        actions.Select(a => Actions.FirstOrDefault(x => x.Type == a.Type)?.Perm).Where(p => p != null).Select(p => p!).Distinct().ToList();
}

// ─────────────────────────────── BrainFlow ───────────────────────────────

/// <summary>What a launch should do differently, decided by automations before Roblox starts.</summary>
internal sealed class LaunchOverrides
{
    public string? Profile;
    public bool? Safe;
    public bool? Hud;
    public bool Track;
    public bool GameMode;
    public List<string> Ran = new();
}

internal static class Flow
{
    public static IEnumerable<FlowRule> AllRules(Settings s)
    {
        foreach (var r in s.Platform.Flows) yield return r;
        foreach (var ext in Extensions.InstalledEnabled(s))
            foreach (var r in ext.Flows) { r.Source = ext.Id; yield return r; }
    }

    static bool Matches(FlowRule r, string trigger, string? placeId, string? gameName)
    {
        if (!r.Enabled || r.Trigger != trigger) return false;
        if (trigger != "join" || string.IsNullOrWhiteSpace(r.TriggerValue)) return true;
        var v = r.TriggerValue.Trim();
        return v == placeId || (gameName != null && gameName.Contains(v, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Runs every matching automation. Launch-time choices go into the returned overrides.</summary>
    public static LaunchOverrides Fire(Settings s, string trigger, string? placeId = null, string? gameName = null)
    {
        var o = new LaunchOverrides();
        if (s.Platform.BuiltinsOff.Contains("brainautomation")) return o;
        foreach (var r in AllRules(s).Where(r => Matches(r, trigger, placeId, gameName)))
        {
            o.Ran.Add(r.Name);
            Run(s, r.Actions, o);
            Logger.Info($"flow: '{r.Name}' ran on {trigger}");
        }
        // "join" rules also apply at launch when the place is known
        if (trigger == "launch" && placeId != null)
            foreach (var r in AllRules(s).Where(r => Matches(r, "join", placeId, gameName))) { o.Ran.Add(r.Name); Run(s, r.Actions, o); }
        return o;
    }

    /// <summary>Executes BrainStrap's own safe actions. Apps are only ever the four known names, resolved on this PC.</summary>
    public static void Run(Settings s, List<FlowAction> actions, LaunchOverrides? o = null)
    {
        foreach (var a in actions)
        {
            try
            {
                switch (a.Type)
                {
                    case "profile.switch":
                        var name = s.Profiles.Keys.FirstOrDefault(k => k.Equals(a.Value, StringComparison.OrdinalIgnoreCase));
                        if (name != null) { if (o != null) o.Profile = name; else { s.ActiveProfile = name; AppState.Save("Profile " + name + " (automation)"); } }
                        break;
                    case "hud.on": if (o != null) o.Hud = true; else { s.Hud.Enabled = true; AppState.Save(); } break;
                    case "hud.off": if (o != null) o.Hud = false; else { s.Hud.Enabled = false; AppState.Save(); } break;
                    case "tracking.start": if (o != null) o.Track = true; break;
                    case "gamemode.on": if (o != null) o.GameMode = true; break;
                    case "safemode.on": if (o != null) o.Safe = true; else { s.SafeMode = true; AppState.Save("Safe mode (automation)"); } break;
                    case "app.start": Connect.StartApp(a.Value); break;
                    case "app.close": Connect.CloseApp(a.Value); break;
                    case "notify": Connect.Notify("BrainStrap", a.Value ?? "Automation ran."); break;
                    case "theme.set":
                        var t = UI.Themes.All(s).FirstOrDefault(x => x.Name.Equals(a.Value, StringComparison.OrdinalIgnoreCase) || x.Id == a.Value);
                        if (t != null) { s.Theme = t.Id; AppState.Save("Theme " + t.Name + " (automation)"); }
                        break;
                    case "webhook": _ = Connect.SendWebhooksAsync(s, "flow", new JsonObject { ["message"] = a.Value ?? "automation" }); break;
                    case "backup": Vault.BackupNow(s, "automation"); break;
                    case "open.page": Session.OpenApp(a.Value ?? "home"); break;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"flow action {a.Type}: {ex.Message}");
            }
        }
    }
}

// ─────────────────────────────── BrainBuilder + BrainPilot ───────────────────────────────

internal sealed record BuildResult(FlowRule? Rule, List<string> Understood, List<string> Problems, List<string> Permissions, List<string> Tests);

internal static class Builder
{
    static string Norm(string t) => " " + Regex.Replace((t ?? "").ToLowerInvariant().Replace('’', '\''), @"\s+", " ").Trim() + " ";

    /// <summary>English → an automation. Same grammar as the website demo.</summary>
    public static BuildResult Build(Settings s, string text)
    {
        var g = Grammar.Load();
        var t = Norm(text);
        var understood = new List<string>();
        var problems = new List<string>();

        // trigger: the earliest phrase found
        GrammarItem? trig = null;
        var trigEnd = 0;
        var best = int.MaxValue;
        foreach (var it in g.Triggers)
            foreach (var p in it.P)
            {
                var i = t.IndexOf(" " + p, StringComparison.Ordinal);
                if (i >= 0 && i < best) { best = i; trig = it; trigEnd = i + p.Length + 1; }
            }
        var rule = new FlowRule { Trigger = trig?.Type ?? "manual", Name = Title(text) };
        if (trig == null) problems.Add("No trigger found, so this becomes a macro you run yourself (Ctrl+K).");
        else understood.Add(trig.Label);

        var rest = trig != null ? t[trigEnd..] : t;
        if (trig?.Arg == "game")
        {
            var m = Regex.Match(rest, @"^\s*([^,]+?)(?=,| then | and |$)");
            if (m.Success) { rule.TriggerValue = m.Groups[1].Value.Trim().TrimEnd('.'); rest = rest[(m.Index + m.Length)..]; understood.Add("Game: " + rule.TriggerValue); }
        }

        foreach (var clause in Regex.Split(rest, @",| then | and |;|\."))
        {
            var c = " " + clause.Trim() + " ";
            if (c.Trim().Length == 0) continue;
            var act = g.Actions.FirstOrDefault(a => a.P.Any(p => c.Contains(" " + p + " ", StringComparison.Ordinal) || c.TrimStart().StartsWith(p + " ", StringComparison.Ordinal)));
            if (act == null) continue;
            var fa = new FlowAction { Type = act.Type };
            if (act.Arg == "profile")
            {
                var prof = s.Profiles.Keys.FirstOrDefault(k => c.Contains(" " + k.ToLowerInvariant() + " "));
                var m = Regex.Match(c, @"(?:switch to|use the|use my|load|change to)\s+(?:the\s+|my\s+)?([\w\- ]+?)\s+profile");
                fa.Value = prof ?? (m.Success ? Cap(m.Groups[1].Value) : null);
                if (fa.Value == null) { problems.Add("Which profile? Say \"switch to Balanced profile\"."); continue; }
                if (!s.Profiles.ContainsKey(fa.Value)) problems.Add($"There's no '{fa.Value}' profile yet; create it in BrainFlags first.");
            }
            else if (act.Arg == "app")
            {
                var app = g.Apps.Keys.FirstOrDefault(k => c.Contains(" " + k));
                if (app == null) continue;                          // "open" without a known app isn't this action
                fa.Value = app;
            }
            else if (act.Arg == "theme")
            {
                var th = UI.Themes.All(s).FirstOrDefault(x => c.Contains(" " + x.Name.ToLowerInvariant()));
                if (th == null) continue;
                fa.Value = th.Name;
            }
            else if (act.Type == "notify")
            {
                var m = Regex.Match(clause, @"(?:to|that|:)\s+(.{3,80})$", RegexOptions.IgnoreCase);
                fa.Value = m.Success ? m.Groups[1].Value.Trim() : "Automation ran.";
            }
            if (rule.Actions.Any(x => x.Type == fa.Type && x.Value == fa.Value)) continue;
            rule.Actions.Add(fa);
            understood.Add(g.ActionLabel(fa));
        }
        if (rule.Actions.Count == 0) { problems.Add("No actions found. Try: \"switch to Balanced profile, start tracking and open Discord\"."); return new BuildResult(null, understood, problems, new(), new()); }

        var perms = g.PermissionsFor(rule.Actions);
        var tests = Simulate(s, rule);
        return new BuildResult(rule, understood, problems, perms, tests);
    }

    /// <summary>A dry run: what would happen, without doing it.</summary>
    public static List<string> Simulate(Settings s, FlowRule r)
    {
        var g = Grammar.Load();
        var lines = new List<string> { "▶ " + g.TriggerLabel(r) };
        foreach (var a in r.Actions)
        {
            var ok = a.Type switch
            {
                "profile.switch" => s.Profiles.ContainsKey(a.Value ?? ""),
                "app.start" or "app.close" => Connect.FindApp(a.Value) != null || a.Type == "app.close",
                "webhook" => s.Platform.Webhooks.Count > 0,
                _ => true,
            };
            lines.Add((ok ? "✓ " : "⚠ ") + g.ActionLabel(a) + (ok ? "" : a.Type == "webhook" ? " (add a webhook in BrainConnect)" : a.Type == "app.start" ? " (app not found on this PC)" : " (missing)"));
        }
        return lines;
    }

    /// <summary>Wraps an automation as an extension package (BrainBuilder's "proposed extension").</summary>
    public static ExtensionManifest AsExtension(FlowRule r, string publisher = "You") => new()
    {
        Id = "user." + Regex.Replace(r.Name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-') is { Length: > 5 } id ? id[..Math.Min(40, id.Length)] : "user.automation",
        Name = r.Name,
        Publisher = publisher,
        Summary = "Made with BrainBuilder: " + Grammar.Load().TriggerLabel(r).ToLowerInvariant() + ".",
        Permissions = Grammar.Load().PermissionsFor(r.Actions),
        Flows = new List<FlowRule> { r },
        Changelog = new List<ExtVersion> { new() { Version = "1.0.0", Date = DateTime.Now.ToString("yyyy-MM-dd"), Notes = "Created with BrainBuilder." } },
    };

    static string Title(string text)
    {
        var t = text.Trim().TrimEnd('.');
        return t.Length > 48 ? t[..48] + "…" : t.Length == 0 ? "Automation" : char.ToUpper(t[0]) + t[1..];
    }

    static string Cap(string s) => string.Join(' ', s.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpper(w[0]) + w[1..]));
}

/// <summary>BrainPilot: plain-English control of the launcher ("switch to Competitive", "show the hud", "open servers", "make it smoother").</summary>
internal static class Pilot
{
    static readonly (string page, string[] words)[] Pages =
    {
        ("home", new[] { "home" }), ("brainai", new[] { "brainai", "ai" }), ("flags", new[] { "flags", "brainflags", "graphics" }),
        ("mods", new[] { "mods", "brainmods", "mod packs" }), ("extensions", new[] { "extensions" }), ("store", new[] { "store", "brainstore", "marketplace" }),
        ("flow", new[] { "flow", "automations", "brainflow" }), ("connect", new[] { "connect", "integrations" }), ("performance", new[] { "performance", "benchmark" }),
        ("servers", new[] { "servers", "server" }), ("studio", new[] { "studio", "themes" }), ("tools", new[] { "tools", "repair", "timeline" }),
    };

    /// <summary>Returns a reply, the actions it would run, a page to open, or a settings request for BrainAI.</summary>
    public static (string reply, List<FlowAction> actions, string? page, string? brainAiRequest) Interpret(Settings s, string text)
    {
        var t = " " + text.ToLowerInvariant().Trim() + " ";
        var m = Regex.Match(t, @"\b(?:open|go to|show me|take me to)\s+(?:the\s+)?([a-z ]+?)(?:\s+page)?\s*$");
        if (m.Success)
        {
            var hit = Pages.FirstOrDefault(p => p.words.Any(w => m.Groups[1].Value.Trim() == w));
            if (hit.page != null) return ($"Opening {hit.page}.", new(), hit.page, null);
        }
        var build = Builder.Build(s, "macro " + text);
        if (build.Rule != null && build.Rule.Actions.Count > 0)
            return ("I'll do this: " + string.Join(", ", build.Rule.Actions.Select(a => Grammar.Load().ActionLabel(a).ToLowerInvariant())) + ".", build.Rule.Actions, null, null);
        var p = BrainAI.Engine.Parse(text);
        if (p.Groups.Count > 0 || p.Explicit.Count > 0)
            return ("That's a graphics request, so here's BrainAI's plan.", new(), null, text);
        return ("I can switch profiles, show the HUD, open apps or pages, start tracking, or tune graphics. Try \"switch to Competitive profile\" or \"make it smoother\".", new(), null, null);
    }
}

// ─────────────────────────────── BrainExtensions + BrainStore ───────────────────────────────

internal sealed record BuiltinExtension(string Id, string Name, string Summary, string Page, string[] Permissions, bool Star = false);

internal sealed record ScanResult(bool Passed, List<(bool ok, string check)> Checks);

internal sealed class StorePublisher
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Verified { get; set; }
    public string Bio { get; set; } = "";
}

internal sealed class StoreReview
{
    public string User { get; set; } = "";
    public int Stars { get; set; }
    public string Text { get; set; } = "";
    public string Date { get; set; } = "";
}

internal sealed class StoreVersion
{
    public string Version { get; set; } = "";
    public string Date { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

internal sealed class StoreItem
{
    public string Type { get; set; } = "extension";        // extension | pack | flags | theme
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Screenshots { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public List<StoreVersion> Versions { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
    public double Rating { get; set; }
    public List<StoreReview> Reviews { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    [JsonIgnore] public object? Payload { get; set; }      // built-in items carry their content directly
}

internal sealed class StoreCollection
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Items { get; set; } = new();
}

internal sealed class StoreIndex
{
    public List<StorePublisher> Publishers { get; set; } = new();
    public List<StoreItem> Items { get; set; } = new();
    public List<StoreCollection> Collections { get; set; } = new();
    public string SubmitUrl { get; set; } = "";
}

internal static class Extensions
{
    public static readonly BuiltinExtension[] Builtins =
    {
        new("brainpilot", "BrainPilot", "AI assistant that controls launcher settings from plain English.", "brainai", new[] { "profiles", "hud", "settings" }, true),
        new("brainhud", "BrainHUD", "Customizable performance overlay: FPS, ping, CPU, GPU and temperatures.", "life", new[] { "hud" }),
        new("brainpulse", "BrainPulse", "Tracks FPS stability, crashes and performance history for every session.", "performance", new[] { "tracking" }),
        new("brainreplay", "BrainReplay", "Replays a timeline of each session's stutters, ping spikes and disconnects.", "performance", new[] { "tracking" }),
        new("brainmacros", "BrainMacros", "Your own one-click shortcuts for launcher tasks, in Ctrl+K and the Jump List.", "flow", new[] { "profiles", "apps" }),
        new("brainautomation", "BrainAutomation", "Rules like 'when I join Bedwars, load Competitive' (BrainFlow).", "flow", new[] { "profiles", "apps", "system" }),
        new("brainnotes", "BrainNotes", "Notes on games and profiles, shown when you launch.", "extensions", Array.Empty<string>()),
        new("brainwidgets", "BrainWidgets", "Clocks, charts, system stats and now-playing on the Home screen.", "home", Array.Empty<string>()),
        new("braindev", "BrainDev", "Tools to build, scan, package and publish extensions.", "extensions", new[] { "files" }),
        new("braincompanion", "BrainCompanion", "A second-screen dashboard with live stats.", "extensions", new[] { "tracking" }),
        new("brainvault", "BrainVault", "Automatic daily backups of extensions, settings and profiles.", "tools", new[] { "files" }),
        new("braincommand", "BrainCommand", "Adds every extension's and macro's commands to the Ctrl+K menu.", "home", Array.Empty<string>()),
    };

    public static bool On(Settings s, string id) => !s.Platform.BuiltinsOff.Contains(id);

    static string Dir => Directory.CreateDirectory(Path.Combine(Paths.Data, "Extensions")).FullName;
    static readonly JsonSerializerOptions Opt = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    static readonly Regex IdRule = new("^[a-z0-9][a-z0-9\\-.]{2,47}$", RegexOptions.Compiled);

    public static List<(ExtensionManifest m, bool enabled)> Installed()
    {
        var list = new List<(ExtensionManifest, bool)>();
        foreach (var d in Directory.GetDirectories(Dir))
        {
            try
            {
                var m = JsonSerializer.Deserialize<ExtensionManifest>(File.ReadAllText(Path.Combine(d, "manifest.json")), Opt);
                if (m != null && Scan(m).Passed) list.Add((m, !File.Exists(Path.Combine(d, "disabled"))));
            }
            catch
            {
            }
        }
        return list;
    }

    public static IEnumerable<ExtensionManifest> InstalledEnabled(Settings s) => Installed().Where(x => x.enabled).Select(x => x.m);

    /// <summary>Automated safety scan. Extensions are data only, so this checks the whole package.</summary>
    public static ScanResult Scan(ExtensionManifest m)
    {
        var g = Grammar.Load();
        var used = g.PermissionsFor(m.Flows.SelectMany(f => f.Actions).Concat(m.Commands.SelectMany(c => c.Actions)));
        var known = g.Actions.Select(a => a.Type).Append("open.page").ToHashSet();
        var actions = m.Flows.SelectMany(f => f.Actions).Concat(m.Commands.SelectMany(c => c.Actions)).ToList();
        var checks = new List<(bool, string)>
        {
            (IdRule.IsMatch(m.Id), "Valid id (lowercase letters, digits, dots, dashes)"),
            (m.Name.Length is > 1 and <= 40 && m.Summary.Length <= 200, "Name and summary are a sensible length"),
            (Regex.IsMatch(m.Version, @"^\d+\.\d+\.\d+$"), "Version looks like 1.2.3"),
            (actions.All(a => known.Contains(a.Type)), "Only uses BrainStrap's built-in actions (no code)"),
            (actions.Where(a => a.Type is "app.start" or "app.close").All(a => a.Value != null && g.Apps.ContainsKey(a.Value)), "Only opens known apps (Discord, OBS, Spotify, Steam)"),
            (used.All(p => m.Permissions.Contains(p)), "Declares every permission it uses"),
            (m.Permissions.All(p => g.Permissions.ContainsKey(p)), "Only asks for permissions BrainStrap knows"),
            (m.Themes.All(c => UI.Themes.FromCode(c) != null), "Themes are valid colour codes"),
            (m.Flows.Count + m.Commands.Count + m.Widgets.Count + m.Themes.Count + m.Packs.Count + m.FlagPresets.Count <= 100, "Reasonable size"),
            (m.FlagPresets.Values.SelectMany(v => v.Keys).All(k => FlagCatalog.Load().Flags.Any(f => f.Id == k)), "Flag presets only use BrainFlags settings"),
        };
        return new ScanResult(checks.All(c => c.Item1), checks);
    }

    public static string Install(ExtensionManifest m)
    {
        var scan = Scan(m);
        if (!scan.Passed) throw new InvalidDataException("The safety scan failed: " + string.Join("; ", scan.Checks.Where(c => !c.ok).Select(c => c.check)));
        var d = Directory.CreateDirectory(Path.Combine(Dir, m.Id)).FullName;
        var file = Path.Combine(d, "manifest.json");
        if (File.Exists(file))
        {
            // version history: keep the previous version next to it
            var old = JsonSerializer.Deserialize<ExtensionManifest>(File.ReadAllText(file), Opt);
            if (old != null) File.Copy(file, Path.Combine(d, $"manifest-{old.Version}.json"), true);
        }
        File.WriteAllText(file, JsonSerializer.Serialize(m, Opt));
        Logger.Info($"extension installed: {m.Id} {m.Version}");
        return d;
    }

    public static void SetEnabled(string id, bool on)
    {
        var flag = Path.Combine(Dir, id, "disabled");
        if (on) { if (File.Exists(flag)) File.Delete(flag); }
        else File.WriteAllText(flag, "");
    }

    public static void Uninstall(string id)
    {
        var d = Path.Combine(Dir, id);
        if (Directory.Exists(d) && Path.GetFullPath(d).StartsWith(Dir, StringComparison.OrdinalIgnoreCase)) Directory.Delete(d, true);
    }

    public static List<string> OlderVersions(string id) =>
        Directory.Exists(Path.Combine(Dir, id))
            ? Directory.GetFiles(Path.Combine(Dir, id), "manifest-*.json").Select(f => Path.GetFileNameWithoutExtension(f)[9..]).ToList()
            : new List<string>();

    public static void RestoreVersion(string id, string version)
    {
        var d = Path.Combine(Dir, id);
        var f = Path.Combine(d, $"manifest-{version}.json");
        if (File.Exists(f)) Install(JsonSerializer.Deserialize<ExtensionManifest>(File.ReadAllText(f), Opt)!);
    }

    public static ExtensionManifest Parse(string json) =>
        JsonSerializer.Deserialize<ExtensionManifest>(json, Opt) ?? throw new InvalidDataException("Not an extension package.");

    public static string Serialize(ExtensionManifest m) => JsonSerializer.Serialize(m, Opt);
}

internal static class Store
{
    static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(10) };
    public const string FirstParty = "rblx.brain";

    /// <summary>The built-in shelf: first-party extensions, creative mod packs and flag presets. Third-party items come from the store index address.</summary>
    public static StoreIndex BuiltIn()
    {
        var idx = new StoreIndex();
        idx.Publishers.Add(new StorePublisher { Id = FirstParty, Name = "RBLX.BRAIN", Verified = true, Bio = "The team behind BrainStrap." });
        foreach (var b in Extensions.Builtins)
            idx.Items.Add(new StoreItem
            {
                Type = "builtin", Id = b.Id, Name = b.Name, Publisher = FirstParty, Summary = b.Summary, Permissions = b.Permissions.ToList(),
                Versions = new List<StoreVersion> { new() { Version = "2.0.0", Date = "2026-11", Notes = "Ships with BrainStrap 2." } }, Tags = new() { "built-in" },
            });
        foreach (var p in ModPacks.BuiltIn)
            idx.Items.Add(new StoreItem
            {
                Type = "pack", Id = p.Id, Name = p.Name, Publisher = FirstParty, Summary = p.Summary, Tags = p.Tags, Payload = p,
                Versions = new List<StoreVersion> { new() { Version = p.Version, Date = "2026-11", Notes = "First release." } },
            });
        foreach (var pr in FlagCatalog.Load().Presets)
            idx.Items.Add(new StoreItem
            {
                Type = "flags", Id = "preset." + pr.Id, Name = pr.Name + " flags", Publisher = FirstParty, Summary = pr.Desc, Payload = pr.Values, Tags = new() { "allowlisted" },
                Versions = new List<StoreVersion> { new() { Version = "2.0.0", Date = "2026-11", Notes = "Allowlisted flags only." } },
            });
        idx.Collections.Add(new StoreCollection { Name = "Competitive setup", Description = "Steady frames and a focused launcher.", Items = new() { "brainhud", "brainautomation", "pack.competitive-focus", "preset.performance" } });
        idx.Collections.Add(new StoreCollection { Name = "Laptop survival kit", Description = "Cooler, quieter, longer battery.", Items = new() { "brainautomation", "brainpulse", "pack.zen-minimal", "preset.balanced" } });
        idx.Collections.Add(new StoreCollection { Name = "Streamer starter", Description = "Overlay, notes and a stream-ready look.", Items = new() { "brainhud", "braincompanion", "pack.streamer-kit", "brainnotes" } });
        return idx;
    }

    public static async Task<StoreIndex> LoadAsync(Settings s)
    {
        var idx = BuiltIn();
        if (!Uri.TryCreate(s.Platform.StoreUrl, UriKind.Absolute, out var u) || u.Scheme != "https") return idx;
        try
        {
            var text = await Http.GetStringAsync(u);
            if (text.Length > 2 * 1024 * 1024) return idx;
            var remote = JsonSerializer.Deserialize<StoreIndex>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (remote == null) return idx;
            foreach (var p in remote.Publishers.Where(p => p.Id != FirstParty)) idx.Publishers.Add(p);
            foreach (var it in remote.Items.Where(i => i.Type is "extension" or "pack" or "flags" or "theme" && Regex.IsMatch(i.Id, "^[a-z0-9][a-z0-9\\-.]{2,47}$")))
                if (idx.Items.All(x => x.Id != it.Id)) idx.Items.Add(it);
            idx.Collections.AddRange(remote.Collections);
            idx.SubmitUrl = remote.SubmitUrl;
        }
        catch (Exception ex)
        {
            Logger.Warn("store: " + ex.GetType().Name);
        }
        return idx;
    }

    public static bool Verified(StoreIndex idx, StoreItem it) => idx.Publishers.FirstOrDefault(p => p.Id == it.Publisher)?.Verified == true;

    /// <summary>Downloads, checks the SHA-256 the store lists, scans, and returns the package. Nothing is installed until you approve the permissions.</summary>
    public static async Task<(ExtensionManifest? ext, ModPack? pack, Dictionary<string, string>? flags, string? theme, ScanResult? scan)> FetchAsync(StoreItem it)
    {
        if (it.Payload is ModPack mp) return (null, mp, null, null, null);
        if (it.Payload is Dictionary<string, string> fv) return (null, null, fv, null, null);
        var v = it.Versions.FirstOrDefault() ?? throw new InvalidDataException("This item has no downloadable version.");
        if (!Uri.TryCreate(v.Url, UriKind.Absolute, out var u) || u.Scheme != "https") throw new InvalidDataException("Downloads must use https.");
        var bytes = await Http.GetByteArrayAsync(u);
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Package is too large.");
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        if (!sha.Equals(v.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The download doesn't match the store's fingerprint (SHA-256). Not installed.");
        var json = Encoding.UTF8.GetString(bytes);
        switch (it.Type)
        {
            case "extension": var m = Extensions.Parse(json); return (m, null, null, null, Extensions.Scan(m));
            case "pack": return (null, JsonSerializer.Deserialize<ModPack>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }), null, null, null);
            case "flags": return (null, null, JsonSerializer.Deserialize<Dictionary<string, string>>(json), null, null);
            case "theme": return (null, null, null, json.Trim().Trim('"'), null);
            default: throw new InvalidDataException("Unknown item type.");
        }
    }
}

// ─────────────────────────────── BrainMods (launcher-side packs) ───────────────────────────────

internal sealed record PackConflict(string Setting, List<string> Packs, string Winner);

/// <summary>
/// Launcher customization packs: theme, accent, font, background, launch screen, HUD placement and Home layout.
/// They only change BrainStrap itself, never the Roblox client, and they are pure data (nothing executes).
/// </summary>
internal static class ModPacks
{
    static ModPack P(string id, string name, string summary, string[] tags, string? theme = null, string? accent = null, string? font = null, bool? minimal = null,
        string? bg = null, string? splash = null, string? hud = null, double? hudOp = null, string[]? widgets = null) => new()
    {
        Id = "pack." + id, Name = name, Summary = summary, Tags = tags.ToList(), ThemeCode = theme, Accent = accent, Font = font, Minimal = minimal,
        Background = bg, Splash = splash, HudCorner = hud, HudOpacity = hudOp, HomeWidgets = widgets?.ToList(),
    };

    static string Code(string id) => UI.Themes.ToCode(UI.Themes.BuiltIn.Concat(UI.Themes.Gallery).First(t => t.Id == id));

    public static readonly ModPack[] BuiltIn =
    {
        P("midnight-arcade", "Midnight Arcade", "Deep navy, electric blue and a pulsing launch screen. Feels like a cabinet booting up.", new[] { "dark", "neon" },
            Code("g-midnight"), "#6EA8FF", null, false, "silk", "pulse", "top-right", 0.85),
        P("competitive-focus", "Competitive Focus", "Pure black, no animations, FPS-only HUD in the corner. Nothing between you and the game.", new[] { "competitive", "minimal" },
            Code("obsidian"), null, null, false, "none", "none", "top-left", 0.95, new[] { "play", "session", "server", "status", "boost", "suggest", "tip" }),
        P("zen-minimal", "Zen Minimal", "Black and white, no descriptions, just the controls you use.", new[] { "minimal" }, null, null, null, true, "none", "minimal"),
        P("sunset-cinema", "Sunset Cinema", "Warm dusk tones, a slow silk background and a cinematic launch.", new[] { "warm", "cinematic" },
            Code("g-sunset"), "#FFB36B", null, false, "silk", "silk"),
        P("neon-terminal", "Neon Terminal", "Green-on-black terminal with a monospace font. Hacker energy, launcher-only.", new[] { "retro", "mono" },
            Code("g-terminal"), "#39FF6A", "Cascadia Mono", false, "none", "minimal"),
        P("arctic-daylight", "Arctic Daylight", "Bright, crisp and easy on a sunny desk.", new[] { "light" }, Code("g-arctic"), null, null, false, "none", "silk"),
        P("royal-gold", "Royal Gold", "Black and gold with a silk launch. Very expensive-looking.", new[] { "dark", "luxury" }, Code("g-gold"), "#F5C451", null, false, "silk", "silk"),
        P("streamer-kit", "Streamer Kit", "Viewer-friendly HUD at the bottom right and session stats first on Home.", new[] { "streaming" },
            Code("violet"), "#A98BFF", null, false, "silk", "pulse", "bottom-right", 0.8, new[] { "session", "server", "play", "status", "boost", "suggest", "tip" }),
        P("ember-forge", "Ember Forge", "Charcoal and molten orange with a pulsing launch.", new[] { "warm", "dark" }, Code("ember"), "#FF8A5B", null, false, "silk", "pulse"),
        P("forest-calm", "Forest Calm", "Soft greens and a quiet launch for long sessions.", new[] { "calm" }, Code("g-forest"), "#7BE495", null, false, "none", "minimal"),
    };

    public static IEnumerable<ModPack> All(Settings s) => BuiltIn.Concat(s.Platform.InstalledPacks)
        .Concat(Extensions.InstalledEnabled(s).SelectMany(e => e.Packs));

    static Dictionary<string, string> Changes(ModPack p)
    {
        var d = new Dictionary<string, string>();
        if (p.ThemeCode != null) d["Theme"] = UI.Themes.FromCode(p.ThemeCode)?.Name ?? "custom";
        if (p.Accent != null) d["Accent"] = p.Accent;
        if (p.Font != null) d["Font"] = p.Font;
        if (p.Minimal != null) d["Minimal mode"] = p.Minimal.Value ? "on" : "off";
        if (p.Background != null) d["Background"] = p.Background;
        if (p.Splash != null) d["Launch screen"] = p.Splash;
        if (p.HudCorner != null) d["HUD position"] = p.HudCorner;
        if (p.HudOpacity != null) d["HUD opacity"] = $"{p.HudOpacity:0.##}";
        if (p.HomeWidgets != null) d["Home layout"] = string.Join(", ", p.HomeWidgets.Take(3)) + "…";
        return d;
    }

    public static Dictionary<string, string> Describe(ModPack p) => Changes(p);

    /// <summary>Mod Conflict Detector: settings changed by more than one pack, and which pack wins (the later one).</summary>
    public static List<PackConflict> Conflicts(List<ModPack> packs)
    {
        var owners = new Dictionary<string, List<string>>();
        foreach (var p in packs)
            foreach (var k in Changes(p).Keys)
                (owners.TryGetValue(k, out var l) ? l : owners[k] = new List<string>()).Add(p.Name);
        return owners.Where(kv => kv.Value.Count > 1).Select(kv => new PackConflict(kv.Key, kv.Value, kv.Value[^1])).ToList();
    }

    /// <summary>Mod Compatibility AI: explains in plain words how a set of packs combines.</summary>
    public static string Explain(List<ModPack> packs)
    {
        if (packs.Count == 0) return "No packs selected.";
        if (packs.Count == 1) return $"{packs[0].Name} works on its own and changes: {string.Join(", ", Changes(packs[0]).Keys)}.";
        var c = Conflicts(packs);
        var parts = new List<string>();
        if (c.Count == 0) parts.Add("These packs don't touch the same settings, so they combine cleanly.");
        else
        {
            parts.Add($"{c.Count} overlap(s). Packs apply top to bottom, so the lower one wins:");
            parts.AddRange(c.Select(x => $"{x.Setting}: {string.Join(" vs ", x.Packs)} → {x.Winner}."));
        }
        if (packs.Any(p => p.Minimal == true) && packs.Any(p => p.ThemeCode != null))
            parts.Add("Minimal mode replaces every theme's colours with black and white, so the theme only shows when Minimal is off.");
        if (packs.Count(p => p.HomeWidgets != null) > 1) parts.Add("More than one pack rearranges Home; only the last layout is kept.");
        return string.Join(" ", parts);
    }

    /// <summary>Applies packs in order. The first time, the current look is saved so Mod Restore can bring it back.</summary>
    public static void Apply(Settings s, List<ModPack> packs, bool remember = true)
    {
        s.Platform.StudioBeforePacks ??= Clone(s.Studio);
        foreach (var p in packs)
        {
            if (p.ThemeCode != null && UI.Themes.FromCode(p.ThemeCode) is { } t)
            {
                var existing = s.Studio.CustomThemes.FirstOrDefault(x => x.Name == t.Name && x.Accent == t.Accent && x.Bg == t.Bg);
                if (existing == null) { t.Id = "pk-" + p.Id.Replace("pack.", ""); s.Studio.CustomThemes.RemoveAll(x => x.Id == t.Id); s.Studio.CustomThemes.Add(t); existing = t; }
                s.Theme = existing.Id;
            }
            if (p.Accent != null && UI.Themes.IsHex(p.Accent)) s.Accent = p.Accent;
            if (p.Font != null) s.Studio.Font = p.Font;
            if (p.Minimal != null) s.Studio.Minimal = p.Minimal.Value;
            if (p.Background is "none" or "silk") s.Studio.Background = p.Background;
            if (p.Splash is "silk" or "pulse" or "minimal" or "none") s.Studio.Splash = p.Splash;
            if (p.HudCorner != null) s.Hud.Corner = p.HudCorner;
            if (p.HudOpacity != null) s.Hud.Opacity = Math.Clamp(p.HudOpacity.Value, 0.3, 1);
            if (p.HomeWidgets != null) s.Studio.HomeWidgets = p.HomeWidgets.ToList();
        }
        if (remember) s.Platform.ActivePacks = packs.Select(p => p.Id).ToList();
        SettingsStore.Normalize(s);
        AppState.Save("Mod packs: " + string.Join(", ", packs.Select(p => p.Name)));
    }

    /// <summary>Mod Restore: back to how BrainStrap looked before any pack (or to BrainStrap's defaults).</summary>
    public static void Restore(Settings s)
    {
        var custom = s.Studio.CustomThemes.Where(t => !t.Id.StartsWith("pk-")).ToList();
        s.Studio = s.Platform.StudioBeforePacks != null ? Clone(s.Platform.StudioBeforePacks) : new StudioSettings();
        s.Studio.CustomThemes = custom;
        if (s.Theme.StartsWith("pk-")) s.Theme = "silk";
        s.Accent = null;
        s.Platform.ActivePacks.Clear();
        s.Platform.StudioBeforePacks = null;
        SettingsStore.Normalize(s);
        AppState.Save("Mod Restore: default look");
    }

    public static void InstallPack(Settings s, ModPack p)
    {
        var old = s.Platform.InstalledPacks.FirstOrDefault(x => x.Id == p.Id);
        if (old != null)
        {
            (s.Platform.PackHistory.TryGetValue(p.Id, out var h) ? h : s.Platform.PackHistory[p.Id] = new()).Add(old);
            s.Platform.InstalledPacks.Remove(old);
        }
        s.Platform.InstalledPacks.Add(p);
        AppState.Save($"Pack installed: {p.Name} {p.Version}");
    }

    static StudioSettings Clone(StudioSettings x) => JsonSerializer.Deserialize<StudioSettings>(JsonSerializer.Serialize(x))!;
}

// ─────────────────────────────── BrainVault + BrainNotes ───────────────────────────────

internal static class Vault
{
    public static string Dir => Directory.CreateDirectory(Path.Combine(Paths.Data, "Vault")).FullName;

    public static string BackupNow(Settings s, string why)
    {
        var file = Path.Combine(Dir, $"vault-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        Backup.Export(s, file);
        var extDir = Path.Combine(Paths.Data, "Extensions");
        if (Directory.Exists(extDir))
        {
            var node = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            node["extensions"] = new JsonArray(Extensions.Installed().Select(e => (JsonNode)JsonNode.Parse(Extensions.Serialize(e.m))!).ToArray());
            File.WriteAllText(file, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        foreach (var old in Directory.GetFiles(Dir, "vault-*.json").OrderByDescending(f => f).Skip(30)) File.Delete(old);
        Logger.Info("vault backup: " + why);
        return file;
    }

    /// <summary>Daily automatic backup (BrainVault), run when BrainStrap opens.</summary>
    public static void Daily(Settings s)
    {
        if (!Extensions.On(s, "brainvault")) return;
        var last = Directory.GetFiles(Dir, "vault-*.json").Select(File.GetLastWriteTime).DefaultIfEmpty(DateTime.MinValue).Max();
        if (DateTime.Now - last > TimeSpan.FromHours(24)) BackupNow(s, "daily");
    }

    public static List<FileInfo> List() => new DirectoryInfo(Dir).GetFiles("vault-*.json").OrderByDescending(f => f.LastWriteTime).ToList();

    public static Settings Restore(string file)
    {
        var restored = Backup.Import(file);
        var node = JsonNode.Parse(File.ReadAllText(file));
        if (node?["extensions"] is JsonArray arr)
            foreach (var e in arr)
                try { Extensions.Install(Extensions.Parse(e!.ToJsonString())); } catch { }
        return restored;
    }
}

internal static class Notes
{
    public static string? Game(Settings s, string? placeId) =>
        placeId != null && Extensions.On(s, "brainnotes") && s.Platform.Notes.TryGetValue("game:" + placeId, out var n) && n.Trim().Length > 0 ? n : null;

    public static string? Profile(Settings s, string name) =>
        Extensions.On(s, "brainnotes") && s.Platform.Notes.TryGetValue("profile:" + name, out var n) && n.Trim().Length > 0 ? n : null;

    public static void Set(Settings s, string key, string text)
    {
        if (text.Trim().Length == 0) s.Platform.Notes.Remove(key); else s.Platform.Notes[key] = text.Trim();
        AppState.Save("Note saved");
    }
}
