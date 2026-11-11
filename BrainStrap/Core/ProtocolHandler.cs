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

    public static void Register(string exePath, Settings s)
    {
        var current = CurrentCommand();
        if (!string.IsNullOrWhiteSpace(current) && !IsBrainStrapCommand(current))
        {
            s.OriginalHandlerCommand = current;          // remember what Roblox registered
            Logger.Info("saved the original roblox-player handler");
        }

        using var root = Registry.CurrentUser.CreateSubKey(KeyPath);
        root.SetValue(null, "URL:Roblox Protocol (BrainStrap)");
        root.SetValue("URL Protocol", "");
        using (var icon = root.CreateSubKey("DefaultIcon")) icon.SetValue(null, $"\"{exePath}\",0");
        using (var cmd = root.CreateSubKey(@"shell\open\command")) cmd.SetValue(null, $"\"{exePath}\" \"%1\"");
        Logger.Info("registered as the roblox-player handler");
    }

    /// <summary>Puts Roblox's own launcher back. If its file is gone, the BrainStrap key is simply removed.</summary>
    public static void Restore(Settings s)
    {
        var exe = ExtractExe(s.OriginalHandlerCommand);
        if (exe != null && File.Exists(exe) && !IsBrainStrapCommand(s.OriginalHandlerCommand))
        {
            using var cmd = Registry.CurrentUser.CreateSubKey(KeyPath + @"\shell\open\command");
            cmd.SetValue(null, s.OriginalHandlerCommand!);
            Logger.Info("restored the original roblox-player handler");
        }
        else
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false); } catch { }
            Logger.Info("removed the BrainStrap roblox-player registration");
        }
    }
}
