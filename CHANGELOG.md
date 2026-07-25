# Claude Code Tray — Shipped Ledger (CHANGELOG)

> Concise index of work that has shipped. **`git log` is the authoritative history** — this file is
> just a searchable map of *what* shipped and *where* the rationale lived. Implementation detail is
> deliberately removed from [ROADMAP.md](ROADMAP.md) once a task ships.
>
> Maintenance: when a task in [ROADMAP.md](ROADMAP.md) ships, move its one-line entry here (under
> its block), reference the commit if useful, and delete it from the roadmap. See the
> `roadmap-docs` skill.
>
> **Blocks A–H were reconstructed from the git history** (110 commits, v1.0.0 → v1.4.6) when this
> ledger was introduced, so their T-numbers are retroactive: one entry per shipped *unit of work*,
> not one per commit. Block I onward is numbered as it happens.

| Block | Theme |
|---|---|
| [A](#block-a--foundation-tray-icon-api-projection) | Foundation — tray, icon, API, projection |
| [B](#block-b--packaging-self-update-ci) | Packaging, self-update, CI |
| [C](#block-c--settings-window-wpf-fluent) | Settings window (WPF Fluent) |
| [D](#block-d--auth--api-resilience) | Auth & API resilience |
| [E](#block-e--reset-notifications--toasts) | Reset notifications & toasts |
| [F](#block-f--statistics-window-pace-report) | Statistics window (pace report) |
| [G](#block-g--localization) | Localization |
| [H](#block-h--tray-display-options) | Tray display options |
| [I](#block-i--context-load-inspector) | Context Load Inspector (active — see ROADMAP) |

## Block A — Foundation (tray, icon, API, projection)

- **T1** — WinForms tray shell: `NotifyIcon` + `ApplicationContext`, with the usage number drawn as a GDI+ **vector** (`GraphicsPath` + outline) at the real `SM_CXSMICON` size under `PerMonitorV2` DPI awareness — never a downscaled bitmap. (`47dae10`)
- **T2** — `ApiClient`: OAuth token read from `~/.claude/.credentials.json`, `anthropic-ratelimit-unified-*` headers parsed into the 5h / 7d / extra windows. (`47dae10`)
- **T3** — `BurnTracker`: utilization history → least-squares slope → exhaustion projection (`Projection.Ok`/`Danger`/`Unknown`). (`9577a0c`)
- **T4** — Projection-driven icon color: the fill bar turns red when the window is on track to run out before it resets. (`9577a0c`, `30a9e79`)
- **T5** — `UsageInsights`: last 24h of `~/.claude/projects/**/*.jsonl` aggregated into a cost-weighted breakdown (subagent share, >150k-context share, per-model split) behind the "Usage insights" submenu and `--insights`. (`9577a0c`)
- **T6** — Single-instance enforcement via a named mutex; a second launch exits silently instead of adding a second tray icon. (`9577a0c`)
- **T7** — Multi-resolution app `.ico` generated from the same GDI+ logo renderer (`--makeicon`), plus the `--render` / `--social` dev helpers. (`9577a0c`, `bea59a3`)
- **T8** — Icon legibility pass: text fitting, glyph centering and scaling verified at 16 / 20 / 32 px. (`849a576`, `ad2ec09`, `8f282b7`)
- **T9** — Last-refresh timestamp in the tooltip; and red (not green) once a window sits at 100%. (`a611198`, `a0b6176`)

## Block B — Packaging, self-update, CI

- **T10** — Inno Setup installer + `build-installer.cmd` — a per-user install with no admin, from a single self-contained `win-x64` publish. `<Version>` in `ClaudeTray.csproj` is the one place a version lives. (`d16f00b`, `980e9a7`)
- **T11** — Apache-2.0 license + generated social-preview card. (`bea59a3`)
- **T12** — `Updater`: checks GitHub Releases, downloads and hands off to the installer for in-app self-update; surfaced as a bold menu item + balloon tip. (`ecfa21c`)
- **T13** — Release scripting (`update-release.cmd` / `.ps1`) and version propagation across manifests. (`20ddd0b`, `0dd0171`, `3556095`)
- **T14** — winget manifests (`alegauss.ClaudeCodeTray`) incl. a pt-BR locale, advertised in the README. Removed once (`6b6f1e5`) and deliberately restored. (`bea59a3`, `ba088cd`, `93521a8`)
- **T15** — Version-parsing hardening so both `v1.2.3` and the stray `v.1.2.3` tag shape resolve. (`4ded0bf`)
- **T16** — GitHub Actions build/release workflow. (`f8f8836`, `1d032f3`)
- **T17** — CI extension: winget manifests updated automatically on release. (`28641af`)
- **T18** — Dependabot action bumps (checkout 5→7, setup-dotnet 4→5, upload-artifact 5→7). (`76eee87`, `d5cbe34`, `4bcbb64`)

## Block C — Settings window (WPF Fluent)

- **T19** — WPF + the built-in .NET Fluent theme (`ThemeMode="System"`) hosted on the WinForms STA thread: one `System.Windows.Application` created lazily and never `Run()`, so both UI stacks share the same message pump with zero extra dependencies. (`4501499`)
- **T20** — `Settings` model persisted as JSON under `%LocalAppData%\ClaudeTray`, clamping every out-of-range or hand-edited value back to its default. (`4501499`)
- **T21** — Refresh-interval setting with a live cost estimate for the chosen cadence. (`4501499`, `24a005f`)
- **T22** — "Show usage percentage on the icon" toggle (otherwise just the fill bar). (`94bde45`)
- **T23** — Non-modal settings dialog, reused instead of stacked on repeat opens. (`d892992`)
- **T24** — `--settings` standalone preview flag + the `preview-ui` skill: build → launch → screenshot → judge, so no UI change is reported done unseen. (`cb2c3fc`)
- **T25** — Start-with-Windows via the per-user HKCU `Run` key (`StartupManager`) — no admin, no scheduled task. (`e6dddf3`)
- **T26** — Notifications settings page. (`1d236e2`)
- **T27** — `SettingsRow` control; Display and Claude Code split into their own pages; wider default window so descriptions aren't cramped. (`bd85eb4`, `538c38a`, `374478b`)

## Block D — Auth & API resilience

- **T28** — Token-refresh handling and error surfacing in `ApiClient`. (`8073217`)
- **T29** — "Open Claude Code" menu item (`cmd /k claude`) — launching the CLI is what refreshes the OAuth token. (`fff746b`)
- **T30** — A distinct "needs auth" icon state. (`e6dddf3`)
- **T31** — Auto-open Claude Code on a 401 (opt-in, once per signed-out spell) + a faster auth-retry poll cadence while signed out. (`2235d41`)
- **T32** — Session-refresh vs. full-login distinction: the `/login` hint is printed only when no refresh token is on disk, because no CLI flag forces a re-login. (`ec79f54`)
- **T33** — Transient-failure tolerance: a single timeout or network blip keeps the last good reading instead of flipping the icon to a scary error. (`2713f82`)
- **T34** — Signed-out and API-error states show the logo (with a red dot for a live error) rather than a misleading `0%`; ToS advisory in the auth messaging. (`dda4641`, `877b273`)
- **T35** — Header-less API responses treated as transient, which removed a phantom weekly reset. (`b44a058`)
- **T36** — Poll-cadence idling: once a window is maxed out, consumption is frozen until it resets, so the loop sleeps to just past the known reset and double-checks instead of re-reading 100% every 5 minutes. (`3e193c3`)

## Block E — Reset notifications & toasts

- **T37** — Detect an *unexpected* weekly reset (the counter dropping to 0% before its deadline) and notify. (`9050f4e`)
- **T38** — Detect a partial mid-window credit (e.g. 91% → 50%), not only full resets. (`15bbb91`)
- **T39** — A distinct color per reset kind. (`d4aaf1a`)
- **T40** — `ToastWindow`: a custom WPF toast with an animated quota bar and four themes (Surprise / Bonus / Weekly / Session), with a system-balloon fallback so an event is never silently dropped. (`de12b7a`)
- **T41** — All reset notifications enabled by default. (`a57d6b1`)
- **T42** — Minimum-usage floors, so a reset from a barely-touched window isn't worth a ping. (`dbd1128`)
- **T43** — `--simulate-reset` and `--capture-toast` dev previews, sharing the live notifier's display strings so the wording can't drift. (`de12b7a`)
- **T44** — Timestamped reset-event log at `%LocalAppData%\ClaudeTray\reset-events.log`, so an anomaly can be reported later with real before/after numbers. (`9050f4e`, `de12b7a`)
- **T45** — The 5h session reset as its own opt-in toggle, separate from the weekly ones. (`a57d6b1`)

## Block F — Statistics window (pace report)

- **T46** — `StatisticsWindow`: the consumption-pace report — 5h session and 7d week usage against the clock, with a verdict. (`2913ed8`)
- **T47** — Tray left-click opens Statistics (right-click keeps the context menu). (`02b1d5b`)
- **T48** — `UsageHistory`: append-only JSONL log of every live reading under `%LocalAppData%\ClaudeTray`, with age-based pruning, so the charts draw *measured* utilization instead of inferring the shape from token counts. (`c41c7ff`)
- **T49** — `--capture-stats`: off-screen `RenderTargetBitmap` capture per tab, since a screen-copy capture can't see a covered window. (`c41c7ff`)
- **T50** — Tab contrast/layout pass + screenshots on the site. (`0fec258`)
- **T51** — Interactive chart elements. (`9b31607`)
- **T52** — Throughput metrics. (`de9e9ce`)
- **T53** — Auto-refresh: fresh readings pushed into an open window on the same cadence as the icon. (`7672898`)
- **T54** — Remaining-quota framing carried into the statistics display. (`08be503`)
- **T55** — API-outage handling: charts keep drawing from the last known local data, with an error banner and the outage marked as red "unavailable" spans along the usage line; `--stats error` / `gapdemo` / `history` previews. (`4c1993f`, `8a4b4c7`)
- **T56** — Day boundaries on multi-day spans. (`6e3c40f`)
- **T57** — Open the weekly tab by default when the session is idle (a flat 5h chart has nothing to show); projection landing time on the chart. (`839f913`, `a0f2bd0`)

## Block G — Localization

- **T58** — `Localization.cs` (`L.T(...)` in code, `{local:Loc key}` in XAML) and every user-visible string extracted to keys. (`214093f`)
- **T59** — Embedded JSON language resources for five languages: `en`, `pt-BR`, `pt-PT`, `fr`, `es`. (`efd5acf`)
- **T60** — Language selection in Settings (`auto` = follow the OS) with a restart prompt, since localized strings resolve when a window is parsed. (`304b9ca`)

## Block H — Tray display options

- **T61** — "Show remaining instead of used": the icon counts down from 100% and the tooltip lines read "… left", while the projection, warning color and flash still track the limit. (`6c1d7cf`)
- **T62** — Remaining-mode wording in the reset toasts ("Had 21% left" instead of "Was 79% used"). (`3bd540d`)
- **T63** — Remaining-mode projection wording — "runs out" rather than a "0% left" that reads like a present value. (`c675cff`)
- **T64** — Softer session-expired tray message, framed around usage rather than an error. (`54b0e03`)
- **T65** — Opt-in near-limit flash: the icon blinks every 500 ms above 90% (the point the API reports `allowed_warning`). Off by default — the warning color tracks the limit regardless. (`52cba9b`)

## Block I — Context Load Inspector

> Phase 1 of the epic: the whole model validated headlessly behind `--context` before any XAML,
> exactly as `--insights` did for `UsageInsights`. Phase 2 puts a window on it. Active tasks and the
> remaining phases are in [ROADMAP.md](ROADMAP.md); design rationale in
> [IMPROVEMENTS.md](IMPROVEMENTS.md) §II.

- **T66** — `ContextScanner.cs`: discovers every context source per project — the user + project instruction chain with `@imports`, the memory index and its files, skill/agent frontmatter, settings sizes — splits **eager** (paid on every request) from **lazy** and **not-loaded**, and resolves each `~/.claude/projects/<slug>` back to a real path by filesystem probing with the transcript `cwd` as the authoritative fallback. Verified byte-for-byte against `find`/`stat` on two projects. (`159d714`)
- **T67** — `TokenEstimate.cs`: chars→tokens for markdown, classified per line (prose / code fence / table), always rendered as an estimate ("≈4.9k"). (`159d714`)
- **T68** — Session-zero calibration from real transcripts: observed startup context for 27 of 33 projects, a robust base-overhead fit (median residual + Theil–Sen slope) putting the unscannable system-prompt/tools/MCP overhead at ≈32k tokens, and a corrected estimate within ±15% of measurement for 23/27 projects (median error 6.2%). Found that `cache_read_input_tokens` must be counted too — see the ROADMAP note. (`159d714`)
- **T69** — Bounded, cached scanning: 92ms cold / 76ms warm over 33 projects and 940 files, cache keyed on a path+size+mtime fingerprint (directory mtimes would miss an in-place `MEMORY.md` edit), file/directory caps reported rather than silent, and a parallel IO-bound walk. Caught a real double-count bug where a one-line `CLAUDE.md` containing `@agents.md` billed a 43 KB file twice. (`159d714`)
- **T70** — `ContextWindow.xaml(.cs)`: the master/detail window on the scanner — projects on the left (name, sources, what a session there costs), and on the right the estimated vs. transcript-measured session zero plus every source grouped by kind with its eager/lazy/index word. Settles the naming decision (**`Context…`** in the tray menu, "Context Load — memories, skills & instructions" as the title) and the first `context.*` keys in all five languages. Previewable standalone via `--context --window [slug]`; the bare `--context` stays the headless report.
- **T71** — The session-zero gauge, the window's hero: one stacked bar of what a session loads against the whole context window, split into Claude Code's own ≈32k base overhead (fitted from this machine's transcripts when there are enough, else the measured fallback) / instructions / memory index / skills & agents index, with the transcript-measured median drawn over it as a tick — the bar is an estimate, the tick is a measurement, and the gap between them is information. Window size read off the observed model, so a 1M-context session isn't charted against 200k. Verified on a bloated project and on this repo's zero-memory state.
- **T72** — Source breakdown made readable and actionable: a tinted load chip per row (`eager` / `lazy` / `lazy + index` / `not loaded`) in the same hues the gauge uses, where a skill's chip says both of its costs at once; a health dot for the two signals the scan already carries (unreadable, or untouched for 90+ days); reveal-in-Explorer / open on the row's context menu and on double-click — never an edit, per §I.4; and a sort picker (most eager tokens / largest file / oldest first) applied within each kind group. Preview affordance `--context --window <slug> --scroll` opens on the table, since the detail pane is taller than the screen.
- **T74** — `ContextRules.cs`: the advisor. 11 grounded rules (oversized eager index / instruction file, dead index pointers, unindexed memories, oversized memory files, missing or invalid frontmatter, byte-identical memory directories, dead project dirs, stale memories, two full instruction files in one root, missing or 500+ char skill descriptions, bloated `settings.json`), each carrying severity + one plain sentence + the concrete fix — no finding without a fix. Headless as `--context --check`, grouped by severity: 41 findings on the dev machine, spot-checked against the filesystem (the byte-identical cluster, the 302 KB dead worktree dir, the unindexed dumont memory). Needed two scanner additions — index link targets and a per-memory-file content hash, with a cache-schema version so an older cache can't serve them empty. **Found and fixed a real parser bug it exposed**: a bare `description:` followed by an indented multi-line scalar (no `>`/`|`) was read as empty, which produced the one false positive and understated every such skill's eager index cost (shared skill index ≈3.1k → ≈3.2k tokens).
- **T73** — Cross-project overview: an "All projects" row leading the master list, with the machine's total footprint, the shared eager context counted once (not once per project), the heaviest session zero, the ten heaviest eager loads as proportional bars that click through to the project, and the two findings that only exist *between* projects — duplicated memory directories and dead project dirs, each with its fix. Detection stays in `ContextRules` (shared with `--context --check`) while the sentences are localized here, so the two surfaces can't disagree about what a duplicate is. The per-session column is deliberately blank on that row: there is no per-session cost for 33 projects at once. Previewable with `--context --window all`.
- **T75** — `ContextUsage.cs`: evidence of use, mined from the transcripts — which skills and agents were actually invoked over the last 30/90 days, surfaced as a "Used 90d" column in the window, a `--context --usage` report, and a `never-invoked` rule. On the dev machine: **38 skills/agents never invoked in 90 days, costing ≈4.4k of eager index in every session** — the number that turns "trim your skills" into a decision. Reads 736 MB of transcripts in 0.6s via an ordinal-substring prefilter before any JSON parse, then ~18 MB on later runs thanks to a per-file (size+mtime) cache; runs after the window is already useful, never inside `Scan`. Privacy: exactly two identifier fields are read from a tool call (`input.skill`, `input.subagent_type`) and nothing else. **Memory files carry no usage annotation on purpose** — a recall leaves no structured trace, so the only evidence would be message content, which the app never reads; parked as T85. A first cut suppressed every claim by requiring the file to be older than the window (mtime as a proxy for age, which a plugin-marketplace refresh invalidates) — replaced by wording that states the fact about the window instead of inferring "never".
- **T76** — What-if simulator: tick any row that actually carries eager cost and the gauge redraws without it, with a panel stating what would be freed per session, the ≈cost per session start, and the before/after ("was ≈39k, would be ≈35k"). No checkbox is offered where removal would free nothing — a tick that can't move the gauge would promise a saving that isn't there. Nothing is written: the selection is read-only state in the window, and the panel says so, because the sanctioned actions belong to T77. Previewable with `--context --window <slug> --simulate`.
- **T77** — Safe actions, and a decision. `ContextPrompt.cs` generates a ready-made cleanup prompt for Claude Code — the findings for this project (or the machine-wide ones) grouped by severity, each with its fix and file, plus whatever is ticked in the what-if simulator, and a closing instruction to show the plan before deleting anything. Copied from the window (with a live "N things worth fixing here" count in both views) and inspectable via `--context --prompt [project]`. **The destructive archive-with-undo was deliberately not built**: §II.8 always made it conditional, and the app staying read-only against `~/.claude` is worth more than a delete button — measuring is a read, rewriting someone's memory directory on a size heuristic is not. §I.4 and the roadmap's non-goals now record that decision instead of leaving it "under design". Reveal-in-Explorer and open shipped earlier with T72.
