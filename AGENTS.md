# AGENTS.md — Claude Code Tray

Foundation doc for working in this repo with maximum predictability and minimum friction.
Read this before touching UI or build. Keep it current when conventions change.

## What this is

A native **Windows tray** app (.NET 10, `win-x64`) that shows your Claude Code rate-limit usage as a
crisp, DPI-aware icon, with burn-rate projection and a local 24h usage breakdown. Ships as a single
self-contained `.exe` (no .NET needed to run) via an Inno Setup installer with in-app auto-update.

Unofficial / community project — not affiliated with Anthropic. Reads only usage data Claude Code
already stores locally; never message content.

## Tech stack — three layers, don't mix them up

| Layer | Tech | Why |
|---|---|---|
| Tray icon + menu + lifetime | **WinForms** (`NotifyIcon`, `ApplicationContext`) | WinForms owns tray icons; WPF has no native tray support. Keep it. |
| Icon pixels | **GDI+ / System.Drawing** (`IconRenderer`) | Vector draw at the exact tray size (`SM_CXSMICON`), DPI-aware. Never a downscaled bitmap. |
| Windows (Settings, future dialogs) | **WPF + built-in .NET Fluent theme** (`ThemeMode="System"`) | Declarative XAML = predictable layout; Fluent = Windows 11 look; **zero extra deps**, so the single-exe/installer story is untouched. |

Both `UseWindowsForms` and `UseWPF` are `true` in `ClaudeTray.csproj`. They coexist on one STA
thread: `Application.Run(new TrayContext())` (WinForms) pumps messages for both. A single
`System.Windows.Application` is created lazily in `Program.OpenSettings` (never `Run()`) so WPF's
Fluent theme + pack-URI resources resolve. (Enabling `UseWPF` drops `System.IO` from implicit
usings — it's re-added via `<Using Include="System.IO" />` in the csproj. Don't remove that.)

**`WpfInputBridge` is load-bearing, not plumbing (T135).** A WinForms pump gives WPF windows mouse
input (that is `WndProc`) but **no keyboard input at all** — WPF's `HwndSource` expects the pump to
offer every thread message to `ComponentDispatcher` before `TranslateMessage`, which WPF's own
`Dispatcher.PushFrame` loop does and WinForms' loop does not. Without the bridge (an `IMessageFilter`
installed in `Main`) nothing typed, tabbed or Esc'd reaches any window. Any new entry point that pumps
with WinForms while showing a WPF window must call `WpfInputBridge.Install()`.

## File map

The sources live under `src/`, one folder per subsystem (T129) — the folder is the map, this table is
the detail. **Where does a new file go?** In the folder of the subsystem it belongs to; **the repo root
is for the project, not for code** (csproj, manifest, icon, `lang/`, `docs/`, the roadmap docs). Two
things are deliberate and must not be "fixed": the namespace stays **flat `ClaudeTray`** — folders are
for the person running `ls`, not for the compiler, so no folder-derived namespaces and no new `using`s
— and `lang/` never moves, because the embedded resource name (`ClaudeTray.lang.en.json`) is derived
from its path and `Localization` matches that string at startup. The csproj globs `**/*.cs`, so a new
folder needs no csproj edit; if a move seems to need one, the move is wrong.

**`src/Tray/` — the resident app**

| File | Responsibility |
|---|---|
| `src/Tray/Program.cs` | Entry point, CLI flags, `TrayContext` (tray icon, menu, poll/flash timers), `OpenSettings`. |
| `src/Tray/IconRenderer.cs` | GDI+ icon (vector number + outline + fill bar + projection color) at the real size; also the app `.ico` and social image. |
| `src/Tray/Updater.cs` | Checks GitHub Releases; downloads/runs the installer for in-app self-update. `CurrentVersion`. |

**`src/Usage/` — quota, spend and live throughput**

| File | Responsibility |
|---|---|
| `src/Usage/ApiClient.cs` | Reads OAuth token from `~/.claude/.credentials.json`, calls the API, parses `anthropic-ratelimit-unified-*` headers. |
| `src/Usage/UsageReport.cs` | The pacing report over the two rate-limit windows (5h session, 7d week): the live headers say how much is used and when it resets, the transcripts give the *shape* of the burn, scaled to land on the live number. |
| `src/Usage/UsageHistory.cs` | Append-only log of each successful poll's rate-limit reading (`usage-history.jsonl`, pruned at 8 days) so the burn-up charts draw *measured* utilization instead of inferring it from token counts. |
| `src/Usage/BurnTracker.cs` | Utilization history → least-squares slope → projects exhaustion (`Projection.Ok/Danger/Unknown`). |
| `src/Usage/UsageInsights.cs` | Aggregates last 24h of `~/.claude/projects/**/*.jsonl` into a cost-weighted breakdown. Owns the per-model `Price` table the whole app shares. |
| `src/Usage/ActivityProfile.cs` | The weekly activity shape: 168 buckets (day-of-week × local hour) of `p(active)`, mined from transcript **timestamps only**, decayed per week and shrunk toward a flat prior. Cached daily in `%LocalAppData%\ClaudeTray`. The projection follows this instead of a uniform slope. |
| `src/Usage/HourlyUsage.cs` | Permanent per-hour aggregate (spend + coverage per local day) folded out of `usage-history.jsonl` before its 8-day pruning discards it. Lets idle be *measured* instead of inferred, and is the store week-over-week comparison reads. |
| `src/Usage/ActivityShape.cs` | The weekly projection spent along that shape: calibrated per *measured active hour*, flat through usually-idle stretches, sloped through working ones. Returns null (→ the old straight line) when the profile is thin or the window can't be calibrated. Weekly only. |
| `src/Usage/TranscriptTail.cs` | Byte-level tail over `~/.claude/projects/**/*.jsonl`: watcher + 3s floor sweep, a per-file cursor that only advances past a newline, and de-duplication by `requestId`. Reports each assistant turn within ~250ms for the cost of the appended bytes. `--tail`. |
| `src/Usage/LiveRate.cs` | The rolling tokens/s over that tail: an age-weighted 60s window (triangular kernel, so a pause decays from the moment it starts) with attack-only smoothing. Caller-driven `Tick` — no timer, so a hidden window costs nothing. Also serves that rate **as a series** (`RateHistory`/`TypeRates`/`Projects(n)`: the same kernel at every second, so the newest point equals the headline, plus a 3τ smoothing warm-up before the first reported point so a second's value never changes after it has been drawn) and each project's **sticky slot** — fixed colour/order while it has anything in the window. Sits beside `WindowPace.TokensPerSecond`; neither is quota. `--live`. |
| `src/Usage/LiveChart.cs` | Draws that rate as the last 3 minutes of **lines** on the Statistics window's **Throughput** tab — two instances, one per project (fixed slot colours + grey "others") and one per token type, both always drawn so a colour never changes meaning. 1 Hz geometry rebuild + a `TranslateTransform` slide that cancels the rebuild's jump. Flat and silent when nothing runs; stopped when the window is hidden or minimized, and not drawn at all while another tab is selected. **Appends** to its own history rather than re-importing the recomputed series, so a turn reported late cannot rewrite points already on screen (wholesale adopt only on first render, a changed series set, or a gap in the clock). Draws two samples **past the left edge** so the line's endpoint is clipped rather than oscillating with the slide, and the slide runs 1.05s so a drifting 1s timer can't leave it stalled at zero. Scaled to the **newest** 180 samples and to a round ceiling ruled + labelled in tok/s in a right-hand gutter — the visible peak, or the p95 of the moving samples once the peak is >2× it, with the runs above it drawn dashed along the ceiling and the hover saying how many and how big. `--stats live` renders a deterministic synthetic chart for screenshots. |

**`src/Context/` — the Context Load Inspector**

| File | Responsibility |
|---|---|
| `src/Context/ContextScanner.cs` | Scans every file Claude Code loads before the first prompt (instruction chain + `@imports`, memory index/files, skill & agent frontmatter), splits **eager** (paid every request) from **lazy**, measures observed session-zero from transcripts, and caches the scan by a path+size+mtime fingerprint. |
| `src/Context/ContextFixture.cs` | Builds a throwaway `~/.claude` lookalike (`--sample`) where all 16 rules fire. Use it for any published screenshot — the real machine's project names are client names. |
| `src/Context/ContextReport.cs` | Renders a whole scan as one markdown document (`--context-report`): summary, project table, findings, evidence, and the method behind the numbers. Paths and counts only. |
| `src/Context/ContextNudges.cs` | Rate limiter for the opt-in context-growth toast: at most one per project per week, remembered in `context-nudges.json`. |
| `src/Context/ContextHistory.cs` | Append-only log of each project's eager context (`context-history.jsonl`), one line per project per day and only when it moved. Feeds the drift sparkline and the "+N this week" line. |
| `src/Context/ContextPrompt.cs` | Builds the cleanup prompt handed to Claude Code: findings + fixes + paths, never file contents, and it asks Claude to show its plan before deleting. The app has **no** write path into `~/.claude` — see IMPROVEMENTS §I.4. |
| `src/Context/ContextUsage.cs` | Mines the transcripts for `Skill`/agent invocations (names and counts only) so the window can say "used 45×" or "never". Per-file cache; runs outside `Scan` because it reads hundreds of MB. Memory recalls are deliberately not counted — see the type doc. |
| `src/Context/ContextRules.cs` | The advisor over a `ContextScan`: grounded rules → `Finding` (severity + one sentence + the concrete fix). No new IO and never file contents; narrowed to what is objectively measurable so it doesn't cry wolf. |
| `src/Context/TokenEstimate.cs` | Chars→tokens estimation for markdown, classified per line (prose / code fence / table). Always rendered as an estimate ("≈4.9k"). |

**`src/Profiles/` — accounts, profiles and settings**

| File | Responsibility |
|---|---|
| `src/Profiles/ClaudeAccount.cs` | The local Claude Code account/install reading behind the **System information** settings page: `.claude.json` + `.claude/.credentials.json` → plan (a rate-limit tier mapped to "Claude Max 5x", unmapped tiers shown verbatim), holder, org, extra usage, dates, config dir (`CLAUDE_CONFIG_DIR` honoured), CLI version, project count. Every field nullable; opens files, never writes one; reads the credentials file **only** for `expiresAt`, `subscriptionType` and the scope *count* — no token reaches the UI. `Read(dir)` reads **one** config dir and `Discover()` returns every **profile** on the machine (default first, deduped by `accountUuid`); the `~/.claude.json` fallback is offered *only* for the home dir, or a second profile would inherit the default account's identity. `Discover()` also resolves each profile's **effective auth** (subscription / API key / Bedrock / Vertex) from files+env — presence of a key only, never a value — and `QueryAuthStatusAsync` asks `claude auth status --json` for the authoritative answer. The settings-file pick prefers whichever candidate **names an account**: a near-empty `~/.claude/.claude.json` (created by anything running with `CLAUDE_CONFIG_DIR=~/.claude`) would otherwise shadow the real `~/.claude.json`. `--profiles [--check]`. |
| `src/Profiles/ProfileStore.cs` | Where everything derived from **one** profile lives: `%LocalAppData%\ClaudeTray\profiles\<key>\`, key = the account (`acct-<digest>`) or the config dir (`dir-<digest>`). `UsageHistory`, `HourlyUsage`, `ContextHistory`, `ContextNudges` and the `ActivityProfile` cache all take that key **explicitly** — no ambient "current profile", so polling several means passing different keys. Owns the one-time **move** of the pre-profile flat files into the default profile's directory (a copy would double the permanent hourly store on the next fold). `context-cache.json` / `context-usage.json` stay shared: keyed by absolute path + size/mtime, they cannot confuse two profiles. |
| `src/Profiles/ProfileActivity.cs` | Which profile is being worked in *now*: the newest `projects\**\*.jsonl` **write timestamp** per config dir — directory entries only, never a file opened — probed on the usage poll's cadence, so auto-follow (T126) costs no resident watcher (the reason T101 was dropped) and ~20ms per config dir. `Pick` applies the policy: on-subscription + credentials on disk, a turn inside `FollowWindowSeconds` (30min), nothing stamped more than `MaxFutureSkewSeconds` ahead, and never below the floor a manual choice stamps. Also the single `<config dir>\projects` resolver `ProfileRef` reuses. |
| `src/Profiles/EnvironmentProfile.cs` | The one thing the app writes outside its own settings file: the user-scope `CLAUDE_CONFIG_DIR`, so a profile picked by hand applies to every Claude Code session (T145). What the tray sets, the tray removes — the previous value is remembered and put back. |
| `src/Profiles/Settings.cs` | `Settings` model (JSON in `%LocalAppData%\ClaudeTray`, path exposed as `Settings.DataDir`); clamps out-of-range values. `MonitoredConfigDir` picks which profile the **icon** follows (`ClaudeAccount.PickMonitored` is the one implementation the tray and `--profiles` share), and `FollowActiveProfile` lets `ProfileActivity` move it. `Clone()` is a JSON round-trip through the same serializer that writes the file, so the settings edit buffer and `ApplySettings`' write-back are **total by construction** — a new field needs no copy line anywhere, which is the point (T141). Also `ClaudeProfile` — a registered profile is `{Label, ConfigDir, WorkDir}` and **nothing else**: no address, no token, no plan, so the tray is never a second store of credentials. |

**`src/Core/` — helpers with no subsystem of their own**

| File | Responsibility |
|---|---|
| `src/Core/Localization.cs` | The dependency-free `L` / `{local:Loc key}` layer over the embedded `lang\<code>.json` files: language picked from Settings, else the OS UI language, English as the fallback for a missing key. |
| `src/Core/SafeWalk.cs` | The recursive `~/.claude` walk every scan goes through: per directory, so an unreadable one (untrusted junction, denied ACL, folder deleted mid-sweep) skips its subtree instead of aborting the sweep. Materializes each directory's entries — a `try` around a lazy `Enumerate*` catches nothing — and resolves a reparse point to its target before opening it. |

**Windows (repo root, until T130 moves them to `src/Ui/`)**

| File | Responsibility |
|---|---|
| `SettingsWindow.xaml(.cs)` | The WPF Fluent settings window. **All layout lives in the XAML.** |
| `ContextWindow.xaml(.cs)` | The Context Load window: master/detail over `ContextScanner` — projects left; right, the session-zero gauge (base overhead / instructions / memory / skills, with the transcript-measured tick) over the per-source eager/lazy breakdown. Scans on a background thread; view models are `public` because WPF binding resolves paths by reflection over public types only. |

## UI conventions — the rules that prevent the bugs we already hit

1. **Layout is declarative.** Put structure in XAML grids with explicit `RowDefinition`/
   `ColumnDefinition`. A `*`-sized spacer row pushes a footer down; `Auto` sizes to content.
2. **Never stack by imperative z-order.** The original WinForms sidebar used multiple `Dock=Top`
   labels relying on reverse add-order and they overlapped. If you must use WinForms, stack with a
   `TableLayoutPanel` (explicit rows), never sibling `Dock=Top`.
3. **Theme via `ThemeMode="System"`** and color via `{DynamicResource ...}` Fluent brushes
   (`TextFillColorPrimaryBrush`, `CardBackgroundFillColorDefaultBrush`, `LayerFillColorDefaultBrush`,
   `AccentFillColorDefaultBrush`, `SubtleFillColorSecondaryBrush`, `CardStrokeColorDefaultBrush`,
   `DividerStrokeColorDefaultBrush`, `AccentButtonStyle`). These auto-adapt to light/dark and the
   system accent. Don't hardcode hex for theme-able surfaces.
4. **A window class exposed to WinForms code must be `internal`** (the model `Settings` is internal):
   add `x:ClassModifier="internal"` to the XAML root and `internal partial class` in code-behind.
5. **Verify by looking, every time.** See the workflow below. Do not report a UI change as done
   without a screenshot.
6. **A screenshot cannot see a keyboard bug, and `--settings` cannot either.** The preview flags run a
   **WPF** `Application.Run`; the tray runs a **WinForms** pump, and the two are different *input*
   environments — that difference is how "no keyboard input in any window" (T135) survived every
   preview, capture and screenshot this repo has ever taken. Anything that involves typing, Tab, Esc or
   a shortcut must be verified under **`--settings-tray`**, which hosts the window the way the tray
   does — and the way to do that is **`scripts\Check-Interaction.ps1`**, not a scratch script. See the
   interaction loop below.

## Visual verification workflow (the predictability loop)

Use the **`preview-ui`** skill, or directly:

```
dotnet build -c Debug
powershell -ExecutionPolicy Bypass -File scripts\Capture-Window.ps1   # -> docs\_preview\settings.png
```

Then Read `docs\_preview\settings.png` and judge it. `--settings` opens the window standalone so no
tray-menu clicking is needed; the capture script is per-monitor-DPI-aware (required at 150–200%).
`docs\_preview\` is git-ignored.

## Interaction verification (the loop a capture cannot close)

A picture proves layout. It cannot prove a key press arrives — that is how T135 survived every
screenshot this repo ever took. `scripts\Check-Interaction.ps1` drives the real UI through UI
Automation and asserts a pass/fail (exit 0 only if every check passed):

```
powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1                    # both cases
powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 -Case Keyboard     # typing/Tab/arrows
powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 -Case Menu         # the tray menu
powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 -Case Menu -UseRunning
powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 -Lang pt-BR        # any shipped language
```

- **Keyboard** launches `--settings-tray` (the WinForms pump), navigates by clicking the sidebar, types
  into a `TextBox` and reads it back through `ValuePattern`, Tabs out, and drives a `Slider` with an
  arrow key. Run it after anything that touches input, focus or hosting.
- **Menu** launches the tray, opens the notification icon's menu and reads its entries, then expands
  *Open Claude Code* and reads the per-profile ones. It **refuses** to run while another tray is alive
  (the single-instance mutex would make its own launch exit silently, and it would then read the other
  tray's menu and call that a pass) — quit that tray, or pass `-UseRunning` to drive it deliberately.
- **Reading nothing is a FAIL, never a pass.** The script's header documents the three UIA traps
  (no clickable point / overflow flyout, collapsed panes absent from the tree, the menu not always
  opening) — read it before writing any new check by hand.

## Build / run / dev helpers

```
dotnet build -c Debug                 # fast compile check
dotnet run -c Release                 # build + run the tray app
dotnet publish -c Release             # single self-contained .exe -> bin\Release\net10.0-windows\win-x64\publish\

dotnet run -- --settings              # open just the Settings window (preview; WPF pump)
dotnet run -- --settings System       # ...opened on the System information page (any page name works)
dotnet run -- --settings-tray [page]  # ...hosted the way the TRAY hosts it (WinForms pump). The only
                                      # preview that can see a keyboard bug — see UI convention 6.
dotnet run -- --profiles              # every Claude Code profile (config dir) discovery finds, in order
dotnet run -- --profiles <dir> [dir]  # ...plus dirs treated as explicitly registered (the Settings list)
dotnet run -- --profiles --check      # ...also asking `claude auth status --json` per profile
                                      # --profiles also prints each profile's last turn and which one
                                      # auto-follow would point the icon at (T126), with the probe cost
dotnet run -- --capture-settings <out.png> [page] [scroll=<dip>] [profile=<n>]
                                      # Settings window rendered OFF-SCREEN to PNG (RenderTargetBitmap).
                                      # Prefer this over scripts\Capture-Window.ps1: that one copies the
                                      # pixels on screen inside the window rect, so any app that steals
                                      # focus or sits on top ends up in the file.
dotnet run -- --lang es --context --window   # any command, rendered in another language (i18n check)
dotnet run -- --render <dir>          # dump tray-icon PNGs at 16/20/32 px
dotnet run -- --insights              # print the 24h usage breakdown to the console

dotnet run -- --activity              # the weekly activity heatmap behind the projection
dotnet run -- --activity --numbers    # ...as raw per-bucket percentages
dotnet run -- --activity --refresh    # ...forcing a rescan past the daily cache
dotnet run -- --activity --root <dir> # ...against a stand-in for ~/.claude
dotnet run -- --activity --measured   # ...plus the same week measured from the folded hourly store
dotnet run -- --activity --fold       # fold every complete day of the raw log into that store now
dotnet run -- --stats shape           # the Statistics window on the weekly tab, activity-aware
                                      #   projection running out before the reset (bands + landing)
dotnet run -- --stats shape ghost     # ...plus a synthetic previous-week ghost curve
dotnet run -- --stats method          # ...with the "how these numbers are measured" popup already open
                                      #   (it's a separate top-level window, so --capture-stats can't
                                      #   see it — use scripts\Capture-Window.ps1 for this one)
dotnet run -- --capture-stats docs\_preview\shape shape   # ...all three tabs to PNG, off-screen
dotnet run -- --capture-stats out shape profile=1     # ...rendered as another profile (T128)

dotnet run -- --context               # what every session costs before you type: per-project table
dotnet run -- --context <slug|name>   # one project, source by source (eager/lazy, bytes, ≈tokens)
dotnet run -- --context --all         # every project in full detail
dotnet run -- --context --calibrate   # estimate vs. transcript-measured session zero, and the fit
dotnet run -- --context --check       # the rule engine's findings, grouped by severity
dotnet run -- --context --usage       # which skills/agents were actually invoked (90d)
dotnet run -- --context --prompt [project]  # the cleanup prompt the window copies
dotnet run -- --context-report report.md   # the whole picture as one markdown file
dotnet run -- --context --skills      # expand the folded skill/agent index instead of one summary row
dotnet run -- --context --no-cache    # force a cold scan (skip %LocalAppData%\ClaudeTray cache)
dotnet run -- --context --root <dir>  # scan a fixture tree instead of ~/.claude
dotnet run -- --context --sample      # build + scan the bundled fixture (all rules fire)
dotnet run -- --context --window      # open just the Context Load window (preview)
dotnet run -- --context --window <slug|name>   # ...opened on one project
dotnet run -- --context --window all   # ...opened on the cross-project overview
dotnet run -- --context --window --sample --lang en  # the fixture, in English (for screenshots)
dotnet run -- --context --window <slug> --scroll  # ...scrolled to the source table (for screenshots)
dotnet run -- --context --window <slug> --simulate # ...with the 3 heaviest sources ticked (what-if)
dotnet run -- --context --window <slug> --demo-history # ...with a synthetic drift series
dotnet run -- --makeicon ClaudeTray.ico   # regenerate the multi-resolution app icon
dotnet run -- --capture-toast context docs\_preview\toast-context.png  # the nudge toast
dotnet run -- --social docs\social-preview.png  # regenerate the social card
```

## Release process

Version lives in **one place**: `<Version>` in `ClaudeTray.csproj`. Everything derives from it.

```
# bump <Version>, then:
build-installer.cmd                   # publish + build dist\ClaudeTray-Setup.exe
```

Then create a GitHub release tagged `vX.Y.Z` and attach `ClaudeTray-Setup.exe`. Installed copies
self-update from it.

## Roadmap & docs maintenance

Planned and shipped work is tracked across **five root files**, each with one job. Read the
**`roadmap-docs` skill** before adding a task, marking one shipped, or editing any of them — it holds
the cross-file update rules, the T-number/block-letter discipline, and the one-task-one-commit rule.

| File | Job |
|---|---|
| [`ROADMAP.md`](ROADMAP.md) | Active backlog — the only source of truth for task status. One line per task. |
| [`CHANGELOG.md`](CHANGELOG.md) | What has shipped. One line per task; `git log` is authoritative for detail. |
| [`IMPROVEMENTS.md`](IMPROVEMENTS.md) | Design rationale for *unshipped* work, plus §I the binding house constraints. |
| [`STRATEGY.md`](STRATEGY.md) | Positioning, licence, distribution, the trust promise. Never a task. |
| [`last-task.md`](last-task.md) | Next free `T<n>` + next block letter + a one-line-per-task log. |

They live at the repo root on purpose: `docs/` is the published GitHub Pages site.

## Conventions

- **Commits**: use `run-commit.cmd -m "<conventional-commits title>"` (stages all, AI writes body).
  **One commit per finished, validated task — never batch two tasks into one commit**, and check
  `git status` first, since it stages everything. A batch of ≥2 tasks runs under `/loop`, one task
  per iteration. See the `roadmap-docs` skill.
- **Privacy**: only token counts, model ids, flags, tool/skill names and the session `cwd` are ever
  read from transcripts — never message content. Keep it that way.
- **A transcript's `cwd` is the working directory of *that turn*, not the project root.** A single
  `cd` inside a session changes it, so naming a project `GetFileName(cwd)` renames it to whatever
  subfolder a command last ran in. The `projects/<slug>` name encodes the root but is lossy (every
  non-alphanumeric becomes `-`, so `…-shio-2026-3` cannot be split back into `2026.3`). Resolve by
  walking the cwd up to the ancestor whose encoding equals the slug — `TranscriptTail.ResolveName`.
- **A transcript "turn" is a `requestId`, not a line.** Claude Code writes **one `assistant` line per
  content block** of a single API response (thinking, then tool_use, …), and every one of those lines
  repeats that response's `message.usage` verbatim. Summing per line double-counts, weighted toward
  heavy tool use. De-duplicate on `requestId` (or `message.id`) — `TranscriptTail` does; the older
  `UsageReport.ScanTokens` does not (its curve is rescaled to the live utilization, which hides it).
- **Single instance** is enforced by a named mutex; a second launch exits silently.
- The marketing page is `docs/index.html` (GitHub Pages, served from `/docs`).
- **New user-visible strings go into all five `lang/*.json`**, not just `en`.
  Verify with `--lang <code>` (process-only override, saved preference untouched) and screenshot at
  least one non-English language before calling a UI change done.
