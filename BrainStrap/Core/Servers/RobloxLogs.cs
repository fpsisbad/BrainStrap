using System.Text;
using System.Text.RegularExpressions;

namespace BrainStrap.Core.Servers;

/// <summary>
/// Reads Roblox's own log files (%LOCALAPPDATA%\Roblox\logs) to learn which game and server you joined and why you
/// disconnected. The same approach other launchers use. Lines are only matched against the patterns below;
/// raw log text is never stored, shown or sent anywhere, because Roblox logs can contain account details.
///
/// NOTE: Roblox can change its log wording at any time. If server info stops appearing, update these patterns.
/// </summary>
internal sealed class LogWatcher : IDisposable
{
    static readonly Regex Join = new(@"Joining game '([0-9a-fA-F\-]{36})' place (\d+)(?: at ([\d\.]+))?", RegexOptions.Compiled);
    static readonly Regex Udmux = new(@"UDMUX Address = ([\d\.]+), Port = \d+ \| RCC Server Address = ([\d\.]+)", RegexOptions.Compiled);
    static readonly Regex ServerId = new(@"serverId: ([\d\.]+)\|\d+", RegexOptions.Compiled);
    static readonly Regex Disconnect = new(@"(?:Disconnect reason received|Sending disconnect with reason|disconnect reason)\s*:?\s*(\d{3})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Private = new(@"joinGamePostPrivateServer|privateServer", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Reserved = new(@"initiateTeleportToReservedServer", RegexOptions.Compiled);
    static readonly Regex Left = new(@"Time to disconnect replication data|Client:Disconnect", RegexOptions.Compiled);

    public static string LogDir => Path.Combine(Paths.Local, "Roblox", "logs");

    public event Action<string, string>? Joined;          // placeId, jobId
    public event Action<string>? ServerAddress;            // ip you are connected to
    public event Action<int>? Disconnected;                // Roblox error code
    public event Action? LeftGame;
    public bool PrivateServer { get; private set; }
    public bool ReservedServer { get; private set; }
    public string? FilePath { get; private set; }

    readonly CancellationTokenSource cts = new();

    public void Start(DateTime launchedUtc) => _ = Task.Run(() => RunAsync(launchedUtc, cts.Token));

    async Task RunAsync(DateTime launchedUtc, CancellationToken ct)
    {
        // the player creates its log a moment after it starts
        for (var i = 0; i < 60 && FilePath == null && !ct.IsCancellationRequested; i++)
        {
            FilePath = NewestPlayerLog(launchedUtc.AddSeconds(-10));
            if (FilePath == null) await Delay(500, ct);
        }
        if (FilePath == null) { Logger.Info("no Roblox log found for this session"); return; }

        using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) { await Delay(400, ct); continue; }
            Handle(line);
        }
    }

    void Handle(string line)
    {
        Match m;
        if ((m = Join.Match(line)).Success)
        {
            PrivateServer = ReservedServer = false;
            Joined?.Invoke(m.Groups[2].Value, m.Groups[1].Value.ToLowerInvariant());
            if (m.Groups[3].Success && m.Groups[3].Value.Length > 0) ServerAddress?.Invoke(m.Groups[3].Value);
        }
        else if ((m = Udmux.Match(line)).Success) ServerAddress?.Invoke(m.Groups[1].Value);
        else if ((m = ServerId.Match(line)).Success) ServerAddress?.Invoke(m.Groups[1].Value);
        else if ((m = Disconnect.Match(line)).Success && int.TryParse(m.Groups[1].Value, out var code)) Disconnected?.Invoke(code);
        else if (Private.IsMatch(line)) PrivateServer = true;
        else if (Reserved.IsMatch(line)) ReservedServer = true;
        else if (Left.IsMatch(line)) LeftGame?.Invoke();
    }

    static async Task Delay(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); } catch (OperationCanceledException) { }
    }

    public static string? NewestPlayerLog(DateTime? after = null)
    {
        try
        {
            if (!Directory.Exists(LogDir)) return null;
            return new DirectoryInfo(LogDir).GetFiles("*.log")
                .Where(f => f.Name.Contains("Player", StringComparison.OrdinalIgnoreCase) && (after == null || f.LastWriteTimeUtc >= after))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }
        catch
        {
            return null;
        }
    }

    public static List<FileInfo> RecentLogs(int n = 20)
    {
        try
        {
            return Directory.Exists(LogDir)
                ? new DirectoryInfo(LogDir).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).Take(n).ToList()
                : new List<FileInfo>();
        }
        catch
        {
            return new List<FileInfo>();
        }
    }

    /// <summary>Game Library: places you joined recently, newest first, read from Roblox's logs.</summary>
    public static List<(string placeId, DateTime when)> RecentPlaces(int maxLogs = 40)
    {
        var seen = new Dictionary<string, DateTime>();
        foreach (var f in RecentLogs(maxLogs))
        {
            try
            {
                using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var r = new StreamReader(fs);
                string? line;
                var lines = 0;
                while ((line = r.ReadLine()) != null && lines++ < 200000)
                {
                    var m = Join.Match(line);
                    if (m.Success && !seen.ContainsKey(m.Groups[2].Value)) seen[m.Groups[2].Value] = f.LastWriteTime;
                }
            }
            catch
            {
            }
        }
        return seen.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    public void Dispose() => cts.Cancel();
}

/// <summary>What Roblox's disconnect codes mean, in plain words.</summary>
internal static class RobloxCodes
{
    public static readonly Dictionary<int, (string title, string help)> Map = new()
    {
        [17] = ("Couldn't reach the server", "The game server didn't answer. Check your internet, then try again or join a different server."),
        [260] = ("Server data error", "Roblox couldn't read data from the server. Rejoining usually fixes it."),
        [261] = ("Error sending data", "Your connection dropped data. A wired connection or a different server helps."),
        [264] = ("Same account joined elsewhere", "Your account joined the same game from another device."),
        [266] = ("Server timed out", "The server stopped responding to you. Often your connection, sometimes the server."),
        [267] = ("Kicked by the game", "The game's own scripts removed you (for example an anti-AFK or admin kick)."),
        [268] = ("Unexpected client behaviour", "Roblox thought something on your side was unusual. If you run anything that modifies Roblox, stop."),
        [271] = ("Server shut down", "The game was shut down, often for an update."),
        [273] = ("Logged in elsewhere", "Your account started playing on another device."),
        [274] = ("Server shut down", "The server closed, usually because the game updated."),
        [275] = ("Server shut down for update", "The game was updated. Rejoin to get the new version."),
        [277] = ("Lost connection", "Your connection to the server dropped. Wi-Fi drops, VPNs and busy networks are the usual causes."),
        [278] = ("Idle for 20 minutes", "Roblox disconnects players who don't move for 20 minutes."),
        [279] = ("Failed to connect", "Roblox couldn't connect you to the game. Firewalls, VPNs or a bad network are common causes."),
        [280] = ("Version mismatch", "Your Roblox is out of date. Restart it so it updates."),
        [282] = ("Disconnected", "The connection ended. Try rejoining."),
        [285] = ("You left", "You left the game normally."),
        [286] = ("Can't join: reserved server", "That server only allows invited players."),
        [517] = ("Game is unavailable", "The game is restarting or shutting down. Try again in a minute."),
        [522] = ("Friend left", "The person you followed left that server."),
        [524] = ("Not authorized to join", "The server is private or you need permission."),
        [529] = ("Roblox service error", "Roblox's servers had a problem. Check status.roblox.com and try again later."),
        [610] = ("Couldn't join that server", "The server is full or no longer exists. Try another one."),
        [769] = ("Teleport failed", "Moving you to another place failed. Rejoin from the website."),
        [770] = ("Teleport failed", "Moving you to another place failed. Rejoin from the website."),
        [772] = ("Teleport failed: server full", "The destination was full."),
        [773] = ("Teleport failed: restricted", "You aren't allowed into that place."),
    };

    public static (string title, string help) Explain(int code) =>
        Map.TryGetValue(code, out var v) ? v : ($"Error {code}", "Roblox reported this error code. Rejoin; if it keeps happening, check status.roblox.com.");
}
