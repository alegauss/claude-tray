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

## Block J — Activity-aware pacing ("the week doesn't burn at 4am")

> The weekly projection extrapolates the **average pace since the window opened**
> ([`UsageReport.Fill`](UsageReport.cs)) — a straight line that spends quota uniformly, including
> through the nights and weekends still ahead. It therefore lands the "you run out here" marker at
> times nobody is working (03:59 on a Friday), and it misreads any window whose *remaining* active/idle
> mix differs from its elapsed one — a partial day, an approaching weekend, a window that opened at
> 02:00. This block models **when** the user is actually active and projects along that shape instead.
> Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §IV.
>
> **Shipped: T86–T93.** The profile (`ActivityProfile.cs`, `--activity`), the staircase projection on
> the weekly chart, the permanent hourly aggregate behind it (`HourlyUsage.cs`), last week's ghost
> curve, the "stop now, resume at …" advice, the tray keeping the grid warm, the incremental sweep
> behind it, and the per-bucket blend toward the measured week. See
> [CHANGELOG.md](CHANGELOG.md) Block J.
>
> Headless: `--activity`, `--activity --measured`, `--activity --fold`; previews `--stats shape`,
> `--stats shape ghost`.
>
> **Second pass (T91–T96).** Building the block exposed four accuracy gaps and two mechanical ones.
> The accuracy work is ordered by how much it changes the projection: T93 first (it removes the
> limitation the UI currently has to disclaim), then T95, then T94.

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
- **No usage annotation on memory files** (settled by dropping T85). T75 annotates skills and agents
  because an invocation is a real tool call; a recall is the harness injecting a file into the
  conversation, and the app never reads message content (§I.1). The task was parked pending a
  structured signal, and the signal was then looked for rather than waited on: across 530 local
  transcripts (195,451 lines) the harness records four kinds of provenance — `attributionSkill`,
  `attributionAgent`, `attributionMcpServer`, `attributionMcpTool` — and sixteen attachment kinds
  including `skill_listing` and `agent_listing_delta`, and **none of them concerns a memory**. No
  `memor*`/`recall*` field exists at all; the only memory paths recorded are in
  `file-history-snapshot`, which tracks files Claude *wrote* — the opposite signal, and one that would
  flag the memories being maintained as the ones in use. So memory rows stay blank, and a wrong "never
  used" — the one error an advisor must not make — stays impossible by construction. Reopen only if a
  memory analogue of `attributionSkill` appears; the re-check is written down on `UsageEvidence`.
- **No live hint on the tray icon** (settled by dropping T101). An animating icon draws the eye
  continuously, and — decisively — it would need a transcript tail running for the whole session.
  T99 deliberately made the tail *window-owned* so a closed Statistics window watches nothing;
  a permanent tail to power an ambient nicety would undo the one property that keeps the feature
  free when nobody is looking. See [CHANGELOG.md](CHANGELOG.md) Block K.
- Pricing/distribution/positioning discussion goes in [STRATEGY.md](STRATEGY.md), never as a
  numbered task.
