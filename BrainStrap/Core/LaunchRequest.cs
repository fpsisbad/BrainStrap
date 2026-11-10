using System.Web;

namespace BrainStrap.Core;

/// <summary>
/// A parsed roblox-player: link, e.g.
///   roblox-player:1+launchmode:play+gameinfo:TICKET+launchtime:1700000000000+placelauncherurl:https%3A%2F%2F...+browsertrackerid:123+robloxLocale:en_us+gameLocale:en_us
///
/// GameInfo is the one-time launch ticket. It is passed to RobloxPlayerBeta.exe exactly like Roblox's own
/// launcher does and is never logged, stored or sent anywhere else.
/// </summary>
internal sealed class LaunchRequest
{
    public string GameInfo { get; private set; } = "";
    public string PlaceLauncherUrl { get; private set; } = "";
    public string BrowserTrackerId { get; private set; } = "";
    public string LaunchTime { get; private set; } = "";
    public string RobloxLocale { get; private set; } = "en_us";
    public string GameLocale { get; private set; } = "en_us";
    public string LaunchMode { get; private set; } = "";

    /// <summary>The place being joined, if the launch URL names one (used for per-game profiles and Discord).</summary>
    public string? PlaceId { get; private set; }

    /// <summary>Returns null for anything that is not a plain "play" link we fully understand.
    /// The caller then hands the link to Roblox's own launcher untouched.</summary>
    public static LaunchRequest? Parse(string uri)
    {
        const string scheme = "roblox-player:";
        if (!uri.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return null;

        var req = new LaunchRequest();
        foreach (var token in uri[scheme.Length..].Split('+'))
        {
            var i = token.IndexOf(':');
            if (i <= 0) continue;                                   // the leading protocol version ("1") has no colon
            var key = token[..i].ToLowerInvariant();
            var val = token[(i + 1)..];
            switch (key)
            {
                case "launchmode": req.LaunchMode = val; break;
                case "gameinfo": req.GameInfo = val; break;
                case "launchtime": req.LaunchTime = val; break;
                case "placelauncherurl": req.PlaceLauncherUrl = Uri.UnescapeDataString(val); break;
                case "browsertrackerid": req.BrowserTrackerId = val; break;
                case "robloxlocale": req.RobloxLocale = val; break;
                case "gamelocale": req.GameLocale = val; break;
            }
        }

        if (!req.LaunchMode.Equals("play", StringComparison.OrdinalIgnoreCase)) return null;
        if (req.GameInfo.Length == 0 || req.PlaceLauncherUrl.Length == 0) return null;

        // Only ever pass a Roblox https address on to the player. Anything else goes to the official launcher.
        if (!Uri.TryCreate(req.PlaceLauncherUrl, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return null;
        var host = u.Host.ToLowerInvariant();
        if (!(host == "roblox.com" || host.EndsWith(".roblox.com") || host == "robloxlabs.com" || host.EndsWith(".robloxlabs.com")))
            return null;

        // a launch-time of "now" if the link had none
        if (req.LaunchTime.Length == 0) req.LaunchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        try
        {
            var pid = HttpUtility.ParseQueryString(u.Query)["placeId"];
            if (pid != null && pid.Length is > 0 and <= 20 && pid.All(char.IsAsciiDigit)) req.PlaceId = pid;
        }
        catch
        {
        }
        return req;
    }

    /// <summary>The command-line RobloxPlayerBeta.exe is started with. Mirrors Roblox's documented launch protocol.</summary>
    public List<string> BuildArgs()
    {
        var a = new List<string> { "--app", "-t", GameInfo, "-j", PlaceLauncherUrl };
        if (BrowserTrackerId.Length > 0) { a.Add("-b"); a.Add(BrowserTrackerId); }
        a.Add("--launchtime=" + LaunchTime);
        a.Add("--rloc"); a.Add(RobloxLocale);
        a.Add("--gloc"); a.Add(GameLocale);
        return a;
    }
}
