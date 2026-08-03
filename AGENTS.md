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
| The window (one shell, three pages) | **WPF + built-in .NET Fluent theme** (`ThemeMode="System"`) | Declarative XAML = predictable layout; Fluent = Windows 11 look; **zero extra deps**, so the single-exe/installer story is untouched. |

Both `UseWindowsForms` and `UseWPF` are `true` in `ClaudeTray.csproj`. They coexist on one STA
thread: `Application.Run(new TrayContext())` (WinForms) pumps messages for both. A single
`System.Windows.Application` is created lazily in `TrayContext.EnsureWpfApp` (never `Run()`) so WPF's
Fluent theme + pack-URI resources resolve. (Enabling `UseWPF` drops `System.IO` from implicit
usings — it's re-added via `<Using Include="System.IO" />` in the csproj. Don't remove that.)

**`WpfInputBridge` is load-bearing, not plumbing (T135).** A WinForms pump gives WPF windows mouse
input (that is `WndProc`) but **no keyboard input at all** — WPF's `HwndSource` expects the pump to
offer every thread message to `ComponentDispatcher` before `TranslateMessage`, which WPF's own
`Dispatcher.PushFrame` loop does and WinForms' loop does not. Without the bridge (an `IMessageFilter`
installed in `Main`) nothing typed, tabbed or Esc'd reaches any window. Any new entry point that pumps
with WinForms while showing a WPF window must call `WpfInputBridge.Install()`.

## File map

The sources live under `src/`, one folder per subsystem (T129–T132) — the folder is the map, this table
is the detail. **Where does a new file go?** In the folder of the subsystem it belongs to; **the repo root
is for the project, not for code** (csproj, manifest, icon, `lang/`, `docs/`, the roadmap docs). Two
things are deliberate and must not be "fixed": the namespace stays **flat `ClaudeTray`** — folders are
for the person running `ls`, not for the compiler, so no folder-derived namespaces and no new `using`s
— and `lang/` never moves, because the embedded resource name (`ClaudeTray.lang.en.json`) is derived
from its path and `Localization` matches that string at startup. The csproj globs `**/*.cs`, so a new
folder needs no csproj edit; if a move seems to need one, the move is wrong.

**`src/Tray/` — the resident app**

| File | Responsibility |
|---|---|
| `src/Tray/Program.cs` | `Main` and nothing else: the arg dispatch that routes every flag, `ArgValue`, plus `StartupManager` (the HKCU Run entry) and `WpfInputBridge` (see above). |
| `src/Tray/TrayContext.cs` | The resident app: tray icon, menu, poll/flash/update timers plus the 6h `_backgroundSample` one (context scan + activity-grid warm-up), `ApplySettings`, tooltip, icon render, the watched-profile list and `OpenMain` — the **one** entry point to the **one** window (T158), from a left-click on the icon or the menu's bold *Open*. |
| `src/Tray/IconRenderer.cs` | GDI+ icon (vector number + outline + fill bar + projection color) at the real size; also the app `.ico` and social image. |
| `src/Tray/Updater.cs` | Checks GitHub Releases; downloads/runs the installer for in-app self-update. `CurrentVersion`. |

**`src/Cli/` — the headless printers** (one file per flag family; `Main` dispatches, these run and exit)

| File | Responsibility |
|---|---|
| `src/Cli/ContextCli.cs` | `--context` and `--context-report`: the scan printed as a report (projects, sources, findings, cleanup prompt, skill/agent usage, calibration) and the same scan written as markdown. |
| `src/Cli/ActivityCli.cs` | `--activity`: the weekly activity profile as a 24×7 shaded grid, plus the measured-hours variant. |
| `src/Cli/LiveCli.cs` | `--tail` and `--live`: each assistant turn as it lands, and the rolling tok/s with its per-project sparklines. |
| `src/Cli/ProfilesCli.cs` | `--profiles [--check]`: every profile on the machine, its auth, its config-dir action and its icon accent band. |
| `src/Cli/SelfTestCli.cs` | `--selftest [--quick]`: 270+ assertions over the pacing, store, grid, tail, live-rate, series, language-table, method-note, number-format and palette rules, on synthetic inputs, exiting non-zero on failure — run on every push by `.github/workflows/check.yml` and again before an installer is packaged by `build.yml`. Writes only a temp tree and a `selftest` profile dir, both removed. The repo's test suite — see below. |
| `src/Cli/ProbeCli.cs` | `--probe [--live] [--all]`: the rate-limit headers verbatim — the recorded capture log first, then one live call, which is itself recorded against the monitored profile (T212) rather than printed and dropped. This app reads four of the fourteen, and its reading is not what the API said. Quota metadata only, never a token. |
| `src/Cli/StatsPreviews.cs` | The one table of Statistics previews `--stats` and `--capture-stats` both read (T186): a variant per row with what it feeds the page, the modifiers that compose with any of them, and the refusal — an unknown name prints the catalogue and exits 1 rather than rendering the default sample as if it were what was asked for. |
| `src/Cli/ToastPreviews.cs` | The same table for the toast cards, read by `--simulate-reset` and `--capture-toast` both (T198). Two rules it carries: an unknown variant is refused with the catalogue, and **a capture flag never defaults its output path** — `--capture-toast` requires one, and a default that is kept goes under git-ignored `docs\_preview\`, never the working directory. |
| `src/Cli/PreviewCli.cs` | The deterministic previews behind the published images: `--simulate-reset`, `--capture-toast`, `--render` (icon contact sheet), `--makeicon`, and the gap-demo report `--stats gapdemo` feeds. |

**`src/Usage/` — quota, spend and live throughput**

| File | Responsibility |
|---|---|
| `src/Usage/ApiClient.cs` | Reads OAuth token from `~/.claude/.credentials.json`, calls the API, parses `anthropic-ratelimit-unified-*` headers. |
| `src/Usage/HeaderProbe.cs` | The capture log behind `--probe`: every rate-limit response whose header *shape* has changed, appended per profile with the headers verbatim. Records a transition whenever it happens, so the reading T181 needs does not depend on somebody running a command at the right moment. |
| `src/Usage/QuotaState.cs` | Which of three states an account is in — in the quota, past it and billing, or stopped — from the utilization, the extra-usage flag and the overage figure. The single answer the icon, the tooltip, the poll's idle and the toast all read, so they cannot disagree about whether work has stopped. |
| `src/Usage/UsageReport.cs` | The pacing report over the two rate-limit windows (5h session, 7d week): the live headers say how much is used and when it resets, the transcripts give the *shape* of the burn, scaled to land on the live number. |
| `src/Usage/UsageHistory.cs` | Append-only log of each successful poll's rate-limit reading (`usage-history.jsonl`, pruned at 8 days) so the burn-up charts draw *measured* utilization instead of inferring it from token counts. |
| `src/Usage/BurnTracker.cs` | Utilization history → least-squares slope → projects exhaustion (`Projection.Ok/Danger/Unknown`). |
| `src/Usage/UsageInsights.cs` | Aggregates last 24h of `~/.claude/projects/**/*.jsonl` into a cost-weighted breakdown. Owns the per-model `Price` table the whole app shares. |
| `src/Usage/ActivityProfile.cs` | The weekly activity shape: 168 buckets (day-of-week × local hour) of `p(active)`, mined from transcript **timestamps only**, decayed per week and shrunk toward a flat prior. Cached daily in `%LocalAppData%\ClaudeTray`, over a per-file sweep cache (`activity-sweep.json`, path+size+mtime → the absolute local hours that file touched) so a rebuild costs only the transcripts that changed; the week index is derived at aggregation, never cached, because it is relative to now. The projection follows this instead of a uniform slope. |
| `src/Usage/HourlyUsage.cs` | Permanent per-hour aggregate (spend + coverage per local day) folded out of `usage-history.jsonl` before its 8-day pruning discards it. Lets idle be *measured* instead of inferred, and is the store week-over-week comparison reads. Owns the measured half of the away-week test (T152): a week is judged only once half its 168 hours carry a reading, then dropped if it is under `AwayFraction` of the median judged week's active hours. |
| `src/Usage/ActivityShape.cs` | The weekly projection spent along that shape: calibrated per *measured active hour*, flat through usually-idle stretches, sloped through working ones. Returns null (→ the old straight line) when the profile is thin or the window can't be calibrated. Weekly only. |
| `src/Usage/TranscriptTail.cs` | Byte-level tail over `~/.claude/projects/**/*.jsonl`: watcher + 3s floor sweep, a per-file cursor that only advances past a newline, and de-duplication by `requestId`. The watcher's paths are the sweep's **work list**; the whole tree is walked only every 30s (`ReconcileMs`), or on every sweep when there is no watcher. Reports each assistant turn within ~250ms for the cost of the appended bytes. `--tail`. |
| `src/Usage/LiveRate.cs` | The rolling tokens/s over that tail: an age-weighted 60s window (triangular kernel, so a pause decays from the moment it starts) with attack-only smoothing. Caller-driven `Tick` — no timer, so a hidden window costs nothing. Also serves that rate **as a series** (`RateHistory`/`TypeRates`/`Projects(n)`: the same kernel at every second, so the newest point equals the headline, plus a 3τ smoothing warm-up before the first reported point so a second's value never changes after it has been drawn) and each project's **sticky slot** — fixed colour/order while it has anything in the window. Sits beside `WindowPace.TokensPerSecond`; neither is quota. `--live`. |
| `src/Usage/LiveChart.cs` | Pointing at a plot snaps a crosshair to a second, dots each line and opens an in-plot readout of that second (T104) — its text comes from the caller's `Readout` callback, its numbers from the *drawn* history. Draws that rate as the last 3 minutes of **lines** on the Statistics window's **Throughput** tab — two instances, one per project (fixed slot colours + grey "others") and one per token type, both always drawn so a colour never changes meaning. 1 Hz rebuild + a `TranslateTransform` slide cancelling its jump, two samples past the left edge so the endpoint is clipped rather than oscillating; see the type doc for both. Flat and silent when nothing runs; stopped when the window is hidden or minimized, and not drawn at all while another tab is selected. **Appends** to its own history rather than re-importing the recomputed series, so a turn reported late cannot rewrite points already on screen (wholesale adopt only on first render, a changed series set, or a gap in the clock). Scaled to the **newest** 180 samples and to a round ceiling ruled + labelled in tok/s in a right-hand gutter — the visible peak, or the p95 of the moving samples once the peak is >2× it, with the runs above it drawn dashed along the ceiling and the hover saying how many and how big. `--stats live` renders a deterministic synthetic chart for screenshots (`ThroughputFixture`). |

**`src/Context/` — the Context Load Inspector**

| File | Responsibility |
|---|---|
| `src/Context/ContextScanner.cs` | Scans every file Claude Code loads before the first prompt (instruction chain + `@imports`, memory index/files, skill & agent frontmatter), splits **eager** (paid every request) from **lazy**, measures observed session-zero from transcripts, and caches the scan by a path+size+mtime fingerprint. |
| `src/Context/ContextFixture.cs` | Builds a throwaway `~/.claude` lookalike (`--sample`) where all 16 rules fire. Use it for any published screenshot — the real machine's project names are client names. |
| `src/Usage/ThroughputFixture.cs` | The deterministic three minutes behind `--stats live` / `--capture-stats … live`: four repos plus a residual, one deliberate cache write, and the pose the readout is held in. Shaped through the real `LiveRate.RateFrom` kernel, so the published image is what the app would draw. `ContextFixture`'s rule: published chart shots come from here. |
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
| `src/Profiles/ClaudeAccount.cs` | The local Claude Code account/install reading behind the **System information** settings page: `.claude.json` + `.claude/.credentials.json` → plan (tier → "Claude Max 5x", unmapped verbatim), holder, org, extra usage, dates, config dir (`CLAUDE_CONFIG_DIR` honoured), CLI version, project count. Every field nullable; opens files, never writes one; reads the credentials file **only** for `expiresAt`, `subscriptionType` and the scope *count* — no token reaches the UI. `Read(dir)` reads **one** config dir; `Discover()` returns every **profile** (default first, deduped by `accountUuid`), and the `~/.claude.json` fallback is offered *only* for the home dir, or a second profile would inherit the default account's identity. **Only `Discover(settings.Profiles)` applies the label the user typed** — anything reporting what a menu will say takes it (T232). `Discover()` also resolves each profile's **effective auth** (subscription / API key / Bedrock / Vertex) from files+env — presence of a key only, never a value — and `QueryAuthStatusAsync` asks `claude auth status --json` for the authoritative answer. The settings-file pick prefers whichever candidate **names an account**: a near-empty `~/.claude/.claude.json` would otherwise shadow the real `~/.claude.json`. `--profiles [--check]`. |
| `src/Profiles/AccountFixture.cs` | Builds a throwaway pair of config dirs — a personal **Max 20x** (no org, so that row collapses) and a **Team seat** (org, type, role) — read back through `ClaudeAccount.Read`, so the page's own parser sees the fixture. Use it for any published shot of **System information** (`--settings System --sample`, `--reveal` to unmask): masking hides a name and an address, but the organization and its mail domain *are* the reading, and here the org is a client's. No token is written even in the fixture. |
| `src/Profiles/EnvironmentFixture.cs` | The sampled `CLAUDE_CONFIG_DIR` behind `--sample-env` (T231): the disagreements this machine is never in, so T172's mark and T173's read-back are visible without rewriting the developer's registry. Modes resolve from the profiles really here; sampling is one-way. |
| `src/Profiles/ProfileStore.cs` | Where everything derived from **one** profile lives: `%LocalAppData%\ClaudeTray\profiles\<key>\`, key = the account (`acct-<digest>`) or the config dir (`dir-<digest>`). `UsageHistory`, `HourlyUsage`, `ContextHistory`, `ContextNudges` and the `ActivityProfile` cache all take that key **explicitly** — no ambient "current profile", so polling several means passing keys. Owns the one-time **move** of the pre-profile flat files into the default profile's dir — a copy would double the hourly store. `context-cache.json` / `context-usage.json` stay shared: keyed by path + size/mtime, they cannot confuse profiles. Also `Observing`: **a store that writes consults it**, so `--second-tray` persists nothing (T239). |
| `src/Profiles/ProfileActivity.cs` | Which profile is being worked in *now*: the newest `projects\**\*.jsonl` **write timestamp** per config dir — directory entries only, never a file opened — probed on the usage poll's cadence, so auto-follow (T126) costs no resident watcher (the reason T101 was dropped) and ~20ms per config dir. `Pick` applies the policy: on-subscription + credentials on disk, a turn inside `FollowWindowSeconds` (30min), nothing stamped more than `MaxFutureSkewSeconds` ahead, and never below the floor a manual choice stamps. Also the single `<config dir>\projects` resolver `ProfileRef` reuses. |
| `src/Profiles/EnvironmentProfile.cs` | The one thing the app writes outside its own settings file: the user-scope `CLAUDE_CONFIG_DIR`, so a profile picked by hand applies to every Claude Code session (T145). What the tray sets, the tray removes — the previous value is remembered and put back. |
| `src/Profiles/Settings.cs` | `Settings` model (JSON in `%LocalAppData%\ClaudeTray`, path exposed as `Settings.DataDir`); clamps out-of-range values. `MonitoredConfigDir` picks which profile the **icon** follows (`ClaudeAccount.PickMonitored` is the one implementation the tray and `--profiles` share), and `FollowActiveProfile` lets `ProfileActivity` move it. `Clone()` is a JSON round-trip through the same serializer that writes the file, so the settings edit buffer and `ApplySettings`' write-back are **total by construction** — a new field needs no copy line anywhere, which is the point (T141). The other end of that round trip is `[TrayOwned]` + `CarryTrayOwnedFrom`: **a setting the page does not edit must carry the `[TrayOwned]` attribute**, or the window's older snapshot of it is written back stale on every Save — the defect T126 and T155 each shipped, and the one thing here no assertion can infer for you (T162). Also `ClaudeProfile` — a registered profile is `{Label, ConfigDir, WorkDir}` and **nothing else**: no address, no token, no plan, so the tray is never a second store of credentials. |

**`src/Core/` — helpers with no subsystem of their own**

| File | Responsibility |
|---|---|
| `src/Core/Localization.cs` | The dependency-free `L` / `{local:Loc key}` layer over the embedded `lang\<code>.json` files: language picked from Settings, else the OS UI language, English as the fallback for a missing key. |
| `src/Core/OutFile.cs` | Creating a file a flag was told to write, directory and all (T187) — the rule every capture shares, stated once instead of at each call site. |
| `src/Core/SampleRoot.cs` | Where a fixture is built: a directory whose path holds no user name (`%PUBLIC%`, temp as fallback), because fixture screenshots get published and an absolute path spells out the Windows account. Shared by `ContextFixture` and `AccountFixture`. |
| `src/Core/ProjectSlug.cs` | The app's **only** reader and writer of the `projects/<slug>` encoding (T105): `Encode` (also what the fixture names its dirs with), `RootFor`/`NameFor`/`ShortNameFor` — exact, by walking a recorded `cwd` up to the ancestor that encodes to the slug — `TryProbe`, the filesystem guess for when no cwd exists, and `Literal`/`Tail` for reporting an unresolvable one. Also the **only** place that decides how a directory is *named on screen* (T154): `ShortName` = its last two segments (`turing/2026.3`), which both the Statistics legend and the Context project list go through, since the leaf alone labels three checkouts of a release folder identically. |
| `src/Core/SafeWalk.cs` | The recursive `~/.claude` walk every scan goes through: per directory, so an unreadable one (untrusted junction, denied ACL, folder deleted mid-sweep) skips its subtree instead of aborting the sweep. Materializes each directory's entries — a `try` around a lazy `Enumerate*` catches nothing — and resolves a reparse point to its target before opening it. |

**`src/Ui/` — the window and its pages.** There are **two** windows (T158): `MainWindow`, the shell
the tray opens, and `ToastWindow`. Everything else is a **page** (`UserControl`) shown inside the
shell's three destinations — `StatisticsPage`, `ContextPage`, `SettingsPage` — so a page owns no
title, size or theme of its own, and "close" on one means `Window.GetWindow(this)?.Close()`.
`PageWindow` is the code-only host that shows a single page for the previews and captures, which are
about the page rather than the shell; `--main` opens the real shell. **A page is built before it is
shown**, so its constructor cannot resolve theme brushes — the dictionary hangs off the window and
`FindResource` throws. Use `TryFindResource` there and re-apply on `Loaded`, the way
`SettingsPage.SelectPage` does. A page with several
independent surfaces is **one class in several files** (T133–T134): `SettingsPage.{General,
Notifications,ClaudeCode,System}.cs` is one file per settings page, `StatisticsPage.{Throughput,
Chart,Profiles,Format,Note}.cs` and `ContextPage.Gauge.cs` the same per surface, and the Context page's
view models are their own types beside it (`ProjectRow.cs`, `SourceRows.cs`, `RowStyle.cs`,
`ContextText.cs`) — a new surface gets a new `partial` file, not another 300 lines in the code-behind.
A `Page`'s generated
`InitializeComponent` builds its `Uri` from the file's **path**, so markup can be moved freely only
while nothing hardcodes a `pack://application:,,,/…` URI — the repo hardcodes none, and adding one
would tie a window to its folder.

| File | Responsibility |
|---|---|
| `src/Ui/MainWindow.xaml(.cs)` | The shell (T158): a nav strip over three destinations, each built on its first visit and then kept collapsed, so a scan / a chart's history / a half-edited settings page survives a switch. Owns the chrome; `Statistics` is the one page the tray reaches into (a fresh reading per poll). |
| `src/Ui/SettingsPage.xaml(.cs)` | The WPF Fluent settings page, with its own six-page sidebar. **All layout lives in the XAML.** Save applies through the callback and confirms in place; Cancel raises `Cancelled` and the shell rebuilds the page from the live model — discarding by construction rather than control by control. |
| `src/Ui/ContextPage.xaml(.cs)` | The Context Load page: master/detail over `ContextScanner` — projects left; right, the session-zero gauge (base overhead / instructions / memory / skills, with the transcript-measured tick) over the per-source eager/lazy breakdown. Scans on a background thread; view models are `public` because WPF binding resolves paths by reflection over public types only. |

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
6. **An `x:Name` is unique across the app, not per XAML file.** WPF scopes names per file, so two pages
   could each name a control `ProfileCombo` and both compile — but the name is the control's identity to
   everything outside the compiler (UI Automation, screen readers, `Check-Interaction.ps1`), and an id
   lookup returns whichever the tree reaches first, which depends on the pages a run happened to build:
   a check can drive the other control and go on passing (T192). Name it for its page
   (`StatsProfileCombo` / `CcProfileCombo`); `--selftest` asserts it, so a collision is a red build.
7. **A screenshot cannot see a keyboard bug, and `--settings` cannot either.** The preview flags run a
   **WPF** `Application.Run`; the tray runs a **WinForms** pump, and the two are different *input*
   environments — that difference is how "no keyboard input in any window" (T135) survived every
   preview, capture and screenshot this repo has ever taken. Anything that involves typing, Tab, Esc or
   a shortcut must be verified under **`--settings-tray`**, which hosts the window the way the tray
   does — and the way to do that is **`scripts\Check-Interaction.ps1`**, not a scratch script. See the
   interaction loop below.

## Visual verification workflow (the predictability loop)

Use the **`preview-ui`** skill, which carries the commands: `dotnet build -c Debug`, then
`scripts\Capture-Window.ps1` (→ git-ignored `docs\_preview\settings.png`), then **Read that PNG and judge
it**. The script is per-monitor-DPI-aware (required at 150–200%) and, being a screen copy, it **names whose
window it copied and writes nothing when that window is not ours** (T199).

## Interaction verification (the loop a capture cannot close)

A picture proves layout. It cannot prove a key press arrives (convention 7, T135).
`scripts\Check-Interaction.ps1` drives the real UI through UIA and asserts a pass/fail.
**Three exit codes (T193): `0` all ran and passed, `2` DEGRADED —
what ran passed but something could not be evaluated, `1` a failure**, and the summary names what did not.

```
powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 `
  [-Case Keyboard|Menu|Profiles|Panes|Names] [-Lang pt-BR] [-UseRunning]   # no -Case runs every one
```

All five are below; listing *three* is how two stayed script-only (T201). The header is the full text.

- **Keyboard** launches `--settings-tray` (the WinForms pump), clicks a sidebar item, types into a `TextBox`
  and reads it back through `ValuePattern`, Tabs out, drives a `Slider` with an arrow key. **`check.yml`
  runs it on every push** (T194): synthesised input reaches a hosted runner, and it needs no credentials.
- **Panes** and **Names** (`--main`) need no second profile either, so with Keyboard they are what a
  one-profile machine and CI run. Panes asserts the report can be *read* — tab headers, and the pane's
  used %, reset caption and live headline in the accessibility tree — and is the only check that would
  notice `PART_SelectedContentHost` going missing again (T176). Names asserts what controls *announce*,
  rows labelled by a neighbouring element included (T175).
- **Profiles** (`--main`) is the only thing that *drives* the picker: **0 → 1 → 0** through the real
  `ComboBox`, reading the report at each stop — the same profile must read the same coming back, the middle
  must differ, the headline never "unavailable" at a settled stop (T165). Below two: **DEGRADED**, no skip.
- **Menu** launches the tray, opens the notification icon's menu, reads its entries, then expands *Open
  Claude Code* for the per-profile ones. One rule, three refusals — **the check must look at what you
  named**: with a tray already resident it launches `-Exe --second-tray` beside it rather than refusing
  (T237); under `-UseRunning` labels match the language that tray resolved, not a `-Lang` it never got;
  and a resident binary that is not `-Exe` leaves the run DEGRADED (T220, T236).
- **An assertion that could have run and didn't is `Unchecked`, never an `Info` line.** T166's timing hung
  off `Combo-Select`'s UIA route alone, so the day `Select()` began throwing, the run would print a note and
  stay green (T193). Both halves are needed: a fallback reaching its target in **one selection change**
  (`Home`/`End` anchor), so the observation holds on either route, and `Unchecked` counting what did not
  run. An absent *precondition* — what can never run here — stays `Info`.
- **Reading nothing is a FAIL, never a pass.** The script's header lists the four UIA traps behind that —
  read it before writing any new check by hand.
- **A custom `TabControl` template must name its content host `PART_SelectedContentHost`.** WPF finds
  the selected tab's content by that exact name, and the `TabItem` peer asks for it to attach the pane's
  children — so an unnamed `ContentPresenter` leaves the **whole body** of every tab out of the UI
  Automation tree: unreadable to a screen reader and to every check, perfect in every screenshot, which
  is how it survived T111 → T165. Same trap for any templated control WPF looks up by name.

## Arithmetic verification (`--selftest`)

`src/Cli/SelfTestCli.cs` **is** this repo's test suite — there is no test project, because a
third-party test framework would break the single-self-contained-`.exe` rule (§I.3). A new invariant
over the pacing, the stores, the grid, the tail, the live rate or the slug encoding is asserted there,
on synthetic inputs, and nowhere else.

```
ClaudeTray.exe --selftest [--quick]      # exit 1 on failure — run it before committing
                                         # --quick skips the sections that wait on real sweeps
                                         # a WinExe writes to no console of its own: from PowerShell,
                                         # Start-Process … -Wait -RedirectStandardOutput st.log
```

- It runs on **every push and PR** (`.github/workflows/check.yml`) and again against the packaged
  single-file `.exe` before an installer is built (`build.yml`). A red commit, not a blocked release.
- Everything it touches is synthetic and removed: a temp transcript tree and a `selftest` profile dir.
  A real profile's stores are never read or written.
- **A check is not finished until it has been seen to fail.** Break the property deliberately, watch the
  assertion go red, then revert — T153's new assertions were each confirmed that way against three
  broken builds, and doing it is what showed *which* assertion catches what: the primed-cursor counts
  catch priming being removed but are blind to the alignment flag being left set (the fragment is
  rejected by the JSON parse either way), which is the whole reason the append-after-a-primed-read pair
  exists. An assertion that has only ever passed is a comment.
- **A `Skip` must not hide the property it guards, and one that always fires is no check at all.** Gate on
  the *precondition*, never a weaker form of the claim — T161 gated on backtracking itself, so the build
  with backtracking removed went green. Then fix the environment: that guard still skipped both probe
  checks on every CI run, whose `%TEMP%` is an 8.3 alias the encoding cannot rebuild, so `Temp` resolves
  the root (`LongPath`) and the summary **names each skip beside the counts** (T169); an unexpected one is red (T218).

## Build / run / dev helpers

**The complete flag catalogue is the `dev-flags` skill** — every preview, capture, headless read-out
and fixture, with what each is for — reference material, consulted rather than read, which a per-turn
budget should not pay for (T191); a flag you add goes there. What stays here is the
handful that carry a *rule*, because getting these wrong produces work that has to be redone.

```
dotnet build -c Debug                 # fast compile check
dotnet run -c Release                 # build + run the tray app
dotnet publish -c Release             # single self-contained .exe -> bin\Release\net10.0-windows\win-x64\publish\

dotnet run -- --main [dest]           # the WHOLE window as the tray opens it (nav strip + destination:
                                      #   Statistics | Context | Settings), under the WinForms pump the
                                      #   tray uses. --settings / --stats / --context --window show one page
                                      #   without the shell; --settings-tray is the only preview that can
                                      #   see a keyboard bug — UI convention 7.
dotnet run -- --capture-settings <out.png> [page] [scroll=<dip>]
dotnet run -- --capture-stats [outBase] [variant] [--sample]
                                      # rendered OFF-SCREEN to PNG. Prefer these over
                                      #   scripts\Capture-Window.ps1, which copies pixels ON SCREEN inside
                                      #   the window rect — anything stealing focus or sitting on top ends
                                      #   up in the file. A popup is the exception: its own window, which
                                      #   an off-screen capture cannot see.
dotnet run -- --lang fr --settings    # any command in another language. Published shots are English; use
                                      #   this to check a layout in the longest translation.
```

## Release process

Version lives in **one place**: `<Version>` in `ClaudeTray.csproj`. Everything derives from it.

Everything that exists to *ship* the app lives in `build\` (T131) — the installer script, the four
build/release scripts and the winget manifests. Each resolves the repo root itself, so it can be run
from anywhere; `scripts\` is a different thing (the screenshot/capture dev tools) and stays put.

```
# bump <Version>, then:
build\build-installer.cmd             # publish + build dist\ClaudeTray-Setup.exe
```

Then create a GitHub release tagged `vX.Y.Z` and attach `ClaudeTray-Setup.exe`. Installed copies
self-update from it.

## Roadmap & docs maintenance

Four root files, each with one job, **written by [roadkeep](https://github.com/alegauss/roadkeep)
and not by hand** — the fields are refused at insertion, the id, the `(deps: … ✅)` annotation and
the `→ §` pointer are derived on render, and an `Edit` is denied by a hook naming the command instead.

| File | Job |
|---|---|
| [`ROADMAP.md`](ROADMAP.md) | Active backlog — the only source of truth for task status. One line per task. |
| [`CHANGELOG.md`](CHANGELOG.md) | What has shipped. One line per task; `git log` is authoritative for detail. |
| [`IMPROVEMENTS.md`](IMPROVEMENTS.md) | Design rationale for *unshipped* work, plus §I the binding house constraints. |
| [`STRATEGY.md`](STRATEGY.md) | Positioning, licence, distribution, the trust promise. Never a task. |

They live at the repo root on purpose: `docs/` is the published GitHub Pages site. This project's
numbers — prefix `T`, the limits, the markers, the ledger's two absences — are
[`roadkeep.toml`](roadkeep.toml), the **only** roadkeep file this repository carries: the tool
arrives as a Claude Code plugin, so there is no copy of it here and no path to a checkout. `/plugin
marketplace add alegauss/roadkeep` once per machine, `/plugin install roadkeep@alegauss` **per
repo** — installed for another, it leaves this one no hook, no tools, no skill. CI needs none of it:
[`roadkeep.yml`](.github/workflows/roadkeep.yml) runs the published action, and `lint` must pass.

Two skills that do not overlap: **`roadkeep`** says which command to call and what each derives (it
ships with the tool, so nothing here repeats it); **`roadmap-docs`** holds this project's own shipping
discipline — one task one commit, the user-facing-surface gate, releases, and the **block theme table**:
a block is a capability of this app and is **reused**, so a new letter is the rare case, not the habit.
Query instead of reading: `pick` chooses the next task, `brief <id>` is what it costs to start one.

## Conventions

- **Commits**: use `run-commit.cmd -m "<conventional-commits title>"` (stages all, AI writes body).
  **`run-commit.cmd` is a global command on the Windows PATH, not a file in this repo** — `ls` and
  `find` inside the tree will not find it and that is not a reason to fall back to raw `git commit`.
  Call it by name from the repo root; `where run-commit.cmd` resolves it if you need to be sure.
  **One commit per finished, validated task — never batch two tasks into one commit**, and check
  `git status` first, since it stages everything. A batch of ≥2 tasks runs under `/loop`, one task
  per iteration. See the `roadmap-docs` skill.
- **Privacy**: only token counts, model ids, flags, tool/skill names and the session `cwd` are ever
  read from transcripts — never message content. Keep it that way.
- **A transcript's `cwd` is the working directory of *that turn*, not the project root.** A single
  `cd` inside a session changes it, so naming a project `GetFileName(cwd)` renames it to whatever
  subfolder a command last ran in. The `projects/<slug>` name encodes the root but is lossy (every
  non-alphanumeric becomes `-`, so `…-shio-2026-3` cannot be split back into `2026.3`). Resolve by
  walking the cwd up to the ancestor whose encoding equals the slug — and do it through
  **`ProjectSlug`**, which owns that encoding for the whole app (T105). Do not write a second reader:
  the last time there were three, one of them encoded a different set of characters.
- **A transcript "turn" is a `requestId`, not a line.** Claude Code writes **one `assistant` line per
  content block** of a single API response (thinking, then tool_use, …), and every one of those lines
  repeats that response's `message.usage` verbatim. Summing per line double-counts, weighted toward
  heavy tool use — measured on a real week here: **41% of the lines are repeats, 1.63× the tokens**.
  De-duplicate on `requestId` (or `message.id`), with the set **global rather than per file** so a
  forked session's inherited copies drop too — `TranscriptTail`, `UsageReport.ScanTokens` and
  `UsageInsights` all do (T102). The exception is `ActivityProfile`: its grid records *presence* per
  hour, so a repeated line sets a bit that is already set, and it deliberately never parses the JSON.
- **A directory's mtime does not move when a file inside it is appended** (NTFS updates it only when
  an entry is added, renamed or removed — measured, not assumed). So "stat the project directories
  and skip the ones that didn't change" **cannot** find a live session: an ongoing session appends to
  a transcript that already exists. T103 uses the watcher's own paths instead, with a periodic whole
  walk to reconcile.
- **Single instance** is enforced by a named mutex; a second launch exits silently.
- The marketing page is `docs/index.html` (GitHub Pages, served from `/docs`).
- **New user-visible strings go into all five `lang/*.json`**, not just `en`. `--selftest` now fails on a
  key that reached one file, or a `{0}` that did not survive translation (T185), so what is left to you is
  the part it cannot hold: `--lang <code>` (process-only override, saved preference untouched) and a
  screenshot in at least one non-English language before calling a UI change done.
