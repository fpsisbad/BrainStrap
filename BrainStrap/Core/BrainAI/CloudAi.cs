using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrainStrap.Core.BrainAI;

/// <summary>
/// Optional cloud mode for the AI Settings Builder. Off by default; uses the user's own Anthropic API key (stored with
/// Windows DPAPI). Only the request text, a hardware summary and the setting catalog are sent: no account, no logs,
/// no files. The answer is never trusted as-is: every value is checked against the catalog, exactly like the local engine.
/// </summary>
internal static class CloudAi
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(40) };

    public static bool Ready(Settings s) => s.Ai.CloudEnabled && !string.IsNullOrEmpty(Secret.Unprotect(s.Ai.KeyProtected));

    public static async Task<(BrainResult? result, string? error)> AskAsync(Settings s, string request, PcInfo pc, Dictionary<string, string> current)
    {
        var key = Secret.Unprotect(s.Ai.KeyProtected);
        if (string.IsNullOrEmpty(key)) return (null, "Add an API key first.");
        var cat = FlagCatalog.Load();

        var options = new JsonObject();
        foreach (var d in cat.Flags)
        {
            options[d.Id] = d.Type switch
            {
                "bool" => new JsonObject { ["name"] = d.Name, ["values"] = new JsonArray("true") },
                "int" => new JsonObject { ["name"] = d.Name, ["range"] = $"{d.Min}-{d.Max}" },
                _ => new JsonObject { ["name"] = d.Name, ["values"] = new JsonArray(d.Options.Select(o => (JsonNode)o.Value).ToArray()) },
            };
        }

        var system =
            "You configure graphics settings for the Roblox Windows player through the launcher BrainStrap. " +
            "You may ONLY use the setting ids and values listed in OPTIONS; anything else is discarded. Leaving a setting out keeps Roblox's default. " +
            "Never suggest anything that changes gameplay, physics or networking. " +
            "Reply with JSON only, no prose around it, in this shape: " +
            "{\"values\":{\"<id>\":\"<value>\"},\"why\":{\"<id>\":\"<one short sentence>\"},\"summary\":\"<one sentence>\"}";
        var user = new StringBuilder()
            .AppendLine("OPTIONS: " + options.ToJsonString())
            .AppendLine($"PC: {pc.Summary}; performance tier {pc.Tier} of 3; {(pc.OnBattery ? "on battery" : "plugged in")}.")
            .AppendLine("CURRENT: " + JsonSerializer.Serialize(current))
            .AppendLine("REQUEST: " + request.Trim())
            .ToString();

        var body = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(s.Ai.Model) ? "claude-haiku-5-5" : s.Ai.Model.Trim(),
            ["max_tokens"] = 900,
            ["system"] = system,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = user }),
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("x-api-key", key);
            req.Headers.Add("anthropic-version", "2023-06-01");
            using var resp = await Http.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return (null, resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "The API key was rejected." : $"The AI service answered {(int)resp.StatusCode}.");

            using var doc = JsonDocument.Parse(text);
            var answer = string.Concat(doc.RootElement.GetProperty("content").EnumerateArray()
                .Where(b => b.TryGetProperty("type", out var t) && t.GetString() == "text")
                .Select(b => b.GetProperty("text").GetString()));
            var start = answer.IndexOf('{');
            var end = answer.LastIndexOf('}');
            if (start < 0 || end <= start) return (null, "The AI didn't return settings.");
            using var parsed = JsonDocument.Parse(answer[start..(end + 1)]);

            var values = new Dictionary<string, string>();
            var why = new Dictionary<string, string>();
            if (parsed.RootElement.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Object)
                foreach (var p in vals.EnumerateObject())
                {
                    var def = cat.Flags.FirstOrDefault(d => d.Id == p.Name);
                    var raw = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText();
                    var clean = def == null ? null : Engine.Clean(def, raw?.ToLowerInvariant() == "true" ? "true" : raw);
                    if (clean != null) values[p.Name] = clean;
                }
            if (parsed.RootElement.TryGetProperty("why", out var w) && w.ValueKind == JsonValueKind.Object)
                foreach (var p in w.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) why[p.Name] = Trim(p.Value.GetString() ?? "", 220);

            var ctx = new Dictionary<string, int> { ["refresh"] = pc.Refresh };
            return (Engine.Recommend(ctx, values, current, why, runRules: false), null);
        }
        catch (TaskCanceledException)
        {
            return (null, "The AI service took too long.");
        }
        catch (Exception ex)
        {
            Logger.Warn("cloud ai: " + ex.GetType().Name);
            return (null, "Couldn't reach the AI service.");
        }
    }

    static string Trim(string s, int max) => s.Length > max ? s[..max] + "…" : s;
}
