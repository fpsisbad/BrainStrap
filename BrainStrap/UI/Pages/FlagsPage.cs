using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using BrainStrap.Core;
using BrainStrap.Core.Perf;
using BrainStrap.Core.BrainAI;
using Microsoft.Win32;

namespace BrainStrap.UI.Pages;

/// <summary>BrainFlags: allowlist-aware FastFlag editor with AI, JSON editor, explorer, conflicts, presets, history, compare, playground.</summary>
internal static class FlagsPage
{
    public static FrameworkElement Build(MainWindow host)
    {
        var s = AppState.S;
        var cat = FlagCatalog.Load();
        var prof = s.Current;
        var page = Ui.Page("BrainFlags", "Roblox only reads flags on its official allowlist (since 29 Sep 2025). BrainFlags shows exactly which ones work, explains each in plain English, and never pretends the rest still do.", out var body);
        void Changed(string why) { AppState.Save(why); host.Refresh(); }

        // ── profile + status strip ──
        var resolved = FlagCatalog.Resolve(cat, prof, s.FlagFormat, s.Platform.WriteIgnoredFlags);
        var ignored = cat.Flags.Count(d => !d.Allowlisted && prof.Values.ContainsKey(d.Id)) + prof.Custom.Keys.Count(k => !FlagCatalog.IsAllowlisted(cat, k));
        body.Children.Add(Ui.Card(
            Ui.Lbl("Profile"),
            Ui.Chips(s.Profiles.Keys.Select(k => (k, k)), s.ActiveProfile, v => { s.ActiveProfile = v; ModsPage.ApplyProfilePack(s, v); Themes.Apply(s); host.ApplyBackground(); Changed("Profile " + v); }),
            Ui.Cols(3,
                Ui.Stat(resolved.Count.ToString(), "flags Roblox will read", "Ok"),
                Ui.Stat(ignored.ToString(), "ignored by Roblox", ignored > 0 ? "Warn" : "Ink"),
                Ui.Stat(cat.Allowlist.Count.ToString(), "flags on Roblox's allowlist")),
            Ui.Buttons(
                Ui.Ghost("New profile", () => { var n = Prompt.Ask(host, "New profile", "Name"); if (n == null || s.Profiles.ContainsKey(n)) return; s.Profiles[n] = new FlagProfile { Values = new(prof.Values) }; s.ActiveProfile = n; Changed("Profile " + n + " created"); }),
                s.ActiveProfile != "Default" ? Ui.Ghost("Delete profile", () => { s.Profiles.Remove(s.ActiveProfile); s.ActiveProfile = "Default"; Changed("Profile deleted"); }) : (UIElement)new Border())));

        // ── BrainFlag AI ──
        var ask = Ui.Field("", 560);
        var aiOut = new StackPanel();
        void Ask()
        {
            aiOut.Children.Clear();
            var text = ask.Text.Trim();
            if (text.Length == 0) return;
            var name = text.Split(' ', ',', '"', ':', '=').FirstOrDefault(FlagCatalog.IsValidFlagName);
            if (name != null)
            {
                var def = cat.Flags.FirstOrDefault(d => d.Flag == name || d.Options.Any(o => o.Set.ContainsKey(name)));
                var (lvl, label) = FlagCatalog.Risk(cat, name);
                aiOut.Children.Add(Ui.Status(lvl, $"{name} · {label}", def != null ? $"{def.Name}: {def.Doc}" : Explain(name)));
                return;
            }
            var (_, r, p) = Engine.Ask(text, PcAnalyzer.Detect(), new Dictionary<string, string>(prof.Values), History.Benchmarks().Count > 0);
            BrainAiPage.ShowPreview(host, aiOut, "BrainFlag AI suggests", r, p);
        }
        ask.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Ask(); };
        body.Children.Add(Ui.Card(Ui.Lbl("BrainFlag AI"), Ui.Title("Ask about a flag, or describe what you want", 15),
            Ui.Desc("Paste a flag name to get it explained with its allowlist status, or say \"smoother on my laptop\" for supported settings."), new Border { Height = 10 },
            Ui.Row(ask, new Border { Width = 10 }, Ui.Pill("Ask", Ask)), aiOut));

        // ── Visual editor (allowlisted) ──
        var editor = new StackPanel();
        foreach (var grp in cat.Flags.Where(d => d.Allowlisted).GroupBy(d => d.Group))
        {
            editor.Children.Add(Ui.Lbl(grp.Key, 6));
            foreach (var d in grp) editor.Children.Add(Row(d, prof, Changed));
        }
        body.Children.Add(Ui.Card(Ui.Lbl("Visual flag editor"),
            Ui.Lbl("Presets"),
            Ui.Buttons(cat.Presets.Select(pr => (UIElement)Ui.Ghost(pr.Name, () => { prof.Values = new(pr.Values); Changed("Preset " + pr.Name); }))
                .Concat(s.Platform.FlagPresets.Keys.Select(k => (UIElement)Ui.Ghost("★ " + k, () => { prof.Values = new(s.Platform.FlagPresets[k]); Changed("Preset " + k); })))
                .Append(Ui.Ghost("Roblox defaults", () => { prof.Values.Clear(); Changed("Roblox defaults"); }))
                .Append(Ui.Pill("Save as preset", () => { var n = Prompt.Ask(host, "Save preset", "Preset name"); if (n != null) { s.Platform.FlagPresets[n] = new(prof.Values); Changed("Preset saved: " + n); } })).ToArray()),
            editor));

        // ── ignored by Roblox ──
        var ign = new StackPanel();
        foreach (var d in cat.Flags.Where(d => !d.Allowlisted))
            ign.Children.Add(Ui.Status("warn", d.Name + (prof.Values.ContainsKey(d.Id) ? $" · set to {Engine.Label(d, prof.Values[d.Id])}" : ""), d.Desc + (d.Manual != null && d.Id == "fps" ? " " + d.Manual.Replace("{value}", "your screen's refresh rate") : ""),
                prof.Values.ContainsKey(d.Id) ? Ui.Ghost("Remove", () => { prof.Values.Remove(d.Id); Changed("Removed ignored " + d.Name); }) : null));
        ign.Children.Add(Ui.ToggleRow("Write ignored flags anyway", "Off by default. Roblox discards them, so this only helps if Roblox adds them back to the allowlist.", s.Platform.WriteIgnoredFlags,
            on => { s.Platform.WriteIgnoredFlags = on; Changed("Write ignored flags " + (on ? "on" : "off")); }));
        body.Children.Add(Ui.Card(Ui.Lbl("Flag Compatibility · not on Roblox's allowlist"), ign));

        // ── Advanced JSON editor + conflict scanner ──
        var json = Ui.Field(FlagWriter.Render(FlagCatalog.Resolve(cat, prof, s.FlagFormat, true)), double.NaN, true);
        json.AcceptsReturn = true; json.TextWrapping = TextWrapping.Wrap; json.MinHeight = 160;
        var jsonOut = new StackPanel();
        body.Children.Add(Ui.Card(Ui.Lbl("Advanced JSON editor · Flag Conflict Scanner"), json,
            Ui.Buttons(
                Ui.Ghost("Scan", () => { jsonOut.Children.Clear(); foreach (var i in Scan(cat, json.Text)) jsonOut.Children.Add(Ui.Status(i.level, i.title, i.detail)); }),
                Ui.Pill("Apply to profile", () =>
                {
                    var (vals, custom, problems) = FromJson(cat, json.Text);
                    if (problems.Count > 0 && MessageBox.Show(host, string.Join("\n", problems) + "\n\nApply the rest?", "BrainFlags", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    prof.Values = vals; prof.Custom = custom;
                    Changed("JSON editor");
                })),
            jsonOut));

        // ── Flag Explorer + Documentation + Risk ratings ──
        var q = Ui.Field("", 320);
        var list = new StackPanel();
        void Explore()
        {
            list.Children.Clear();
            var term = q.Text.Trim().ToLowerInvariant();
            var names = cat.Allowlist.Concat(cat.Flags.Where(d => !d.Allowlisted).Select(d => d.Flag).Where(f => f.Length > 0)).Distinct();
            foreach (var n in names.Where(n => term.Length == 0 || n.ToLowerInvariant().Contains(term) || Doc(cat, n).ToLowerInvariant().Contains(term)).Take(30))
            {
                var (lvl, label) = FlagCatalog.Risk(cat, n);
                list.Children.Add(Ui.Status(lvl, n, label + " · " + Doc(cat, n)));
            }
        }
        q.TextChanged += (_, _) => Explore();
        Explore();
        body.Children.Add(Ui.Card(Ui.Lbl("Flag Explorer · Documentation · Risk ratings"), Ui.Row(Ui.Text("Search  ", 13, "Muted"), q), new Border { Height = 10 }, list,
            Ui.Buttons(Ui.Ghost("Roblox's allowlist announcement", () => Ui.Open(cat.AllowlistSource)))));

        // ── Compare ──
        var a = new DarkCombo { Width = 200, Margin = new Thickness(0, 0, 10, 0) };
        var b = new DarkCombo { Width = 200 };
        foreach (var k in s.Profiles.Keys.Concat(s.Platform.FlagPresets.Keys.Select(x => "★ " + x)).Concat(cat.Presets.Select(x => "Preset: " + x.Name))) { a.Items.Add(k); b.Items.Add(k); }
        a.SelectedIndex = 0; b.SelectedIndex = Math.Min(1, b.Items.Count - 1);
        var cmp = new StackPanel();
        Dictionary<string, string> Vals(string key) =>
            key.StartsWith("★ ") ? s.Platform.FlagPresets.GetValueOrDefault(key[2..]) ?? new() :
            key.StartsWith("Preset: ") ? cat.Presets.First(p => "Preset: " + p.Name == key).Values :
            s.Profiles.GetValueOrDefault(key)?.Values ?? new();
        void Compare()
        {
            cmp.Children.Clear();
            if (a.SelectedItem is not string ka || b.SelectedItem is not string kb) return;
            var va = Vals(ka); var vb = Vals(kb);
            foreach (var d in cat.Flags.Where(d => va.ContainsKey(d.Id) || vb.ContainsKey(d.Id)))
            {
                var x = Engine.Label(d, va.GetValueOrDefault(d.Id)); var y = Engine.Label(d, vb.GetValueOrDefault(d.Id));
                cmp.Children.Add(Ui.KV(d.Name, $"{x}   ·   {y}", x == y ? "Muted" : "Accent"));
            }
            if (cmp.Children.Count == 0) cmp.Children.Add(Ui.Small("Both are empty (Roblox defaults)."));
        }
        a.SelectionChanged += (_, _) => Compare(); b.SelectionChanged += (_, _) => Compare();
        Compare();
        body.Children.Add(Ui.Card(Ui.Lbl("Flag Compare"), Ui.Row(a, b), new Border { Height = 10 }, cmp));

        // ── Playground ──
        var play = Ui.Field("{\n  \"FIntDebugForceMSAASamples\": 1,\n  \"DFIntTaskSchedulerTargetFps\": 240\n}", double.NaN, true);
        play.AcceptsReturn = true; play.MinHeight = 110; play.TextWrapping = TextWrapping.Wrap;
        var playOut = new StackPanel();
        body.Children.Add(Ui.Card(Ui.Lbl("Flag Playground · nothing is applied"), Ui.Desc("Paste any configuration (from a video or a friend) to see what Roblox would actually read and what it would do.", 0), new Border { Height = 8 }, play,
            Ui.Buttons(Ui.Pill("Test", () =>
            {
                playOut.Children.Clear();
                var issues = Scan(cat, play.Text);
                var (vals, _, _) = FromJson(cat, play.Text);
                var read = issues.Count(i => i.level == "ok");
                playOut.Children.Add(Ui.Status(read > 0 ? "ok" : "warn", $"Roblox would read {read} of {issues.Count(i => i.title != "(file)")} flag(s)", ""));
                foreach (var i in issues) playOut.Children.Add(Ui.Status(i.level, i.title, i.detail));
                playOut.Children.Add(Ui.Text(Engine.Predict(new(), vals, PcAnalyzer.Detect().Refresh, false).Describe(), 13, "Ink", FontWeights.SemiBold));
            })), playOut));

        // ── History, backup, restore, import/export ──
        var hist = new StackPanel();
        foreach (var e in Timeline.List().Where(e => e.Summary.Contains("→") || e.Summary.Contains("Preset") || e.Summary.Contains("BrainAI") || e.Summary.Contains("JSON")).Take(8))
        {
            var entry = e;
            hist.Children.Add(Ui.Status("info", $"{e.When:d MMM HH:mm}", e.Summary, Ui.Ghost("Restore", () => { Timeline.Restore(entry.File); host.Refresh(); })));
        }
        if (hist.Children.Count == 0) hist.Children.Add(Ui.Small("Flag changes appear here. Every change is backed up automatically before it happens.", 0));
        var good = s.Platform.KnownGood.GetValueOrDefault(s.ActiveProfile);
        body.Children.Add(Ui.Card(Ui.Lbl("Flag History · Auto Backup · Flag Restore"), hist,
            Ui.Buttons(
                good != null ? Ui.Pill("Restore known working settings", () => { prof.Values = new(good); Changed("Flag Restore: known working"); }) : (UIElement)Ui.Small("Known-working settings are saved after a smooth session.", 0),
                Ui.Ghost("Export JSON", () =>
                {
                    var d = new SaveFileDialog { Filter = "JSON|*.json", FileName = s.ActiveProfile + ".flags.json" };
                    if (d.ShowDialog(host) == true) File.WriteAllText(d.FileName, FlagWriter.Render(FlagCatalog.Resolve(cat, prof, s.FlagFormat, s.Platform.WriteIgnoredFlags)));
                }),
                Ui.Ghost("Import JSON", () =>
                {
                    var d = new OpenFileDialog { Filter = "JSON|*.json" };
                    if (d.ShowDialog(host) != true || new FileInfo(d.FileName).Length > 256 * 1024) return;
                    var (vals, custom, problems) = FromJson(cat, File.ReadAllText(d.FileName));
                    prof.Values = vals; prof.Custom = custom;
                    Changed("Imported flags" + (problems.Count > 0 ? $" ({problems.Count} skipped)" : ""));
                }))));
        return page;
    }

    static FrameworkElement Row(FlagDef d, FlagProfile prof, Action<string> changed)
    {
        var cur = prof.Values.GetValueOrDefault(d.Id);
        var (lvl, label) = d.Risk == "medium" ? ("info", "debug flag · may change") : ("ok", "supported");
        var head = Ui.Row(Ui.Title(d.Name, 14), Ui.Text("   " + label, 11.5, Ui.LevelBrush(lvl), mono: true));
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(head);
        panel.Children.Add(Ui.Desc(d.Desc));
        void Set(string? v) { if (v == null) prof.Values.Remove(d.Id); else prof.Values[d.Id] = v; changed($"{d.Name} {Engine.Label(d, cur)} → {Engine.Label(d, v)}"); }
        var opts = new List<(string, string)> { ("Default", "") };
        if (d.Type == "bool") opts.Add(("On", "true"));
        else if (d.Type == "choice") opts.AddRange(d.Options.Select(o => (o.Label, o.Value)));
        else opts.AddRange(d.Chips.Select(c => (c.ToString(), c.ToString())));
        var chips = Ui.Chips(opts, cur ?? "", v => Set(v.Length == 0 ? null : v));
        chips.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(chips);
        if (d.Type == "int")
        {
            var slider = new Slider { Minimum = d.Min, Maximum = d.Max, Width = 320, Value = int.TryParse(cur, out var n) ? n : d.Min, IsSnapToTickEnabled = true, TickFrequency = 1 };
            var val = Ui.Text(cur ?? "default", 12.5, "Muted", mono: true);
            slider.ValueChanged += (_, _) => val.Text = ((int)slider.Value).ToString();
            slider.LostMouseCapture += (_, _) => Set(((int)slider.Value).ToString());
            panel.Children.Add(Ui.Row(slider, Ui.Text("  ", 12), val));
        }
        return panel;
    }

    static string Doc(Catalog cat, string name)
    {
        var d = cat.Flags.FirstOrDefault(x => x.Flag == name || x.Options.Any(o => o.Set.ContainsKey(name)));
        return d != null ? $"{d.Name}: {d.Doc}" : Explain(name);
    }

    /// <summary>Plain-English reading of any flag name from its prefix and words.</summary>
    public static string Explain(string name)
    {
        var kind = name.StartsWith("DFFlag") ? "a dynamic on/off flag" : name.StartsWith("FFlag") ? "an on/off flag" : name.Contains("Int") ? "a number setting" :
                   name.Contains("String") ? "a text setting" : name.Contains("Log") ? "a logging setting" : "a flag";
        var words = System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(name, "^(D|S)?F(Flag|Int|String|Log)", ""), "([a-z])([A-Z])", "$1 $2").ToLowerInvariant();
        return $"This is {kind} about \"{words}\". It isn't in BrainStrap's documented catalog, so BrainFlags can't say exactly what it does.";
    }

    static List<(string level, string title, string detail)> Scan(Catalog cat, string text)
    {
        var res = new List<(string, string, string)>();
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return new() { ("bad", "(file)", "Must be a JSON object.") };
            var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            foreach (var dup in names.GroupBy(n => n).Where(g => g.Count() > 1)) res.Add(("bad", dup.Key, $"Appears {dup.Count()} times: only the last one counts."));
            if (names.Count(n => n.StartsWith("FFlagDebugGraphicsPrefer")) > 1) res.Add(("bad", "Graphics API", "More than one renderer is forced; pick one."));
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var v = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText();
                var i = FlagValidator.Check(p.Name, v);
                res.Add((i.Level, p.Name, i.Message));
            }
        }
        catch (JsonException ex) { res.Add(("bad", "(file)", "Not valid JSON: " + ex.Message)); }
        return res;
    }

    /// <summary>JSON → catalog values where a flag is known, everything else (valid, non-gameplay) as custom flags.</summary>
    static (Dictionary<string, string> vals, Dictionary<string, string> custom, List<string> problems) FromJson(Catalog cat, string text)
    {
        var vals = new Dictionary<string, string>();
        var custom = new Dictionary<string, string>();
        var problems = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(text);
            var map = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText());
            var used = new HashSet<string>();
            foreach (var d in cat.Flags)
            {
                if (d.Type is "int" or "bool" && d.Flag.Length > 0 && map.TryGetValue(d.Flag, out var v))
                {
                    var val = d.Type == "bool" ? "true" : v;
                    if (Engine.Clean(d, val) is { } c) { vals[d.Id] = c; used.Add(d.Flag); }
                }
                else if (d.Type == "choice")
                    foreach (var o in d.Options)
                        if (o.Set.All(kv => map.TryGetValue(kv.Key, out var mv) && string.Equals(mv, kv.Value.ToString(), StringComparison.OrdinalIgnoreCase)))
                        { vals[d.Id] = o.Value; foreach (var k in o.Set.Keys) used.Add(k); }
            }
            foreach (var kv in map.Where(kv => !used.Contains(kv.Key)))
            {
                var i = FlagValidator.Check(kv.Key, kv.Value);
                if (i.Level == "bad") problems.Add($"{kv.Key}: {i.Message}"); else custom[kv.Key] = kv.Value;
            }
        }
        catch (JsonException ex) { problems.Add("Not valid JSON: " + ex.Message); }
        return (vals, custom, problems);
    }
}
