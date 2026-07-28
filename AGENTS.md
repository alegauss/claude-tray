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

## File map

| File | Responsibility |
|---|---|
| `Program.cs` | Entry point, CLI flags, `TrayContext` (tray icon, menu, poll/flash timers), `OpenSettings`. |
| `ApiClient.cs` | Reads OAuth token from `~/.claude/.credentials.json`, calls the API, parses `anthropic-ratelimit-unified-*` headers. |
| `BurnTracker.cs` | Utilization history → least-squares slope → projects exhaustion (`Projection.Ok/Danger/Unknown`). |
| `UsageInsights.cs` | Aggregates last 24h of `~/.claude/projects/**/*.jsonl` into a cost-weighted breakdown. Owns the per-model `Price` table the whole app shares. |
| `ContextScanner.cs` | Scans every file Claude Code loads before the first prompt (instruction chain + `@imports`, memory index/files, skill & agent frontmatter), splits **eager** (paid every request) from **lazy**, measures observed session-zero from transcripts, and caches the scan by a path+size+mtime fingerprint. |
| `ContextFixture.cs` | Builds a throwaway `~/.claude` lookalike (`--sample`) where all 16 rules fire. Use it for any published screenshot — the real machine's project names are client names. |
| `ContextReport.cs` | Renders a whole scan as one markdown document (`--context-report`): summary, project table, findings, evidence, and the method behind the numbers. Paths and counts only. |
| `ContextNudges.cs` | Rate limiter for the opt-in context-growth toast: at most one per project per week, remembered in `context-nudges.json`. |
| `ContextHistory.cs` | Append-only log of each project's eager context (`context-history.jsonl`), one line per project per day and only when it moved. Feeds the drift sparkline and the "+N this week" line. |
| `ContextPrompt.cs` | Builds the cleanup prompt handed to Claude Code: findings + fixes + paths, never file contents, and it asks Claude to show its plan before deleting. The app has **no** write path into `~/.claude` — see IMPROVEMENTS §I.4. |
| `ContextUsage.cs` | Mines the transcripts for `Skill`/agent invocations (names and counts only) so the window can say "used 45×" or "never". Per-file cache; runs outside `Scan` because it reads hundreds of MB. Memory recalls are deliberately not counted — see the type doc. |
| `ContextRules.cs` | The advisor over a `ContextScan`: grounded rules → `Finding` (severity + one sentence + the concrete fix). No new IO and never file contents; narrowed to what is objectively measurable so it doesn't cry wolf. |
| `TokenEstimate.cs` | Chars→tokens estimation for markdown, classified per line (prose / code fence / table). Always rendered as an estimate ("≈4.9k"). |
| `ActivityProfile.cs` | The weekly activity shape: 168 buckets (day-of-week × local hour) of `p(active)`, mined from transcript **timestamps only**, decayed per week and shrunk toward a flat prior. Cached daily in `%LocalAppData%\ClaudeTray`. The projection follows this instead of a uniform slope. |
| `HourlyUsage.cs` | Permanent per-hour aggregate (spend + coverage per local day) folded out of `usage-history.jsonl` before its 8-day pruning discards it. Lets idle be *measured* instead of inferred, and is the store week-over-week comparison reads. |
| `ActivityShape.cs` | The weekly projection spent along that shape: calibrated per *measured active hour*, flat through usually-idle stretches, sloped through working ones. Returns null (→ the old straight line) when the profile is thin or the window can't be calibrated. Weekly only. |
| `IconRenderer.cs` | GDI+ icon (vector number + outline + fill bar + projection color) at the real size; also the app `.ico` and social image. |
| `Updater.cs` | Checks GitHub Releases; downloads/runs the installer for in-app self-update. `CurrentVersion`. |
| `Settings.cs` | `Settings` model (JSON in `%LocalAppData%\ClaudeTray`); clamps out-of-range values. |
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

## Visual verification workflow (the predictability loop)

Use the **`preview-ui`** skill, or directly:

```
dotnet build -c Debug
powershell -ExecutionPolicy Bypass -File scripts\Capture-Window.ps1   # -> docs\_preview\settings.png
```

Then Read `docs\_preview\settings.png` and judge it. `--settings` opens the window standalone so no
tray-menu clicking is needed; the capture script is per-monitor-DPI-aware (required at 150–200%).
`docs\_preview\` is git-ignored.

## Build / run / dev helpers

```
dotnet build -c Debug                 # fast compile check
dotnet run -c Release                 # build + run the tray app
dotnet publish -c Release             # single self-contained .exe -> bin\Release\net10.0-windows\win-x64\publish\

dotnet run -- --settings              # open just the Settings window (preview)
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
dotnet run -- --capture-stats docs\_preview\shape shape   # ...both tabs to PNG, off-screen

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
- **Single instance** is enforced by a named mutex; a second launch exits silently.
- The marketing page is `docs/index.html` (GitHub Pages, served from `/docs`).
- **New user-visible strings go into all five `lang/*.json`**, not just `en`.
  Verify with `--lang <code>` (process-only override, saved preference untouched) and screenshot at
  least one non-English language before calling a UI change done.
