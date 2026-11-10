using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BrainStrap.Core.BrainAI;

// The local BrainAI engine. Same rules file (brainai.json) and same logic as the website demo (web/brainai.js),
// so a request typed on the website gets the same answer as in the app. Nothing here leaves this PC.

internal sealed class BrainGroup
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("w")] public Dictionary<string, int> W { get; set; } = new();
    [JsonPropertyName("hw")] public Dictionary<string, int> Hw { get; set; } = new();
    [JsonPropertyName("p")] public List<string> P { get; set; } = new();
}

internal sealed class BrainGenre
{
    [JsonPropertyName("match")] public List<string> Match { get; set; } = new();
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

internal sealed class BrainCond
{
    [JsonPropertyName("gte")] public Dictionary<string, int> Gte { get; set; } = new();
    [JsonPropertyName("lte")] public Dictionary<string, int> Lte { get; set; } = new();
}

internal sealed class BrainRule
{
    [JsonPropertyName("set")] public string Set { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("if")] public BrainCond If { get; set; } = new();
    [JsonPropertyName("why")] public string Why { get; set; } = "";
}

internal sealed class BrainRules
{
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("groups")] public List<BrainGroup> Groups { get; set; } = new();
    [JsonPropertyName("genres")] public List<BrainGenre> Genres { get; set; } = new();
    [JsonPropertyName("rules")] public List<BrainRule> Rules { get; set; } = new();
    [JsonPropertyName("impact")] public Dictionary<string, Dictionary<string, double[]>> Impact { get; set; } = new();
}

internal sealed record BrainParse(List<string> Groups, Dictionary<string, int> W, Dictionary<string, int> Hw, Dictionary<string, string> Explicit);
internal sealed record BrainChange(string Id, string Name, string From, string To, string Why, string? Manual = null);
internal sealed record BrainResult(Dictionary<string, string> Values, List<BrainChange> Changes);
internal sealed record BrainPrediction(int Lo, int Hi, int CeilingFrom, int CeilingTo, int Refresh, string Confidence)
{
    public string Describe()
    {
        var text = Lo == 0 && Hi == 0 ? "No change to average FPS expected." :
            $"Estimated average FPS change: {Sign(Lo)}% to {Sign(Hi)}%.";
        if (CeilingTo != CeilingFrom) text += $" FPS limit {CeilingFrom} → {CeilingTo}.";
        text += Confidence == "medium"
            ? " Based on typical hardware and your own benchmarks."
            : " Rough estimate from typical hardware: run a benchmark before and after to measure it on your PC.";
        return text;
    }

    static string Sign(int v) => v > 0 ? "+" + v : v.ToString();
}

internal static class Engine
{
    static BrainRules? cached;

    public static BrainRules Rules
    {
        get
        {
            if (cached != null) return cached;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("brainai.json")
                               ?? throw new InvalidOperationException("brainai.json is missing from the build.");
            cached = JsonSerializer.Deserialize<BrainRules>(stream) ?? new BrainRules();
            return cached;
        }
    }

    static readonly Regex Neg = new(@"(?:\bnot|\bdon'?t|\bdo not|\bno|\bnever|\blower|\bless|\bwithout|\breduce|\bworse)\s+(?:\w+\s+){0,2}$", RegexOptions.Compiled);

    static string Norm(string t) =>
        " " + Regex.Replace((t ?? "").ToLowerInvariant().Replace('‘', '\'').Replace('’', '\''), @"\s+", " ").Trim() + " ";

    static bool IsLetter(string s, int i) => i >= 0 && i < s.Length && s[i] >= 'a' && s[i] <= 'z';

    static bool Has(string text, string phrase)
    {
        var prefix = phrase.EndsWith('*');
        var p = prefix ? phrase[..^1] : phrase;
        var from = 0;
        while (true)
        {
            var i = text.IndexOf(p, from, StringComparison.Ordinal);
            if (i < 0) return false;
            from = i + 1;
            if (IsLetter(text, i - 1)) continue;
            var a = i + p.Length;
            if (!prefix && IsLetter(text, a) && !(text[a] == 's' && !IsLetter(text, a + 1))) continue;
            var before = text.Substring(Math.Max(0, i - 28), i - Math.Max(0, i - 28));
            if (Neg.IsMatch(before) && !p.StartsWith("don") && !p.StartsWith("do not")) continue;
            return true;
        }
    }

    public static BrainParse Parse(string raw)
    {
        var text = Norm(raw);
        var w = new Dictionary<string, int> { ["perf"] = 0, ["quality"] = 0, ["battery"] = 0, ["latency"] = 0 };
        var hw = new Dictionary<string, int>();
        var groups = new List<string>();
        var ex = new Dictionary<string, string>();

        foreach (var g in Rules.Groups)
        {
            if (!g.P.Any(p => Has(text, p))) continue;
            groups.Add(g.Id);
            foreach (var kv in g.W) w[kv.Key] = w.GetValueOrDefault(kv.Key) + kv.Value;
            foreach (var kv in g.Hw) hw[kv.Key] = kv.Value;
        }
        if (groups.Contains("ugly-ok") && groups.Contains("pretty")) { groups.Remove("pretty"); w["quality"] -= 2; }
        foreach (var k in w.Keys.ToList()) w[k] = Math.Clamp(w[k], -3, 6);

        var m = Regex.Match(text, @"\b(?:cap|limit|lock|target|unlock|set|run at|aim for)\b[^.\d]{0,24}?(\d{2,4})\s*(?:fps|hz|frames)?");
        if (!m.Success) m = Regex.Match(text, @"\b(\d{2,4})\s*(?:fps|hz)\s*(?:cap|limit|lock|target|max)\b");
        if (m.Success) ex["fps"] = Math.Clamp(int.Parse(m.Groups[1].Value), 10, 1000).ToString();
        if (Regex.IsMatch(text, @"\b(uncap|uncapped|unlimited fps|no fps cap|remove the cap|unlock fps|unlock the fps)\b")) ex["fps"] = "$high";
        if (Regex.IsMatch(text, @"\bvulkan\b")) ex["api"] = "vulkan";
        else if (Regex.IsMatch(text, @"\b(dx11|d3d11|directx ?11|direct3d)\b")) ex["api"] = "d3d11";
        else if (Regex.IsMatch(text, @"\bopengl\b")) ex["api"] = "opengl";
        if (Regex.IsMatch(text, @"\b(fps counter|show (?:my |the )?fps|see (?:my |the )?fps)\b")) ex["showfps"] = "true";
        if (Regex.IsMatch(text, @"\b(no|without|turn off|disable|remove) shadows\b")) ex["shadows"] = "true";
        if (Regex.IsMatch(text, @"\b(no|without|turn off|disable|remove) (?:menu )?blur\b")) ex["blur"] = "true";
        if (Regex.IsMatch(text, @"\b(no|without|turn off|disable|remove) (?:bloom|post[- ]?processing|post fx|effects)\b")) ex["postfx"] = "true";
        if (Regex.IsMatch(text, @"\bfuture lighting\b")) ex["lighting"] = "future";
        else if (Regex.IsMatch(text, @"\bvoxel\b")) ex["lighting"] = "voxel";
        return new BrainParse(groups, w, hw, ex);
    }

    /// <summary>Weights from the request plus facts about the PC. Real hardware detection wins over hints in the text.</summary>
    public static Dictionary<string, int> Context(BrainParse p, PcInfo? pc)
    {
        var ctx = new Dictionary<string, int>(p.W);
        if (pc != null)
        {
            ctx["tier"] = pc.Tier;
            ctx["laptop"] = pc.Laptop ? 1 : 0;
            ctx["refresh"] = pc.Refresh;
        }
        else
        {
            var tier = 1;
            if (p.Hw.TryGetValue("tierMax", out var mx)) tier = Math.Min(tier, mx);
            if (p.Hw.TryGetValue("tierMin", out var mn)) tier = Math.Max(tier, mn);
            ctx["tier"] = tier;
            ctx["laptop"] = p.Hw.GetValueOrDefault("laptop");
            ctx["refresh"] = 60;
        }
        return ctx;
    }

    static bool Cond(BrainCond c, Dictionary<string, int> ctx)
    {
        foreach (var kv in c.Gte) if (!(ctx.GetValueOrDefault(kv.Key) >= kv.Value)) return false;
        foreach (var kv in c.Lte) if (!(ctx.GetValueOrDefault(kv.Key) <= kv.Value)) return false;
        return true;
    }

    static string Token(string v, Dictionary<string, int> ctx)
    {
        var refresh = ctx.GetValueOrDefault("refresh", 60);
        if (refresh <= 0) refresh = 60;
        return v switch
        {
            "$refresh" => Math.Clamp(refresh, 30, 1000).ToString(),
            "$high" => Math.Min(1000, Math.Max(240, refresh * 2)).ToString(),
            _ => v,
        };
    }

    public static string Label(FlagDef d, string? v)
    {
        if (string.IsNullOrEmpty(v)) return "Default";
        if (d.Type == "bool") return v == "true" ? "On" : "Off";
        if (d.Type == "choice") return d.Options.FirstOrDefault(o => o.Value == v)?.Label ?? v;
        return v;
    }

    /// <summary>Only values the catalog allows survive (also used for cloud answers and imported profiles).</summary>
    public static string? Clean(FlagDef d, string? v)
    {
        if (v == null) return null;
        switch (d.Type)
        {
            case "bool": return v == "true" ? "true" : null;
            case "int": return int.TryParse(v, out var n) ? Math.Clamp(n, d.Min, d.Max).ToString() : null;
            case "choice": return d.Options.Any(o => o.Value == v) ? v : null;
            default: return null;
        }
    }

    public static BrainResult Recommend(Dictionary<string, int> ctx, Dictionary<string, string> explicitValues, Dictionary<string, string> current,
                                        Dictionary<string, string>? whyOverride = null, bool runRules = true)
    {
        var catalog = FlagCatalog.Load();
        var values = new Dictionary<string, string>();
        var why = new Dictionary<string, string>();
        foreach (var r in runRules ? Rules.Rules : new List<BrainRule>())
        {
            if (values.ContainsKey(r.Set) || !Cond(r.If, ctx)) continue;
            values[r.Set] = Token(r.Value, ctx);
            why[r.Set] = r.Why.Replace("$refresh", ctx.GetValueOrDefault("refresh", 60).ToString());
        }
        foreach (var kv in explicitValues)
        {
            values[kv.Key] = Token(kv.Value, ctx);
            why[kv.Key] = whyOverride != null && whyOverride.TryGetValue(kv.Key, out var w) ? w : "You asked for this.";
        }

        var outValues = new Dictionary<string, string>();
        var changes = new List<BrainChange>();
        foreach (var d in catalog.Flags)
        {
            var v = Clean(d, values.GetValueOrDefault(d.Id));
            var cur = current.GetValueOrDefault(d.Id);
            if (v != null) outValues[d.Id] = v;
            else if (cur != null) outValues[d.Id] = cur;
            if (v != null && v != cur)
                changes.Add(new BrainChange(d.Id, d.Name, Label(d, cur), Label(d, v), why.GetValueOrDefault(d.Id, ""),
                    d.Allowlisted ? null : (d.Manual ?? "Roblox ignores this flag now.").Replace("{value}", Label(d, v))));
        }
        return new BrainResult(outValues, changes);
    }

    static double[] Impact(string id, string? v) =>
        v != null && Rules.Impact.TryGetValue(id, out var t) && t.TryGetValue(v, out var r) && r.Length == 2 ? r : new double[] { 0, 0 };

    public static BrainPrediction Predict(Dictionary<string, string> current, Dictionary<string, string> values, int refresh, bool measured)
    {
        var los = new List<double>();
        var his = new List<double>();
        foreach (var d in FlagCatalog.Load().Flags)
        {
            if (d.Id == "fps") continue;
            var a = Impact(d.Id, current.GetValueOrDefault(d.Id));
            var b = Impact(d.Id, values.GetValueOrDefault(d.Id));
            double d1 = b[0] - a[0], d2 = b[1] - a[1];
            if (d1 == 0 && d2 == 0) continue;
            los.Add(Math.Min(d1, d2));
            his.Add(Math.Max(d1, d2));
        }
        static double Combine(List<double> xs) =>
            xs.OrderByDescending(x => Math.Abs(x)).Select((x, i) => x * Math.Pow(0.8, i)).Sum();
        static int Clamp(double x) => (int)Math.Clamp(Math.Round(x), -80, 150);
        static int Cap(string? v) => v != null && int.TryParse(v, out var n) ? n : 60;
        return new BrainPrediction(Clamp(Combine(los)), Clamp(Combine(his)), Cap(current.GetValueOrDefault("fps")), Cap(values.GetValueOrDefault("fps")),
            refresh, measured ? "medium" : "low");
    }

    /// <summary>Profile generator: turns a Roblox genre into a plain-language request for the same engine.</summary>
    public static string GenreText(string? genre)
    {
        var g = (genre ?? "").ToLowerInvariant();
        return Rules.Genres.FirstOrDefault(x => x.Match.Any(m => g.Contains(m)))?.Text ?? "smooth";
    }

    /// <summary>One call for the whole flow: request text -> recommendation + prediction.</summary>
    public static (BrainParse parse, BrainResult result, BrainPrediction prediction) Ask(string request, PcInfo? pc, Dictionary<string, string> current, bool measured)
    {
        var p = Parse(request);
        var ctx = Context(p, pc);
        var r = Recommend(ctx, p.Explicit, current);
        return (p, r, Predict(current, r.Values, ctx.GetValueOrDefault("refresh", 60), measured));
    }
}
