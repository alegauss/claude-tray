# Last task number — `T85` · next block letter — `J`

> **Single source of truth for the next free task number.** The next new task is `T86`; after
> assigning it, bump the number above and append a log line below.
>
> **Next block letter — `J`** (Block **I** = Context Load Inspector — measure what every session
> costs before the first prompt: the eager/lazy split over the instruction chain, memory index and
> skill descriptions, a session-zero gauge calibrated against real transcripts, a grounded rule
> engine, and evidence-based pruning; §II, created 2026-07-25).
>
> **`CHANGELOG.md` — not `ROADMAP.md` — is authoritative for the real maximum block letter**, since
> the roadmap is periodically pruned of fully-shipped blocks. Grep it before bumping the letter.

## Structural notes

- Blocks **A–H** are retroactive: assigned when this ledger was introduced (2026-07-25) by reading
  the 110-commit history from v1.0.0 to v1.4.6, one task per shipped *unit of work* rather than one
  per commit. Their entries live only in [CHANGELOG.md](CHANGELOG.md) — there is no per-task log line
  below for them, and none should be back-filled.
- Block **I** is the first block numbered as it happens.
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
