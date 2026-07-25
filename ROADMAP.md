# ROADMAP — Claude Code Tray

Planned work, newest epic first. A task is done when its **Done when** line is true and the
change is committed (`run-commit.cmd -m "…"`). Keep this file honest: check boxes only after
verification, and delete tasks that stop making sense.

---

# Epic 1 — Context Load Inspector ("what every session costs you before you type")

## The problem

Claude Code loads things into context *before the first prompt*: the user `CLAUDE.md`, the
project `CLAUDE.md` / `AGENTS.md` chain, the `MEMORY.md` index of the file-based memory, and
the frontmatter description of every available skill. That is a fixed toll paid on **every
single request of the session**, because it sits in the cached prefix — and today it is
completely invisible. Nobody knows their number.

The tray app is already the thing that watches usage, already reads `~/.claude`, and already
knows per-model token pricing. It is the natural place to answer: *how much of my quota do I
burn just by opening a session in this project, and which files are worth keeping?*

Measured on one real developer machine (see [Appendix A](#appendix-a--measured-baseline)):
the worst project carries a **20 KB memory index** and **122 memory files (316 KB)**, has
**~6 index pointers that resolve to nothing**, and **duplicates its entire memory dir** with a
sibling worktree project. That is a concrete, fixable, recurring cost.

## The insight that makes this feature good

**Not everything is loaded.** Total-bytes-on-disk is the wrong number and would only scare
people. There are two very different buckets, and the whole UI hangs off this distinction:

| Bucket | What's in it | Cost |
|---|---|---|
| **Eager** — paid every session | user + project `CLAUDE.md`/`AGENTS.md` (and `@imports`), `MEMORY.md` index, every skill's *name + description*, agent descriptions | Real. Multiplies by every request in the session. |
| **Lazy** — paid only if used | memory file *bodies* (recalled on relevance), skill *bodies* (read on invoke), referenced `references/*.md` | Usually free. A 33 KB skill body you never trigger costs nothing. |

So a 316 KB memory dir may cost less per session than one bloated `CLAUDE.md`. Showing that
correctly is what turns this from a file browser into advice.

## Naming

Suggested over "Memories & Skills": the menu item is **`Context…`** and the window is
**"Context Load — memories, skills & instructions"**. Reasons: it names the *cost* rather than
the *folders*, it stays correct when Claude Code adds a fifth kind of context file, it fits the
app's existing vocabulary (usage / burn / projection), and it survives translation cleanly.
"Memories & Skills" works as the window subtitle. Decide before Task 1.4 — the tab/menu string
is the first thing that leaks into `lang/*.json`.

---

## Phase 1 — The scanner (headless, no UI)

Build and validate the whole model behind the CLI first, exactly like `--insights` did for
`UsageInsights`. No XAML until the numbers are trustworthy.

- [ ] **1.1 `ContextScanner.cs` — discover every context source, per project**
  - **Scope**: enumerate `~/.claude/projects/*` and resolve each slug back to a real filesystem
    path (`d--Git-foo-bar` → `d:\Git\foo\bar`); mark unresolvable ones as **orphans**. For each
    project collect: the user `~/.claude/CLAUDE.md`; the project `CLAUDE.md` + `AGENTS.md` chain
    including nested dirs and `@path` imports (follow one level, flag cycles); `memory/MEMORY.md`
    + `memory/*.md`; skills from `~/.claude/skills/`, `<project>/.claude/skills/` and
    `~/.claude/plugins/**/skills/`; `<project>/.claude/agents/*.md`; `settings.json` /
    `settings.local.json` sizes.
  - **Why**: every later task is a view over this one model. Get the shape right once.
  - **Note**: slug resolution is lossy (`-` vs path separator vs literal hyphen). Prefer probing
    candidate splits against the filesystem over trying to be clever; keep a `Resolved` bool.
  - **Done when**: `dotnet run -- --context` prints, per project, one line per source with
    kind / eager-or-lazy / bytes / estimated tokens / last-modified, and the totals match a
    manual `find`/`wc` spot check on two projects.

- [ ] **1.2 Token estimation you can defend**
  - **Scope**: a `TokenEstimate` helper (chars ÷ ~3.7 for markdown prose, with a correction for
    code fences and tables). Always render as an estimate ("≈4.9k"), never a fake-precise number.
  - **Why**: bytes are meaningless to a user reasoning about a 200k window and a 5h quota.
  - **Done when**: estimates land within ±15% of the calibration in 1.3 across ten sampled files.

- [ ] **1.3 "Session zero" calibration from real transcripts**
  - **Scope**: for each project, find the *first* assistant record of recent sessions in
    `~/.claude/projects/<slug>/*.jsonl` and read `usage.input_tokens + cache_creation_input_tokens`
    — that is the *observed* startup context (system prompt + tools + instructions + memory index).
    Show it beside the scanner's estimate, and derive the delta attributable to the app's own
    prompt so the estimate can be corrected.
  - **Why**: turns the whole feature from a guess into a measurement, using data
    `UsageInsights` already parses. Also gives the honest framing: *"opening a session here
    starts you at ~28k tokens, ≈$0.04 before you type"* (reuse `UsageInsights.Price`).
  - **Privacy**: usage counts and model ids only — never message content. Non-negotiable.
  - **Done when**: `--context` prints observed vs estimated session-zero per project, and the
    two track each other across at least five projects.

- [ ] **1.4 Bounded, cached scanning**
  - **Scope**: cache the scan in `%LocalAppData%\ClaudeTray`, invalidated by directory mtimes;
    skip transcript files older than the window using the `File.GetLastWriteTimeUtc` guard from
    `UsageInsights`; hard-cap total files walked and report when the cap truncated the scan.
  - **Why**: 35 projects × hundreds of `.jsonl` must never stall the tray's poll timer. And a
    silent cap that reads as "all clear" is worse than no feature.
  - **Done when**: a cold scan of the full `~/.claude` tree completes in <1.5s on the dev
    machine, a warm one in <150ms, and the tray icon never stops updating during a scan.

## Phase 2 — The window

Follow the UI rules in [AGENTS.md](AGENTS.md): layout in XAML, `ThemeMode="System"`,
`{DynamicResource}` Fluent brushes, `x:ClassModifier="internal"`, and **verify with a screenshot**
via the `preview-ui` skill.

- [ ] **2.1 `ContextWindow.xaml(.cs)` shell**
  - **Scope**: master/detail — project list on the left (name, grade, eager tokens), source
    detail on the right. Opened from the tray menu; add `--context` / `--context <slug>` to
    `Program.cs` so the window can be previewed standalone like `--settings`.
  - **Done when**: the window opens from the menu and from the CLI, and `docs\_preview\context.png`
    looks right in both light and dark.

- [ ] **2.2 The session-zero gauge (the hero element)**
  - **Scope**: one honest visual at the top: a bar of eager context against the 200k window,
    split by source, with the observed measurement overlaid as a tick. Caption: tokens, ≈cost
    per session start, and share of the context window.
  - **Why**: this single number is the reason someone opens the window. Everything else is detail.
  - **Done when**: the gauge renders for a healthy project, a bloated one, and a project with no
    memory at all (this repo — see 4.4) without layout breaking.

- [ ] **2.3 Source breakdown, sortable and actionable**
  - **Scope**: grouped rows (Instructions / Memory / Skills / Agents), each with kind badge,
    **Eager** or **Lazy** chip, size, tokens, last-modified, and a health dot. Row actions:
    reveal in Explorer, open in default editor. Sort by tokens or by age.
  - **Done when**: clicking a row opens the right file; sorting by tokens puts the 20 KB
    `MEMORY.md` first.

- [ ] **2.4 Cross-project overview**
  - **Scope**: an "All projects" view: total footprint, the ten heaviest eager loads, and the
    duplicate/orphan clusters found in 3.1.
  - **Why**: the expensive problems (duplication across worktree siblings, dead project dirs)
    are only visible *between* projects, never inside one.

## Phase 3 — The advisor (the "can this be improved?" part)

This is where the feature earns its place. Each finding = severity + one plain sentence + the
concrete fix. No finding without a fix.

- [ ] **3.1 Rule engine with real, grounded rules**
  Every rule below fires on the measured baseline, so none of them are hypothetical:
  | Rule | Severity | Fix offered |
  |---|---|---|
  | `MEMORY.md` index > ~8 KB | high — it is eager, every session | split by area / prune |
  | index pointer resolves to no file | medium | drop the line |
  | memory file with no pointer in the index | medium | add pointer or delete |
  | single memory file > 4 KB | low | "one memory = one fact" — split |
  | missing/invalid frontmatter (`name`, `description`, `type`) | medium | a description is what recall matches on; add it |
  | memory dir byte-identical to a sibling project's | high | one shared dir + junction, not two copies |
  | project dir whose real path no longer exists | medium | archive the whole dir |
  | memory untouched > 90 days | info | review queue |
  | `CLAUDE.md` > ~12 KB, or restating `AGENTS.md` | high | it is eager — trim or move detail into a skill |
  | skill `description` missing trigger words, or > ~500 chars | medium | descriptions are eager; names/descriptions are the index, bodies are not |
  | `settings.json` > ~40 KB | low | usually accumulated permissions; consolidate |
  - **Done when**: `--context --check` prints findings grouped by severity, and every rule has
    at least one true positive and no false positive on the dev machine.

- [ ] **3.2 Evidence, not opinion: was it ever actually used?**
  - **Scope**: mine the transcripts for `Skill` invocations and memory-recall markers over the
    last 30/90 days, and annotate each skill/memory with "used 12×" or **"never used"**.
  - **Why**: the highest-value feature in the epic. "Trim your memory" is nagging; "this skill
    has never been invoked in 90 days and its description costs you ~180 tokens every session"
    is a decision. Prune by evidence.
  - **Privacy**: tool/skill *names* and counts only — never arguments, never message content.
  - **Done when**: the window shows a usage count or "never" per skill and per memory file, and
    a spot check against a known-invoked skill (`preview-ui` in this repo) matches.

- [ ] **3.3 What-if simulator**
  - **Scope**: tick items to hypothetically remove; the session-zero gauge and the ≈cost update
    live. Nothing is written until "Apply".
  - **Why**: makes the payoff of cleanup visible *before* the risk of deleting anything.

- [ ] **3.4 Safe actions only**
  - **Scope**: reveal / open / **copy a ready-made cleanup prompt for Claude** ("Here are the 6
    stale pointers in MEMORY.md — fix the index"). If a destructive action ships at all, it moves
    files to `memory/.archive/<date>/` with a visible undo — never a silent delete, never a
    bulk delete.
  - **Why**: this app's whole trust model is read-only observation of `~/.claude`. Writing into
    a developer's memory is a different risk class; hand the edit to Claude instead of guessing.
  - **Done when**: the archive round-trips (archive → undo restores byte-identical files).

- [ ] **3.5 Context debt grade + drift**
  - **Scope**: an A–F grade per project from eager tokens + open findings. Track the eager total
    over time (same shape as `UsageHistory.cs`) and show "+2.1 KB this week" with a sparkline.
  - **Why**: bloat arrives one memory at a time; only the trend makes it noticeable.

- [ ] **3.6 Optional nudge, in the existing toast style**
  - **Scope**: when a project's eager context crosses a user-set threshold, one toast
    (`ToastWindow`), rate-limited to at most once a week per project. Default **off**; a checkbox
    in Settings → Notifications.
  - **Why**: the app already owns tasteful, color-coded toasts. Reuse, don't invent.

## Phase 4 — Ship it properly

- [ ] **4.1 Localize everything** — all new strings into `lang/en.json` first, then the other four
  (`pt-BR`, `pt-PT`, `fr`, `es`); `{local:Loc key}` in XAML, `L.T(...)` in code. 220 keys today —
  keep the `context.*` prefix in its own commented section.
  - **Done when**: `--context` renders correctly with `--settings` language set to `es` and `pt-BR`
    (screenshot both, per the existing i18n preview convention).
- [ ] **4.2 `--context-report <file.md>`** — write the findings as a markdown report. Paths and
  numbers only; no file contents. Useful to hand straight to Claude for the cleanup.
- [ ] **4.3 Live refresh** — a debounced `FileSystemWatcher` on `~/.claude` so the window updates
  while Claude Code writes memories, with the watcher disposed with the window.
- [ ] **4.4 Fixtures + dogfood** — this repo's own memory dir is empty, which makes it the
  zero-state test case. Add a `--context --sample` fixture set (healthy / bloated / orphaned) so
  the UI can be previewed and screenshotted without depending on the dev's real `~/.claude`.
- [ ] **4.5 Docs** — README section with a screenshot, a `docs/index.html` block, and an
  `AGENTS.md` file-map row for `ContextScanner.cs` / `ContextWindow.xaml`. Restate the privacy
  line explicitly: sizes, names, timestamps and token counts only; nothing leaves the machine.

---

## Appendix A — measured baseline

Taken from one real developer machine (35 project dirs, 17 with memory). Project names are
withheld deliberately — this file is published with the repo.

| Observation | Value | Rule it proves |
|---|---|---|
| Heaviest memory dir | 122 files / 316 KB | 3.1 size rules |
| `MEMORY.md` index (eager, every session) | 20 KB ≈ 5–6k tokens | 3.1 index size |
| Index lines vs actual files | 128 vs 122 | 3.1 stale pointers |
| Largest single memory file | 18 KB | 3.1 "one fact" |
| Memory dirs byte-identical to a sibling | 2 pairs | 3.1 duplication |
| Project dirs with an empty/dead memory dir | 4 | 3.1 orphans |
| `type:` distribution | 93 project / 12 feedback / 2 reference / 0 user | 3.1 frontmatter |
| Plugin skills available | 31 `SKILL.md`, largest 33 KB | Eager/lazy split — bodies are lazy, the 31 descriptions are not |
| `settings.json` | 89 KB | 3.1 settings bloat |

## Appendix B — non-goals

- **Not a memory editor.** Read, measure, advise, and hand the edit to Claude. (3.4 archive is
  the one deliberate exception.)
- **No content display or export.** Sizes, names, frontmatter, timestamps, token counts. The
  app's promise is that it never reads what you said to Claude, and this feature must not be
  the thing that breaks it.
- **No network.** The scan is local, like everything else in the app.
- **Not a Claude Code config manager.** Hooks, MCP servers and permissions are only ever
  *measured* here, never edited.
