# AGENTS.md — Claude Code Tray

Foundation doc for working in this repo with maximum predictability and minimum friction.
Read this before touching UI or build. Keep it current when conventions change.

## What this is

A native **Windows tray** app (.NET 10, `win-x64`) that shows your Claude Code rate-limit usage as a
crisp, DPI-aware icon, with burn-rate projection and a local 24h usage breakdown. Ships as a single
self-contained `.exe` (no .NET needed to run) via an Inno Setup installer with in-app auto-update.

Unofficial / community project — not affiliated with Anthropic. Reads only usage data Claude Code
already stores locally; never message content.

## What earns a byte of this file

This file is loaded on **every** turn and is held to a byte budget by `roadkeep lint`, so it is
zero-sum: a new rule is paid for by an old one, and "whatever the editor has not needed lately" is a
worse selector than what follows. **A rule earns its bytes if getting it wrong has produced a defect
*and* it cannot be asserted in `--selftest` instead** (T219). Three consequences, in the order to try
them: a property a check can hold **becomes a check** — T167's number convention and T192's id
uniqueness both did, and the paragraph goes; **reference material goes to a skill**, consulted rather
than read, which is where the flag catalogue (`dev-flags`, T191) and the per-file map (`file-map`) are;
and the **discovery story goes to `IMPROVEMENTS.md`**, leaving the rule and the `T<n>` that carries it.

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

The sources live under `src/`, one folder per subsystem (T129–T132) — **the folder is the map**, and the
per-file detail is the **`file-map` skill**: reference material, consulted rather than read (T219). What
stays here is placement, which no `ls` of the tree answers.

**Where does a new file go?** In the folder of the subsystem it belongs to; **the repo root is for the
project, not for code** (csproj, manifest, icon, `lang/`, `site/`, the roadmap docs). Three things are
deliberate and must not be "fixed": the namespace stays **flat `ClaudeTray`** — folders are for the
person running `ls`, not for the compiler, so no folder-derived namespaces and no new `using`s; **`lang/`
never moves**, because the embedded resource name (`ClaudeTray.lang.en.json`) is derived from its path
and `Localization` matches that string at startup; and the csproj globs `**/*.cs`, so a new folder needs
no csproj edit — if a move seems to need one, the move is wrong.

| Folder | What belongs in it |
|---|---|
| `src/Tray/` | The resident app: `Main`'s arg dispatch, the tray icon and its menu, the timers, the icon pixels, self-update. |
| `src/Cli/` | The headless printers — one file per flag family, `Main` dispatches and they run and exit. `SelfTestCli` is this repo's test suite. |
| `src/Usage/` | Quota, spend and live throughput: the API and its headers, the pacing report, the per-poll stores, the projection, the transcript tail and its charts. |
| `src/Context/` | The Context Load Inspector: the scan, its rules, its report and histories, the cleanup prompt. |
| `src/Profiles/` | Accounts, profiles and settings: the account reading, the per-profile store, the environment write, the `Settings` model. |
| `src/Core/` | Helpers with no subsystem of their own: localization, the slug encoding, the safe walk, output files, sample roots. |
| `src/Ui/` | The windows and their pages — and the rules below, which are not derivable from the folder. |

**`src/Ui/` carries rules, not just files.** There are **two** windows (T158): `MainWindow`, the shell
the tray opens, and `ToastWindow`. Everything else is a **page** (`UserControl`) shown inside the
shell's three destinations — `StatisticsPage`, `ContextPage`, `SettingsPage` — so a page owns no
title, size or theme of its own, and "close" on one means `Window.GetWindow(this)?.Close()`.
`PageWindow` is the code-only host that shows a single page for the previews and captures, which are
about the page rather than the shell; `--main` opens the real shell. **A page is built before it is
shown**, so its constructor cannot resolve theme brushes — the dictionary hangs off the window and
`FindResource` throws. Use `TryFindResource` there and re-apply on `Loaded`, the way
`SettingsPage.SelectPage` does. A page with several independent surfaces is **one class in several
files** (T133–T134): a new surface gets a new `partial` file, not another 300 lines in the code-behind.
A `Page`'s generated `InitializeComponent` builds its `Uri` from the file's **path**, so markup can be
moved freely only while nothing hardcodes a `pack://application:,,,/…` URI — the repo hardcodes none,
and adding one would tie a window to its folder.

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
what ran passed but something could not be evaluated, `1` a failure**; the summary names what did not.

```
powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 `
  [-Case Keyboard|Menu|Profiles|Link|Panes|Sessions|Names|Switch] [-Lang pt-BR] [-UseRunning]  # none runs all
```

All eight are below; listing *three* is how two stayed script-only (T201). The header is the full text.

- **Keyboard** launches `--settings-tray` (the WinForms pump), clicks a sidebar item, types into a `TextBox`
  and reads it back through `ValuePattern`, Tabs out, drives a `Slider` with an arrow key. **`check.yml`
  runs it on every push** (T194): synthesised input reaches a hosted runner and it needs no credentials.
- **Panes** and **Names** (`--main`) need no second profile, so with Keyboard they are what a one-profile
  machine and CI run. Panes asserts the report can be *read* — tab headers, the pane's used %, reset caption
  and live headline in the tree — the only check that would notice `PART_SelectedContentHost` going missing
  again (T176); Names, what controls *announce*, rows labelled by a neighbouring element included (T175).
- **Sessions** (`--main`) drives the pane a capture cannot finish checking (T359): a row click has to
  unfold its call tree *into the tree* (T329), and the ⓘ note behind the list-price figure lives in a
  `Popup` — its own window, which `--capture-stats` cannot photograph at all. Waits for the scan; no
  rows is **DEGRADED**, never a pass.
- **Profiles** (`--main`) is the only thing that *drives* the page's picker: **0 → 1 → 0** through the real
  `ComboBox`, reading the report at each stop — the same profile reads the same coming back, the middle
  differs, the headline never "unavailable" at a settled stop (T165). Below two: **DEGRADED**, no skip.
- **Menu** launches the tray, opens the notification icon's menu, reads its entries, then expands *Open
  Claude Code* for the per-profile ones. One rule, three refusals — **the check must look at what you
  named**: a resident tray means `-Exe --second-tray` beside it (T237); `-UseRunning` matches the language
  that tray resolved, not a `-Lang` it never got; a resident binary that is not `-Exe` is DEGRADED (T220, T236).
- **Switch** drives the submenu entry that moves the **icon**'s account — `AdoptMonitored`, not the
  page's picker above; **DEGRADED** below two (T294).
- **Link** swaps the linking card's two side pickers: the plan must *move*, and one profile on both
  sides is refused with the button disabled — a seeded pair photographs right either way (T370).
- **An assertion that could have run and didn't is `Unchecked`, never an `Info` line.** T166's timing hung
  off `Combo-Select`'s UIA route alone, so the day `Select()` began throwing, the run would print a note and
  stay green (T193). Both halves are needed: a fallback reaching its target in **one selection change**
  (`Home`/`End` anchor), so the observation holds either route, and `Unchecked` counting what did not run.
  An absent *precondition* — what can never run here — stays `Info`.
- **Reading nothing is a FAIL, never a pass.** The script's header lists the UIA traps behind it — read
  it before writing a check by hand.
- **A custom `TabControl` template must name its content host `PART_SelectedContentHost`.** WPF finds
  the selected tab's content by that exact name, and the `TabItem` peer asks for it to attach the pane's
  children — so an unnamed `ContentPresenter` leaves the **whole body** of every tab out of the UI
  Automation tree: unreadable to a screen reader and to every check, perfect in every screenshot, which
  is how it survived T111 → T165. Same trap for any templated control WPF looks up by name.

## Verification in code (`--selftest`)

`src/Cli/SelfTestCli.cs` **is** this repo's test suite — there is no test project, because a
third-party test framework would break the single-self-contained-`.exe` rule (§I.3). It holds **two
kinds of claim**, and a new one of either goes there rather than into prose: an **invariant** over
synthetic inputs (pacing, stores, grid, tail, live rate, slug, numbers, languages), and **one claim
in two places** — a document against the thing it documents, which is what holds up the ledger's
index, the file map, the flag catalogue and the active marker (T223, T242–T244). The second kind
reads repository files, so an installed copy has none and skips it by name.

```
ClaudeTray.exe --selftest [--quick]      # exit 1 on failure — run it before committing
                                         # --quick skips the sections that wait on real sweeps
                                         # a WinExe writes to no console of its own: from PowerShell,
                                         # Start-Process … -Wait -RedirectStandardOutput st.log
```

- It runs on **every push and PR** (`.github/workflows/check.yml`) and again against the packaged
  single-file `.exe` before an installer is built (`build.yml`). A red commit, not a blocked release.
- **Build `-c Debug` to work on a check; `-c Release` only to ship.** Debug lays down 5 files / 2 MB,
  Release 252 / 156 MB — CI runs that output, so it stays self-contained (T341). **A no-op is 1–3s in
  either.** A rebuild after touching one `.cs` is **not a stable quantity here**: 39s to 413s for the
  identical edit inside one hour, and a v1.5.3 worktree with half the code took 409–742s — so it is the
  machine, not the repo (Defender real-time on, power plan Balanced). Never quote a single figure for it.
- **A change to a *document* needs no rebuild at all** — the repo-reading checks open their files at run
  time, so `--no-build` re-answers them in ~8s. T354's red marker was found and fixed that way (T345).
- Everything it **writes** is synthetic and removed: a temp transcript tree and a `selftest` profile
  dir. A real profile's stores are never read or written, and the repository files above are only read.
- **A check is not finished until it has been seen to fail.** Break the property deliberately, watch the
  assertion go red, then revert. Doing it is what shows *which* assertion catches what — T153's three
  broken builds are why the append-after-a-primed-read pair exists at all. An assertion that has only
  ever passed is a comment.
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
dotnet build -c Debug                 # fast compile check. Two failures here are not your code, both
                                      #   measured: MSB1011 = an interrupted build left a *_wpftmp.csproj
                                      #   (delete it; a completed build sweeps stale ones, T269), and a
                                      #   missing generated file (BG1002/CS2001/CS0103 on an x:Name) is
                                      #   the markup pass racing the IDE's design-time builds over one
                                      #   obj\ (T270). Build again — but never in a retry loop, which
                                      #   cannot tell a race from a broken tree.
                                      #   Debug is framework-dependent: needs WindowsDesktop.App 10.x (T341).
dotnet run -c Release                 # build + run the tray app
dotnet publish -c Release             # single self-contained .exe -> bin\Release\net10.0-windows\win-x64\publish\

dotnet run -- --main [dest]           # the WHOLE window as the tray opens it (nav strip + destination:
                                      #   Statistics | Context | Settings), under the WinForms pump the
                                      #   tray uses. --settings / --stats / --context --window show one page
                                      #   without the shell; --settings-tray is the only preview that can
                                      #   see a keyboard bug — UI convention 7.
dotnet run -- --capture-settings <out.png> [page] [card=<x:Name>]   # card= frames that element and reports
dotnet run -- --capture-stats [outBase] [variant] [--sample]        #   whether the viewport held it (T375)
                                      # Both OFF-SCREEN to PNG. Prefer them over scripts\Capture-Window.ps1,
                                      #   which copies pixels ON SCREEN in the window rect — anything stealing
                                      #   focus or sitting on top lands in the file. A popup is the exception:
                                      #   its own window, which an off-screen capture cannot see.
dotnet run -- --lang fr --settings    # any command in another language. Published shots are English; use
                                      #   this to check a layout in the longest translation.
```

## Release process

Version lives in **one place**: `<Version>` in `ClaudeTray.csproj`. Everything derives from it.

Everything that exists to *ship* the app lives in `build\` (T131) — the installer script, the
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

They live at the repo root, where a reader looks for them and nothing serves them. This project's
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
- The marketing page is the [`site/`](site/README.md) workspace, prerendered; `docs/` no longer exists.
- **New user-visible strings go into all five `lang/*.json`**, not just `en`. `--selftest` now fails on a
  key that reached one file, or a `{0}` that did not survive translation (T185), so what is left to you is
  the part it cannot hold: `--lang <code>` (process-only override, saved preference untouched) and a
  screenshot in at least one non-English language before calling a UI change done.
