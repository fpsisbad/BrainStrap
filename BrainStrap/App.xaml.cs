using System.Windows;
using BrainStrap.Core;
using BrainStrap.Core.Servers;
using BrainStrap.UI;

namespace BrainStrap;

public partial class App : Application
{
    static Mutex? uiLock;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, a) =>
        {
            Logger.Error("unhandled UI error", a.Exception);
            a.Handled = true;
        };
        Logger.Prune();
        var args = e.Args;
        string? Arg(string name, int offset = 1)
        {
            var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + offset < args.Length ? args[i + offset] : null;
        }

        try
        {
            var s = AppState.S;
            Themes.Apply(s);

            // 0) build tool: save a picture of every page, then quit
            if (Arg("--shots") is { } shotDir)
            {
                await UI.Shots.RunAsync(shotDir);
                Shutdown(0);
                return;
            }

            // 1) the Play button on roblox.com: a roblox-player: link -> launch screen, then Roblox
            var link = args.FirstOrDefault(a => a.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase));
            if (link != null)
            {
                Shutdown(await Launcher.RunAsync(link));
                return;
            }

            // 1a) "Play Roblox" from inside BrainStrap, the Start menu or the Jump List
            if (args.Any(a => a.Equals("--roblox", StringComparison.OrdinalIgnoreCase)))
            {
                Shutdown(await Launcher.RunAppAsync());
                return;
            }

            // 1b) a brainstrap:// link (RBLX.BRAIN website, Stream Deck, shortcuts). Every change asks first.
            var bs = args.FirstOrDefault(a => a.StartsWith("brainstrap:", StringComparison.OrdinalIgnoreCase));
            string? linkPage = null;
            if (bs != null)
            {
                linkPage = BrainStrap.Core.Platform.Connect.HandleUri(s, bs);
                if (linkPage == null) { Shutdown(0); return; }
            }

            // 2) a game started from a BrainStrap join button: follow it as a session
            if (args.Any(a => a.Equals("--watch", StringComparison.OrdinalIgnoreCase)))
            {
                var place = Arg("--watch");
                await Session.WatchAsync(RobloxApi.IsId(place) ? place : null);
                Shutdown(0);
                return;
            }

            // 3) the taskbar Jump List: --play <placeId> [jobId]
            if (Arg("--play") is { } playId && RobloxApi.IsId(playId))
            {
                var job = Arg("--play", 2);
                try { Launcher.Prepare(s, playId); } catch (Exception ex) { Logger.Warn("prepare: " + ex.Message); }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(JoinLinks.App(playId, RobloxApi.IsJobId(job) ? job : null)) { UseShellExecute = true });
                if (Session.Wanted(s)) await Session.WatchAsync(playId);
                Shutdown(0);
                return;
            }

            // 4) "Apps & features" uninstall
            if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                Installer.Uninstall(s);
                MessageBox.Show("Roblox's own launcher is back, and mods and graphics flags were removed from the Roblox folder.\n\nYour BrainStrap settings and mods are still in %LOCALAPPDATA%\\BrainStrap; delete that folder if you want them gone too.",
                    "BrainStrap", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(0);
                return;
            }

            // 5) the BrainStrap window (only one at a time)
            uiLock = new Mutex(true, "BrainStrap.SettingsWindow", out var first);
            if (!first)
            {
                Shutdown(0);
                return;
            }

            try { BrainStrap.Core.Platform.Vault.Daily(s); } catch (Exception ex) { Logger.Warn("vault: " + ex.Message); }
            var win = new MainWindow(linkPage ?? Arg("--page"));
            MainWindow = win;
            win.Closed += (_, _) => Shutdown(0);
            win.Show();
        }
        catch (Exception ex)
        {
            Logger.Error("startup failed", ex);
            MessageBox.Show("BrainStrap hit a problem while starting:\n\n" + ex.Message, "BrainStrap", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
