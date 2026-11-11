using Microsoft.Win32;

namespace BrainStrap.Core;

/// <summary>
/// Registers BrainStrap as the program that opens roblox-player: links (what the "Play" button on roblox.com uses).
/// Only the current user's registry (HKCU\Software\Classes\roblox-player) is written, so no administrator rights are needed,
/// and the previous handler is saved so it can be restored exactly.
/// </summary>
internal static class ProtocolHandler
{
    const string KeyPath = @"Software\Classes\roblox-player";

    /// <summary>The command Windows will currently run for roblox-player: links (HKCU wins over HKLM).</summary>
    public static string? CurrentCommand()
    {
        using var k = Registry.ClassesRoot.OpenSubKey(@"roblox-player\shell\open\command");
        return k?.GetValue(null) as string;
    }

    /// <summary>Pulls the program path out of a registry command such as: "C:\x\y.exe" "%1"</summary>
    public static string? ExtractExe(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = command.Trim();
        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        var sp = command.IndexOf(' ');
        return sp > 0 ? command[..sp] : command;
    }

    public static bool IsBrainStrapCommand(string? command)
    {
        var exe = ExtractExe(command);
        return exe != null && Path.GetFileName(exe).Equals("BrainStrap.exe", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRegisteredToUs() => Demo.On || IsBrainStrapCommand(CurrentCommand());

    const string LinkKeyPath = @"Software\Classes\roblox";

    /// <summary>The command Windows runs for roblox:// links (joining a server from a link or from BrainStrap).</summary>
    public static string? CurrentLinkCommand()
    {
        using var k = Registry.ClassesRoot.OpenSubKey(@"roblox\shell\open\command");
        return k?.GetValue(null) as string;
    }

    public static void Register(string exePath, Settings s)
    {
        var current = CurrentCommand();
        if (!string.IsNullOrWhiteSpace(current) && !IsBrainStrapCommand(current))
        {
            s.OriginalHandlerCommand = current;          // remember what Roblox registered
            Logger.Info("saved the original roblox-player handler");
        }
        var link = CurrentLinkCommand();
        if (!string.IsNullOrWhiteSpace(link) && !IsBrainStrapCommand(link)) s.OriginalLinkCommand = link;

        Write(KeyPath, "URL:Roblox Protocol (BrainStrap)", exePath);
        // roblox:// join links too, so joining works on a PC that has never had Roblox's own launcher
        Write(LinkKeyPath, "URL:Roblox Link (BrainStrap)", exePath);
        Logger.Info("registered as the roblox-player and roblox link handler");
    }

    static void Write(string keyPath, string name, string exePath)
    {
        using var root = Registry.CurrentUser.CreateSubKey(keyPath);
        root.SetValue(null, name);
        root.SetValue("URL Protocol", "");
        using (var icon = root.CreateSubKey("DefaultIcon")) icon.SetValue(null, $"\"{exePath}\",0");
        using (var cmd = root.CreateSubKey(@"shell\open\command")) cmd.SetValue(null, $"\"{exePath}\" \"%1\"");
    }

    /// <summary>Puts Roblox's own launcher back. If its file is gone, the BrainStrap keys are simply removed.</summary>
    public static void Restore(Settings s)
    {
        Undo(KeyPath, s.OriginalHandlerCommand);
        Undo(LinkKeyPath, s.OriginalLinkCommand);
        Logger.Info("gave the Roblox links back");
    }

    static void Undo(string keyPath, string? original)
    {
        var exe = ExtractExe(original);
        if (exe != null && File.Exists(exe) && !IsBrainStrapCommand(original))
        {
            using var cmd = Registry.CurrentUser.CreateSubKey(keyPath + @"\shell\open\command");
            cmd.SetValue(null, original!);
        }
        else
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); } catch { }
        }
    }
}
