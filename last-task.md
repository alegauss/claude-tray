# Last task number — `T148` · next block letter — `U`

> **Single source of truth for the next free task number.** The next new task is `T149`; after
> assigning it, bump the number above and append a log line below.
>
> **Next block letter — `U`** (Block **T** = One profile, the whole environment — the tray writes the
> user-scope `CLAUDE_CONFIG_DIR` itself, reversing T143; created 2026-07-31).
>
> **`CHANGELOG.md` — not `ROADMAP.md` — is authoritative for the real maximum block letter**, since
> the roadmap is periodically pruned of fully-shipped blocks. Grep it before bumping the letter.

## Structural notes

- Blocks **A–H** are retroactive: assigned when this ledger was introduced (2026-07-25) by reading
  the 110-commit history from v1.0.0 to v1.4.6, one task per shipped *unit of work* rather than one
  per commit. Their entries live only in [CHANGELOG.md](CHANGELOG.md) — there is no per-task log line
  below for them, and none should be back-filled.
- Block **I** is the first block numbered as it happens.
- Block **J** opens against the Statistics window, not the Context Load Inspector — the two blocks are
  independent, so J's tasks carry no dependency on T85.
- **T91–T96 are Block J's second pass**, added 2026-07-28 right after T86–T90 shipped: same theme, so
  they stay in J rather than opening K (Block I set the precedent — shipped T66–T84 alongside an
  active T85). They came out of building the first pass, not out of planning it.
- **T102–T108 are Block K's second pass**, added 2026-07-28 as T97–T100 closed: same theme, so they
  stay in K rather than opening L (the precedent Block J set with T91–T96). They came out of
  *building* the block — one latent bug it exposed elsewhere (T102), two costs it introduced, two
  readings it stops short of, one duplicated resolver. T96 was widened rather than duplicated: the
  missing test surface is one gap, not one per block.
- Block **L** (T109, created 2026-07-29) opens on a **field report**, not on a plan: the failure came
  in as a screenshot of the Statistics window. It is a block rather than a K task because the defect
  is under every local scan, K's included, and nothing about it is specific to live throughput.
- Block **K** (T97–T101, created 2026-07-28) is a *new* block rather than a third Block J pass: J is
  about the weekly **projection** (where the quota lands), K is about **instantaneous** throughput
  (what is running now, and in which project) on a clock that is local rather than the API's. It opens
  against the same Statistics window, so it carries no dependency on J's open tasks.
- **T110–T112** (2026-07-29) stay in Block **K** rather than opening M: all three are the live strip's
  own presentation — T110 its scale, T111 its own tab, T112 its handling of outliers. T110 took the
  magnitude half of K's own T104, which remains open for the per-column hover.
- Block **N** (T120–T121, created 2026-07-31) opens on a **user question** rather than a plan — "can
  the About page show the plan, the holder and the config directory?" — and became its own page
  instead of an About addition: About is the project's identity, this is the *machine's*. T121 stays
  open because the page cannot be screenshotted from a real account (see IMPROVEMENTS §VI.1).
- Block **O** (T122–T127, created 2026-07-31; **T127 split out of T125** while building it —
  its design already ordered "keying first, polling second", and the two halves verify
  independently, so the keying shipped alone rather than as half a large commit) is a *new* block rather than a Block N second pass: N is
  one read-only page about **this** machine's single login, O is a **model** — several config dirs, the
  launch path, the stores keyed per account. It carries a dependency on T120 because `ClaudeAccount` is
  the reader it parameterizes. **T124 is a defect that predates the block** (an environment
  `ANTHROPIC_API_KEY` makes Claude Code bill the Console while the tray reports an idle subscription),
  found while testing whether `CLAUDE_CONFIG_DIR` isolates auth — it does not — and it is ordered before
  the multi-profile polling because it is wrong today, for everyone with that variable set. **Closed with
  T126** (2026-07-31), so the block is out of `ROADMAP.md` — see [CHANGELOG.md](CHANGELOG.md) Block O.
- Block **M** (T113, created 2026-07-29) is a one-task block on the *window's* chrome rather than a
  fourth Block K item: K is the live strip, and the method note it hid behind an ⓘ describes every
  number in the window. Block L set the precedent that a block may hold a single task.
- Block **P** (T129–T134, created 2026-07-31) is the first block about the **repo** rather than the app:
  57 entries in a flat root, and four files past the point where one file is one idea. It carries no
  dependency on any earlier block and every task in it is rename + reference fix with **zero behaviour
  change** — a Block P commit that alters what the app does is a mistake. T129–T131 move files
  (sources / windows / release scripts, split by *failure mode*: compiler, generated XAML URIs, CI),
  T132–T134 split the oversized ones. §VIII.0 lists the four things that must not move; `lang/` is the
  sharp one — its resource names are path-derived, so moving it breaks localization at runtime with a
  green build.
- Block **Q** (T135, created 2026-07-31) opens on a **field report**, like Block L: *"I can't type
  anything in this field"* — and the field was not the bug. Under the tray's WinForms message pump the
  WPF windows never received **any** keyboard input, in every window, since the first one shipped. It is
  its own block rather than a Block O task because it belongs to the app's **hosting**, not to profiles;
  T123's Name box is merely the first control anyone had a reason to type into. The trap it leaves
  behind is in [AGENTS.md](AGENTS.md): `--settings` runs a **WPF** pump and cannot see it, which is why
  every preview and screenshot of the UI looked correct — `--settings-tray` is the one that reproduces
  the tray. **T142 is Q's second pass** (added 2026-07-31): the reproduction flag exists, but nothing yet
  *runs* against it, and the checks that found and verified T135/T137 live in a scratch directory.
- Block **R** (T136, created 2026-07-31) is a **second pass over Block O**, opened by a field report
  from the first real use of a second login: *"I registered the profile, clicked sign in, logged in, and
  it still said sign in"* — plus two profiles showing the same name. It is a new block rather than more
  Block O tasks because O is shipped and pruned, and because these are defects found by *using* the
  feature, not gaps in its design. T136 is the one that damaged state: the tray created the stub config
  that then misdescribed the user's own default profile; T137 is the one that made the user click twice.
  **T138–T140 and T143 were added the same day, from the same session.** They share a theme the two
  shipped tasks did not: the profile *model* is right and the **controls and names** around it are not —
  three controls that read as a profile switch when one is, a manual pick that a continuously-active
  profile undoes in seconds, a derived label that is somebody's email address, and (T143, an idea) the
  machine-wide variable the reporter correctly noticed the tray never sets. **Closed with T143**
  (2026-07-31): all six tasks shipped, so the block is out of `ROADMAP.md` — see
  [CHANGELOG.md](CHANGELOG.md) Block R.
- Block **S** (T141, created 2026-07-31) is a one-task block on the **Settings model round-trip**, not a
  Block R item: it is not about profiles at all. `SettingsWindow` copies the model field by field and
  `ApplySettings` copies it back, so any field missing from that list is silently reset on Save —
  `MonitoredConfigDir` was (fixed in T126, which is how the class was found) and the context-growth pair
  still is. Block L set the precedent that a block may hold a single task; the reason this is a *block*
  rather than a stray fix is that the task is to make the copy total by construction, which retires a
  hand-maintained `AGENTS.md` rule. **Closed with T141** (2026-07-31) — its only task — so the block is
  out of `ROADMAP.md`; see [CHANGELOG.md](CHANGELOG.md) Block S.
- Docs live at the **repo root** (`ROADMAP.md`, `CHANGELOG.md`, `IMPROVEMENTS.md`, `STRATEGY.md`,
  `last-task.md`), not under `docs/` — `docs/` is the published GitHub Pages site.

## Log

One line per task, exactly of the form
`- **T<n> SHIPPED** (Block X — short title) — YYYY-MM-DD.`
The implementation story goes in the **commit message** and the `CHANGELOG.md` one-liner, *never*
here. This file is a terse index, not a memory.

- **T1–T65 SHIPPED** (Blocks A–H — tray foundation through display options) — v1.0.0 … v1.4.6, retroactively numbered 2026-07-25.
- **T66 SHIPPED** (Block I — ContextScanner: discover every context source per project) — 2026-07-25.
- **T67 SHIPPED** (Block I — TokenEstimate: per-line markdown token estimation) — 2026-07-25.
- **T68 SHIPPED** (Block I — session-zero calibration from real transcripts) — 2026-07-25.
- **T69 SHIPPED** (Block I — bounded, cached, parallel scanning) — 2026-07-25.
- **T70 SHIPPED** (Block I — ContextWindow: master/detail window on the scanner) — 2026-07-25.
- **T71 SHIPPED** (Block I — session-zero gauge with the measured tick) — 2026-07-25.
- **T72 SHIPPED** (Block I — source table: load chips, health dots, row actions, sort) — 2026-07-25.
- **T74 SHIPPED** (Block I — grounded rule engine behind --context --check) — 2026-07-25.
- **T73 SHIPPED** (Block I — cross-project "All projects" overview) — 2026-07-25.
- **T75 SHIPPED** (Block I — usage evidence for skills and agents from transcripts) — 2026-07-25.
- **T76 SHIPPED** (Block I — what-if simulator over the gauge) — 2026-07-25.
- **T77 SHIPPED** (Block I — cleanup prompt for Claude; no write path) — 2026-07-25.
- **T78 SHIPPED** (Block I — A-F context debt grade and drift sparkline) — 2026-07-25.
- **T79 SHIPPED** (Block I — opt-in context-growth nudge, off by default) — 2026-07-25.
- **T80 SHIPPED** (Block I — localization audit, --lang override, es/fr/pt-BR verified) — 2026-07-25.
- **T81 SHIPPED** (Block I — --context-report markdown document) — 2026-07-25.
- **T82 SHIPPED** (Block I — debounced live refresh over ~/.claude) — 2026-07-25.
- **T83 SHIPPED** (Block I — --sample fixture tree; all 16 rules fire on it) — 2026-07-25.
- **T84 SHIPPED** (Block I — README, site and llms.txt docs with fixture screenshots) — 2026-07-25.
- **T86 SHIPPED** (Block J — ActivityProfile: a weekly activity shape from the transcripts) — 2026-07-28.
- **T87 SHIPPED** (Block J — staircase projection on the weekly chart) — 2026-07-28.
- **T88 SHIPPED** (Block J — permanent per-hour usage aggregate folded out of the pruned log) — 2026-07-28.
- **T89 SHIPPED** (Block J — ghost curve of the previous week behind the weekly chart) — 2026-07-28.
- **T90 SHIPPED** (Block J — "stop now, resume at ..." advice on the weekly projection) — 2026-07-28.
- **T97 SHIPPED** (Block K — TranscriptTail: byte-level tail over the transcripts) — 2026-07-28.
- **T98 SHIPPED** (Block K — LiveRate: age-weighted rolling tokens/s beside the window average) — 2026-07-28.
- **T99 SHIPPED** (Block K — the throughput row becomes a moving 3-minute strip) — 2026-07-28.
- **T100 SHIPPED** (Block K — per-project attribution in the live strip) — 2026-07-28.
- **T101 DROPPED** (Block K — tray-icon live hint; now a binding non-goal) — 2026-07-28.
- **T109 SHIPPED** (Block L — SafeWalk: an unreadable directory can't fail a whole scan) — 2026-07-29.
- **T110 SHIPPED** (Block K — the strip draws, and scales to, the 3 minutes it shows) — 2026-07-29.
- **T111 SHIPPED** (Block K — the live strip moves to a Throughput tab of its own) — 2026-07-29.
- **T112 SHIPPED** (Block K — percentile ceiling and broken-bar marks for outlier seconds) — 2026-07-29.
- **T113 SHIPPED** (Block M — the method note moves behind an info button and popup) — 2026-07-29.
- **T114 SHIPPED** (Block K — the rolling rate as a per-second series, plus sticky project slots) — 2026-07-29.
- **T115 SHIPPED** (Block K — a project's series stays on the chart until it ages out, not until it pauses) — 2026-07-29.
- **T116 SHIPPED** (Block K — two line charts of the rolling rate replace the stacked bars) — 2026-07-29.
- **T106 DROPPED** (Block K — spreading a turn over a guessed duration; the rolling rate answers it) — 2026-07-29.
- **T117 SHIPPED** (Block K — the chart's left edge stops oscillating: overscan, slide phase, smoothing warm-up) — 2026-07-29.
- **T118 SHIPPED** (Block K — a poll-driven refresh no longer blanks the whole window) — 2026-07-29.
- **T119 SHIPPED** (Block K — the chart appends instead of recomputing, so a late turn can't rewrite what was drawn) — 2026-07-29.
- **T120 SHIPPED** (Block N — System information page over the local Claude Code account) — 2026-07-31.
- **T122 SHIPPED** (Block O — a profile is a config dir; discovery and the picker) — 2026-07-31.
- **T123 SHIPPED** (Block O — the profile list editor and the per-profile Open Claude Code submenu) — 2026-07-31.
- **T124 SHIPPED** (Block O — per-profile auth reading, and the "not your subscription" warning) — 2026-07-31.
- **T125 SHIPPED** (Block O — every per-profile store keyed by profile, with the one-time migration) — 2026-07-31.
- **T127 SHIPPED** (Block O — a heartbeat per profile, the Profile submenu, and the icon's chosen profile) — 2026-07-31.
- **T128 SHIPPED** (Block O — Statistics reports on a chosen profile, transcripts included) — 2026-07-31.
- **T126 SHIPPED** (Block O — the icon follows the profile a turn just landed in) — 2026-07-31.
- **T135 SHIPPED** (Block Q — the WPF windows get keyboard input under the WinForms pump) — 2026-07-31.
- **T136 SHIPPED** (Block R — the default profile stops being described by a stub the tray created) — 2026-07-31.
- **T137 SHIPPED** (Block R — the menu re-reads the profiles when it opens, so a fresh login shows) — 2026-07-31.
- **T138 SHIPPED** (Block R — the Claude Code page gets a real "drives the icon" control) — 2026-07-31.
- **T140 SHIPPED** (Block R — no email in a label, and no two profiles sharing one) — 2026-07-31.
- **T139 SHIPPED** (Block R — a manual profile pick pins the icon until "Resume following") — 2026-07-31.
- **T143 SHIPPED** (Block R — a copyable `setx` command, never run by the tray itself) — 2026-07-31.
- **T141 SHIPPED** (Block S — the Settings copy is total by construction, so no field is reset on Save) — 2026-07-31.
- **T142 SHIPPED** (Block Q — `Check-Interaction.ps1`: the keyboard and the tray menu, driven and asserted) — 2026-07-31.
- **T144 SHIPPED** (Block T — the launch's config-dir decision is made against what the child inherits) — 2026-07-31.
- **T145 SHIPPED** (Block T — the tray writes the user-scope `CLAUDE_CONFIG_DIR` for a pinned profile) — 2026-07-31.
- **T146 SHIPPED** (Block T — "Open Claude Code" collapses back to one command while the profile is global) — 2026-07-31.
- **T147 SHIPPED** (Block T — a per-profile accent band on the tray icon says whose number it is) — 2026-07-31.
- **T148 SHIPPED** (Block T — the Profile submenu is populated at menu-open, so Right expands it) — 2026-07-31.
- **T129 SHIPPED** (Block P — the 30 non-UI sources move into `src/`, five folders) — 2026-07-31.
- **T131 SHIPPED** (Block P — installer, build/release scripts and winget manifests move into `build/`) — 2026-07-31.
- **T130 SHIPPED** (Block P — the four windows and `SettingsRow` move into `src/Ui/`) — 2026-07-31.
- **T132 SHIPPED** (Block P — `Program.cs` splits into `Main`, `src/Cli/*` and `Tray/TrayContext.cs`) — 2026-07-31.
- **T133 SHIPPED** (Block P — the two big code-behinds split into `partial` files per surface) — 2026-07-31.
