using System.Text;

namespace BrainStrap.Core;

/// <summary>A report you can paste into a bug report. It contains no account name, ticket, cookie or launch link.</summary>
internal static class Diagnostics
{
    public static string Build(Settings s)
    {
        var sb = new StringBuilder();
        var cat = FlagCatalog.Load();
        var installed = RobloxInstall.Newest();
        var flags = FlagCatalog.Resolve(cat, s.Current, s.FlagFormat);
        var mods = ModManager.Scan();

        sb.AppendLine($"BrainStrap {Installer.Version}");
        sb.AppendLine($"Windows: {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64" : "32")}-bit)");
        sb.AppendLine($"Installed copy: {(Installer.IsInstalled ? "yes" : "no")}, running from install: {(Installer.RunningFromInstall ? "yes" : "no")}");
        sb.AppendLine($"Registered for roblox-player links: {(ProtocolHandler.IsRegisteredToUs() ? "yes" : "no")}");
        sb.AppendLine($"Original handler saved: {(string.IsNullOrWhiteSpace(s.OriginalHandlerCommand) ? "no" : "yes")}");
        sb.AppendLine($"Roblox folder: {(installed?.Name ?? "not found")}");
        sb.AppendLine($"Safe mode: {s.SafeMode}   Check for updates: {s.CheckForUpdates}   Close on launch: {s.CloseOnLaunch}");
        sb.AppendLine($"Profile: {s.ActiveProfile} ({s.Profiles.Count} total), game profiles: {s.GameProfiles.Count}");
        sb.AppendLine($"Flags that would be written: {flags.Count}  (format: {s.FlagFormat})");
        foreach (var kv in flags) sb.AppendLine($"    {kv.Key} = {kv.Value}");
        sb.AppendLine($"Mods enabled: {s.ModsEnabled}; files: {mods.Count(m => m.Allowed)} usable, {mods.Count(m => !m.Allowed)} skipped");
        sb.AppendLine($"Discord presence: {(s.Discord.Enabled ? "on" : "off")}");
        sb.AppendLine($"Theme: {s.Theme}");
        return sb.ToString();
    }
}
