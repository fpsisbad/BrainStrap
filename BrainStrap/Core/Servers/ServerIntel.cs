using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using BrainStrap.Core.Perf;

namespace BrainStrap.Core.Servers;

internal sealed record GameInfo(string PlaceId, long UniverseId, string Name, string Genre, long Playing, int MaxPlayers);
internal sealed record ServerEntry(string Id, int Playing, int Max, double Fps, double Ping);
internal sealed record ServerSuggestion(ServerEntry Server, string Why);
internal sealed record Region(string City, string RegionName, string Country, string Org)
{
    public override string ToString() => string.Join(", ", new[] { City, RegionName, Country }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
}

/// <summary>Public Roblox information that needs no login. BrainStrap never sends your cookie, so it can't see private data.</summary>
internal static class RobloxApi
{
    static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(8),
        DefaultRequestHeaders = { { "User-Agent", "BrainStrap/2.0" } },
    };

    static readonly Dictionary<string, GameInfo> GameCache = new();

    public static async Task<GameInfo?> GameAsync(string placeId)
    {
        if (!IsId(placeId)) return null;
        lock (GameCache) if (GameCache.TryGetValue(placeId, out var hit)) return hit;
        try
        {
            using var d1 = JsonDocument.Parse(await Http.GetStringAsync($"https://apis.roblox.com/universes/v1/places/{placeId}/universe"));
            if (!d1.RootElement.TryGetProperty("universeId", out var u) || u.ValueKind != JsonValueKind.Number) return null;
            var uid = u.GetInt64();
            using var d2 = JsonDocument.Parse(await Http.GetStringAsync($"https://games.roblox.com/v1/games?universeIds={uid}"));
            var data = d2.RootElement.GetProperty("data");
            if (data.GetArrayLength() == 0) return null;
            var g = data[0];
            string Str(string k) => g.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            long Num(string k) => g.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
            var genre = string.Join(" ", new[] { Str("genre_l1"), Str("genre_l2"), Str("genre") }.Where(x => x.Length > 0 && x != "All"));
            var info = new GameInfo(placeId, uid, Str("name"), genre, Num("playing"), (int)Num("maxPlayers"));
            lock (GameCache) GameCache[placeId] = info;
            return info;
        }
        catch (Exception ex)
        {
            Logger.Warn("game lookup failed: " + ex.GetType().Name);
            return null;
        }
    }

    /// <summary>Public servers with free space (up to 300). Ping and FPS are what Roblox reports for each server's players.</summary>
    public static async Task<List<ServerEntry>> PublicServersAsync(string placeId, int pages = 3)
    {
        var list = new List<ServerEntry>();
        if (!IsId(placeId)) return list;
        string? cursor = null;
        for (var i = 0; i < pages; i++)
        {
            try
            {
                var url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?sortOrder=Asc&excludeFullGames=true&limit=100" +
                          (cursor != null ? "&cursor=" + Uri.EscapeDataString(cursor) : "");
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
                foreach (var s in doc.RootElement.GetProperty("data").EnumerateArray())
                {
                    double D(string k) => s.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
                    var id = s.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    if (id.Length > 0) list.Add(new ServerEntry(id, (int)D("playing"), (int)D("maxPlayers"), Math.Round(D("fps"), 1), Math.Round(D("ping"))));
                }
                cursor = doc.RootElement.TryGetProperty("nextPageCursor", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                if (cursor == null) break;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Logger.Warn("Roblox rate-limited the server list");
                break;
            }
            catch (Exception ex)
            {
                Logger.Warn("server list failed: " + ex.GetType().Name);
                break;
            }
        }
        return list;
    }

    /// <summary>Smart Server Suggestions: low reported ping, healthy server FPS, and room to join. Roblox does not publish server locations.</summary>
    public static List<ServerSuggestion> Suggest(List<ServerEntry> servers, int n = 3)
    {
        double Score(ServerEntry s)
        {
            var fill = s.Max > 0 ? (double)s.Playing / s.Max : 0;
            var score = s.Ping > 0 ? s.Ping : 250;
            if (fill < 0.2) score += 40;          // nearly empty: little going on
            if (fill > 0.9) score += 30;          // nearly full: may not fit
            if (s.Fps > 0 && s.Fps < 50) score += (50 - s.Fps) * 2;
            return score;
        }
        return servers.Where(s => s.Playing < s.Max).OrderBy(Score).Take(n)
            .Select(s => new ServerSuggestion(s, $"{(s.Ping > 0 ? s.Ping + " ms average ping" : "ping unknown")}, server at {s.Fps:0} FPS, {s.Playing}/{s.Max} players"))
            .ToList();
    }

    public static async Task<string?> IconAsync(long universeId)
    {
        try
        {
            var dir = Directory.CreateDirectory(Path.Combine(Paths.State, "icons")).FullName;
            var file = Path.Combine(dir, universeId + ".png");
            if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromDays(7)) return file;
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://thumbnails.roblox.com/v1/games/icons?universeIds={universeId}&size=150x150&format=Png&isCircular=false"));
            var data = doc.RootElement.GetProperty("data");
            if (data.GetArrayLength() == 0) return null;
            var url = data[0].GetProperty("imageUrl").GetString();
            if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "https" || !u.Host.EndsWith(".rbxcdn.com")) return null;
            await File.WriteAllBytesAsync(file, await Http.GetByteArrayAsync(u));
            return file;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsId(string? s) => s != null && s.Length is > 0 and <= 20 && s.All(char.IsAsciiDigit);
    public static bool IsJobId(string? s) => s != null && System.Text.RegularExpressions.Regex.IsMatch(s, "^[0-9a-fA-F-]{36}$");

    public static async Task<(bool ok, string version)> StatusAsync()
    {
        try
        {
            using var r = await Http.GetAsync("https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer");
            if (!r.IsSuccessStatusCode) return (false, "");
            using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            return (true, d.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "");
        }
        catch
        {
            return (false, "");
        }
    }
}

/// <summary>Server Region Viewer. Off by default because it sends the Roblox server's IP address to ipinfo.io.</summary>
internal static class ServerGeo
{
    static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(6) };
    static string CacheFile => Path.Combine(Paths.State, "regions.json");

    public static async Task<Region?> LookupAsync(string ip, Settings s)
    {
        if (!s.Servers.RegionLookup || !IPAddress.TryParse(ip, out var addr) || IsPrivate(addr)) return null;
        var cache = Load();
        if (cache.TryGetValue(ip, out var hit)) return hit;
        try
        {
            using var d = JsonDocument.Parse(await Http.GetStringAsync($"https://ipinfo.io/{ip}/json"));
            string Str(string k) => d.RootElement.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
            var r = new Region(Str("city"), Str("region"), Str("country"), Str("org"));
            cache[ip] = r;
            try { File.WriteAllText(CacheFile, JsonSerializer.Serialize(cache)); } catch { }
            return r;
        }
        catch
        {
            return null;
        }
    }

    static Dictionary<string, Region> Load()
    {
        try { return File.Exists(CacheFile) ? JsonSerializer.Deserialize<Dictionary<string, Region>>(File.ReadAllText(CacheFile)) ?? new() : new(); }
        catch { return new(); }
    }

    public static bool IsPrivate(IPAddress a)
    {
        var b = a.GetAddressBytes();
        return IPAddress.IsLoopback(a) || b.Length != 4 || b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }
}

/// <summary>Join Link Generator and the joining itself. Joining goes through Roblox's own app, which handles your login.</summary>
internal static class JoinLinks
{
    public static string App(string placeId, string? jobId = null) =>
        $"roblox://experiences/start?placeId={placeId}" + (RobloxApi.IsJobId(jobId) ? $"&gameInstanceId={jobId}" : "");

    public static string Web(string placeId, string? jobId = null) =>
        $"https://www.roblox.com/games/start?placeId={placeId}" + (RobloxApi.IsJobId(jobId) ? $"&gameInstanceId={jobId}" : "");

    public static string Page(string placeId) => $"https://www.roblox.com/games/{placeId}";

    /// <summary>Accepts the private-server links Roblox gives out and returns a safe https link, or null.</summary>
    public static string? CleanPrivateLink(string raw)
    {
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var u) || u.Scheme != "https") return null;
        var host = u.Host.ToLowerInvariant();
        if (host != "www.roblox.com" && host != "roblox.com") return null;
        var q = System.Web.HttpUtility.ParseQueryString(u.Query);
        if (u.AbsolutePath.StartsWith("/share", StringComparison.OrdinalIgnoreCase) && q["code"] != null) return u.GetLeftPart(UriPartial.Query);
        if (u.AbsolutePath.StartsWith("/games/", StringComparison.OrdinalIgnoreCase) && q["privateServerLinkCode"] != null) return u.GetLeftPart(UriPartial.Query);
        return null;
    }

    /// <summary>
    /// Joins through BrainStrap's own launch flow: a second copy of BrainStrap makes sure Roblox is installed and current,
    /// applies the right profile for the place, starts the player with the join link and follows the session.
    /// Private-server share links are web pages, so those open in the browser and come back through the Play button.
    /// </summary>
    public static void Join(string placeId, string? jobId, string? httpsLink = null)
    {
        try
        {
            if (httpsLink != null) { Process.Start(new ProcessStartInfo(httpsLink) { UseShellExecute = true }); return; }
            Process.Start(new ProcessStartInfo(Paths.SelfExe) { UseShellExecute = false, ArgumentList = { App(placeId, jobId) } });
        }
        catch (Exception ex) { Logger.Error("could not open the join link", ex); }
    }
}

internal sealed record DiagResult(string Name, string Value, string Level, string Help);

/// <summary>Connection Diagnostics: separates your Wi-Fi/router from your internet and from Roblox.</summary>
internal static class NetDiag
{
    public static async Task<List<DiagResult>> RunAsync(string? serverIp)
    {
        var res = new List<DiagResult>();
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
            .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1)
            .FirstOrDefault();
        if (nic == null)
        {
            res.Add(new DiagResult("Network", "Not connected", "bad", "Windows has no active network with a router."));
            return res;
        }

        var wifi = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
        res.Add(new DiagResult("Connection type", wifi ? "Wi-Fi" : nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "Wired (Ethernet)" : nic.NetworkInterfaceType.ToString(),
            wifi ? "warn" : "ok", wifi ? "Wi-Fi is the most common cause of ping spikes. A cable is steadier." : "A wired connection is the most stable."));

        var vpn = NetworkInterface.GetAllNetworkInterfaces().Any(n => n.OperationalStatus == OperationalStatus.Up &&
            System.Text.RegularExpressions.Regex.IsMatch(n.Description + " " + n.Name, "vpn|wireguard|tap-windows|tunnel|openvpn|nordlynx|proton|mullvad", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        res.Add(new DiagResult("VPN", vpn ? "Looks active" : "None found", vpn ? "warn" : "ok", vpn ? "VPNs add distance and can cause error 279/277. Try without it." : ""));

        var gw = nic.GetIPProperties().GatewayAddresses.First(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Address.ToString();
        var router = await Pinger.BurstAsync(gw, 12, 120);
        res.Add(new DiagResult("Your router", Fmt(router), router.LossPct > 1 || router.Jitter > 10 || router.Avg > 15 ? "bad" : "ok",
            router.LossPct > 1 || router.Jitter > 10 ? "Problems already between your PC and your router: Wi-Fi signal or interference. Move closer or use a cable." : "Your PC to router link is healthy."));

        var internet = await Pinger.BurstAsync("1.1.1.1", 12, 120);
        res.Add(new DiagResult("The internet", Fmt(internet), internet.LossPct > 2 || internet.Jitter > 20 ? "bad" : internet.Avg > 80 ? "warn" : "ok",
            internet.LossPct > 2 ? "Packets are lost after your router: your internet provider or a busy home network (downloads, streams)." : "Your internet connection looks fine."));

        var sw = Stopwatch.StartNew();
        try
        {
            await Dns.GetHostAddressesAsync("www.roblox.com");
            res.Add(new DiagResult("DNS (finding roblox.com)", $"{sw.ElapsedMilliseconds} ms", sw.ElapsedMilliseconds > 300 ? "warn" : "ok", sw.ElapsedMilliseconds > 300 ? "Slow name lookups delay joining. A faster DNS (like 1.1.1.1) can help." : ""));
        }
        catch
        {
            res.Add(new DiagResult("DNS (finding roblox.com)", "Failed", "bad", "Your PC can't look up roblox.com. Check your DNS settings."));
        }

        try
        {
            using var http = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(8) };
            sw.Restart();
            using var r = await http.GetAsync("https://www.roblox.com/", HttpCompletionOption.ResponseHeadersRead);
            res.Add(new DiagResult("Roblox website", $"{sw.ElapsedMilliseconds} ms", sw.ElapsedMilliseconds > 1500 ? "warn" : "ok", ""));
        }
        catch
        {
            res.Add(new DiagResult("Roblox website", "Unreachable", "bad", "roblox.com didn't answer. Roblox may be down (status.roblox.com) or something is blocking it."));
        }

        if (serverIp != null)
        {
            var srv = await Pinger.BurstAsync(serverIp, 12, 150);
            res.Add(new DiagResult("Game server", srv.Samples > 0 && srv.LossPct < 100 ? Fmt(srv) : "Doesn't answer ping",
                srv.LossPct >= 100 ? "warn" : srv.Avg > 150 || srv.Jitter > 25 ? "bad" : srv.Avg > 90 ? "warn" : "ok",
                srv.LossPct >= 100 ? "Many Roblox servers ignore ping, so this can't be measured here." : srv.Avg > 150 ? "This server is far away. Try Server Suggestions for a closer one." : ""));
        }
        return res;
    }

    static string Fmt(PingStats p) => p.LossPct >= 100 ? "No reply" : $"{p.Avg:0} ms, jitter {p.Jitter:0} ms, loss {p.LossPct:0.#}%";
}
