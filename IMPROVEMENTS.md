# Claude Code Tray — Design rationale (IMPROVEMENTS)

> The **what/why** behind *unshipped* work. Status lives **only** in [ROADMAP.md](ROADMAP.md) and
> [CHANGELOG.md](CHANGELOG.md) — never put ✅/📋 markers in this file's prose.
>
> **When a task ships, delete its design subsection here.** `git log` is the history. Letting
> shipped implementation reports accrete in this file is the single failure mode it exists to avoid.
>
> Sections are Roman-numbered and referenced from the roadmap as `→ §II.3`.

| § | Subject |
|---|---|
| [§I](#i--house-constraints) | House constraints (binding, app-wide) |
| [§II](#ii--context-load-inspector) | Context Load Inspector (Block I) |
| [§III](#iii--measured-baseline-context-load) | Measured baseline for the context feature |

---

## §I — House constraints

Binding for every task. These are product decisions, not preferences — a task that violates one is
wrong even if it works.

### §I.1 Privacy promise

The app reads only what Claude Code already stores locally, and from transcripts **only** usage
counts, model ids, flags, tool/skill *names* and the session `cwd` — **never message content**. This
is the app's whole trust model. Any new reader of `~/.claude` inherits it.

### §I.2 No network beyond two endpoints

The usage API (`ApiClient`) and GitHub Releases (`Updater`). Nothing else — no telemetry, no
analytics, no crash reporting. Every scan and every computation is local.

### §I.3 Zero extra dependencies

The single self-contained `.exe` + installer story depends on the app having no third-party
packages. WPF's built-in Fluent theme, GDI+ and `System.Text.Json` are the toolkit. A task that
wants a NuGet package needs to justify breaking this first (see §I.5).

### §I.4 Read-only against `~/.claude`

The app observes; it does not manage Claude Code. It never edits `CLAUDE.md`, memories, skills,
hooks, MCP servers or permissions.

**Settled with T77: there is no exception.** The archive-with-undo that was under design (move files
to `memory/.archive/<date>/`) was deliberately *not* built. What shipped instead is a generated
cleanup prompt: the app writes down what it measured — paths, sizes, token estimates — and hands that
to Claude Code, which can read the files and decide. Measuring is a read; rewriting somebody's memory
directory on a size heuristic is a different risk class, and the tool that can actually read the file
should be the one to touch it. Revisit only with a concrete reason the copy-a-prompt path cannot
cover.

### §I.5 What NOT to build (binding non-goals)

- **No tokenizer dependency.** Token counts stay estimates with a visible "≈" (§II.0.3). A real
  tokenizer would mean a native/managed dependency and break §I.3.
- **Not a memory editor / not a config manager.** Measure and advise; hand the edit to Claude.
- **No content display or export**, anywhere, ever — sizes, names, frontmatter, timestamps, counts.
- **Don't swap the UI stack.** WinForms owns the tray icon (WPF has no native tray support); WPF owns
  windows. Both live on one STA thread pumped by `Application.Run(new TrayContext())`. This is
  settled — see [AGENTS.md](AGENTS.md).
- **Don't stack imperative layout.** Layout is declarative XAML with explicit rows/columns; the
  original WinForms sidebar overlapped precisely because it relied on reverse `Dock=Top` add-order.
- **No hardcoded hex for theme-able surfaces** — `{DynamicResource}` Fluent brushes only, so light,
  dark and the system accent all follow for free.
- **No second source of truth for the version.** `<Version>` in `ClaudeTray.csproj`; everything else
  derives from it.

---

## §II — Context Load Inspector

**The problem.** Claude Code loads things into context *before the first prompt*: the user
`CLAUDE.md`, the project `CLAUDE.md` / `AGENTS.md` chain, the `MEMORY.md` index of the file-based
memory, and the frontmatter description of every available skill. That is a fixed toll paid on
**every single request of the session**, because it sits in the cached prefix — and today it is
completely invisible. Nobody knows their number.

The tray app is already the thing that watches usage, already reads `~/.claude`, and already knows
per-model token pricing. It is the natural place to answer: *how much of my quota do I burn just by
opening a session in this project, and which files are worth keeping?*

**The insight the whole UI hangs off.** Not everything is loaded. Total-bytes-on-disk is the wrong
number and would only scare people:

| Bucket | What's in it | Cost |
|---|---|---|
| **Eager** — paid every session | user + project `CLAUDE.md`/`AGENTS.md` (and `@imports`), `MEMORY.md` index, every skill's *name + description*, agent descriptions | Real. Multiplies by every request in the session. |
| **Lazy** — paid only if used | memory file *bodies* (recalled on relevance), skill *bodies* (read on invoke), referenced `references/*.md` | Usually free. A 33 KB skill body you never trigger costs nothing. |
| **Not loaded** — measured only | `settings.json` / `settings.local.json` | Never in the prompt. Its size is a symptom (accumulated permissions), not a cost. |

So a 316 KB memory dir may cost less per session than one bloated `CLAUDE.md`. Showing that
correctly is what turns this from a file browser into advice.

### §II.0 What Phase 1 measured, and what it constrains

Three findings from the shipped scanner (Block I, T66–T69) bind the UI design:

1. **The invisible base overhead dominates.** ≈32k tokens (p25 30k, p75 34k) of Claude Code's own
   system prompt + tool definitions + MCP schemas, against only ≈4k–22k of *scannable* instructions
   across 33 real projects. A total that omits it makes a bloated project look like the whole
   problem when it is a third of it. **Base and scannable stay separate wherever either is shown** —
   in the gauge, in a cross-project total, and in a grade.
2. **Observed session zero needs all three usage terms** — `input_tokens +
   cache_creation_input_tokens + cache_read_input_tokens`. The stable prefix is shared *between*
   sessions, so a session opened while an earlier one's cache is alive reports most of its startup
   as a cache *read*. Counting only the creation side made 21 of 33 projects look like they had no
   measurable startup at all.
3. **The estimate is good but not tunable by that fit.** Corrected estimate within ±15% for 23/27
   projects, median error 6.2%. A uniform change to the chars-per-token divisors is absorbed by the
   fitted base, so the calibration cannot pick them; Theil–Sen over project pairs suggests
   instruction-heavy projects are under-estimated by ~20%. **Every displayed number stays an
   estimate with a visible "≈".**

### §II.12 Markdown report

`--context-report <file.md>` writes the findings as markdown — paths and numbers only, no file
contents (§I.1). Useful to hand straight to Claude for the cleanup, and the natural companion to the
copy-a-prompt action shipped in T77.

### §II.13 Live refresh

A debounced `FileSystemWatcher` on `~/.claude` so the window updates while Claude Code writes
memories, with the watcher disposed with the window. The scan is already cheap enough (76ms warm) for
this to be a re-scan rather than an incremental update.

### §II.14 Fixtures + dogfood

This repo's own memory dir is empty, which makes it the zero-state test case. A `--context --sample`
fixture set (healthy / bloated / orphaned) lets the UI be previewed and screenshotted without
depending on the dev's real `~/.claude`. Phase 1 already added the seam: `--context --root <dir>`
points the scan at any tree, and was used to prove the import/cycle/orphan paths.

### §II.15 Docs

README section with a screenshot, a `docs/index.html` block, and an [AGENTS.md](AGENTS.md) file-map
row for `ContextWindow.xaml`. Restate the privacy line explicitly: sizes, names, timestamps and
token counts only; nothing leaves the machine.

---

## §III — Measured baseline (context load)

Taken from one real developer machine (33 project dirs, 19 with memory). Project names are withheld
deliberately — this file is published with the repo. The right-hand column is the rule (now shipped in
`ContextRules.cs`) the observation justifies.

| Observation | Value | Rule it proves |
|---|---|---|
| Heaviest memory dir | 122 files / 316 KB | size rules |
| `MEMORY.md` index (eager, every session) | 20 KB ≈ 5.4k tokens | index size |
| Index pointers vs memory files | 121 pointers / 122 files, none dead (the extra index *lines* are headers); one unindexed memory in another project | stale pointers / orphan memories |
| Largest single memory file | 18 KB | "one fact" |
| Memory dirs byte-identical to a sibling | 2 pairs | duplication |
| Project dirs whose path no longer exists | 2 (+1 that is not a path at all) | orphans |
| `type:` distribution | 93 project / 12 feedback / 2 reference / 0 user | frontmatter |
| Plugin skills available | 31 `SKILL.md`, largest 33 KB, ≈3.1k tokens of eager descriptions | the eager/lazy split — bodies are lazy, the 31 descriptions are not |
| `settings.json` | 87 KB | settings bloat |
| Base overhead (system prompt + tools + MCP) | ≈32k tokens, p25 30k / p75 34k | §II.0.1 — the gauge must show it |
| Heaviest scannable eager load | ≈22k tokens (a 43 KB `AGENTS.md` + 20 KB index) | the hero number the gauge shows |
