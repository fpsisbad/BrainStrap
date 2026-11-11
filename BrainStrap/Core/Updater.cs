using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BrainStrap.Core;

/// <summary>
/// Keeps BrainStrap itself up to date from its GitHub Releases. It asks GitHub's public API which release is
/// newest, downloads BrainStrap.exe from that release, and only swaps it in if its SHA-256 matches the
/// fingerprint GitHub publishes for the file.
/// </summary>
internal static class Updater
{
    public const string DefaultRepo = "fpsisbad/BrainStrap";

    internal sealed record Release(string Version, string Url, string Sha256, long Size, string Notes);

    static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false })
    {
        Timeout = TimeSpan.FromMinutes(10),
        DefaultRequestHeaders = { { "User-Agent", "BrainStrap/" + Installer.Version }, { "Accept", "application/vnd.github+json" } },
    };

    public static string Repo(Settings s) => Regex.IsMatch(s.Platform.GitHubRepo ?? "", @"^[\w.-]{1,60}/[\w.-]{1,100}$") ? s.Platform.GitHubRepo : DefaultRepo;

    static Version? Parse(string v) => System.Version.TryParse(v.TrimStart('v', 'V'), out var x) ? x : null;

    /// <summary>The newest release if it is newer than this copy, otherwise null.</summary>
    public static async Task<Release?> CheckAsync(Settings s, CancellationToken ct = default)
    {
        try
        {
            var repo = Repo(s);
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest", ct));
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var latest = Parse(tag);
            var mine = Parse(Installer.Version);
            if (latest == null || mine == null || latest <= mine) return null;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
            {
                if (a.GetProperty("name").GetString() != "BrainStrap.exe") continue;
                var url = a.GetProperty("browser_download_url").GetString() ?? "";
                var digest = a.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
                var m = Regex.Match(digest, "^sha256:([0-9a-fA-F]{64})$");
                // no fingerprint, or a link that isn't this repository's own release download: don't offer it
                if (!m.Success || !url.StartsWith($"https://github.com/{repo}/releases/download/", StringComparison.OrdinalIgnoreCase)) return null;
                var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                return new Release(tag.TrimStart('v', 'V'), url, m.Groups[1].Value.ToLowerInvariant(), a.GetProperty("size").GetInt64(), notes.Length > 600 ? notes[..600] + "…" : notes);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Logger.Info("update check: " + ex.Message); }
        return null;
    }

    /// <summary>Downloads the release, checks it, swaps it in and starts it. Returns a problem to show, or null (the app is then about to close).</summary>
    public static async Task<string?> ApplyAsync(Release r, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Paths.Data, "Update")).FullName;
        var fresh = Path.Combine(dir, "BrainStrap.new.exe");
        try
        {
            using (var resp = await Http.GetAsync(r.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(fresh, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
                var buf = new byte[1 << 16];
                long got = 0;
                int read;
                while ((read = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, read), ct);
                    got += read;
                    progress?.Report(Math.Min(1, (double)got / Math.Max(1, r.Size)));
                }
            }
            string hash;
            await using (var fs = File.OpenRead(fresh)) hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
            if (hash != r.Sha256)
            {
                File.Delete(fresh);
                return "The download didn't match the fingerprint GitHub lists for it, so it was thrown away.";
            }

            // a running exe can't be overwritten, but it can be renamed out of the way
            var target = Installer.IsInstalled ? Installer.InstalledExe : Paths.SelfExe;
            var old = target + ".old";
            try { if (File.Exists(old)) File.Delete(old); } catch { }
            File.Move(target, old);
            try { File.Move(fresh, target); }
            catch { File.Move(old, target); throw; }
            Logger.Info("updated BrainStrap to " + r.Version);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = false });
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Error("update failed", ex);
            return "Couldn't update: " + ex.Message;
        }
    }

    /// <summary>Removes the copy left behind by the last update.</summary>
    public static void Tidy()
    {
        foreach (var f in new[] { Installer.InstalledExe + ".old", Paths.SelfExe + ".old" })
            try { if (File.Exists(f)) File.Delete(f); } catch { }
    }
}
