using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BrainStrap.Core;

internal sealed class FlagOption
{
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("set")] public Dictionary<string, JsonElement> Set { get; set; } = new();
}

internal sealed class FlagDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("group")] public string Group { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("desc")] public string Desc { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "bool";   // bool | int | choice
    [JsonPropertyName("flag")] public string Flag { get; set; } = "";
    [JsonPropertyName("on")] public JsonElement? On { get; set; }            // value written when a bool toggle is on
    [JsonPropertyName("min")] public int Min { get; set; }
    [JsonPropertyName("max")] public int Max { get; set; } = int.MaxValue;
    [JsonPropertyName("chips")] public List<int> Chips { get; set; } = new();
    [JsonPropertyName("options")] public List<FlagOption> Options { get; set; } = new();
    [JsonPropertyName("allowlisted")] public bool Allowlisted { get; set; } = true;
    [JsonPropertyName("manual")] public string? Manual { get; set; }
    [JsonPropertyName("doc")] public string Doc { get; set; } = "";
    [JsonPropertyName("risk")] public string Risk { get; set; } = "low";
}

internal sealed class FlagPreset
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("desc")] public string Desc { get; set; } = "";
    [JsonPropertyName("values")] public Dictionary<string, string> Values { get; set; } = new();
}

internal sealed class Catalog
{
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("note")] public string Note { get; set; } = "";
    [JsonPropertyName("updated")] public string Updated { get; set; } = "";
    [JsonPropertyName("allowlist")] public List<string> Allowlist { get; set; } = new();
    [JsonPropertyName("allowlistSource")] public string AllowlistSource { get; set; } = "";
    [JsonPropertyName("flags")] public List<FlagDef> Flags { get; set; } = new();
    [JsonPropertyName("presets")] public List<FlagPreset> Presets { get; set; } = new();
}

internal static class FlagCatalog
{
    static Catalog? cached;

    /// <summary>Loads catalog.json from inside the exe (it is an embedded resource).</summary>
    public static Catalog Load()
    {
        if (cached != null) return cached;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("catalog.json")
                           ?? throw new InvalidOperationException("catalog.json is missing from the build.");
        cached = JsonSerializer.Deserialize<Catalog>(stream) ?? new Catalog();
        return cached;
    }

    // Roblox flag names always start with one of these prefixes.
    static readonly Regex NameRule =
        new(@"^(FFlag|DFFlag|SFFlag|FInt|DFInt|SFInt|FString|DFString|SFString|FLog|DFLog|SFLog)[A-Za-z0-9_]{2,120}$",
            RegexOptions.Compiled);

    public static bool IsValidFlagName(string name) => NameRule.IsMatch(name);

    /// <summary>Roblox's official allowlist (29 Sep 2025): the only local flags the Roblox Player reads.</summary>
    public static bool IsAllowlisted(Catalog cat, string flagName) => cat.Allowlist.Contains(flagName, StringComparer.Ordinal);

    /// <summary>Flag Risk Rating: Supported · Debug (may change) · Ignored by Roblox · Blocked.</summary>
    public static (string level, string label) Risk(Catalog cat, string flagName)
    {
        if (FlagValidator.Check(flagName, "0").Message.StartsWith("Could change")) return ("bad", "Blocked: could change gameplay");
        if (!IsAllowlisted(cat, flagName)) return ("warn", "Ignored by Roblox (not on the allowlist)");
        return flagName.Contains("Debug") ? ("info", "Supported · debug flag, may change") : ("ok", "Supported");
    }

    /// <summary>Turns a profile into the flag-name -> value map that goes into ClientAppSettings.json.</summary>
    public static SortedDictionary<string, object> Resolve(Catalog cat, FlagProfile profile, string format, bool includeIgnored = false)
    {
        var map = new SortedDictionary<string, object>(StringComparer.Ordinal);

        foreach (var def in cat.Flags)
        {
            if (!profile.Values.TryGetValue(def.Id, out var v) || string.IsNullOrWhiteSpace(v)) continue;
            if (!def.Allowlisted && !includeIgnored) continue;          // Roblox ignores it: don't pretend

            switch (def.Type)
            {
                case "bool":
                    if (v == "true" && def.Flag.Length > 0)
                        map[def.Flag] = def.On.HasValue ? FromJson(def.On.Value) : true;
                    break;

                case "int":
                    if (int.TryParse(v, out var n) && def.Flag.Length > 0)
                        map[def.Flag] = Math.Clamp(n, def.Min, def.Max);
                    break;

                case "choice":
                    var opt = def.Options.FirstOrDefault(o => o.Value == v);
                    if (opt != null)
                        foreach (var kv in opt.Set) map[kv.Key] = FromJson(kv.Value);
                    break;
            }
        }

        foreach (var kv in profile.Custom)
            if (IsValidFlagName(kv.Key) && (includeIgnored || IsAllowlisted(cat, kv.Key))) map[kv.Key] = ParseLoose(kv.Value);

        if (format != "string") return map;

        // some Roblox builds want every value as text ("True", "144")
        var asText = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var kv in map)
            asText[kv.Key] = kv.Value is bool b ? (b ? "True" : "False") : Convert.ToString(kv.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        return asText;
    }

    static object FromJson(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        _ => e.ToString() ?? "",
    };

    public static object ParseLoose(string raw)
    {
        raw = raw.Trim();
        if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        if (long.TryParse(raw, out var n)) return n;
        return raw;
    }
}
