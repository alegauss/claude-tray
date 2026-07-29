# Last task number — `T111` · next block letter — `M`

> **Single source of truth for the next free task number.** The next new task is `T112`; after
> assigning it, bump the number above and append a log line below.
>
> **Next block letter — `M`** (Block **L** = Scan resilience — no single unreadable directory under
> `~/.claude` may fail a whole scan; created 2026-07-29 from a field report).
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
- **T110–T111** (2026-07-29) stay in Block **K** rather than opening M: both are the live strip's own
  presentation — T110 its scale, T111 its own tab. T110 took the magnitude half of K's own T104, which
  remains open for the per-column hover.
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
