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
> **T66–T69 shipped** (Phase 1 — the headless scanner behind `--context`; see
> [CHANGELOG.md](CHANGELOG.md) Block I). Three findings from it bind the rest of the block and are
> written up in §II.0: the **≈32k base overhead** no scan can see must be a separate segment of the
> gauge, observed session zero needs **all three** usage terms, and every displayed token count stays
> an estimate with a visible "≈".

**The window (needs the scanner — T66–T69, all shipped):**
- 📋 **T70** (deps: —) **`ContextWindow.xaml(.cs)` shell** — master/detail: project list left (name, grade, eager tokens), source detail right; opened from the tray menu and previewable standalone via `--context` / `--context <slug>` like `--settings`, so the `preview-ui` loop applies unchanged. Settles the **naming decision** (`Context…` vs "Memories & Skills") because it is the first user-visible string and the first `lang/*.json` key. → §II.1
- 📋 **T71** (deps: T70) **The session-zero gauge (the hero element)** — one honest bar of eager context against the 200k window, split by source, with the observed measurement overlaid as a tick and the ≈32k base overhead as its own segment (§II.0.1); caption = tokens, ≈cost per session start, share of the window. Must render for a healthy project, a bloated one, and a zero-memory project (this repo). → §II.2
- 📋 **T72** (deps: T70) **Source breakdown, sortable and actionable** — grouped rows (Instructions / Memory / Skills / Agents) with kind badge, Eager/Lazy/— chip, size, tokens, last-modified, health dot; a skill's chip must say *both* that its body is lazy and its description eager. Row actions: reveal in Explorer, open in editor. Sort by tokens or age. → §II.3
- 📋 **T73** (deps: T70, T74) **Cross-project overview** — an "All projects" view: total footprint, the ten heaviest eager loads, and the duplicate/orphan clusters from the rule engine. The expensive problems are only visible *between* projects. → §II.4

**The advisor (where the feature earns its place — no finding without a fix):**
- 📋 **T74** (deps: —) **Rule engine with real, grounded rules** — the 11 rules in §II.5 (oversized eager index, stale/orphan index pointers, duplicated memory dirs, dead project dirs, missing frontmatter, bloated `CLAUDE.md`, weak skill descriptions, `settings.json` bloat…), each = severity + one plain sentence + the concrete fix. Every rule fires on the §III baseline, so none are hypothetical. Ships headless first as `--context --check`, grouped by severity, with at least one true positive and no false positive on the dev machine. → §II.5
- 📋 **T75** (deps: —) **Evidence: was it ever actually used?** — mine transcripts for `Skill` invocations and memory-recall markers over 30/90 days and annotate each skill/memory "used 12×" or **"never used"**. The highest-value idea in the block: *"never invoked in 90 days and its description costs you ~180 tokens every session"* is a decision, not nagging. Names and counts only — never arguments, never content. → §II.6
- 📋 **T76** (deps: T71, T74) **What-if simulator** — tick items to hypothetically remove; the gauge and ≈cost update live; nothing is written until "Apply". Makes the payoff visible before the risk. → §II.7
- 📋 **T77** (deps: T74) **Safe actions only** — reveal / open / **copy a ready-made cleanup prompt for Claude**. If a destructive action ships at all it moves files to `memory/.archive/<date>/` with a visible undo that round-trips byte-identically — never a silent or bulk delete. → §II.8
- 💭 **T78** (deps: T74) **Context debt grade + drift** — an A–F grade per project from eager tokens + open findings, and the eager total tracked over time (same shape as `UsageHistory.cs`) with "+2.1 KB this week" and a sparkline. Bloat arrives one memory at a time; only the trend makes it noticeable. → §II.9
- 💭 **T79** (deps: T71) **Optional nudge, in the existing toast style** — one `ToastWindow` when a project's eager context crosses a user-set threshold, rate-limited to once a week per project, default **off**, checkbox in Settings → Notifications. → §II.10

**Ship it properly:**
- 📋 **T80** (deps: T70, T71, T72) **Localize everything** — all new strings into `lang/en.json` first, then `pt-BR`, `pt-PT`, `fr`, `es`; `context.*` prefix in its own commented section. Done when the window renders correctly with the language set to `es` and `pt-BR` (screenshot both, per the existing i18n preview convention). → §II.11
- 📋 **T81** (deps: T74) **`--context-report <file.md>`** — write the findings as a markdown report, paths and numbers only, no file contents. The natural companion to T77's copy-a-prompt. → §II.12
- 📋 **T82** (deps: T70) **Live refresh** — a debounced `FileSystemWatcher` on `~/.claude` so the window updates while Claude Code writes memories, disposed with the window. A warm re-scan is 76ms, so this can be a re-scan rather than an incremental update. → §II.13
- 📋 **T83** (deps: T70) **Fixtures + dogfood** — a `--context --sample` fixture set (healthy / bloated / orphaned) so the UI can be previewed and screenshotted without depending on the dev's real `~/.claude`; this repo's empty memory dir is the zero state. The seam exists already (`--context --root <dir>`). → §II.14
- 📋 **T84** (deps: T70, T71, T72) **Docs** — README section with a screenshot, a `docs/index.html` block, an [AGENTS.md](AGENTS.md) file-map row for `ContextWindow.xaml`, and the privacy line restated: sizes, names, timestamps and token counts only; nothing leaves the machine. → §II.15

## Non-goals (do NOT add as tasks)

Binding constraints — see [IMPROVEMENTS.md](IMPROVEMENTS.md) §I for the full text. Summary:

- **No tokenizer dependency**, and no third-party NuGet package in general — the single
  self-contained `.exe` + installer story depends on it. Token counts stay estimates with a visible "≈".
- **Never read message content** from transcripts. Usage counts, model ids, flags, tool/skill *names*
  and the session `cwd` only. No content display or export anywhere in the app.
- **No network** beyond the usage API and GitHub Releases. No telemetry, analytics or crash reporting.
- **Not a memory editor, not a Claude Code config manager.** Hooks, MCP servers, permissions and
  instruction files are *measured*, never edited — measure, advise, hand the edit to Claude. The one
  sanctioned exception under design is the archive-with-undo in T77.
- **Don't swap the UI stack.** WinForms owns the tray icon, WPF owns windows, both on one STA thread.
  No imperative `Dock=Top` stacking; no hardcoded hex for theme-able surfaces.
- **No second source of truth for the version** — `<Version>` in `ClaudeTray.csproj` only.
- Pricing/distribution/positioning discussion goes in [STRATEGY.md](STRATEGY.md), never as a
  numbered task.
