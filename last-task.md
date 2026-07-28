# Last task number — `T101` · next block letter — `L`

> **Single source of truth for the next free task number.** The next new task is `T102`; after
> assigning it, bump the number above and append a log line below.
>
> **Next block letter — `L`** (Block **K** = Live throughput — read the transcript tail instead of the
> rate-limit API, turn it into a rolling tokens/s that decays, draw it as motion whose moving axis is
> time, and attribute it per project; §V, created 2026-07-28).
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
- Block **K** (T97–T101, created 2026-07-28) is a *new* block rather than a third Block J pass: J is
  about the weekly **projection** (where the quota lands), K is about **instantaneous** throughput
  (what is running now, and in which project) on a clock that is local rather than the API's. It opens
  against the same Statistics window, so it carries no dependency on J's open tasks.
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
