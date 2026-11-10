using System.Windows;
using System.Windows.Controls;
using BrainStrap.Core;

namespace BrainStrap.UI.Pages;

/// <summary>Launch: BrainStrap's own Roblox install, the Play button, launch options and Windows tuning.</summary>
internal static class LaunchPage
{
    public static FrameworkElement Build(MainWindow host)
    {
        var s = AppState.S;
        var page = Ui.Page("Launch", "BrainStrap is the whole launcher. It downloads Roblox straight from Roblox's own servers, keeps it up to date, applies your setup and starts the game. Nothing else has to be installed.", out var body);
        var registered = ProtocolHandler.IsRegisteredToUs();
        var roblox = RobloxInstall.Newest();

        // ── Roblox ──
        var latestText = Ui.Text("Checking…", 13, "Muted", FontWeights.SemiBold);
        var msg = Ui.Small("", 10);
        var main = Ui.AsyncPill(roblox == null ? "Install Roblox" : "Check for updates and install", async () =>
        {
            var problem = await Launcher.InstallNowAsync();
            if (problem != null) Ui.Msg(msg, "Couldn't finish: " + problem, "Bad");
            host.Refresh();
        });
        body.Children.Add(Ui.Card(
            Ui.Head("download", "Roblox", "The official, unmodified Roblox player. Every package is checked against Roblox's own checksum before it is used."),
            Ui.KV("Installed", roblox == null ? "Not installed yet" : $"{roblox.Name}  ·  {roblox.Stamp.ToLocalTime():d MMM yyyy}", roblox == null ? "Warn" : "Ink"),
            Ui.KV("Managed by", roblox == null ? "—" : RobloxInstall.IsOwn(roblox) ? "BrainStrap" : "Roblox's own launcher (BrainStrap takes over at the next update)", roblox != null && RobloxInstall.IsOwn(roblox) ? "Ok" : "Muted"),
            Ui.KV("Latest", latestText),
            Ui.KV("Disk space", Ui.Mb(RobloxDeploy.DiskUse()) + " in BrainStrap's Roblox folder"),
            Ui.Buttons(main, Ui.IconPill("play", "Play Roblox", HomePage.PlayRoblox, ghost: true, height: 38)),
            msg));
        _ = FillLatest();
        async Task FillLatest()
        {
            var latest = await RobloxInstall.LatestVersionAsync();
            if (latest == null) Ui.Msg(latestText, "Couldn't reach Roblox", "Warn");
            else if (RobloxInstall.Find(latest) != null) Ui.Msg(latestText, latest + "  ·  up to date", "Ok");
            else Ui.Msg(latestText, latest + "  ·  BrainStrap will download it the next time you play", "Warn");
        }

        // ── the Play button ──
        var setupMsg = Ui.Small("", 10);
        body.Children.Add(Ui.Card(
            Ui.Head("shield", "The Play button", "When BrainStrap is set up, pressing Play on roblox.com opens BrainStrap, which then starts Roblox with your setup."),
            Ui.Status(registered ? "ok" : "warn", registered ? "BrainStrap opens when you press Play" : "Roblox's own launcher opens when you press Play", ""),
            Ui.Buttons(
                Ui.Pill(registered && Installer.IsInstalled ? "Re-register BrainStrap" : "Set up BrainStrap", () =>
                {
                    Installer.Install(s);
                    host.Refresh();
                }),
                Ui.Ghost("Give the Play button back to Roblox", () =>
                {
                    ProtocolHandler.Restore(s);
                    SettingsStore.Save(s);
                    host.Refresh();
                })),
            setupMsg));

        // ── options ──
        body.Children.Add(Ui.Card(
            Ui.Head("rocket", "Launch options"),
            Ui.ToggleRow("BrainStrap installs and updates Roblox itself", "On: no other launcher is needed. Off: Roblox's own launcher is used for installs and updates, if it is on this PC.", s.ManageRoblox, v => { s.ManageRoblox = v; AppState.Save("Own Roblox installs " + (v ? "on" : "off")); }),
            Ui.ToggleRow("Check for Roblox updates before launching", "Asks Roblox's public version service which version is current, and updates first if yours is old.", s.CheckForUpdates, v => { s.CheckForUpdates = v; AppState.Save("Update check " + (v ? "on" : "off")); }),
            Ui.ToggleRow("Close BrainStrap when Roblox starts", "BrainStrap stays open in the background only if a feature needs it (session tracking, the HUD, Discord presence).", s.CloseOnLaunch, v => { s.CloseOnLaunch = v; AppState.Save("Close on launch " + (v ? "on" : "off")); }),
            Ui.ToggleRow("Safe mode", "Launch with no graphics flags and no mods. Use this to check whether a problem comes from your setup.", s.SafeMode, v => { s.SafeMode = v; AppState.Save("Safe mode " + (v ? "on" : "off")); host.UpdateStatus(); }),
            Ui.ToggleRow("Keep the Play button", "If Roblox's own updater ever runs and takes the Play button back, BrainStrap registers itself again.", s.KeepRegistered, v => { s.KeepRegistered = v; AppState.Save("Keep registered " + (v ? "on" : "off")); })));

        // ── Windows tuning ──
        body.Children.Add(Ui.Card(
            Ui.Head("cpu", "Windows tuning for Roblox", "Windows' own per-app settings, switched for you. They are the same switches you'd find in Windows Settings, and turning them off here puts them back."),
            Ui.ToggleRow("Use the high-performance graphics card", "For laptops with two graphics chips: tells Windows to run Roblox on the fast one (Settings → Display → Graphics).", s.Tuning.HighPerformanceGpu, v => { s.Tuning.HighPerformanceGpu = v; Tuning.ApplyAll(s); AppState.Save("High-performance GPU " + (v ? "on" : "off")); }),
            Ui.ToggleRow("Turn off fullscreen optimisations", "The \"Disable fullscreen optimisations\" tick box on Roblox's Compatibility tab. Can smooth out frame pacing on some PCs.", s.Tuning.NoFullscreenOptimizations, v => { s.Tuning.NoFullscreenOptimizations = v; Tuning.ApplyAll(s); AppState.Save("Fullscreen optimisations " + (v ? "off" : "on")); })));

        // ── housekeeping ──
        var pruneMsg = Ui.Small("", 10);
        body.Children.Add(Ui.Card(
            Ui.Head("sliders", "Housekeeping", "Old copies of Roblox are removed after each update. This clears anything left over, including copies from Roblox's own launcher."),
            Ui.Buttons(Ui.Ghost("Remove old Roblox versions", () =>
            {
                var all = RobloxInstall.List();
                if (all.Count < 2) { Ui.Msg(pruneMsg, "Only one Roblox version is installed. Nothing to remove."); return; }
                if (MessageBox.Show(host, $"Delete {all.Count - 1} old Roblox version folder(s) and keep {all[0].Name}?", "BrainStrap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                var (removed, problem) = RobloxInstall.PruneOld();
                Ui.Msg(pruneMsg, problem ?? $"Removed {removed} old version(s).", problem == null ? "Ok" : "Warn");
            })),
            pruneMsg));
        return page;
    }
}
