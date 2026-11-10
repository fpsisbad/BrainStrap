using System.Text.RegularExpressions;

namespace BrainStrap.Core;

/// <summary>
/// Plain text log in %LOCALAPPDATA%\BrainStrap\logs. Launch links, tickets and cookies must never end up here,
/// so every line is scrubbed before it is written (and the rest of the code never passes them in the first place).
/// </summary>
internal static class Logger
{
    static readonly object Gate = new();
    static string FilePath => Path.Combine(Paths.Logs, $"brainstrap-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string msg) => Write("INFO ", msg);
    public static void Warn(string msg) => Write("WARN ", msg);
    public static void Error(string msg, Exception? ex = null) =>
        Write("ERROR", ex == null ? msg : $"{msg}: {ex.GetType().Name}: {ex.Message}");

    static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss} [{level}] {Scrub(msg)}{Environment.NewLine}");
        }
        catch
        {
            // logging must never break a launch
        }
    }

    static string Scrub(string s)
    {
        s = Regex.Replace(s, @"roblox-player:\S+", "roblox-player:[removed]", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"(gameinfo[:=]|\s-t\s+|ROBLOSECURITY[=:]\s*)\S+", "$1[removed]", RegexOptions.IgnoreCase);
        return s;
    }

    /// <summary>Deletes logs older than 14 days.</summary>
    public static void Prune()
    {
        try
        {
            foreach (var f in Directory.GetFiles(Paths.Logs, "brainstrap-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-14)) File.Delete(f);
        }
        catch
        {
        }
    }
}
