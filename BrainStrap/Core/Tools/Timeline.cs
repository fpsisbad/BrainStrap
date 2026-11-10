using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrainStrap.Core;

internal sealed record TimelineEntry(string File, DateTime When, string Summary);

/// <summary>
/// Settings Timeline + Instant Rollback: every saved change is kept as a snapshot (the newest 200), with a short
/// description of what changed. Secrets (API key, private-server links) are left out of snapshots.
/// </summary>
internal static class Timeline
{
    static string Dir => Directory.CreateDirectory(Path.Combine(Paths.State, "timeline")).FullName;
    static string? lastJson;
    static readonly object Gate = new();

    public static void Record(Settings s, string? why)
    {
        try
        {
            var json = Scrub(SettingsStore.ToJson(s));
            lock (Gate)
            {
                lastJson ??= Latest()?.json;
                if (json == lastJson) return;
                var summary = why ?? Describe(lastJson, json);
                var name = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.json";
                var wrapper = new JsonObject { ["when"] = DateTime.Now.ToString("o"), ["summary"] = summary, ["settings"] = JsonNode.Parse(json) };
                File.WriteAllText(Path.Combine(Dir, name), wrapper.ToJsonString());
                lastJson = json;
                foreach (var old in Directory.GetFiles(Dir, "*.json").OrderByDescending(f => f).Skip(200)) File.Delete(old);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("timeline: " + ex.Message);
        }
    }

    static (string file, string json)? Latest()
    {
        var f = Directory.GetFiles(Dir, "*.json").OrderByDescending(x => x).FirstOrDefault();
        if (f == null) return null;
        var node = JsonNode.Parse(File.ReadAllText(f));
        return (f, node?["settings"]?.ToJsonString() ?? "");
    }

    public static List<TimelineEntry> List()
    {
        var list = new List<TimelineEntry>();
        foreach (var f in Directory.GetFiles(Dir, "*.json").OrderByDescending(x => x).Take(200))
        {
            try
            {
                var n = JsonNode.Parse(File.ReadAllText(f));
                list.Add(new TimelineEntry(f, DateTime.Parse(n?["when"]?.GetValue<string>() ?? DateTime.MinValue.ToString("o")), n?["summary"]?.GetValue<string>() ?? ""));
            }
            catch
            {
            }
        }
        return list;
    }

    /// <summary>Puts the settings back to a snapshot. The current state is snapshotted first, so a rollback can be undone too.</summary>
    public static Settings Restore(string file)
    {
        var n = JsonNode.Parse(File.ReadAllText(file));
        var restored = SettingsStore.FromJson(n?["settings"]?.ToJsonString() ?? "{}");
        var current = AppState.S;
        // secrets were never in the snapshot: keep the current ones
        restored.Ai.KeyProtected = current.Ai.KeyProtected;
        foreach (var fav in restored.Servers.Favorites)
            fav.PrivateLinkProtected = current.Servers.Favorites.FirstOrDefault(c => c.PlaceId == fav.PlaceId && c.Name == fav.Name)?.PrivateLinkProtected;
        restored.OriginalHandlerCommand = current.OriginalHandlerCommand;
        Record(current, "Before rollback");
        AppState.S = restored;
        SettingsStore.Save(restored);
        Record(restored, $"Rolled back to {File.GetLastWriteTime(file):d MMM, HH:mm}");
        return restored;
    }

    static string Scrub(string json)
    {
        var n = JsonNode.Parse(json)!.AsObject();
        if (n["Ai"] is JsonObject ai) ai.Remove("KeyProtected");
        if (n["Servers"]?["Favorites"] is JsonArray favs)
            foreach (var f in favs) if (f is JsonObject o) o.Remove("PrivateLinkProtected");
        return n.ToJsonString();
    }

    /// <summary>"FPS cap 144 → 240 in Default", "Theme silk → ember" ...</summary>
    static string Describe(string? before, string after)
    {
        if (before == null) return "First snapshot";
        var a = Flatten(JsonNode.Parse(before));
        var b = Flatten(JsonNode.Parse(after));
        var changes = new List<string>();
        foreach (var k in a.Keys.Union(b.Keys))
        {
            a.TryGetValue(k, out var va);
            b.TryGetValue(k, out var vb);
            if (va == vb) continue;
            changes.Add(Human(k, va, vb));
        }
        if (changes.Count == 0) return "No visible change";
        return changes.Count <= 3 ? string.Join(" · ", changes) : string.Join(" · ", changes.Take(3)) + $" · +{changes.Count - 3} more";
    }

    static string Human(string key, string? from, string? to)
    {
        var cat = FlagCatalog.Load();
        var parts = key.Split('.');
        if (parts.Length >= 4 && parts[0] == "Profiles" && parts[2] == "Values")
        {
            var def = cat.Flags.FirstOrDefault(f => f.Id == parts[3]);
            var name = def?.Name ?? parts[3];
            string L(string? v) => def != null ? BrainAI.Engine.Label(def, v) : v ?? "Default";
            return $"{name} {L(from)} → {L(to)} ({parts[1]})";
        }
        if (parts.Length >= 3 && parts[0] == "Profiles" && parts[2] == "Custom") return $"Custom flag {parts.ElementAtOrDefault(3)} {(to == null ? "removed" : "set")}";
        if (parts[0] == "Profiles" && parts.Length == 2) return to == null ? $"Profile {parts[1]} deleted" : $"Profile {parts[1]} created";
        var label = key.Replace("Studio.", "").Replace("Life.", "").Replace("Perf.", "").Replace("Servers.", "").Replace("Hud.", "HUD ");
        return $"{label}: {Short(from)} → {Short(to)}";
    }

    static string Short(string? v) => v == null ? "—" : v.Length > 24 ? v[..24] + "…" : v;

    static Dictionary<string, string?> Flatten(JsonNode? n, string prefix = "", Dictionary<string, string?>? acc = null)
    {
        acc ??= new Dictionary<string, string?>();
        switch (n)
        {
            case JsonObject o:
                if (o.Count == 0 && prefix.Length > 0) acc[prefix] = "{}";
                foreach (var kv in o) Flatten(kv.Value, prefix.Length == 0 ? kv.Key : prefix + "." + kv.Key, acc);
                break;
            case JsonArray arr:
                acc[prefix] = arr.ToJsonString();
                break;
            default:
                acc[prefix] = n?.ToJsonString().Trim('"');
                break;
        }
        return acc;
    }
}
