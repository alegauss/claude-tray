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

## Block Z — What the app knows and doesn't say

> Block V ended by making the evidence behind the projection *correct* — a holiday no longer votes,
> in either grid. Reading the four commits back against the UI turned up the other half of the same
> idea: the app now knows how much evidence it has and never said so. The number on screen (T159) and
> the guard behind it that could not fire on an ordinary machine (T160) have both shipped; what remains
> is **coverage** over the two things most likely to produce the next such gap — both behind shipped
> defects already, neither with a single assertion — and one idea deliberately left as one.
> Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §XIII.

- 📋 **T162** (deps: —) **The Settings carry-over list is hand-maintained, and forgetting a line is a silent revert** — T141 made the *copy* total by construction, but `ApplySettings` then re-carries four tray-owned fields (`Metric`, the two `EnvironmentProfile*`, `MonitoredConfigDir`) one line at a time, and a field missing from that list is written back stale on every Save. That exact bug has shipped twice (T126, T155). Declare tray-owned once on `Settings`, carry by the declaration, assert the two agree. → §XIII.4
- 💭 **T163** (deps: —) **A straight-line projection never says why it isn't a shaped one** — when the profile is thin the app silently falls back to the average-pace line and the note reverts to generic text, so the one thing the user might act on (*keep the tray running another week and this gets better*) is the one thing never said. Needs design: what to say, where, and how not to turn a method note into a nag. → §XIII.5

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
- **No activity-aware tray notification** (settled by T87). The nudge threshold is the wall-clock
  verdict and stays that way: the shaped projection changed what the *chart* says, not what is worth
  interrupting somebody for, and a second, softer notification channel would need its own
  justification rather than inheriting this one's.
- **No live hint on the tray icon** (settled by dropping T101). An animating icon draws the eye
  continuously, and — decisively — it would need a transcript tail running for the whole session.
  T99 deliberately made the tail *window-owned* so a closed Statistics window watches nothing;
  a permanent tail to power an ambient nicety would undo the one property that keeps the feature
  free when nobody is looking. See [CHANGELOG.md](CHANGELOG.md) Block K.
- Pricing/distribution/positioning discussion goes in [STRATEGY.md](STRATEGY.md), never as a
  numbered task.
