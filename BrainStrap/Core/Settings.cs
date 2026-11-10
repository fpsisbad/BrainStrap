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
}

internal sealed class StudioSettings
{
    public List<ThemeDef> CustomThemes { get; set; } = new();
    public string? Font { get; set; }
    public bool Minimal { get; set; }
    public string Background { get; set; } = "none";      // none | silk | image | video
    public string? BackgroundPath { get; set; }
    public double BackgroundDim { get; set; } = 0.6;
    public string Splash { get; set; } = "silk";           // silk | pulse | minimal | none
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
    public List<Companion> Companions { get; set; } = new();
}

internal sealed class Settings
{
    public int SchemaVersion { get; set; } = 2;

    // look
    public string Theme { get; set; } = "silk";
    public string? Accent { get; set; }

    // launching
    public bool CloseOnLaunch { get; set; } = true;
    public bool SafeMode { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public bool KeepRegistered { get; set; } = true;

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

    public string? OriginalHandlerCommand { get; set; }

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
