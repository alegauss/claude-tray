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
> **Shipped: T86–T92.** The profile (`ActivityProfile.cs`, `--activity`), the staircase projection on
> the weekly chart, the permanent hourly aggregate behind it (`HourlyUsage.cs`), last week's ghost
> curve, the "stop now, resume at …" advice, the tray keeping the grid warm, and the incremental
> sweep behind it. See [CHANGELOG.md](CHANGELOG.md) Block J.
>
> Headless: `--activity`, `--activity --measured`, `--activity --fold`; previews `--stats shape`,
> `--stats shape ghost`.
>
> **Second pass (T91–T96).** Building the block exposed four accuracy gaps and two mechanical ones.
> The accuracy work is ordered by how much it changes the projection: T93 first (it removes the
> limitation the UI currently has to disclaim), then T95, then T94.

- 📋 **T93** (deps: T88) **Prefer the measured grid once there are ~3 weeks of it** — `HourlyUsage` sees *all* usage against the limit, including from another machine or claude.ai; the transcript grid structurally cannot, which is the limitation the method note has to disclaim today. Blend toward measured as its coverage grows, keeping the transcript grid to bootstrap the first weeks. → §IV.9
- 📋 **T94** (deps: T93) **Intensity, not just presence** — every active hour is currently paced at the same rate, so a heavy Monday morning and a light Friday evening spend identically. Add a per-bucket intensity (mean spend per active hour, relative to the average) from the measured store and weight the projection by it. → §IV.10
- 📋 **T95** (deps: —) **Don't let a holiday teach the model** — a week off currently votes "these hours are idle" like any other week; only the flat prior softens it. Drop weeks whose total activity is a small fraction of the median week from the denominator, and say so in `--activity`. → §IV.11
- 💭 **T96** (deps: —) **`--selftest` for the pacing *and* live math** — Block J added arithmetic with real edge cases (a flat profile must reproduce the straight line exactly, folding must stay idempotent, advice must never exceed its target, the ghost must stay hidden under its gates) and the repo has no test surface at all. **Block K doubled the surface**: the tail's cursor (a partial line waits for its newline, a shrunk file resets without re-reporting, a primed offset aligns) and the rate's kernel (sustained R reads as R, one burst decays linearly to a true zero at W, the smoothed value never exceeds the weighted one, per-project rates sum to the headline) are all verified only by hand against synthetic roots today. A deterministic self-check over synthetic inputs, in-app, keeps the zero-dependency rule intact. → §IV.12

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
  free when nobody is looking. See [CHANGELOG.md](CHANGELOG.md) Block K.
- Pricing/distribution/positioning discussion goes in [STRATEGY.md](STRATEGY.md), never as a
  numbered task.
