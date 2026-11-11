using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BrainStrap.Core.Platform;

namespace BrainStrap.Core;

/// <summary>One set of graphics choices. Values are keyed by catalog id ("fps" -> "144", "postfx" -> "true").</summary>
internal sealed class FlagProfile
{
    public Dictionary<string, string> Values { get; set; } = new();

    /// <summary>Advanced: raw FLAGNAME -> value pairs the user typed in. Names are validated before use.</summary>
    public Dictionary<string, string> Custom { get; set; } = new();

    /// <summary>Optional BrainStrap theme to switch to while this profile is active ("profile themes").</summary>
    public string? Theme { get; set; }

    /// <summary>Optional BrainMods pack applied when this profile is selected ("mod profiles").</summary>
    public string? ModPack { get; set; }
}

internal sealed class DiscordSettings
{
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = "";
    public bool ShowGameName { get; set; } = true;
    public string LargeImageKey { get; set; } = "";
}

internal sealed class AiSettings
{
    /// <summary>Off by default: BrainAI runs fully on this PC. Cloud mode uses the user's own API key.</summary>
    public bool CloudEnabled { get; set; }
    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>The API key, encrypted with Windows DPAPI for this Windows user. Never written in plain text.</summary>
    public string? KeyProtected { get; set; }
}

internal sealed class PerfSettings
{
    /// <summary>Record playtime, FPS and ping for each session (stays on this PC).</summary>
    public bool TrackSessions { get; set; } = true;
    public string? PresentMonPath { get; set; }

    /// <summary>Extra PresentMon arguments, for a PresentMon version with different options. Empty = BrainStrap's defaults.</summary>
    public string PresentMonExtraArgs { get; set; } = "";
    public bool MeasureFpsInSessions { get; set; } = true;
}

internal sealed class HudSettings
{
    public bool Enabled { get; set; }
    public string Corner { get; set; } = "top-left";   // top-left | top-right | bottom-left | bottom-right
    public double Opacity { get; set; } = 0.9;
    public bool ShowFps { get; set; } = true;
    public bool ShowPing { get; set; } = true;
    public bool ShowHardware { get; set; } = true;
    public bool ShowClock { get; set; } = true;
}

internal sealed class FavoriteServer
{
    public string Name { get; set; } = "";
    public string PlaceId { get; set; } = "";
    public string? JobId { get; set; }

    /// <summary>A saved private-server link, DPAPI-encrypted (anyone with the link can join that server).</summary>
    public string? PrivateLinkProtected { get; set; }
    public DateTime Added { get; set; } = DateTime.UtcNow;
}

internal sealed class ServerSettings
{
    /// <summary>Look up the Roblox server's location (sends the server's IP address to ipinfo.io). Off by default.</summary>
    public bool RegionLookup { get; set; }

    /// <summary>Ping the game server during a session to track latency.</summary>
    public bool TrackPing { get; set; } = true;
    public List<FavoriteServer> Favorites { get; set; } = new();
}

internal sealed class ThemeDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Dark { get; set; } = true;
    public string Bg { get; set; } = "#121317";
    public string Panel { get; set; } = "#18191D";
    public string Panel2 { get; set; } = "#1D1E23";
    public string Ink { get; set; } = "#F2F3F5";
    public string Muted { get; set; } = "#A1A3AA";
    public string Dim { get; set; } = "#6E7077";
    public string Pill { get; set; } = "#26272C";
    public string PillHover { get; set; } = "#303137";
    public string Accent { get; set; } = "#F2F3F5";
    /// <summary>The second colour of the accent gradient (optional; worked out from the accent when missing).</summary>
    public string? Accent2 { get; set; }
}

internal sealed class StudioSettings
{
    public List<ThemeDef> CustomThemes { get; set; } = new();
    public string? Font { get; set; }
    public bool Minimal { get; set; }
    public string Background { get; set; } = "silk";      // silk (live) | none | image | video
    public string? BackgroundPath { get; set; }
    public double BackgroundDim { get; set; } = 0.6;
    public string Splash { get; set; } = "silk";           // silk | pulse | minimal | none
    /// <summary>How strong the live silk backdrop is behind the app (0.2 to 1).</summary>
    public double SilkStrength { get; set; } = 1;
    public string? SplashImage { get; set; }
    public string? LaunchSound { get; set; }
    public double LaunchSoundVolume { get; set; } = 0.6;
    public List<string> HomeWidgets { get; set; } = new() { "play", "status", "boost", "session", "server", "suggest", "tip" };
    public List<string> HiddenWidgets { get; set; } = new();

    /// <summary>Optional https address of a JSON list of community themes (see README). Empty = built-in gallery only.</summary>
    public string GalleryUrl { get; set; } = "";
}

internal sealed class Companion
{
    public string Path { get; set; } = "";
    public string Args { get; set; } = "";
    public bool CloseAfter { get; set; } = true;
}

internal sealed class LifeSettings
{
    public bool GameModePlus { get; set; }
    public bool BatteryAware { get; set; }
    public string? BatteryProfile { get; set; }
    public bool JumpList { get; set; } = true;
    /// <summary>Remind me to take a break every this many minutes of play (0 = off).</summary>
    public int BreakMinutes { get; set; }
    public List<Companion> Companions { get; set; } = new();
}

/// <summary>BrainSight: a crosshair drawn by BrainStrap in its own click-through window over the screen centre.</summary>
internal sealed class CrosshairSettings
{
    public bool Enabled { get; set; }
    public string Shape { get; set; } = "cross";    // cross | dot | circle | tee
    public int Size { get; set; } = 22;
    public int Thickness { get; set; } = 2;
    public int Gap { get; set; } = 4;
    public string Color { get; set; } = "#FFFFFF";
    public double Opacity { get; set; } = 0.9;
    public bool Outline { get; set; } = true;
}

/// <summary>Windows' own per-app settings for Roblox (Settings → Display → Graphics, and the Compatibility tab).</summary>
/// <summary>An app in BrainOverlay.</summary>
internal sealed class OverlayApp
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Icon { get; set; } = "globe";
    public string Caption { get; set; } = "WEB";
    public bool Enabled { get; set; } = true;
    public bool Open { get; set; }
    public bool Pinned { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }

    public OverlayApp Copy() => (OverlayApp)MemberwiseClone();

    public static readonly OverlayApp[] Builtins =
    {
        new() { Id = "youtube", Name = "YouTube", Url = "https://www.youtube.com/", Icon = "play", Caption = "VIDEOS" },
        new() { Id = "spotify", Name = "Spotify", Url = "https://open.spotify.com/", Icon = "music", Caption = "MUSIC" },
        new() { Id = "discord", Name = "Discord", Url = "https://discord.com/app", Icon = "chat", Caption = "FRIENDS" },
        new() { Id = "twitch", Name = "Twitch", Url = "https://www.twitch.tv/", Icon = "broadcast", Caption = "STREAMS" },
    };
}

internal sealed class OverlaySettings
{
    public bool Enabled { get; set; } = true;
    public string Hotkey { get; set; } = "Ctrl+Shift+B";
    public List<OverlayApp> Apps { get; set; } = new();
}

internal sealed class TuningSettings
{
    public bool HighPerformanceGpu { get; set; }
    public bool NoFullscreenOptimizations { get; set; }
}

internal sealed class Settings
{
    public int SchemaVersion { get; set; } = 2;

    // look
    public string Theme { get; set; } = "cinema";
    public string? Accent { get; set; }

    // launching
    public bool CloseOnLaunch { get; set; } = true;
    public bool SafeMode { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public bool KeepRegistered { get; set; } = true;
    /// <summary>BrainStrap downloads and updates Roblox itself, straight from Roblox's servers. No other launcher needed.</summary>
    public bool ManageRoblox { get; set; } = true;
    /// <summary>Look for a newer BrainStrap on GitHub once a day.</summary>
    public bool AutoUpdate { get; set; } = true;
    public DateTime LastUpdateCheck { get; set; }
    /// <summary>Which design generation these settings were last opened with (one-time upgrades).</summary>
    public int Look { get; set; }

    // graphics
    public string FlagFormat { get; set; } = "native";
    public string ActiveProfile { get; set; } = "Default";
    public Dictionary<string, FlagProfile> Profiles { get; set; } = new() { ["Default"] = new FlagProfile() };
    public Dictionary<string, string> GameProfiles { get; set; } = new();

    // features
    public bool ModsEnabled { get; set; } = true;
    public DiscordSettings Discord { get; set; } = new();
    public AiSettings Ai { get; set; } = new();
    public PerfSettings Perf { get; set; } = new();
    public HudSettings Hud { get; set; } = new();
    public ServerSettings Servers { get; set; } = new();
    public StudioSettings Studio { get; set; } = new();
    public LifeSettings Life { get; set; } = new();
    public PlatformSettings Platform { get; set; } = new();
    public CrosshairSettings Crosshair { get; set; } = new();
    public TuningSettings Tuning { get; set; } = new();
    public OverlaySettings Overlay { get; set; } = new();

    public string? OriginalHandlerCommand { get; set; }
    public string? OriginalLinkCommand { get; set; }

    [JsonIgnore]
    public FlagProfile Current => Profiles.TryGetValue(ActiveProfile, out var p) ? p : Profiles["Default"];

    public FlagProfile ProfileFor(string? placeId)
    {
        if (placeId != null && GameProfiles.TryGetValue(placeId, out var name) && Profiles.TryGetValue(name, out var p))
            return p;
        return Current;
    }

    public string ProfileNameFor(string? placeId) =>
        placeId != null && GameProfiles.TryGetValue(placeId, out var name) && Profiles.ContainsKey(name) ? name : ActiveProfile;
}

/// <summary>The one Settings instance shared by every page, the launcher and the session.</summary>
internal static class AppState
{
    static Settings? s;
    public static Settings S
    {
        get => s ??= SettingsStore.Load();
        set => s = value;
    }

    /// <summary>Saves and records the change in the settings timeline (for rollback).</summary>
    public static void Save(string? why = null)
    {
        SettingsStore.Save(S);
        Timeline.Record(S, why);
    }
}

internal static class SettingsStore
{
    public static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static Settings Load()
    {
        Settings s;
        try
        {
            s = File.Exists(Paths.SettingsFile)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(Paths.SettingsFile), Opt) ?? new Settings()
                : new Settings();
        }
        catch (Exception ex)
        {
            Logger.Error("settings file unreadable, starting from defaults", ex);
            try { File.Copy(Paths.SettingsFile, Paths.SettingsFile + ".bad", true); } catch { }
            s = new Settings();
        }
        Normalize(s);
        return s;
    }

    public static Settings FromJson(string json)
    {
        var s = JsonSerializer.Deserialize<Settings>(json, Opt) ?? new Settings();
        Normalize(s);
        return s;
    }

    public static string ToJson(Settings s) => JsonSerializer.Serialize(s, Opt);

    public static void Save(Settings s)
    {
        if (Demo.On) return;    // the screenshot mode never writes anything
        try
        {
            var tmp = Paths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, ToJson(s));
            File.Move(tmp, Paths.SettingsFile, true);
        }
        catch (Exception ex)
        {
            Logger.Error("could not save settings", ex);
        }
    }

    public static void Normalize(Settings s)
    {
        s.Profiles ??= new();
        s.GameProfiles ??= new();
        s.Discord ??= new();
        s.Ai ??= new();
        s.Perf ??= new();
        s.Hud ??= new();
        s.Servers ??= new();
        s.Servers.Favorites ??= new();
        s.Studio ??= new();
        s.Studio.CustomThemes ??= new();
        s.Studio.HomeWidgets ??= new();
        s.Studio.HiddenWidgets ??= new();
        s.Life ??= new();
        s.Platform ??= new();
        s.Platform.BuiltinsOff ??= new();
        s.Platform.Flows ??= new();
        s.Platform.Notes ??= new();
        s.Platform.Following ??= new();
        s.Platform.ActivePacks ??= new();
        s.Platform.InstalledPacks ??= new();
        s.Platform.PackHistory ??= new();
        s.Platform.Webhooks ??= new();
        s.Platform.FlagPresets ??= new();
        s.Platform.KnownGood ??= new();
        foreach (var w in new[] { "clock", "stats", "fpschart", "spotify", "notes" })
            if (!s.Studio.HomeWidgets.Contains(w)) { s.Studio.HomeWidgets.Add(w); if (w is "clock" or "spotify") s.Studio.HiddenWidgets.Add(w); }
        s.Life.Companions ??= new();
        s.Crosshair ??= new();
        s.Tuning ??= new();
        s.Overlay ??= new();
        s.Overlay.Apps ??= new();
        s.Overlay.Apps.RemoveAll(a => a == null || string.IsNullOrWhiteSpace(a.Id) || !a.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        if (!UI.OverlayHost.Hotkeys.Contains(s.Overlay.Hotkey)) s.Overlay.Hotkey = "Ctrl+Shift+B";
        if (s.Look < 5)
        {
            // the 5th-generation look: Cinema is the new default, unless a theme was picked on purpose
            if ((s.Theme is "atelier" or "aurora" or "silk") && s.Accent == null) s.Theme = "cinema";
            s.Look = 5;
        }
        if (s.Look < 3)
        {
            // the 3rd-generation look: the live silk backdrop is on unless a picture or video was chosen
            if (s.Studio.Background == "none") s.Studio.Background = "silk";
            s.Look = 3;
        }
        if (s.Look < 4)
        {
            // the 4th-generation look: Aurora is the new default, unless a theme or accent was picked on purpose
            if (s.Theme == "silk" && s.Accent == null) s.Theme = "atelier";
            s.Look = 4;
        }
        s.Life.BreakMinutes = Math.Clamp(s.Life.BreakMinutes, 0, 600);
        s.Studio.SilkStrength = Math.Clamp(s.Studio.SilkStrength, 0.2, 1);
        s.Crosshair.Size = Math.Clamp(s.Crosshair.Size, 4, 120);
        s.Crosshair.Thickness = Math.Clamp(s.Crosshair.Thickness, 1, 12);
        s.Crosshair.Gap = Math.Clamp(s.Crosshair.Gap, 0, 60);
        s.Crosshair.Opacity = Math.Clamp(s.Crosshair.Opacity, 0.2, 1);
        if (!s.Profiles.ContainsKey("Default")) s.Profiles["Default"] = new FlagProfile();
        foreach (var k in s.Profiles.Keys.ToList())
        {
            s.Profiles[k] ??= new FlagProfile();
            s.Profiles[k].Values ??= new();
            s.Profiles[k].Custom ??= new();
        }
        if (!s.Profiles.ContainsKey(s.ActiveProfile)) s.ActiveProfile = "Default";
        if (s.FlagFormat != "string") s.FlagFormat = "native";
        // widgets added in later versions show up for existing users too
        foreach (var w in new[] { "play", "status", "boost", "session", "server", "suggest", "tip" })
            if (!s.Studio.HomeWidgets.Contains(w)) s.Studio.HomeWidgets.Add(w);
        s.Hud.Opacity = Math.Clamp(s.Hud.Opacity, 0.3, 1);
        s.Studio.BackgroundDim = Math.Clamp(s.Studio.BackgroundDim, 0, 0.95);
        s.Studio.LaunchSoundVolume = Math.Clamp(s.Studio.LaunchSoundVolume, 0, 1);
    }
}

/// <summary>Windows DPAPI: secrets are encrypted so only this Windows user on this PC can read them.</summary>
internal static class Secret
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BrainStrap.v1");

    public static string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));

    public static string? Unprotect(string? protectedText)
    {
        if (string.IsNullOrEmpty(protectedText)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedText), Entropy, DataProtectionScope.CurrentUser)); }
        catch { return null; }
    }
}
