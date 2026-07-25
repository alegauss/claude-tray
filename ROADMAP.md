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

## Block I — Context Load Inspector ("what every session costs you before you type") (§II)

> Claude Code loads the user `CLAUDE.md`, the project `CLAUDE.md`/`AGENTS.md` chain, the `MEMORY.md`
> index and every skill's description **before the first prompt** — a fixed toll paid on every
> request of the session, today completely invisible. This app already watches usage, already reads
> `~/.claude` and already knows per-model pricing, so it is the natural place to show the number and
> advise on it. Design in [IMPROVEMENTS.md](IMPROVEMENTS.md) §II; measured baseline §III.
>
> **T66–T75 shipped** — the whole window and the advisor's engine: the headless scanner behind
> `--context`, the window with its session-zero gauge, source table, cross-project overview and
> what-if simulator, the rule engine behind `--context --check`, the evidence pass behind
> `--context --usage`, the cleanup prompt behind `--context --prompt`, the A-F debt grade with its
> drift history, and the opt-in growth nudge; see
> [CHANGELOG.md](CHANGELOG.md) Block I). Three findings from Phase 1 bind the rest of the block and
> are written up in §II.0: the **≈32k base overhead** no
> scan can see is its own segment of the gauge, observed session zero needs **all three** usage terms,
> and every displayed token count stays an estimate with a visible "≈".
>
> The window is `Context…` in the tray menu, "Context Load — memories, skills & instructions" as the
> title, and previews standalone with `--context --window [slug]` (the bare `--context` stays the
> headless report).

- 💭 **T85** (deps: —) **Usage evidence for memory files — only if a structured signal appears** — T75 covers skills and agents, where an invocation is a real tool call in the transcript. A memory recall has no such record: the harness injects it into the conversation, so the only trace is message content, which the app never reads (§I.1). Annotating memories would therefore mean guessing, and a wrong "never used" is the one error an advisor must not make. Revisit only if Claude Code starts recording recalls as structured metadata.

**Ship it properly:**
- 📋 **T81** (deps: T74) **`--context-report <file.md>`** — write the findings as a markdown report, paths and numbers only, no file contents. The natural companion to T77's copy-a-prompt. → §II.12
- 📋 **T82** (deps: T70) **Live refresh** — a debounced `FileSystemWatcher` on `~/.claude` so the window updates while Claude Code writes memories, disposed with the window. A warm re-scan is 76ms, so this can be a re-scan rather than an incremental update. → §II.13
- 📋 **T83** (deps: T70) **Fixtures + dogfood** — a `--context --sample` fixture set (healthy / bloated / orphaned) so the UI can be previewed and screenshotted without depending on the dev's real `~/.claude`; this repo's empty memory dir is the zero state. The seam exists already (`--context --root <dir>`). → §II.14
- 📋 **T84** (deps: T70, T71, T72) **Docs** — README section with a screenshot, a `docs/index.html` block, and the privacy line restated: sizes, names, timestamps and token counts only; nothing leaves the machine. (The [AGENTS.md](AGENTS.md) file-map row shipped with T70, since a stale file map misleads the next session.) → §II.15

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
