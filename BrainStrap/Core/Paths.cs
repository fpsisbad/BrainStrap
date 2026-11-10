using System.Diagnostics;

namespace BrainStrap.Core;

/// <summary>Every folder BrainStrap reads or writes. Nothing outside these is touched except the Roblox version folder.</summary>
internal static class Paths
{
    public static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>%LOCALAPPDATA%\BrainStrap</summary>
    public static string Data { get; } = Directory.CreateDirectory(Path.Combine(Local, "BrainStrap")).FullName;
    public static string Logs => Directory.CreateDirectory(Path.Combine(Data, "logs")).FullName;
    public static string Mods => Directory.CreateDirectory(Path.Combine(Data, "Mods")).FullName;
    public static string Backups => Directory.CreateDirectory(Path.Combine(Data, "Backups")).FullName;
    public static string State => Directory.CreateDirectory(Path.Combine(Data, "State")).FullName;
    public static string SettingsFile => Path.Combine(Data, "settings.json");

    /// <summary>%LOCALAPPDATA%\Roblox\Versions  (per-user install, the default)</summary>
    public static string RobloxVersions => Path.Combine(Local, "Roblox", "Versions");

    /// <summary>C:\Program Files (x86)\Roblox\Versions  (machine-wide install, older)</summary>
    public static string RobloxVersionsMachine =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Roblox", "Versions");

    public static string SelfExe => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
}
