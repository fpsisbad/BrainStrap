using System.Diagnostics;
using System.Text.Json;

namespace BrainStrap.Core;

internal sealed record InstalledVersion(string Folder, string Name, DateTime Stamp)
{
    public string PlayerExe => Path.Combine(Folder, "RobloxPlayerBeta.exe");
}

/// <summary>Finds installed Roblox players (BrainStrap's own installs first, then any Roblox put there by Roblox's launcher) and asks Roblox which version is current.</summary>
internal static class RobloxInstall
{
    static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false, UseProxy = true })
    {
        Timeout = TimeSpan.FromSeconds(6),
        DefaultRequestHeaders = { { "User-Agent", "BrainStrap/1.0" } },
    };

    /// <summary>All installed player versions, newest first.</summary>
    public static List<InstalledVersion> List()
    {
        var list = new List<InstalledVersion>();
        foreach (var root in new[] { RobloxDeploy.Root, Paths.RobloxVersions, Paths.RobloxVersionsMachine })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root, "version-*"))
            {
                if (dir.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)) continue;
                var exe = Path.Combine(dir, "RobloxPlayerBeta.exe");
                if (File.Exists(exe)) list.Add(new InstalledVersion(dir, Path.GetFileName(dir), File.GetLastWriteTimeUtc(exe)));
            }
        }
        return list.OrderByDescending(v => v.Stamp).ToList();
    }

    public static InstalledVersion? Newest() => List().FirstOrDefault();

    /// <summary>An installed copy of exactly this version, if there is one.</summary>
    public static InstalledVersion? Find(string version) => List().FirstOrDefault(v => v.Name.Equals(version, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when this copy lives in BrainStrap's own folder.</summary>
    public static bool IsOwn(InstalledVersion v) => v.Folder.StartsWith(RobloxDeploy.Root, StringComparison.OrdinalIgnoreCase);

    /// <summary>The current Windows player version folder name (like "version-0123abcd...") from Roblox's public settings service, or null if offline.</summary>
    public static async Task<string?> LatestVersionAsync()
    {
        try
        {
            using var resp = await Http.GetAsync("https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer");
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("clientVersionUpload", out var v) ? v.GetString() : null;
        }
        catch (Exception ex)
        {
            Logger.Warn("version check failed: " + ex.GetType().Name);
            return null;
        }
    }

    /// <summary>Roblox's own launcher, used to install and update the player and as the fallback for links BrainStrap does not handle.</summary>
    public static string? FindOfficialLauncher(Settings s)
    {
        var fromBackup = ProtocolHandler.ExtractExe(s.OriginalHandlerCommand);
        if (fromBackup != null && File.Exists(fromBackup)) return fromBackup;

        foreach (var root in new[] { Paths.RobloxVersions, Paths.RobloxVersionsMachine })
        {
            if (!Directory.Exists(root)) continue;
            var found = Directory.EnumerateDirectories(root, "version-*")
                .Select(d => Path.Combine(d, "RobloxPlayerLauncher.exe"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (found != null) return found;
        }
        return null;
    }

    public static bool IsRobloxRunning() => Process.GetProcessesByName("RobloxPlayerBeta").Length > 0;

    /// <summary>Deletes every version folder except the newest. Refuses while Roblox is running.</summary>
    public static (int removed, string? problem) PruneOld()
    {
        if (IsRobloxRunning()) return (0, "Close Roblox first.");
        var all = List();
        if (all.Count < 2) return (0, null);
        var removed = 0;
        foreach (var v in all.Skip(1))
        {
            // only ever delete a plain "version-*" folder directly inside a Roblox Versions folder
            var parent = Path.GetDirectoryName(v.Folder);
            var ok = parent != null && (parent.Equals(Paths.RobloxVersions, StringComparison.OrdinalIgnoreCase) || parent.Equals(RobloxDeploy.Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                                        parent.Equals(Paths.RobloxVersionsMachine, StringComparison.OrdinalIgnoreCase));
            if (!ok || !v.Name.StartsWith("version-", StringComparison.Ordinal)) continue;
            try { Directory.Delete(v.Folder, true); removed++; }
            catch (Exception ex) { Logger.Warn("could not delete " + v.Name + ": " + ex.Message); }
        }
        return (removed, null);
    }

    /// <summary>Public, no-login lookup of a game's name for Discord presence.</summary>
    public static async Task<string?> GameNameAsync(string placeId)
    {
        try
        {
            using var r1 = await Http.GetAsync($"https://apis.roblox.com/universes/v1/places/{placeId}/universe");
            if (!r1.IsSuccessStatusCode) return null;
            using var d1 = JsonDocument.Parse(await r1.Content.ReadAsStringAsync());
            if (!d1.RootElement.TryGetProperty("universeId", out var u) || u.ValueKind != JsonValueKind.Number) return null;

            using var r2 = await Http.GetAsync($"https://games.roblox.com/v1/games?universeIds={u.GetInt64()}");
            if (!r2.IsSuccessStatusCode) return null;
            using var d2 = JsonDocument.Parse(await r2.Content.ReadAsStringAsync());
            var data = d2.RootElement.GetProperty("data");
            return data.GetArrayLength() > 0 ? data[0].GetProperty("name").GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
