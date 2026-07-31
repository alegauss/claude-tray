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

## Block O — Profiles ("personal, work, empresa2 — one tray, several logins")

> Claude Code is **one account per config dir**: `.claude.json` holds a single `oauthAccount` and
> `.credentials.json` a single `claudeAiOauth`, and `/login` overwrites rather than accumulates. So
> "several subscriptions on one Windows" means several **config dirs** (`CLAUDE_CONFIG_DIR`), and the
> unit this block models is a **profile** — `{Label, ConfigDir, WorkDir}` — never an "account" the tray
> pretends to own. Today the tray sees exactly one: it polls `~/.claude/.credentials.json` and every
> local reader is scoped to `~/.claude`, so a second profile's usage appears **nowhere**, silently.
>
> Three things "switching" could mean, and only two are possible: which profile the tray **monitors**
> (pure read), which profile a **new** session opens in (one env var on the launch path the tray
> already has), and the account of an **already running** session — which no GUI can change, because
> the environment is fixed at process creation. See Non-goals.
>
> The block's whole safety argument is that it **writes nothing into a config dir**: measured, the CLI
> creates a missing `CLAUDE_CONFIG_DIR` itself, and `claude auth login|logout|status --json` is the
> supported seam for the parts that do need a write — the same "hand the edit to Claude Code" that T77
> settled for memory. Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §VII.
>
> **Shipped: T122–T125, T127** — the profile model and discovery (`ClaudeAccount.Discover`, `--profiles`) with
> the picker on the System information page, then the list editor on the Claude Code page and the
> per-profile `Open Claude Code` submenu, then the per-profile auth reading with the
> off-the-subscription warning, then per-profile data isolation with its migration, and the
> per-profile polling with the Profile submenu.
> See [CHANGELOG.md](CHANGELOG.md) Block O.
>
> **T125 was split while building it.** Its design section ordered the work "keying first,
> polling second", and the two halves are independently valuable and independently
> verifiable: the keying shipped as T125, and the fan-out — N heartbeats, which profile the
> icon follows, the others in the tooltip — shipped as **T127**. Nothing was dropped.

- 📋 **T128** (deps: T127) **Statistics for a profile other than the monitored one** — T127 gave every profile a poll, its own stored series and a reading in the Profile submenu, but the Statistics window is still built around one `PaceSnapshot`: charts, projection, activity shape and throughput all describe the monitored profile. The data for the others is already on disk (their `usage-history.jsonl`, keyed per profile since T125), so this is a profile selector in the window plus recomputing from that profile's stores — and the Throughput tab needs its *own* config dir for the transcript tail, which currently reads only `~/.claude/projects`. → §VII.6
- 💭 **T126** (deps: T127) **The icon follows the profile you're working in** — `TranscriptTail` already watches transcripts byte-for-byte; with N profiles it can see *which* config dir just had a turn land and point the icon at that one, so nobody clicks "switch" at all. Manual override stays. Open question is the icon itself: at 16px there is no room for a label (the number fills it), so the profile has to read from the tooltip and menu, with at most a small per-profile colour dot — and only if it survives being looked at next to the projection colour, which already means something. → §VII.5

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
