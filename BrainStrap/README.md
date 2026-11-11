# BrainStrap 2.0

A Roblox launcher for Windows in the RBLX.BRAIN style, with 60 features across six areas: BrainAI, Performance Lab, Server Intelligence, Customization Studio, Advanced Tools and BrainLife. When you press **Play** on roblox.com, Windows opens BrainStrap. It shows a launch screen, picks the right profile, applies your graphics flags and mods, starts Roblox, and then watches the session from the outside.

> **Status: not compiled or tested yet.** This source (39 C# files, about 7,600 lines) was written without a Windows PC or the .NET SDK. It passed automated checks for brackets, XAML names and handlers, and for calls between files, but it has never been built or run. **Expect compile errors on the first build**, and test everything on your own PC before you give the .exe to anyone. The checklist at the bottom lists what still needs checking against the real Roblox.

## The 60 features and where they live

| Area | Features | In the app |
|---|---|---|
| **BrainAI** | ⭐BrainAI Optimizer, ⭐One Click Optimization (BrainBoost), AI FPS Doctor, AI Lag Detective, AI Settings Builder, AI Compatibility Checker, AI Performance Predictions, AI PC Analyzer, AI Troubleshooter, AI Profile Generator | BrainAI page, plus BrainBoost on Home |
| **Performance Lab** | ⭐BrainBoost, FPS Benchmark, Frametime Graph, Performance History, FPS Stability Score, Smart Graphics Profiles, Thermal Monitor, Memory Monitor, Background App Advisor, Benchmark Comparison | Performance page and Graphics page |
| **Server Intelligence** | ⭐BrainServers, Server Region Viewer, Ping History, Connection Quality Score, Smart Server Suggestions, Server Population Viewer, Server Favorites, Private Server Shortcuts, Join Link Generator, Connection Diagnostics | Servers page |
| **Customization Studio** | ⭐BrainThemes, Live Theme Preview, Custom Launch Animations, Custom Launch Sounds, Theme Marketplace (share codes and community lists), Minimal Mode, Custom Fonts, Custom Backgrounds, Layout Builder, Profile Themes | Studio page |
| **Advanced Tools** | ⭐BrainProfiles, Profile Auto Selection, Safe FastFlag Editor, FastFlag Validator, Settings Timeline, Instant Rollback, Roblox Repair Center, Cache Manager, Update Inspector, Crash Report Analyzer | Tools and Graphics pages, plus the launch screen |
| **BrainLife** | ⭐BrainHUD, Game Library, Taskbar Jump List, Battery-Aware Profiles, Game Mode+, Companion Apps, Screenshot Gallery, Playtime Journal, Backup & Restore, Command Palette (Ctrl+K) | BrainLife page and Home |

## New in 2.1: independent, and a new look

- **BrainStrap is the whole launcher.** `Core/RobloxDeploy.cs` downloads the official, unmodified Roblox player from Roblox's own download servers, checks every package against the MD5 in Roblox's manifest, unpacks it into `%LOCALAPPDATA%\BrainStrap\Roblox\Versions` and keeps it updated. No Roblox launcher and no other bootstrapper is needed. If Roblox adds a package this version doesn't know, or a download fails, it falls back to the Roblox that is already installed (or Roblox's own launcher when there is one). The switch is Launch → "BrainStrap installs and updates Roblox itself".
- **roblox:// join links** are handled by BrainStrap too, so Quick Play and server joins work on a PC that has never had Roblox's launcher.
- **The interface was rebuilt**: its own title bar, a live silk backdrop (`UI/Silk.cs`, the website's shader worked out on the processor), glass cards, line icons (`UI/Icons.cs`), bundled Manrope and JetBrains Mono (SIL Open Font License, texts in `Assets/Fonts`), a Home page with a Play button, and motion throughout.
- **BrainSight** (BrainLife page): a crosshair overlay in BrainStrap's own click-through window, shown only while Roblox is in front.
- **Windows tuning** (Launch page): the high-performance GPU preference and the "disable fullscreen optimisations" compatibility switch, written exactly where Windows Settings writes them and removed again when switched off.
- **`BrainStrap.exe --shots <folder>`** saves a picture of every page with sample data. The build runs it and publishes the pictures to the `ci-shots` branch; the website shows those real screenshots.

## The four pillars (new in this version)

| Pillar | What it is | In the app |
|---|---|---|
| **BrainExtensions** | 12 built-in extensions (⭐BrainPilot, BrainHUD, BrainPulse, BrainReplay, BrainMacros, BrainAutomation, BrainNotes, BrainWidgets, BrainDev, BrainCompanion, BrainVault, BrainCommand) plus installable ones. Extensions are **data only**: JSON built from BrainStrap's fixed list of actions, so none can run code. Each declares permissions, passes a 10-point safety scan, and keeps its older versions for rollback. | BrainExtensions page |
| **BrainMods** | Launcher-side packs: theme, accent, font, background, launch screen, HUD placement, Home layout. 10 built-in packs, Mod Studio, collections, conflict detector, live preview in a sandbox copy, per-profile packs, version history, one-click restore. Packs never touch the Roblox client. | BrainMods page |
| **BrainFlags** | An editor that respects Roblox's FastFlag allowlist (September 2025). Only allowlisted flags are written; everything else is shown as "ignored by Roblox" with the manual step that replaces it. BrainFlag AI, visual + JSON editors, explorer with docs and risk ratings, conflict scanner, presets, compare, history, known-good restore, playground. | BrainFlags page |
| **BrainConnect** | RBLX.BRAIN (`brainstrap://` links, always confirmed), Discord, GitHub, OBS, Spotify, Windows, Roblox Studio, Twitch/YouTube shortcuts, Stream Deck links, webhooks, BrainAPI. No passwords or tokens are requested anywhere; every integration has an off switch. | BrainConnect page |

Plus **BrainFlow** (visual automations: trigger → actions), **BrainStore** (one shelf for extensions, packs, flag presets and themes) and **BrainBuilder** (a sentence → a proposed extension with a dry-run test and permission review).

### The flag allowlist

`catalog.json` carries the allowlist (`allowlist[]`, with `allowlistSource`). `FlagCatalog.Resolve` skips anything not on it unless the user turns on "write ignored flags anyway". When Roblox changes the list, update `catalog.json` only. The FPS cap is no longer a flag: BrainStrap tells the user to set **Maximum Frame Rate** in Roblox's own settings.

### BrainStore index format

The built-in shelf is first-party only. A community store is a JSON file over https (set in BrainStore → Store sources):

```
{ "publishers": [ { "id": "acme", "name": "Acme", "verified": true, "bio": "" } ],
  "items": [ { "type": "extension|pack|flags|theme", "id": "acme.stream-mode", "name": "Stream mode", "publisher": "acme",
               "summary": "", "description": "", "screenshots": ["https://…"], "permissions": ["hud","apps"],
               "versions": [ { "version": "1.0.0", "date": "2026-11-01", "notes": "", "url": "https://…", "sha256": "…" } ],
               "dependencies": [], "rating": 0, "reviews": [], "tags": [] } ],
  "collections": [ { "name": "", "description": "", "items": ["acme.stream-mode"] } ],
  "submitUrl": "https://…" }
```

Downloads must be https, under 1 MB, and match `sha256`; extensions are then safety-scanned and shown with their permissions before install. **Ratings and reviews come from your index: BrainStrap ships with none and never invents any.** "Verified" is whatever your index says, so only mark publishers you have actually checked.

## Get a real BrainStrap.exe (GitHub Actions)

`.github/workflows/build.yml` builds the exe on GitHub's Windows machines, so you don't need Windows or the SDK yourself:

1. Create a **public** GitHub repository and push this folder (the one containing `.github/` and `BrainStrap/`).
2. Actions → **Build BrainStrap** → Run workflow. If the compiler reports errors, they are listed in the run log; fix and re-run.
3. When it's green, the Release contains `BrainStrap.exe` and its SHA-256.
4. On the website, set `repo: 'you/your-repo'` in `BRAINSTRAP_DOWNLOAD`. The Download button then points at `https://github.com/you/your-repo/releases/latest/download/BrainStrap.exe`, which always serves the newest build.

## How BrainAI works (be honest about this on your site)

- **The local engine** (`brainai.json` and `Core/BrainAI/Engine.cs`) is a **rules-based expert system**, not a neural network. It turns plain-English requests into weights (smooth, quality, battery, latency) and combines them with the detected hardware. It only picks values that `catalog.json` allows, explains every change, and estimates a *range* with a stated confidence. The website demo runs the same `brainai.json` (`web/brainai.js`), so the website and the app give identical answers.
- **The diagnostics** (FPS Doctor, Lag Detective, Compatibility Checker, Troubleshooter) reason over real measurements: PresentMon frame times, Windows performance counters, ping, Roblox's logs, and your settings.
- **Cloud mode** (optional and off by default) sends the request, a hardware summary and the setting list to Anthropic with the **user's own API key**. That key is encrypted with Windows DPAPI. No account details, logs or files are sent, and the answer is checked against the catalog before anything is shown. The default model is `claude-haiku-5-5` and can be changed in BrainAI → Cloud AI.

## What it never does (enforced in code)

- **No injection, hooks or memory access in Roblox.** FPS comes from PresentMon (ETW, from the outside). CPU, GPU and RAM figures come from Windows performance counters, like Task Manager. Server details come from Roblox's own log files. The HUD is a separate click-through window.
- **Your `.ROBLOSECURITY` cookie is never touched.** Joining servers goes through Roblox's own `roblox://` links and your normal login. The Cache Manager never touches `Roblox\LocalStorage`.
- **Logs never leave the PC.** Only matched patterns are used. The crash analyzer shows short excerpts with tickets, tokens and cookies scrubbed out.
- **No gameplay-changing flags.** The validator blocks physics, network, replication, character and similar flag names, both in the editor and in imports.
- **Network use is opt-in or public:**
  - Roblox's public APIs: version, game info, public server lists, icons.
  - ipinfo.io for the server region: off by default. It sends the *Roblox server's* IP.
  - Anthropic: only when cloud AI is switched on and a key has been added.
- **Secrets are encrypted with DPAPI:** the cloud AI key and private-server links. Both are left out of timeline snapshots and backups.
- **No admin rights**, except one optional Windows prompt that adds the user to "Performance Log Users" so PresentMon can read frame timing.

## Build it

1. Install the **.NET 8 SDK** on Windows 10 or 11.
2. Run `dotnet restore`. It fetches three NuGet packages: `System.Management`, `System.Diagnostics.PerformanceCounter` and `System.Security.Cryptography.ProtectedData`.
3. Publish:
```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```
4. Sign the exe, publish its SHA-256 hash, and host it yourself (GitHub Releases works well). Then put the link and the hash into the website's `BRAINSTRAP_DOWNLOAD` block.

### FPS measuring (PresentMon)

BrainStrap does **not** download PresentMon itself.
- **Option 1:** the user downloads the console exe from https://github.com/GameTechDev/PresentMon/releases and clicks **Performance → Locate…**.
- **Option 2:** you ship `PresentMon.exe` next to `BrainStrap.exe` (it's MIT-licensed, so include its licence).

BrainStrap runs PresentMon with:
`--process_name RobloxPlayerBeta.exe --output_file <csv> --session_name … --stop_existing_session --terminate_on_proc_exit [--timed N --terminate_after_timed]`

It reads the `msBetweenPresents` / `MsBetweenPresents` / `FrameTime` column. If a newer PresentMon renames its options, use **Performance → Advanced** for extra arguments, or edit `Core/Perf/FrameCapture.cs`.

### Theme Marketplace list format

Host a JSON file over https in either form:
- `["BST1.…", "BST1.…"]`
- `{ "themes": [ { "code": "BST1.…" } ] }`

Codes come from Studio → Copy share code, or from the theme builder on the website. A code contains only a name and nine colours.

## Verify on a real PC (none of this could be checked here)

1. **It builds.** Fix whatever the compiler reports.
2. **The launch arguments** in `LaunchRequest.BuildArgs` still match what Roblox's own launcher passes. Compare them in Task Manager → Details → Command line.
3. **The version check** (`clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer`) returns `clientVersionUpload` and `version`.
4. **The Roblox log patterns** in `Core/Servers/RobloxLogs.cs` match current logs: "Joining game", "UDMUX Address", "serverId:" and the disconnect reasons. If server info never appears, open a log in `%LOCALAPPDATA%\Roblox\logs` and update the regexes.
5. **Server joins:** `roblox://experiences/start?placeId=…&gameInstanceId=…` lands in that server. The public server list (`games.roblox.com/v1/games/{id}/servers/Public`) still works without a login, and gets rate-limited if hammered.
6. **PresentMon:** a 60-second benchmark produces frames. Then check the permission flow: the admin prompt, signing out and back in, and measuring without admin.
7. **Performance counters:** "GPU Engine" (engtype_3D), "GPU Process Memory" and "Thermal Zone Information" show values. NVIDIA GPU temperatures come through `nvml.dll`.
8. **The HUD** stays on top and click-through in borderless fullscreen, and Ctrl+Shift+H toggles it.
9. **Flags** still apply. Set Render quality to 1 in BrainFlags as a quick test (the game should look visibly lower), then try "Write values as text" if nothing happens. Compare `catalog.json`'s allowlist with Roblox's current one.
9b. **The new pillars:** install the sample extension from BrainDev, apply and restore a mod pack, build a flow with BrainBuilder and launch Roblox, register `brainstrap://` and open `brainstrap://open/flags`, add a Discord webhook and press Test.
10. **Mods, rollback, backup and uninstall** all restore things exactly.
11. **Cloud AI** works with a real key, and a wrong key gives a clear message.

BrainStrap is an independent project. It is not made, endorsed or sponsored by Roblox Corporation. Roblox is a trademark of Roblox Corporation.
