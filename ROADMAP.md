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

- 📋 **T86** (deps: —) **`ActivityProfile` — a weekly activity shape from the transcripts** — 168 buckets (day-of-week × local hour) holding `p(active)` over the last N weeks with recency decay, built from `~/.claude/projects` timestamps (`usage-history.jsonl` retains only 8 days, so it cannot see past weeks), cached in `%LocalAppData%\ClaudeTray\activity-profile.json` and recomputed ~daily. Headless `--activity` prints the grid. → §IV.1
- 📋 **T87** (deps: T86) **Staircase projection on the weekly chart** — spend the remaining quota weighted by `p_h` instead of uniformly: flat through projected-idle hours, sloped through active ones, with faint amber bands behind the projected-idle stretches. Falls back to today's straight line below ~3 weeks of coverage. Red stays reserved for "no reading" (`OutageBandBrush`); the grey even-pace line and the verdict chip are deliberately untouched. → §IV.2
- 💭 **T88** (deps: —) **Long-term hourly usage summary** — fold each pruned day of `usage-history.jsonl` into a permanent, tiny per-hour aggregate (~168 floats/week) before it is discarded, so idle can eventually be *measured* rather than inferred from transcripts, and so week-over-week comparison has a source. → §IV.3
- 💭 **T89** (deps: T88) **Ghost curve of the previous week** — overlay last week's burn-up faintly behind the current one, answering "is this week worse than the last?" without a second chart. → §IV.4
- 💭 **T90** (deps: T87) **"When to stop, when to resume"** — turn the projection into one actionable sentence ("stop now and resume tomorrow at 09:00 and you close the week at ~92%") instead of only a warning. Needs T87's shape to be trustworthy first. → §IV.5

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
- **Don't swap the UI stack.** WinForms owns the tray icon, WPF owns windows, both on one STA thread.
  No imperative `Dock=Top` stacking; no hardcoded hex for theme-able surfaces.
- **No second source of truth for the version** — `<Version>` in `ClaudeTray.csproj` only.
- Pricing/distribution/positioning discussion goes in [STRATEGY.md](STRATEGY.md), never as a
  numbered task.
