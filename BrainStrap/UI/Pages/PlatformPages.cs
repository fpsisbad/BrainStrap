using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainStrap.Core;
using BrainStrap.Core.Platform;
using Microsoft.Win32;

namespace BrainStrap.UI.Pages;

/// <summary>BrainMods: launcher customization packs (never the Roblox client).</summary>
internal static class ModsPage
{
    public static FrameworkElement Build(MainWindow host)
    {
        var s = AppState.S;
        var page = Ui.Page("BrainMods", "Customization packs for BrainStrap itself: theme, fonts, launch screen, HUD and layout. Packs are pure data, launcher-side only, and one click puts everything back.", out var body);
        var all = ModPacks.All(s).ToList();
        var selected = s.Platform.ActivePacks.Select(id => all.FirstOrDefault(p => p.Id == id)).Where(p => p != null).Select(p => p!).ToList();
        void Done(string why) { Themes.Apply(s); Dwm.ApplyTitleBar(host, Themes.Effective(s).Dark); host.ApplyBackground(); host.Refresh(); }

        // ── collection being built ──
        var col = new StackPanel();
        var explain = Ui.Small(ModPacks.Explain(selected), 8);
        foreach (var c in ModPacks.Conflicts(selected)) col.Children.Add(Ui.Status("warn", $"Conflict: {c.Setting}", $"{string.Join(" and ", c.Packs)} both change it; {c.Winner} wins."));
        foreach (var (p, i) in selected.Select((x, n) => (x, n)))
            col.Children.Add(Ui.Status("ok", $"{i + 1}. {p.Name}", string.Join(" · ", ModPacks.Describe(p).Select(kv => $"{kv.Key}: {kv.Value}")),
                Ui.Ghost("Remove", () => { s.Platform.ActivePacks.Remove(p.Id); AppState.Save(); host.Refresh(); })));
        if (selected.Count == 0) col.Children.Add(Ui.Small("No packs active. Pick some below to build a collection.", 0));
        body.Children.Add(Ui.Card(Ui.Lbl("Mod Collection · Conflict Detector · Compatibility AI"), col, explain,
            Ui.Buttons(
                Ui.Pill("Apply collection", () => { ModPacks.Apply(s, selected); Done("packs"); }),
                Ui.Ghost("Mod Restore (default look)", () => { ModPacks.Restore(s); Done("restore"); }))));

        // ── marketplace grid with live preview + sandbox ──
        var grid = new WrapPanel();
        foreach (var p in all)
        {
            var pack = p;
            var t = pack.ThemeCode != null ? Themes.FromCode(pack.ThemeCode) : null;
            var sw = new Border { Height = 70, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 12) };
            if (t != null)
            {
                var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(t.Bg), 0.0));
                g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(t.Panel), 0.55));
                g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(pack.Accent ?? t.Accent), 1.0));
                sw.Background = g;
            }
            else sw.Background = pack.Minimal == true ? Brushes.Black : new SolidColorBrush(Color.FromRgb(0x26, 0x27, 0x2C));
            var card = new StackPanel { Width = 220 };
            card.Children.Add(sw);
            card.Children.Add(Ui.Title(pack.Name, 15));
            card.Children.Add(Ui.Text($"by {pack.Creator} · v{pack.Version}", 11.5, "Dim", mono: true));
            card.Children.Add(Ui.Small(pack.Summary, 4));
            var inSel = s.Platform.ActivePacks.Contains(pack.Id);
            card.Children.Add(Ui.Buttons(
                Ui.Ghost("Live preview", () =>
                {
                    // sandbox: apply to a throwaway copy first, so nothing is saved until you choose to
                    var copy = SettingsStore.FromJson(SettingsStore.ToJson(s));
                    ModPacks.Apply(copy, new List<ModPack> { pack }, remember: false);
                    var keep = AppState.S;
                    AppState.S = copy; Themes.Apply(copy); host.ApplyBackground();
                    AppState.S = keep;
                    if (MessageBox.Show(host, $"Previewing '{pack.Name}'. Keep it?", "BrainMods", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    { if (!s.Platform.ActivePacks.Contains(pack.Id)) s.Platform.ActivePacks.Add(pack.Id); ModPacks.Apply(s, ModPacksFor(s)); }
                    Done("preview");
                }),
                Ui.Ghost(inSel ? "In collection" : "Add", () => { if (!inSel) { s.Platform.ActivePacks.Add(pack.Id); AppState.Save(); host.Refresh(); } })));
            var frame = new Border { Child = card, Style = (Style)Application.Current.FindResource("Tile"), Margin = new Thickness(0, 0, 12, 12) };
            grid.Children.Add(frame);
        }
        body.Children.Add(Ui.Card(Ui.Lbl("Mod Marketplace · Live Preview · Mod Sandbox"), Ui.Desc("Packs are data only: previewing one never runs any code. More packs arrive through the BrainStore.", 0), new Border { Height = 10 }, grid,
            Ui.Buttons(Ui.Ghost("Browse BrainStore", () => host.Go("store")))));

        // ── mod profiles ──
        var mp = new StackPanel();
        foreach (var name in s.Profiles.Keys)
        {
            var prof = s.Profiles[name];
            var combo = new DarkCombo { Width = 240 };
            combo.Items.Add("No pack");
            foreach (var p in all) combo.Items.Add(p.Name);
            combo.SelectedIndex = prof.ModPack == null ? 0 : Math.Max(0, all.FindIndex(p => p.Id == prof.ModPack) + 1);
            combo.SelectionChanged += (_, _) => { prof.ModPack = combo.SelectedIndex <= 0 ? null : all[combo.SelectedIndex - 1].Id; AppState.Save("Mod profile for " + name); };
            mp.Children.Add(Ui.KV(name, combo, 200));
        }
        body.Children.Add(Ui.Card(Ui.Lbl("Mod Profiles"), Ui.Desc("Each graphics profile can switch the launcher to its own pack when you select it.", 0), new Border { Height = 10 }, mp));

        // ── version history + creators ──
        var vh = new StackPanel();
        foreach (var kv in s.Platform.PackHistory)
            foreach (var old in kv.Value.AsEnumerable().Reverse())
            {
                var o = old;
                vh.Children.Add(Ui.Status("info", $"{o.Name} v{o.Version}", "Older version", Ui.Ghost("Restore", () => { ModPacks.InstallPack(s, o); host.Refresh(); })));
            }
        if (vh.Children.Count == 0) vh.Children.Add(Ui.Small("When a pack updates, its previous versions are kept here.", 0));
        var creators = new WrapPanel();
        foreach (var c in all.Select(p => p.Creator).Distinct())
        {
            var name = c;
            var f = s.Platform.Following.Contains(name);
            creators.Children.Add(Ui.Ghost((f ? "✓ Following " : "Follow ") + name, () => { if (f) s.Platform.Following.Remove(name); else s.Platform.Following.Add(name); AppState.Save(); host.Refresh(); }));
        }
        body.Children.Add(Ui.Card(Ui.Lbl("Mod Version History"), vh, Ui.Lbl("Mod Creator Profiles", 12), creators));

        // ── BrainMod Studio: build your own pack from the current look ──
        var packName = Ui.Field("My pack", 260);
        var packSummary = Ui.Field("", 420);
        body.Children.Add(Ui.Card(
            Ui.Lbl("BrainMod Studio"),
            Ui.Title("Turn your current look into a pack", 15),
            Ui.Desc("Set things up in Studio (theme, font, background, launch screen, HUD, Home layout), then save it as a pack to share or switch back to later."),
            new Border { Height = 10 },
            Ui.KV("Name", packName, 120), Ui.KV("Description", packSummary, 120),
            Ui.Buttons(
                Ui.Pill("Save pack", () =>
                {
                    var p = Capture(s, packName.Text.Trim(), packSummary.Text.Trim());
                    ModPacks.InstallPack(s, p);
                    host.Refresh();
                }),
                Ui.Ghost("Export pack file", () =>
                {
                    var d = new SaveFileDialog { Filter = "BrainMods pack|*.brainpack.json", FileName = packName.Text.Trim() + ".brainpack.json" };
                    if (d.ShowDialog(host) == true) File.WriteAllText(d.FileName, JsonSerializer.Serialize(Capture(s, packName.Text.Trim(), packSummary.Text.Trim()), new JsonSerializerOptions { WriteIndented = true }));
                }),
                Ui.Ghost("Import pack file", () =>
                {
                    var d = new OpenFileDialog { Filter = "BrainMods pack|*.json" };
                    if (d.ShowDialog(host) != true || new FileInfo(d.FileName).Length > 256 * 1024) return;
                    var p = JsonSerializer.Deserialize<ModPack>(File.ReadAllText(d.FileName), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (p == null || p.Name.Length == 0) { MessageBox.Show(host, "That isn't a BrainMods pack.", "BrainMods"); return; }
                    p.Id = "pack.user-" + System.Text.RegularExpressions.Regex.Replace(p.Name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
                    ModPacks.InstallPack(s, p);
                    host.Refresh();
                }))));

        // ── classic Roblox content mods ──
        body.Children.Add(Ui.Card(Ui.Lbl("Roblox content files (classic mods)"),
            Ui.Desc("Separate from packs: this replaces files inside Roblox (sounds, cursors). It changes the Roblox client, which can conflict with Roblox's rules, so it stays off unless you choose it.", 0),
            Ui.Buttons(Ui.Ghost("Open classic mods", () => host.Go("mods-classic")))));
        return page;
    }

    /// <summary>Mod Profiles: a graphics profile can carry its own pack.</summary>
    public static void ApplyProfilePack(Settings s, string profile)
    {
        if (!s.Profiles.TryGetValue(profile, out var p) || p.ModPack == null) return;
        var pack = ModPacks.All(s).FirstOrDefault(x => x.Id == p.ModPack);
        if (pack == null) return;
        s.Platform.ActivePacks = new List<string> { pack.Id };
        ModPacks.Apply(s, new List<ModPack> { pack });
    }

    static List<ModPack> ModPacksFor(Settings s) => s.Platform.ActivePacks.Select(id => ModPacks.All(s).FirstOrDefault(p => p.Id == id)).Where(p => p != null).Select(p => p!).ToList();

    static ModPack Capture(Settings s, string name, string summary)
    {
        var t = Themes.Effective(s);
        return new ModPack
        {
            Id = "pack.user-" + System.Text.RegularExpressions.Regex.Replace((name.Length > 0 ? name : "my pack").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-'),
            Name = name.Length > 0 ? name : "My pack", Summary = summary, Creator = "You", Version = DateTime.Now.ToString("yyyy.M.d"),
            ThemeCode = Themes.ToCode(t), Accent = s.Accent, Font = s.Studio.Font, Minimal = s.Studio.Minimal,
            Background = s.Studio.Background is "none" or "silk" ? s.Studio.Background : "none", Splash = s.Studio.Splash,
            HudCorner = s.Hud.Corner, HudOpacity = s.Hud.Opacity, HomeWidgets = s.Studio.HomeWidgets.ToList(),
        };
    }
}

/// <summary>BrainExtensions: built-in and installed extensions, BrainNotes, BrainCompanion, BrainDev and BrainBuilder.</summary>
internal static class ExtensionsPage
{
    public static FrameworkElement Build(MainWindow host)
    {
        var s = AppState.S;
        var page = Ui.Page("BrainExtensions", "Install features that change how BrainStrap works. Extensions are built from BrainStrap's own safe actions, so none of them can run code, and each one shows exactly what it's allowed to do.", out var body);

        // ── BrainOverlay: apps over the game ──
        var ov = s.Overlay;
        var apps = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var app in UI.OverlayHost.Apps(s))
        {
            var a = app;
            var cell = new StackPanel { Width = 190, Margin = new Thickness(0, 0, 18, 18) };
            var glyph = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(28), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12), Child = Icons.Make(a.Icon, 22, 1.6) };
            glyph.SetResourceReference(Border.BorderBrushProperty, "Line2");
            cell.Children.Add(glyph);
            var nm = new TextBlock { Text = a.Name, FontSize = 28 };
            nm.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            cell.Children.Add(nm);
            cell.Children.Add(Ui.Text(a.Caption, 10, "Dim", mono: true));
            var row = Ui.Row(Ui.Toggle(a.Enabled, on => { a.Enabled = on; AppState.Save(a.Name + " in the overlay " + (on ? "on" : "off")); }));
            row.Margin = new Thickness(0, 10, 0, 0);
            if (!OverlayApp.Builtins.Any(x => x.Id == a.Id))
                row.Children.Add(Ui.Ghost("Remove", () => { ov.Apps.Remove(a); AppState.Save("Overlay app removed"); host.Refresh(); }));
            cell.Children.Add(row);
            apps.Children.Add(cell);
        }
        var addName = Ui.Field("My site", 180);
        var addUrl = Ui.Field("https://", 320, true);
        var preview = Ui.Small("", 8);
        UI.OverlayHost? trial = null;
        body.Children.Add(Ui.Card(
            Ui.Head("", "YouTube, Spotify, Discord and Twitch, over your game.", $"Press {ov.Hotkey} while you play. The game dims, your apps come up, and the same keys put you straight back. Pin a panel to keep a video or chat on screen while you play. You sign in on each service's own page; BrainStrap never sees a password."),
            Ui.ToggleRow("BrainOverlay", "BrainStrap's own windows floating over Roblox, like the Steam overlay. Nothing is added to the game.", ov.Enabled && Extensions.On(s, "brainoverlay"),
                on => { ov.Enabled = on; if (on) s.Platform.BuiltinsOff.Remove("brainoverlay"); AppState.Save("BrainOverlay " + (on ? "on" : "off")); }),
            apps,
            Ui.Lbl("Hotkey", 6),
            Ui.Chips(UI.OverlayHost.Hotkeys.Select(k => (k, k)).ToArray(), ov.Hotkey, v => { ov.Hotkey = v; AppState.Save("Overlay hotkey " + v); host.Refresh(); }),
            Ui.Lbl("Add any site", 14),
            Ui.Row(addName, new Border { Width = 10 }, addUrl, new Border { Width = 10 }, Ui.Ghost("Add", () =>
            {
                var url = addUrl.Text.Trim();
                if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "https") { Ui.Msg(preview, "Sites have to start with https://", "Warn"); return; }
                var label = addName.Text.Trim().Length > 0 ? addName.Text.Trim() : u.Host;
                ov.Apps.Add(new OverlayApp { Id = "site-" + Guid.NewGuid().ToString("N")[..8], Name = label.Length > 24 ? label[..24] : label, Url = u.ToString(), Icon = "globe", Caption = u.Host.ToUpperInvariant() });
                AppState.Save("Overlay app added");
                host.Refresh();
            })),
            Ui.Buttons(Ui.Pill("Try the overlay now", () =>
            {
                trial ??= new UI.OverlayHost(s, null);
                trial.Open();
            })),
            preview));

        var builtins = new StackPanel();
        foreach (var b in Extensions.Builtins)
        {
            var ext = b;
            builtins.Children.Add(Ui.Status(Extensions.On(s, ext.Id) ? "ok" : "info", (ext.Star ? "★ " : "") + ext.Name, ext.Summary,
                Ui.Row(
                    Ui.Toggle(Extensions.On(s, ext.Id), on => { if (on) s.Platform.BuiltinsOff.Remove(ext.Id); else s.Platform.BuiltinsOff.Add(ext.Id); AppState.Save(ext.Name + (on ? " on" : " off")); }),
                    new Border { Width = 10 },
                    Ui.Ghost("Open", () => host.Go(ext.Page)))));
        }
        body.Children.Add(Ui.Card(Ui.Lbl("Built-in extensions · by RBLX.BRAIN ✓ verified"), builtins));

        var inst = new StackPanel();
        foreach (var (m, on) in Extensions.Installed())
        {
            var ext = m;
            var older = Extensions.OlderVersions(ext.Id);
            inst.Children.Add(Ui.Status(on ? "ok" : "info", $"{ext.Name} v{ext.Version} · {ext.Publisher}", ext.Summary + $" · permissions: {(ext.Permissions.Count == 0 ? "none" : string.Join(", ", ext.Permissions))}",
                Ui.Row(
                    Ui.Toggle(on, v => { Extensions.SetEnabled(ext.Id, v); }),
                    new Border { Width = 10 },
                    older.Count > 0 ? Ui.Ghost("v" + older[^1], () => { Extensions.RestoreVersion(ext.Id, older[^1]); host.Refresh(); }) : (UIElement)new Border(),
                    Ui.Ghost("Remove", () => { Extensions.Uninstall(ext.Id); host.Refresh(); }))));
        }
        if (inst.Children.Count == 0) inst.Children.Add(Ui.Small("Nothing installed yet. Get extensions from the BrainStore, build one with BrainBuilder, or import a package.", 0));
        body.Children.Add(Ui.Card(Ui.Lbl("Installed"), inst, Ui.Buttons(Ui.Pill("BrainStore", () => host.Go("store")), Ui.Ghost("Import package", () =>
        {
            var d = new OpenFileDialog { Filter = "BrainStrap extension|*.brainext.json;*.json" };
            if (d.ShowDialog(host) != true || new FileInfo(d.FileName).Length > 1024 * 1024) return;
            var m = Extensions.Parse(File.ReadAllText(d.FileName));
            if (StorePage.ReviewPermissions(host, m.Name, m.Permissions, Extensions.Scan(m))) { Extensions.Install(m); host.Refresh(); }
        }))));

        // ── BrainBuilder ──
        var text = Ui.Field("When I join Bedwars, switch to Competitive profile, show the hud and open Discord", 620);
        var outBox = new StackPanel();
        body.Children.Add(Ui.Card(Ui.Lbl("BrainBuilder"), Ui.Title("Describe a feature. BrainBuilder makes it an extension.", 15),
            Ui.Desc("It builds an editable automation, runs a dry-run test, and shows the permissions it needs before you decide to install."), new Border { Height = 10 }, text,
            Ui.Buttons(Ui.Pill("Build", () => ShowBuild(host, outBox, Builder.Build(s, text.Text)))), outBox));

        // ── BrainNotes ──
        if (Extensions.On(s, "brainnotes"))
        {
            var key = new DarkCombo { Width = 300 };
            var keys = s.Profiles.Keys.Select(k => ("profile:" + k, "Profile · " + k))
                .Concat(BrainStrap.Core.Perf.History.Playtime().Where(g => g.placeId != null).Take(20).Select(g => ("game:" + g.placeId, "Game · " + g.game))).ToList();
            foreach (var (_, label) in keys) key.Items.Add(label);
            var note = Ui.Field("", double.NaN);
            note.AcceptsReturn = true; note.MinHeight = 70; note.TextWrapping = TextWrapping.Wrap;
            key.SelectionChanged += (_, _) => note.Text = key.SelectedIndex >= 0 ? s.Platform.Notes.GetValueOrDefault(keys[key.SelectedIndex].Item1) ?? "" : "";
            if (keys.Count > 0) key.SelectedIndex = 0;
            body.Children.Add(Ui.Card(Ui.Lbl("BrainNotes"), Ui.Desc("Game notes show on the launch screen when you start that game.", 0), new Border { Height = 8 }, key, new Border { Height = 8 }, note,
                Ui.Buttons(Ui.Pill("Save note", () => { if (key.SelectedIndex >= 0) Notes.Set(s, keys[key.SelectedIndex].Item1, note.Text); }))));
        }

        if (Extensions.On(s, "braincompanion"))
            body.Children.Add(Ui.Card(Ui.Lbl("BrainCompanion"), Ui.Desc("A live dashboard window for your second screen: FPS, ping, CPU, GPU, session time and game.", 0),
                Ui.Buttons(Ui.Pill("Open companion window", () => new CompanionWindow().Show()))));

        // ── BrainDev ──
        if (Extensions.On(s, "braindev"))
        {
            var src = Ui.Field(Extensions.Serialize(new ExtensionManifest
            {
                Id = "you.stream-mode", Name = "Stream mode", Publisher = "You", Summary = "One command to get stream-ready.",
                Permissions = new() { "hud", "apps" },
                Commands = new() { new ExtCommand { Title = "Stream mode", Actions = new() { new FlowAction { Type = "hud.on" }, new FlowAction { Type = "app.start", Value = "obs" } } } },
                Changelog = new() { new ExtVersion { Version = "1.0.0", Date = DateTime.Now.ToString("yyyy-MM-dd"), Notes = "First version." } },
            }), double.NaN, true);
            src.AcceptsReturn = true; src.MinHeight = 200; src.TextWrapping = TextWrapping.Wrap;
            var scanBox = new StackPanel();
            body.Children.Add(Ui.Card(Ui.Lbl("BrainDev · build, scan, package, publish"), src,
                Ui.Buttons(
                    Ui.Ghost("Safety scan", () =>
                    {
                        scanBox.Children.Clear();
                        try { foreach (var (ok, check) in Extensions.Scan(Extensions.Parse(src.Text)).Checks) scanBox.Children.Add(Ui.Status(ok ? "ok" : "bad", check, "")); }
                        catch (Exception ex) { scanBox.Children.Add(Ui.Status("bad", "Not a valid package", ex.Message)); }
                    }),
                    Ui.Pill("Install locally", () => { var m = Extensions.Parse(src.Text); if (StorePage.ReviewPermissions(host, m.Name, m.Permissions, Extensions.Scan(m))) { Extensions.Install(m); host.Refresh(); } }),
                    Ui.Ghost("Export package", () =>
                    {
                        var m = Extensions.Parse(src.Text);
                        var d = new SaveFileDialog { Filter = "BrainStrap extension|*.brainext.json", FileName = m.Id + ".brainext.json" };
                        if (d.ShowDialog(host) == true) File.WriteAllText(d.FileName, Extensions.Serialize(m));
                    }),
                    Ui.Ghost("Publish to BrainStore", () => MessageBox.Show(host, "Publishing goes through the BrainStore's review: export the package, then submit it at the store's submission page (BrainStore → Submit). It's checked by the same safety scan plus a human review before it's listed.", "BrainDev"))),
                scanBox));
        }
        return page;
    }

    public static void ShowBuild(MainWindow host, StackPanel box, BuildResult r)
    {
        var s = AppState.S;
        box.Children.Clear();
        if (r.Understood.Count > 0) box.Children.Add(Ui.Small("Understood: " + string.Join(" · ", r.Understood), 10));
        foreach (var p in r.Problems) box.Children.Add(Ui.Status("warn", p, ""));
        if (r.Rule == null) return;
        box.Children.Add(Ui.Lbl("Test run (nothing happens yet)", 12));
        foreach (var t in r.Tests) box.Children.Add(Ui.Text(t, 13, t.StartsWith("⚠") ? "Warn" : "Ink", mono: true));
        box.Children.Add(Ui.Lbl("Permissions it needs", 12));
        var g = Grammar.Load();
        foreach (var p in r.Permissions) box.Children.Add(Ui.Status(g.Permissions.TryGetValue(p, out var d) && d.Length > 1 && d[1] == "high" ? "warn" : "info", d != null ? d[0] : p, ""));
        box.Children.Add(Ui.Buttons(
            Ui.Pill("Add as automation", () => { s.Platform.Flows.Add(r.Rule); AppState.Save("BrainFlow: " + r.Rule.Name); host.Go("flow"); }),
            Ui.Ghost("Install as extension", () => { var m = Builder.AsExtension(r.Rule); if (StorePage.ReviewPermissions(host, m.Name, m.Permissions, Extensions.Scan(m))) { Extensions.Install(m); host.Go("extensions"); } })));
    }
}

/// <summary>BrainStore: extensions, mod packs, flag presets and themes, with publishers, collections, reviews, versions and permission review.</summary>
internal static class StorePage
{
    static StoreItem? open;

    public static FrameworkElement Build(MainWindow host)
    {
        var s = AppState.S;
        var page = Ui.Page("BrainStore", "One marketplace for extensions, mod packs, flag presets and themes. Every item lists its permissions, versions and publisher, and downloads are checked against the store's fingerprint before the safety scan.", out var body);
        var holder = new StackPanel();
        body.Children.Add(holder);
        holder.Children.Add(Ui.Small("Loading the store…"));
        _ = Load();
        async Task Load()
        {
            var idx = await Store.LoadAsync(s);
            holder.Children.Clear();
            if (open != null) { Detail(host, holder, idx, open); return; }
            var grid = new WrapPanel();
            void Fill(string f)
            {
                grid.Children.Clear();
                foreach (var it in idx.Items.Where(i => f == "all" || (f == "extension" ? i.Type is "extension" or "builtin" : f == "following" ? s.Platform.Following.Contains(i.Publisher) : i.Type == f)))
                    grid.Children.Add(Tile(host, holder, idx, it));
            }
            var filter = Ui.Chips(new[] { ("All", "all"), ("Extensions", "extension"), ("Mod packs", "pack"), ("Flag presets", "flags"), ("Themes", "theme"), ("Following", "following") }, "all", Fill);
            Fill("all");
            holder.Children.Add(Ui.Card(Ui.Lbl("Browse"), filter, new Border { Height = 10 }, grid));
            var cols = new StackPanel();
            foreach (var c in idx.Collections)
                cols.Children.Add(Ui.Status("info", c.Name, c.Description + " · " + string.Join(", ", c.Items.Select(id => idx.Items.FirstOrDefault(i => i.Id == id)?.Name ?? id))));
            holder.Children.Add(Ui.Card(Ui.Lbl("Community collections"), cols));
            var url = Ui.Field(s.Platform.StoreUrl, 440, true);
            holder.Children.Add(Ui.Card(Ui.Lbl("Store sources"),
                Ui.Desc("BrainStrap ships with the RBLX.BRAIN shelf. Add a community store index (https) to see third-party items; its publishers' verified status comes from that index.", 0),
                new Border { Height = 8 }, Ui.Row(url, new Border { Width = 10 }, Ui.Pill("Save", () => { s.Platform.StoreUrl = url.Text.Trim(); AppState.Save("Store source"); host.Refresh(); })),
                idx.SubmitUrl.StartsWith("https://") ? Ui.Buttons(Ui.Ghost("Submit an extension", () => Ui.Open(idx.SubmitUrl))) : (UIElement)new Border()));
        }
        return page;
    }

    static FrameworkElement Tile(MainWindow host, StackPanel holder, StoreIndex idx, StoreItem it)
    {
        var sp = new StackPanel { Width = 220 };
        var badge = it.Type switch { "pack" => "MOD PACK", "flags" => "FLAG PRESET", "theme" => "THEME", _ => "EXTENSION" };
        sp.Children.Add(Ui.Text(badge, 10.5, "Accent", FontWeights.Bold, true));
        sp.Children.Add(Ui.Title(it.Name, 15));
        var pub = idx.Publishers.FirstOrDefault(p => p.Id == it.Publisher);
        sp.Children.Add(Ui.Text((pub?.Name ?? it.Publisher) + (Store.Verified(idx, it) ? "  ✓ verified" : ""), 11.5, Store.Verified(idx, it) ? "Ok" : "Dim"));
        sp.Children.Add(Ui.Small(it.Summary, 6));
        if (it.Rating > 0) sp.Children.Add(Ui.Text($"{new string('★', (int)Math.Round(it.Rating))} {it.Rating:0.0} · {it.Reviews.Count} reviews", 12, "Muted"));
        var b = new Button { Content = sp, Style = (Style)Application.Current.FindResource("TileButton"), Margin = new Thickness(0, 0, 12, 12), VerticalAlignment = VerticalAlignment.Stretch };
        b.Click += (_, _) => { open = it; host.Refresh(); };
        return b;
    }

    /// <summary>The item page: screenshots, reviews, version history, permissions and verified status.</summary>
    static void Detail(MainWindow host, StackPanel holder, StoreIndex idx, StoreItem it)
    {
        var s = AppState.S;
        var pub = idx.Publishers.FirstOrDefault(p => p.Id == it.Publisher);
        var head = new StackPanel();
        head.Children.Add(Ui.Buttons(Ui.Ghost("← Back to the store", () => { open = null; host.Refresh(); })));
        head.Children.Add(Ui.Big(it.Name));
        head.Children.Add(Ui.Text($"{pub?.Name ?? it.Publisher}{(Store.Verified(idx, it) ? "  ✓ verified developer" : "  · unverified")} · v{it.Versions.FirstOrDefault()?.Version ?? "?"}", 13, Store.Verified(idx, it) ? "Ok" : "Warn"));
        head.Children.Add(Ui.Small(it.Description.Length > 0 ? it.Description : it.Summary, 8));
        head.Children.Add(Ui.Buttons(
            Ui.AsyncPill(it.Type == "builtin" ? (Extensions.On(s, it.Id) ? "Installed" : "Turn on") : "Install", async () =>
            {
                if (it.Type == "builtin") { s.Platform.BuiltinsOff.Remove(it.Id); AppState.Save(it.Name + " on"); host.Refresh(); return; }
                var (ext, pack, flags, theme, scan) = await Store.FetchAsync(it);
                if (ext != null) { if (ReviewPermissions(host, ext.Name, ext.Permissions, scan!)) { foreach (var dep in ext.Dependencies) if (idx.Items.FirstOrDefault(x => x.Id == dep) is { } d && d.Type == "builtin") s.Platform.BuiltinsOff.Remove(d.Id); Extensions.Install(ext); } }
                else if (pack != null) { ModPacks.InstallPack(s, pack); if (!s.Platform.ActivePacks.Contains(pack.Id)) s.Platform.ActivePacks.Add(pack.Id); AppState.Save(); }
                else if (flags != null) { s.Platform.FlagPresets[it.Name] = flags; AppState.Save("Flag preset: " + it.Name); }
                else if (theme != null && Themes.FromCode(theme) is { } t) { s.Studio.CustomThemes.Add(t); AppState.Save("Theme: " + t.Name); }
                MessageBox.Show(host, $"{it.Name} is ready.", "BrainStore");
            }),
            pub != null ? Ui.Ghost(s.Platform.Following.Contains(pub.Id) ? "✓ Following" : "Follow " + pub.Name, () => { if (!s.Platform.Following.Remove(pub.Id)) s.Platform.Following.Add(pub.Id); AppState.Save(); host.Refresh(); }) : (UIElement)new Border()));
        holder.Children.Add(Ui.Card(head));

        var shots = new WrapPanel();
        foreach (var url in it.Screenshots.Where(u => u.StartsWith("https://")).Take(4))
        {
            try
            {
                var img = new System.Windows.Controls.Image { Width = 260, Height = 150, Stretch = Stretch.UniformToFill, Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(url)) };
                shots.Children.Add(new Border { Child = img, CornerRadius = new CornerRadius(12), ClipToBounds = true, Margin = new Thickness(0, 0, 10, 10) });
            }
            catch { }
        }
        if (shots.Children.Count > 0) holder.Children.Add(Ui.Card(Ui.Lbl("Screenshots"), shots));

        var perms = new StackPanel();
        var g = Grammar.Load();
        foreach (var p in it.Permissions) perms.Children.Add(Ui.Status(g.Permissions.TryGetValue(p, out var d) && d[1] == "high" ? "warn" : "info", d?[0] ?? p, d != null ? $"{d[1]} sensitivity" : ""));
        if (it.Permissions.Count == 0) perms.Children.Add(Ui.Small("Needs no permissions.", 0));
        var vers = new StackPanel();
        foreach (var v in it.Versions) vers.Children.Add(Ui.KV("v" + v.Version, $"{v.Date} · {v.Notes}"));
        holder.Children.Add(Ui.Cols(2, Ui.Card(Ui.Lbl("Permissions"), perms), Ui.Card(Ui.Lbl("Version history"), vers)));

        var rev = new StackPanel();
        foreach (var r in it.Reviews.Take(10)) rev.Children.Add(Ui.Status("info", $"{new string('★', Math.Clamp(r.Stars, 1, 5))} · {r.User}", r.Text));
        if (it.Reviews.Count == 0) rev.Children.Add(Ui.Small("No reviews yet.", 0));
        if (it.Dependencies.Count > 0) rev.Children.Add(Ui.Small("Needs: " + string.Join(", ", it.Dependencies), 8));
        holder.Children.Add(Ui.Card(Ui.Lbl("Ratings & reviews"), rev));
    }

    /// <summary>The permission review shown before anything is installed.</summary>
    public static bool ReviewPermissions(Window owner, string name, List<string> perms, ScanResult scan)
    {
        var g = Grammar.Load();
        var lines = perms.Count == 0 ? "• Nothing: it only adds things you choose to run." : string.Join("\n", perms.Select(p => "• " + (g.Permissions.TryGetValue(p, out var d) ? $"{d[0]} ({d[1]})" : p)));
        var checks = string.Join("\n", scan.Checks.Select(c => (c.ok ? "✓ " : "✕ ") + c.check));
        if (!scan.Passed) { MessageBox.Show(owner, $"'{name}' failed the safety scan and can't be installed:\n\n{checks}", "BrainStrap"); return false; }
        return MessageBox.Show(owner, $"Install '{name}'?\n\nIt will be allowed to:\n{lines}\n\nSafety scan passed:\n{checks}", "BrainStrap", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }
}

/// <summary>BrainFlow: visual automations (trigger → actions), macros, and BrainBuilder.</summary>
internal static class FlowPage
{
    public static FrameworkElement Build(MainWindow host)
    {
        var s = AppState.S;
        var g = Grammar.Load();
        var page = Ui.Page("BrainFlow", "Connect launcher actions into automations: when something happens, BrainStrap does the rest. Macros are flows you run yourself from Ctrl+K, the Jump List or a Stream Deck.", out var body);

        // ── builder ──
        var text = Ui.Field("When Roblox launches, load Balanced profile and start tracking", 620);
        var outBox = new StackPanel();
        body.Children.Add(Ui.Card(Ui.Lbl("Build from a sentence"), text, Ui.Buttons(Ui.Pill("Build flow", () => ExtensionsPage.ShowBuild(host, outBox, Builder.Build(s, text.Text)))), outBox));

        // ── flows as node chains ──
        foreach (var r in Flow.AllRules(s).ToList())
        {
            var rule = r;
            var chain = new WrapPanel();
            chain.Children.Add(Node(g.TriggerLabel(rule), true));
            foreach (var a in rule.Actions) { chain.Children.Add(Ui.Text("  →  ", 16, "Dim")); chain.Children.Add(Node(g.ActionLabel(a), false)); }
            var own = rule.Source == null;
            body.Children.Add(Ui.Card(
                Ui.Row(Ui.Title(rule.Name, 15), Ui.Text(rule.Source != null ? "   from " + rule.Source : "", 12, "Dim")),
                new Border { Height = 10 }, chain,
                Ui.Buttons(
                    own ? Ui.Toggle(rule.Enabled, on => { rule.Enabled = on; AppState.Save("Flow " + rule.Name + (on ? " on" : " off")); }) : (UIElement)new Border(),
                    new Border { Width = 10 },
                    rule.Trigger == "manual" ? Ui.Pill("Run now", () => Flow.Run(s, rule.Actions)) : Ui.Ghost("Test run", () => MessageBox.Show(host, string.Join("\n", Builder.Simulate(s, rule)), "BrainFlow test")),
                    own ? Ui.Ghost("Delete", () => { s.Platform.Flows.Remove(rule); AppState.Save("Flow deleted"); host.Refresh(); }) : (UIElement)new Border())));
        }

        // ── manual editor ──
        var trig = new DarkCombo { Width = 230, Margin = new Thickness(0, 0, 10, 0) };
        foreach (var t in g.Triggers) trig.Items.Add(t.Label);
        trig.SelectedIndex = 0;
        var tval = Ui.Field("", 180);
        var act = new DarkCombo { Width = 230, Margin = new Thickness(0, 0, 10, 0) };
        foreach (var a in g.Actions) act.Items.Add(a.Label);
        act.SelectedIndex = 0;
        var aval = Ui.Field("", 180);
        var draft = new FlowRule { Name = "My flow" };
        var draftView = Ui.Small("No actions yet.", 8);
        var name = Ui.Field("My flow", 260);
        body.Children.Add(Ui.Card(Ui.Lbl("Visual automation builder"),
            Ui.KV("Name", name, 120),
            Ui.KV("When", Ui.Row(trig, tval), 120),
            Ui.KV("Then", Ui.Row(act, aval, new Border { Width = 10 }, Ui.Ghost("Add step", () =>
            {
                draft.Actions.Add(new FlowAction { Type = g.Actions[act.SelectedIndex].Type, Value = aval.Text.Trim().Length > 0 ? aval.Text.Trim() : null });
                draftView.Text = string.Join("  →  ", draft.Actions.Select(g.ActionLabel));
            })), 120),
            draftView,
            Ui.Desc("Values: a profile name, an app (discord, obs, spotify, steam), a theme name, or notification text. For \"When I join a game\", put a place ID or part of the game's name."),
            Ui.Buttons(Ui.Pill("Save flow", () =>
            {
                if (draft.Actions.Count == 0) return;
                draft.Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : "My flow";
                draft.Trigger = g.Triggers[trig.SelectedIndex].Type;
                draft.TriggerValue = tval.Text.Trim().Length > 0 ? tval.Text.Trim() : null;
                s.Platform.Flows.Add(draft);
                AppState.Save("BrainFlow: " + draft.Name);
                host.Refresh();
            }))));
        if (!Extensions.On(s, "brainautomation"))
            body.Children.Insert(0, Ui.Card(Ui.Status("warn", "BrainAutomation is off", "Flows won't run until you turn it on in BrainExtensions.", Ui.Ghost("Turn on", () => { s.Platform.BuiltinsOff.Remove("brainautomation"); AppState.Save(); host.Refresh(); }))));
        return page;
    }

    static Border Node(string text, bool trigger)
    {
        var b = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(1), Child = Ui.Text(text, 13, trigger ? "AccentInk" : "Ink", FontWeights.SemiBold) };
        b.SetResourceReference(Border.BackgroundProperty, trigger ? "Accent" : "Pill");
        b.SetResourceReference(Border.BorderBrushProperty, "Line2");
        return b;
    }
}

/// <summary>BrainConnect: integrations, each with minimum permissions and a one-click disconnect.</summary>
internal static class ConnectPage
{
    public static FrameworkElement Build(MainWindow host)
    {
        var s = AppState.S;
        var page = Ui.Page("BrainConnect", "Plug BrainStrap into the rest of your setup. Nothing here needs your passwords: apps are found on this PC, links are opened in your browser, and each integration can be switched off, which also forgets its data.", out var body);
        UIElement Integ(string title, string desc, bool on, Action<bool>? toggle, params UIElement[] extra)
        {
            var sp = new StackPanel();
            sp.Children.Add(toggle != null ? Ui.ToggleRow(title, desc, on, toggle) : new StackPanel { Children = { Ui.Title(title, 15), Ui.Desc(desc) } });
            if (extra.Length > 0) sp.Children.Add(Ui.Buttons(extra));
            return Ui.Card(sp);
        }

        body.Children.Add(Integ("★ RBLX.BRAIN", "brainstrap:// links let the RBLX.BRAIN website send themes, flag presets and mod packs straight into BrainStrap (it always asks first), and power Stream Deck buttons.",
            Connect.ProtocolRegistered(), on => { Connect.RegisterProtocol(on); AppState.Save("brainstrap:// links " + (on ? "on" : "off")); }));
        body.Children.Add(Integ("Discord", "Rich Presence shows the game you're in. Launch notifications post to a Discord channel through a webhook you add below.", s.Discord.Enabled,
            on => { s.Discord.Enabled = on; AppState.Save("Discord presence " + (on ? "on" : "off")); }, Ui.Ghost("Presence settings", () => host.Go("presence"))));
        var repo = Ui.Field(s.Platform.GitHubRepo, 260, true);
        body.Children.Add(Integ("GitHub", "Update checks and issue reports for your BrainStrap release repository. Verified extensions in the BrainStore are published from GitHub too.", true, null,
            repo, Ui.Ghost("Save", () => { s.Platform.GitHubRepo = repo.Text.Trim(); AppState.Save("GitHub repo"); }),
            Ui.AsyncPill("Check for updates", async () =>
            {
                var r = await Connect.LatestReleaseAsync(s.Platform.GitHubRepo);
                MessageBox.Show(host, r == null ? "No release found (is the repository name right and public?)." : $"Latest release: {r.Value.tag}{(r.Value.tag.TrimStart('v') == Installer.Version ? " (you have it)" : "")}", "BrainStrap");
                if (r != null && r.Value.tag.TrimStart('v') != Installer.Version) Ui.Open(r.Value.url);
            }, ghost: true),
            Ui.Ghost("Report an issue", () => { if (s.Platform.GitHubRepo.Contains('/')) Ui.Open(Connect.IssueUrl(s.Platform.GitHubRepo, Diagnostics.Build(s))); })));
        body.Children.Add(Integ("OBS Studio", $"Writes live FPS and ping to a text file. In OBS add Text (GDI+) → Read from file → {Connect.ObsFile}", s.Platform.ObsStats,
            on => { s.Platform.ObsStats = on; if (!on) try { File.Delete(Connect.ObsFile); } catch { } AppState.Save("OBS stats " + (on ? "on" : "off")); },
            Ui.Ghost("Copy file path", () => Ui.Copy(Connect.ObsFile)), Ui.Ghost("Open OBS", () => Connect.StartApp("obs"))));
        body.Children.Add(Integ("Spotify", "Shows what's playing on the Home screen, read from the Spotify app on this PC. No account login.", s.Platform.SpotifyWidget,
            on => { s.Platform.SpotifyWidget = on; if (on) s.Studio.HiddenWidgets.Remove("spotify"); else s.Studio.HiddenWidgets.Add("spotify"); AppState.Save("Spotify widget"); }));
        body.Children.Add(Integ("Windows", "Notifications for crashes, disconnects and automations, plus the taskbar Jump List.", s.Platform.Notifications,
            on => { s.Platform.Notifications = on; AppState.Save("Notifications " + (on ? "on" : "off")); }, Ui.Ghost("Jump List settings", () => host.Go("life"))));
        var studio = Connect.FindStudio();
        body.Children.Add(Integ("Roblox Studio", studio != null ? "Studio is installed. Launch it from here; Studio isn't limited by the Player's flag allowlist." : "Roblox Studio isn't installed on this PC.", true, null,
            studio != null ? Ui.Pill("Launch Studio", () => Ui.Open(studio)) : Ui.Ghost("Get Studio", () => Ui.Open("https://create.roblox.com/"))));
        body.Children.Add(Integ("Twitch & YouTube", "Shortcuts for your streaming and recording workflow.", true, null,
            Ui.Ghost("Twitch stream manager", () => Ui.Open("https://dashboard.twitch.tv/stream-manager")), Ui.Ghost("YouTube upload", () => Ui.Open("https://studio.youtube.com/")),
            Ui.Ghost("Open OBS", () => Connect.StartApp("obs"))));
        var deck = new StackPanel();
        foreach (var (ex, what) in Connect.Commands) deck.Children.Add(Ui.Status("info", ex, what, Ui.Ghost("Copy", () => Ui.Copy(ex))));
        body.Children.Add(Ui.Card(Ui.Lbl("Stream Deck · BrainAPI links"), Ui.Desc("Use these with a Stream Deck 'Website' or 'Open' button, a desktop shortcut, or your own tools. Turn on RBLX.BRAIN links above first.", 0), new Border { Height = 8 }, deck));

        var hooks = new StackPanel();
        foreach (var w in s.Platform.Webhooks.ToList())
        {
            var hook = w;
            hooks.Children.Add(Ui.Status("info", hook.Name, "Events: " + string.Join(", ", hook.Events),
                Ui.Row(Ui.Ghost("Test", () => _ = Connect.SendWebhooksAsync(s, "flow", new System.Text.Json.Nodes.JsonObject { ["message"] = "BrainStrap test ✓" })),
                       Ui.Ghost("Remove", () => { s.Platform.Webhooks.Remove(hook); AppState.Save("Webhook removed"); host.Refresh(); }))));
        }
        var hn = Ui.Field("Discord #gaming", 200);
        var hu = Ui.Field("", 380, true);
        body.Children.Add(Ui.Card(Ui.Lbl("Webhooks"),
            Ui.Desc("Send session start, end and crash events to services you choose (Discord channel webhooks work out of the box). Only the game, play time and performance numbers are sent. Addresses are stored encrypted.", 0),
            new Border { Height = 8 }, hooks, Ui.Row(hn, new Border { Width = 8 }, hu, new Border { Width = 8 }, Ui.Pill("Add", () =>
            {
                var u = hu.Text.Trim();
                if (!u.StartsWith("https://")) { MessageBox.Show(host, "Webhook addresses must start with https://", "BrainStrap"); return; }
                s.Platform.Webhooks.Add(new Webhook { Name = hn.Text.Trim(), UrlProtected = Secret.Protect(u) });
                AppState.Save("Webhook added");
                host.Refresh();
            }))));
        body.Children.Add(Ui.Card(Ui.Lbl("BrainAPI"),
            Ui.Desc("Build on BrainStrap: brainstrap:// links (above), webhooks (above), and extension packages (BrainExtensions → BrainDev). Packages are JSON built from BrainStrap's actions, so they're safe to share.", 0),
            Ui.Buttons(Ui.Ghost("Open BrainDev", () => host.Go("extensions")))));
        return page;
    }
}

/// <summary>BrainCompanion: a big, glanceable live dashboard for a second screen.</summary>
internal sealed class CompanionWindow : Window
{
    public CompanionWindow()
    {
        Title = "BrainCompanion";
        Width = 560; Height = 470;
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(ForegroundProperty, "Ink");
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        var root = new StackPanel { Margin = new Thickness(28) };
        var theme = Themes.Effective(AppState.S);
        Color Res(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;
        var back = new Canvas();
        var silk = new Silk((Color)ColorConverter.ConvertFromString(theme.Bg), Res("Ink"), Res("Accent"), Res("Accent2"), theme.Dark, 0.8, 1.5, -0.8, 0.7);
        back.Children.Add(silk);
        var layers = new Grid();
        layers.SizeChanged += (_, _) => { silk.Width = layers.ActualWidth; silk.Height = layers.ActualHeight; };
        layers.Children.Add(back);
        layers.Children.Add(root);
        Content = layers;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void Draw()
        {
            root.Children.Clear();
            var live = BrainStrap.Core.Perf.History.ReadLive();
            root.Children.Add(Ui.Lbl("BrainCompanion · live"));
            if (live == null)
            {
                root.Children.Add(Ui.Text("Waiting for Roblox…", 30, "Ink", FontWeights.ExtraBold));
                root.Children.Add(Ui.Small("Start a game and this fills in: FPS, ping, CPU, GPU and session time.", 6));
                return;
            }
            root.Children.Add(Ui.Text(live.GameName ?? "Roblox", 24, "Ink", FontWeights.ExtraBold));
            root.Children.Add(new Border { Height = 16 });
            root.Children.Add(Ui.TileRow(
                Ui.StatTile("pulse", live.Measuring && live.Fps > 0 ? $"{live.Fps:0}" : "—", "FPS"),
                Ui.StatTile("globe", live.Ping is { Samples: > 0 } p && p.LossPct < 100 ? $"{p.Avg:0} ms" : "—", "ping")));
            root.Children.Add(Ui.TileRow(
                Ui.StatTile("cpu", live.Hw != null ? $"{live.Hw.CpuPct:0}% / {live.Hw.GpuPct:0}%" : "—", "CPU / GPU"),
                Ui.StatTile("clock", $"{(DateTime.UtcNow - live.Started).TotalMinutes:0} min", "session")));
        }
        timer.Tick += (_, _) => Draw();
        Loaded += (_, _) => { Draw(); timer.Start(); };
        Closed += (_, _) => timer.Stop();
    }
}
