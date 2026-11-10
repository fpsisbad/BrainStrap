using Microsoft.Win32;

namespace BrainStrap.Core;

/// <summary>
/// "Install" means: keep one copy of BrainStrap.exe in %LOCALAPPDATA%\BrainStrap, register it for roblox-player: links,
/// and add a Start menu shortcut and an Apps &amp; features entry. All of it is per-user; no administrator rights.
/// </summary>
internal static class Installer
{
    public static string InstalledExe => Path.Combine(Paths.Data, "BrainStrap.exe");
    public static bool IsInstalled => File.Exists(InstalledExe);
    public static bool RunningFromInstall =>
        string.Equals(Path.GetFullPath(Paths.SelfExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BrainStrap";
    public const string Version = "2.0.0";

    static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "BrainStrap.lnk");

    public static void Install(Settings s)
    {
        if (!RunningFromInstall) File.Copy(Paths.SelfExe, InstalledExe, true);
        ProtocolHandler.Register(InstalledExe, s);
        WriteUninstallEntry();
        TryCreateShortcut();
        SettingsStore.Save(s);
        Logger.Info("installed");
    }

    /// <summary>Puts everything back the way Roblox had it. Leaves your settings and mods folder in place.</summary>
    public static void Uninstall(Settings s)
    {
        ProtocolHandler.Restore(s);
        ModManager.RevertEverywhere();
        FlagWriter.RemoveEverywhere();
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        try { if (File.Exists(StartMenuLink)) File.Delete(StartMenuLink); } catch { }
        SettingsStore.Save(s);
        Logger.Info("uninstalled");
    }

    static void WriteUninstallEntry()
    {
        using var k = Registry.CurrentUser.CreateSubKey(UninstallKey);
        k.SetValue("DisplayName", "BrainStrap");
        k.SetValue("DisplayVersion", Version);
        k.SetValue("Publisher", "BrainStrap");
        k.SetValue("DisplayIcon", InstalledExe);
        k.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    static void TryCreateShortcut()
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic link = shell.CreateShortcut(StartMenuLink);
            link.TargetPath = InstalledExe;
            link.WorkingDirectory = Paths.Data;
            link.Description = "BrainStrap";
            link.Save();
        }
        catch (Exception ex)
        {
            Logger.Warn("could not create the Start menu shortcut: " + ex.Message);
        }
    }
}
