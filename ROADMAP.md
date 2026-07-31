# Claude Code Tray — Roadmap (active backlog)

> **Single source of truth for task status.** Flat, one line per task.
> Only **unshipped** work lives here (📋 designed · 💭 idea · ⏳ partial · 🛠 in-progress).
> Shipped work is moved to [CHANGELOG.md](CHANGELOG.md); design rationale (the *what/why* per task)
> lives in [IMPROVEMENTS.md](IMPROVEMENTS.md); positioning/distribution decisions live in
> [STRATEGY.md](STRATEGY.md).
>
> **How to pick work:** lowest-numbered task in a block whose `deps` are all shipped.
> The `→` pointer is the section in IMPROVEMENTS.md with the full design.
>
> **Next free task number lives in [last-task.md](last-task.md)** — read it before adding a task.
> Maintenance rules: the `roadmap-docs` skill.

| Symbol | Meaning |
|---|---|
| 📋 | Designed but not started |
| 💭 | Idea worth exploring; needs design |
| ⏳ | Partial — direction is right, more work remains |
| 🛠 | In progress |

## Block I — Context Load Inspector ("what every session costs you before you type")

> **Shipped: T66–T84.** The scanner, the window (session-zero gauge, source table, cross-project
> overview, what-if simulator), the rule engine, the evidence pass, the A–F grade with drift, the
> opt-in nudge, live refresh, the fixture, the markdown report and the docs. See
> [CHANGELOG.md](CHANGELOG.md) Block I for what each task did; the measured baseline the whole block
> was calibrated against is [IMPROVEMENTS.md](IMPROVEMENTS.md) §III.
>
> In the tray menu it is `Context…`; the window is "Context Load — memories, skills & instructions".
> Headless: `--context`, `--context --check`, `--context --usage`, `--context --prompt`,
> `--context-report <file.md>`, `--context --sample`.

- 💭 **T85** (deps: —) **Usage evidence for memory files — only if a structured signal appears** — T75 covers skills and agents, where an invocation is a real tool call in the transcript. A memory recall has no such record: the harness injects it into the conversation, so the only trace is message content, which the app never reads (§I.1). Annotating memories would therefore mean guessing, and a wrong "never used" is the one error an advisor must not make. Revisit only if Claude Code starts recording recalls as structured metadata.

## Block J — Activity-aware pacing ("the week doesn't burn at 4am")

> The weekly projection extrapolates the **average pace since the window opened**
> ([`UsageReport.Fill`](UsageReport.cs)) — a straight line that spends quota uniformly, including
> through the nights and weekends still ahead. It therefore lands the "you run out here" marker at
> times nobody is working (03:59 on a Friday), and it misreads any window whose *remaining* active/idle
> mix differs from its elapsed one — a partial day, an approaching weekend, a window that opened at
> 02:00. This block models **when** the user is actually active and projects along that shape instead.
> Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §IV.
>
> **Shipped: T86–T90.** The profile (`ActivityProfile.cs`, `--activity`), the staircase projection on
> the weekly chart, the permanent hourly aggregate behind it (`HourlyUsage.cs`), last week's ghost
> curve, and the "stop now, resume at …" advice. See [CHANGELOG.md](CHANGELOG.md) Block J.
>
> Headless: `--activity`, `--activity --measured`, `--activity --fold`; previews `--stats shape`,
> `--stats shape ghost`.
>
> **Second pass (T91–T96).** Building the block exposed four accuracy gaps and two mechanical ones.
> The accuracy work is ordered by how much it changes the projection: T93 first (it removes the
> limitation the UI currently has to disclaim), then T95, then T94.

- 📋 **T91** (deps: —) **The tray keeps the profile warm** — the grid is only ever recomputed when someone opens Statistics, so a machine whose owner never opens it projects from an ageing shape, and the first open on a fresh install waits on a ~15s sweep. Sample it on launch and every 6h from the tray's background timer instead, exactly as T79 already samples context. → §IV.7
- 📋 **T92** (deps: —) **Incremental transcript sweep** — the daily rebuild re-reads every transcript from scratch (15s / 93k requests measured). Cache per file by path+size+mtime → its hourly counts, the way `ContextUsage` already does, so the recompute costs only the files that changed. → §IV.8
- 📋 **T93** (deps: T88) **Prefer the measured grid once there are ~3 weeks of it** — `HourlyUsage` sees *all* usage against the limit, including from another machine or claude.ai; the transcript grid structurally cannot, which is the limitation the method note has to disclaim today. Blend toward measured as its coverage grows, keeping the transcript grid to bootstrap the first weeks. → §IV.9
- 📋 **T94** (deps: T93) **Intensity, not just presence** — every active hour is currently paced at the same rate, so a heavy Monday morning and a light Friday evening spend identically. Add a per-bucket intensity (mean spend per active hour, relative to the average) from the measured store and weight the projection by it. → §IV.10
- 📋 **T95** (deps: —) **Don't let a holiday teach the model** — a week off currently votes "these hours are idle" like any other week; only the flat prior softens it. Drop weeks whose total activity is a small fraction of the median week from the denominator, and say so in `--activity`. → §IV.11
- 💭 **T96** (deps: —) **`--selftest` for the pacing *and* live math** — Block J added arithmetic with real edge cases (a flat profile must reproduce the straight line exactly, folding must stay idempotent, advice must never exceed its target, the ghost must stay hidden under its gates) and the repo has no test surface at all. **Block K doubled the surface**: the tail's cursor (a partial line waits for its newline, a shrunk file resets without re-reporting, a primed offset aligns) and the rate's kernel (sustained R reads as R, one burst decays linearly to a true zero at W, the smoothed value never exceeds the weighted one, per-project rates sum to the headline) are all verified only by hand against synthetic roots today. A deterministic self-check over synthetic inputs, in-app, keeps the zero-dependency rule intact. → §IV.12

## Block K — Live throughput ("what is burning right now")

> The throughput row under both charts ([`WindowPace.TokensPerSecond`](UsageReport.cs)) is a **window
> average**: non-cache-read tokens ÷ seconds since the window opened. On the weekly tab the denominator
> reaches 604,800s, so a 200k-token burst moves the third decimal — the number is immobile by
> construction, not by refresh rate. And it is recomputed only when the tray pushes a new
> `PaceSnapshot` (default 300s, floor 30s, far longer while a window is maxed), which is the right
> cadence for rate-limit headers and useless for a flow.
>
> A flow rate is the one number in this app that *should* move, and the data for it is already there
> and already local: transcripts are append-only, and each `assistant` line carries `message.usage`
> the moment a turn lands. This block reads that tail instead of the API, turns it into a rolling rate,
> and draws it as motion where the moving axis **is** time — then attributes it per project, which is
> the question someone running Claude Code across five repos actually has. Design:
> [IMPROVEMENTS.md](IMPROVEMENTS.md) §V.
>
> **Shipped: T97–T100.** The tail reader (`TranscriptTail.cs`, `--tail`), the rolling rate
> (`LiveRate.cs`, `--live`), the moving strip under both charts (`LiveStrip.cs`, `--stats live`) and
> its per-project attribution — see [CHANGELOG.md](CHANGELOG.md) Block K. **T101 was dropped**, which
> §V.5 pre-authorised: see Non-goals.
>
> **Second pass (T102–T108).** Building the block turned up one latent correctness bug that predates
> it (T102), two costs it introduced (T103, T108), two readings it stops just short of (T104, T107),
> and one duplicated resolver (T105). Ordered by what a user would notice: T102 first — it is wrong
> today, everywhere the transcripts are counted — then T104, then the rest.
>
> **Shipped since, from watching the real thing: T110–T112, T114–T116.** The strip got a labelled
> ceiling scaled to what is on screen (T110), its own **Throughput** tab (T111) and percentile clipping
> (T112); then the row stopped being bars at all — `LiveRate` serves the rolling rate as a series with
> sticky per-project slots (T114), a paused project stays on the chart until it ages out instead of
> blinking (T115), and the tab now carries **two line charts** of that rate, per project and per token
> type, both always drawn (T116). **T106 was dropped** in the process: the rolling rate absorbs the
> end-of-turn attribution it was about without inventing a duration. What is left of T104 is the
> per-sample hover.

- 📋 **T102** (deps: —) **`ScanTokens` counts one API response several times** — Claude Code writes one `assistant` line per *content block*, each repeating that response's `usage`; T97 found this and de-duplicates on `requestId`, but the older sweep still sums per line. The weekly curve hides it (it is rescaled to the live utilization) yet the *shape* is skewed toward heavy tool use, and `MeasuredActiveHours` and `ActivityProfile` read the same samples. The parser already emits the id. → §V.7
- 📋 **T103** (deps: T97) **The tail re-enumerates the whole tree every 3s** — 602 transcripts in 17ms today, metadata-only, but it is O(all history) and grows forever while the window is open. Enumerate only directories whose mtime moved, or fall back to watcher-reported paths once the tree passes a size. → §V.8
- 📋 **T104** (deps: T99, T110, T111, T116) **The charts have no per-sample reading** — T110 gave the row a magnitude, T111 a tab with real height and T116 lines you can follow, which leaves the one interaction a time-series should not ship without: hover a point for its second, its project and the tokens that actually landed in it (the raw buckets are still there, beside the rate the line draws). → §V.9
- 💭 **T105** (deps: T100) **One resolver for slug → project path** — T100 recovers a project's real folder by walking the `cwd` up to the ancestor whose encoding matches the slug; `ContextScanner.CwdFromTranscripts` answers the same question separately and less exactly. Two readers of the same lossy encoding is one too many. → §V.10
- 💭 **T107** (deps: T98) **Say what the cache re-read is costing** — measured on real traffic: ~30,000 tok/s of cache read against ~150 tok/s of real work, a 200× ratio the app now computes and shows nobody. That number is what a large eager context costs *per turn*, which makes it the missing link between this block and the Context Load Inspector. → §V.12
- 💭 **T108** (deps: T99) **Move the `--stats live` fixture out of the code-behind** — the synthetic three minutes behind the published screenshot is hand-shaped inside `StatisticsWindow`, and two tasks now depend on it. It belongs beside `ContextFixture`. → §V.13

## Block N — System information ("which plan am I actually on?")

> The account facts a Claude Code user has to `cat ~/.claude.json | jq` for — the plan behind the
> rate limit, who the login belongs to, where the config lives, which CLI is installed — are all
> already on disk, and the tray already reads that directory for everything else. **T120 shipped**
> the Settings page over them (`ClaudeAccount.cs`, `--settings System`): see
> [CHANGELOG.md](CHANGELOG.md) Block N. What it stops short of is a *publishable* picture of itself.

- 📋 **T121** (deps: T120) **A fixture account so the page can be screenshotted** — every other window has a published shot; this one has none, because the only account on a real machine is the developer's, and masking hides the name and the email but not the organization or its mail domain. `ContextFixture` already builds a throwaway `~/.claude` for exactly this reason; the same idea needs a synthetic `.claude.json`/`.credentials.json` pair (a Max 20x personal account and a Team seat, so both the with-org and no-org layouts render) behind a `--settings System --sample`, and then the README and the site block get their image. → §VI.1

## Block P — Project layout ("`ls` should describe the app")

> Every source file in this project is in the **repo root**: 34 `.cs`, 4 `.xaml`, the installer script,
> five release scripts, and the ~10 markdown/config files, in one flat listing of 58 entries. Nothing
> about that listing says which files are the tray, which are the windows, which read `~/.claude` and
> which talk to the API — `AGENTS.md`'s file map is the only map, and a table maintained by hand is a
> poor substitute for a directory that is right by construction. Four files have also grown past the
> point where one file is one idea (`Program.cs` 2.5k lines, the two big code-behinds ~1.3k each,
> `SettingsWindow.xaml` six pages).
>
> This block moves the files (T129–T131) and then splits the ones that are too big to read (T132–T134).
> Every task is **rename + reference fix, zero behaviour change** — a Block P commit that alters what
> the app does is a mistake, not a bonus. The layout and the four things that must *not* move
> (the flat namespace, `lang/`, the root docs, the csproj) are in
> [IMPROVEMENTS.md](IMPROVEMENTS.md) §VIII.0.

- 📋 **T129** (deps: —) **The 29 non-UI sources move into `src/`** — `Context/` (9: scanner, usage, rules, report, prompt, history, nudges, fixture, token estimate), `Usage/` (11: API client, reports, histories, burn, live rate/chart, insights, activity), `Profiles/` (4: account, store, activity, settings), `Core/` (2: localization, safe walk), `Tray/` (3: program, icon renderer, updater). Pure `git mv`: the namespace stays flat, so no `.cs` file's *contents* change — only `AGENTS.md`'s file map, which gains the folder grouping and a "where does a new file go" rule so the root can't re-accrete. Gate: `dotnet build` plus the headless smoke set (`--context`, `--activity`, `--profiles`, `--tail`). → §VIII.1
- 📋 **T130** (deps: T129) **The four windows move into `src/Ui/`** — the 4 `.xaml` + their code-behind + `SettingsRow.cs`. Riskier than T129 because a WPF `Page`'s generated resource URI is derived from its path; no `pack://` URI is hardcoded anywhere in the repo (checked), so the generated `InitializeComponent` should absorb the move, but the gate is a `preview-ui` screenshot of all four windows, not a green build. → §VIII.2
- 📋 **T131** (deps: —) **Build, installer and winget manifests move into `build/`** — `build.cmd`, `build-installer.cmd`, `update-release.cmd/.ps1`, `update-winget.ps1`, `installer.iss`, `winget/`. The `.cmd`s are already self-relative (`%~dp0`), but `installer.iss` resolves `bin\…`, `ClaudeTray.ico` and `OutputDir=dist` against its own directory, and `.github/workflows/build.yml` invokes `ISCC.exe installer.iss` and `./update-winget.ps1` from the repo root — so CI moves with them, and README (build/release section) plus STRATEGY §III cite the paths. Independent of T129/T130: no C# is touched. → §VIII.3
- 📋 **T132** (deps: T129) **`Program.cs` splits three ways** — 2.5k lines / 130 KB holding two unrelated programs. `Main` and the arg dispatch stay in `Tray/Program.cs`; the ~1,000 lines of headless printers (`--context`/`--activity`/`--live`/`--tail`/`--profiles`, the markdown report writer, the fixture/capture/render entry points) become `src/Cli/*.cs`, one file per flag family; the ~1,080-line `TrayContext` (menu, timers, poll, tooltip, icon render) becomes `Tray/TrayContext.cs`. Mechanical — moved verbatim, not rewritten. → §VIII.4
- 📋 **T133** (deps: T130) **The two big code-behinds split per surface** — `ContextWindow.xaml.cs` (1,369 lines, 7 types) and `StatisticsWindow.xaml.cs` (1,269 lines: session tab, week tab, throughput tab, activity shape, method popup, profile selector) each carry several independent surfaces plus their helper types in one file. `partial class` per tab, helper types to their own files, no XAML change. → §VIII.5
- 💭 **T134** (deps: T130) **`SettingsWindow.xaml`: six pages in one file** — 1,019 lines holding General, Display, Claude Code, Notifications, System information and About, over ~170 lines of shared row/value styles they all consume. Needs design before it is worth doing: a `UserControl` per page reads better but separates the styles from their users, and the UI conventions (all layout in XAML, nothing hardcoded that a theme owns) must survive it. → §VIII.6

## Block T — One profile, the whole environment

> **A deliberate reversal of T143.** That task refused to write a user-level environment variable and
> shipped a copyable `setx` command instead, on the grounds that the variable reaches every Claude Code
> session on the machine — including ones the tray never sees — and keeps applying after the tray is
> uninstalled. The user's requirement is precisely that reach: *"quando eu mudar para um dos perfis, o
> Windows use este perfil em todo o ambiente"*, VS Code opened from the Start menu included. Nothing the
> tray does to a process it launches can reach that; a user-scope variable is the only mechanism, and
> §I.4 does not forbid it (it is about not editing files inside `~/.claude`). The T143 objection is
> answered rather than ignored: what the tray sets, the tray removes — turning the toggle off, or
> resuming auto-follow, restores what was there before.
>
> **What it syncs to is the pin, not the icon.** T139 already models a manual pick as a distinct,
> persistent, user-visible state ("· pinned" + "Resume following"), and auto-follow only moves the icon
> while nothing is pinned. Syncing the variable to every icon movement would rewrite the environment
> continuously on a machine where a profile is more or less always active — which is the observation
> that produced T139 in the first place. Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §XI.
>
> **T144 shipped** the prerequisite: a launch now decides against what the child inherits, and `~/.claude`
> is selected by *removing* the variable rather than setting it. See [CHANGELOG.md](CHANGELOG.md).

- 📋 **T145** (deps: T144) **The toggle that makes a profile the whole machine's** — a **Use the chosen profile everywhere in Windows** switch on the Claude Code page, off by default so no existing install has its environment rewritten by an update. While on, a **manual** pick (the tray's Profile submenu or the Settings *Icon profile* button — T139 already unifies the two) writes `CLAUDE_CONFIG_DIR` at **user** scope through the same `ActionFor` three-way as T144, so choosing the `~/.claude` profile *removes* the variable instead of setting it. Auto-follow never writes. `WM_SETTINGCHANGE` is broadcast so Explorer refreshes its block and the next Start-menu launch picks it up without a sign-out; the UI states plainly that already-running processes keep the old value (the non-goal "no switching the account of a running session" is unchanged — this switches the *next* one). Turning the toggle off, or "Resume following", restores the previous value, so the tray never leaves a setting behind it no longer manages. → §XI.1
- 📋 **T146** (deps: T145) **With the toggle on, "Open Claude Code" is one command again** — the per-profile submenu (T123) exists because a launch is the only place the tray can select a profile; once the variable is global, both entries would do the same thing and the submenu is a menu level asking a question the user already answered. It collapses back to a plain command while the toggle is on, and stays exactly as it is while off. The one thing that must not be lost is T137's "— entrar" affordance for a profile with no credentials on disk. → §XI.2
- 📋 **T147** (deps: —) **The icon says whose number it is** — with the profile global, "which profile am I in?" becomes the question the tray exists to answer at a glance, and today nothing on the icon answers it: the number, the fill and the projection colour are identical for both profiles, and only the tooltip names one. A static mark (the profile's initial, or a per-profile accent) has to survive 16 px and must not collide with the existing colour language, where colour means *projection*, not identity. Not a live hint — the T101 non-goal is about animation and a permanent transcript tail, neither of which this needs. → §XI.3

## Non-goals (do NOT add as tasks)

Binding constraints — see [IMPROVEMENTS.md](IMPROVEMENTS.md) §I for the full text. Summary:

- **No tokenizer dependency**, and no third-party NuGet package in general — the single
  self-contained `.exe` + installer story depends on it. Token counts stay estimates with a visible "≈".
- **Never read message content** from transcripts. Usage counts, model ids, flags, tool/skill *names*
  and the session `cwd` only. No content display or export anywhere in the app.
- **No network** beyond the usage API and GitHub Releases. No telemetry, analytics or crash reporting.
- **Not a memory editor, not a Claude Code config manager.** Hooks, MCP servers, permissions and
  instruction files are *measured*, never edited — measure, advise, hand the edit to Claude. **T77
  settled this: no write path at all.** The archive-with-undo was considered and dropped in favour of
  a generated cleanup prompt (see [IMPROVEMENTS.md](IMPROVEMENTS.md) §I.4).
- **Never swap, copy or move a credential or config file to switch accounts.** Block O switches
  profiles by launching Claude Code with a different `CLAUDE_CONFIG_DIR`; the file-juggling
  alternative races Claude Code's own refresher (measured: an 8h access token, a 5-day refresh token,
  `.credentials.json` rewritten on refresh, and `.claude.json` rewritten constantly), splits identity
  across two files, and would make the tray the thing that broke somebody's login. §I.4 has no
  exception, and credential material is the last place to invent one.
- **No switching the account of a running session.** The environment is fixed when the process
  starts; a profile change applies to the *next* session. The UI says so rather than implying
  otherwise.
- **Profiles are contexts, not quota pools.** No string anywhere may suggest changing accounts
  because one hit its limit. Monitoring two subscriptions with each one's own token is reading what
  you own; nudging somebody to hop when a window maxes out is limit circumvention wearing a
  convenience costume, and it would contradict the README's own terms section.
- **Don't swap the UI stack.** WinForms owns the tray icon, WPF owns windows, both on one STA thread.
  No imperative `Dock=Top` stacking; no hardcoded hex for theme-able surfaces.
- **No second source of truth for the version** — `<Version>` in `ClaudeTray.csproj` only.
- **No live hint on the tray icon** (settled by dropping T101). An animating icon draws the eye
  continuously, and — decisively — it would need a transcript tail running for the whole session.
  T99 deliberately made the tail *window-owned* so a closed Statistics window watches nothing;
  a permanent tail to power an ambient nicety would undo the one property that keeps the feature
  free when nobody is looking. §V.5 pre-authorised dropping it.
- Pricing/distribution/positioning discussion goes in [STRATEGY.md](STRATEGY.md), never as a
  numbered task.
