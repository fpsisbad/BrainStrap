using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using BrainStrap.Core.BrainAI;
using BrainStrap.Core.Perf;
using BrainStrap.Core.Platform;
using BrainStrap.UI;

namespace BrainStrap.Core;

internal sealed record Prepared(InstalledVersion Version, string Profile, int Flags, int Mods, string? Tip, LaunchOverrides Overrides);

/// <summary>
/// What happens when you press Play on roblox.com and Windows hands BrainStrap a roblox-player: link.
///   1. Understand the link. Anything unusual goes straight to Roblox's own launcher, untouched.
///   2. If Roblox is missing or out of date, BrainStrap downloads it itself from Roblox's servers (RobloxDeploy).
///      Only if that fails does Roblox's own launcher get involved, when there is one.
///   3. Otherwise: launch screen, pick the right profile, apply flags and mods, start RobloxPlayerBeta.exe, run the session.
/// BrainStrap never injects into, hooks, patches or reads the memory of the Roblox process.
/// </summary>
internal static class Launcher
{
    /// <summary>Picks the profile (game → battery → active), applies mods and flags. Used by Play and by server joins.</summary>
    public static Prepared Prepare(Settings s, string? placeId, InstalledVersion? use = null)
    {
        var installed = use ?? RobloxInstall.Newest() ?? throw new InvalidOperationException("Roblox is not installed.");
        var profileName = s.ProfileNameFor(placeId);
        string? tip = null;
        var ov = Flow.Fire(s, "launch", placeId);                       // BrainFlow: "when Roblox launches / when I join X"
        if (Power.Status().onBattery) { var b = Flow.Fire(s, "battery", placeId); ov.Profile ??= b.Profile; ov.Ran.AddRange(b.Ran); }
        if (ov.Profile != null && s.Profiles.ContainsKey(ov.Profile)) profileName = ov.Profile;

        var hasGameProfile = placeId != null && s.GameProfiles.ContainsKey(placeId);
        if (ov.Profile == null && !hasGameProfile && s.Life.BatteryAware && Power.Status().onBattery && s.Life.BatteryProfile is { } bp && s.Profiles.ContainsKey(bp))
        {
            profileName = bp;
            tip = "On battery: using your " + bp + " profile.";
        }
        if (!hasGameProfile && placeId != null && tip == null)
        {
            // Profile Auto Selection: suggest from what happened last time in this game
            var last = History.Sessions().FirstOrDefault(r => r.PlaceId == placeId && r.Fps != null);
            if (last?.Fps is { } f && f.AvgFps < 45)
                tip = $"Last time here you averaged {f.AvgFps:0} FPS. BrainAI can build a profile for this game.";
        }

        if (Notes.Game(s, placeId) is { } note) tip = "Note: " + note;
        else if (ov.Ran.Count > 0) tip = "Automation: " + string.Join(", ", ov.Ran);
        var safe = ov.Safe ?? s.SafeMode;
        var profile = s.Profiles.TryGetValue(profileName, out var p) ? p : s.Current;
        var flags = safe ? new SortedDictionary<string, object>() : FlagCatalog.Resolve(FlagCatalog.Load(), profile, s.FlagFormat, s.Platform.WriteIgnoredFlags);
        var mods = 0;
        try
        {
            var (applied, _, _) = ModManager.Reconcile(installed.Folder, s.ModsEnabled && !safe);
            mods = applied + ModManager.Scan().Count(m => m.Allowed);
            FlagWriter.Apply(installed.Folder, flags);
        }
        catch (Exception ex)
        {
            Logger.Error("could not apply flags or mods; launching without them", ex);
        }
        return new Prepared(installed, profileName, flags.Count, s.ModsEnabled && !safe ? mods : 0, tip, ov);
    }

    /// <summary>The Play button on roblox.com (a roblox-player: link).</summary>
    public static async Task<int> RunAsync(string uri)
    {
        var req = LaunchRequest.Parse(uri);
        if (req == null)
        {
            Logger.Info("not a plain play link; handing it to Roblox's launcher");
            return await HandOff(uri, AppState.S, "BrainStrap handed this link to Roblox's own launcher.");
        }
        return await LaunchAsync(req.BuildArgs(), req.PlaceId, uri);
    }

    /// <summary>"Play Roblox" inside BrainStrap: opens the Roblox app itself, with your setup applied.</summary>
    public static Task<int> RunAppAsync() => LaunchAsync(new List<string> { "--app" }, null, null);

    /// <summary>Install or update Roblox now (the button on the Launch page). Returns a problem to show, or null.</summary>
    public static async Task<string?> InstallNowAsync()
    {
        var s = AppState.S;
        var splash = new LaunchSplash(s);
        splash.Show();
        try
        {
            splash.Status("Checking Roblox…");
            var latest = await RobloxInstall.LatestVersionAsync();
            if (latest == null) return "Couldn't reach Roblox to see which version is current.";
            await RobloxDeploy.InstallAsync(latest, splash.Reporter());
            RobloxDeploy.Prune(latest);
            await splash.FinishAsync("Roblox is ready.");
            splash = null;
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error("install failed", ex);
            return ex.Message;
        }
        finally { splash?.Close(); }
    }

    /// <summary>Makes sure a current Roblox is installed. Returns the copy to start, or null when Roblox's own launcher has to take over.</summary>
    static async Task<InstalledVersion?> EnsureRobloxAsync(Settings s, LaunchSplash? splash)
    {
        var installed = RobloxInstall.Newest();
        var latest = s.CheckForUpdates || installed == null ? await RobloxInstall.LatestVersionAsync() : null;
        if (latest != null && RobloxInstall.Find(latest) is { } exact) installed = exact;
        var current = installed != null && (latest == null || installed.Name.Equals(latest, StringComparison.OrdinalIgnoreCase));
        if (current) return installed;

        if (s.ManageRoblox && latest != null)
        {
            try
            {
                splash?.Status(installed == null ? "Installing Roblox…" : "Updating Roblox…");
                var fresh = await RobloxDeploy.InstallAsync(latest, splash?.Reporter());
                RobloxDeploy.Prune(latest);
                return fresh;
            }
            catch (Exception ex)
            {
                Logger.Error("BrainStrap's own Roblox install failed; falling back", ex);
                splash?.Status("Couldn't update Roblox directly, trying another way…");
                await Task.Delay(900);
            }
        }
        // fall back: Roblox's own launcher if it is on this PC, otherwise the copy we have (Roblox will say if it is too old)
        return RobloxInstall.FindOfficialLauncher(s) != null ? null : installed;
    }

    static async Task<int> LaunchAsync(List<string> args, string? placeId, string? uri)
    {
        var s = AppState.S;
        LaunchSplash? splash = null;
        try
        {
            if (s.Studio.Splash != "none")
            {
                Themes.Apply(s);
                splash = new LaunchSplash(s);
                splash.Show();
                splash.Status("Checking Roblox…");
            }

            var before = RobloxInstall.Newest();
            var ready = await EnsureRobloxAsync(s, splash);
            if (ready == null)
            {
                if (uri == null || RobloxInstall.FindOfficialLauncher(s) == null)
                {
                    splash?.Close();
                    MessageBox.Show("BrainStrap couldn't download Roblox just now.\n\nCheck your internet connection and try again. Details are in the log folder (About → Open logs).",
                        "BrainStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return 2;
                }
                splash?.Status(before == null ? "Installing Roblox with Roblox's own installer…" : "Updating Roblox with Roblox's own updater…");
                await Task.Delay(400);
                splash?.Close();
                splash = null;
                return await HandOff(uri, s, null);
            }
            _ = UpdateInspector.RefreshAsync();
            Tuning.Apply(s, ready.PlayerExe);

            splash?.Status(s.SafeMode ? "Safe mode: no flags or mods" : "Applying your setup…");
            splash?.Progress(1);
            var prep = Prepare(s, placeId, ready);
            splash?.Detail($"{prep.Profile} profile · {prep.Flags} flag(s){(prep.Mods > 0 ? $" · {prep.Mods} mod file(s)" : "")}", prep.Tip);

            var psi = new ProcessStartInfo(prep.Version.PlayerExe) { WorkingDirectory = prep.Version.Folder, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Logger.Info($"starting {prep.Version.Name} (place {placeId ?? "?"}, {prep.Flags} flag(s){(s.SafeMode ? ", safe mode" : "")})");
            splash?.Status("Starting Roblox…");
            var proc = Process.Start(psi);
            if (proc == null) { splash?.Close(); return uri != null ? await HandOff(uri, s, null) : 1; }
            PlayLaunchSound(s);

            if (splash != null)
            {
                // keep the launch screen up until Roblox's window appears (max 20 s)
                for (var i = 0; i < 80; i++)
                {
                    try { proc.Refresh(); if (proc.HasExited || proc.MainWindowHandle != IntPtr.Zero) break; } catch { break; }
                    await Task.Delay(250);
                }
                await splash.FinishAsync();
                splash = null;
            }

            if (Session.Wanted(s) || prep.Overrides.Hud == true || prep.Overrides.Track) await Session.RunAsync(proc, placeId, prep.Profile, DateTime.UtcNow, prep.Overrides);
            else if (!s.CloseOnLaunch) await proc.WaitForExitAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Logger.Error("launch failed", ex);
            splash?.Close();
            try { if (uri != null) return await HandOff(uri, s, null); }
            catch (Exception ex2) { Logger.Error("fallback failed too", ex2); }
            MessageBox.Show("BrainStrap could not start Roblox.\n\n" + ex.Message + "\n\nDetails are in the log folder (About → Open logs).",
                "BrainStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return 1;
        }
    }

    /// <summary>Custom Launch Sounds: plays the user's own sound file when Roblox starts.</summary>
    public static void PlayLaunchSound(Settings s)
    {
        var path = s.Studio.LaunchSound;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            var player = new MediaPlayer { Volume = s.Studio.LaunchSoundVolume };
            player.MediaEnded += (_, _) => player.Close();
            player.Open(new Uri(path));
            player.Play();
        }
        catch (Exception ex)
        {
            Logger.Warn("launch sound: " + ex.Message);
        }
    }

    static async Task<int> HandOff(string uri, Settings s, string? message)
    {
        var official = RobloxInstall.FindOfficialLauncher(s);
        if (official == null)
        {
            MessageBox.Show("This kind of Roblox link needs Roblox's own launcher, which isn't on this PC.\n\nPress Play on a game instead, or install Roblox once from roblox.com/download.",
                "BrainStrap", MessageBoxButton.OK, MessageBoxImage.Information);
            return 2;
        }

        if (message != null) Logger.Info(message);
        var psi = new ProcessStartInfo(official) { UseShellExecute = false };
        psi.ArgumentList.Add(uri);
        using var p = Process.Start(psi);
        if (p != null)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            try { await p.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { }
        }

        if (s.KeepRegistered && Installer.IsInstalled)
        {
            try
            {
                ProtocolHandler.Register(Installer.InstalledExe, s);
                SettingsStore.Save(s);
            }
            catch (Exception ex)
            {
                Logger.Warn("could not re-register: " + ex.Message);
            }
        }
        return 0;
    }
}
