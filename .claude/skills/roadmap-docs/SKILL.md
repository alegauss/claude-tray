---
name: roadmap-docs
description: How to maintain the Claude Code Tray roadmap/docs — the five root files (ROADMAP.md, CHANGELOG.md, IMPROVEMENTS.md, STRATEGY.md, last-task.md), their single-responsibility split, and the cross-file update rules. Also the one-task-one-commit rule and the user-facing-surface gate (README, docs/index.html, lang/*.json) that a shipped feature must pass. Use whenever adding a new task, marking a task shipped, editing any of those five docs, or picking the next T-number.
---

# Roadmap & docs maintenance

## ⛔ READ FIRST — one task, one commit (non-negotiable)

**You may NOT do more than one task before committing.** This is the single most violated rule, so it
is stated up front and it is absolute:

- **One task → one `run-commit.cmd`.** The moment a task is complete and validated, do the doc sync +
  `cd` to the repo root + `run-commit.cmd -m "<ascii conventional-commits title>"` **before touching
  the next task.** Finishing a task means *the commit landed* — code + `ROADMAP`/`CHANGELOG`/
  `last-task.md` sync in that one commit.
- **A multi-task request (e.g. "execute Phase 1 of Block I", a whole block, or a list of `T<n>`s) is
  NOT permission to batch.** It is a request to run tasks **one-at-a-time, committing after each.**
  Never implement task 2 while task 1 is uncommitted. A single giant diff spanning many tasks with one
  commit (or no commit) at the end is the failure this rule exists to prevent.
- **For any batch of ≥2 tasks you MUST drive it with the `/loop` skill** (self-paced): exactly one
  task per iteration, `run-commit.cmd` at the end of the iteration, then let the loop advance. Do not
  hand-roll a loop that defers commits.
- **Self-check before starting task N+1:** run `git status` / `git log -1`. If the previous task's work
  is not already committed, STOP and commit it first. If you are about to edit files for a new task
  and the working tree still shows the prior task's changes, you have already broken this rule —
  commit now.

The full commit + batch mechanics are rules 7–8 below.

---

The roadmap is **split across five files at the repo root** that must be kept in sync. Each has one
job — never duplicate content between them, and when you touch one, check whether a sibling needs
updating:

| File | Single responsibility | Granularity |
| --- | --- | --- |
| [`ROADMAP.md`](../../../ROADMAP.md) | **Task status** — the *only* source of truth for what's done/active. Active backlog only (📋 designed · 💭 idea · ⏳ partial · 🛠 in-progress). | one line per task |
| [`CHANGELOG.md`](../../../CHANGELOG.md) | What has **shipped** — a searchable index; `git log` is authoritative for detail. | one line per shipped task |
| [`IMPROVEMENTS.md`](../../../IMPROVEMENTS.md) | **Design rationale** (the what/why) for *unshipped* sections only, plus §I house constraints. No status tables, no shipped implementation reports. | prose per active section |
| [`STRATEGY.md`](../../../STRATEGY.md) | Positioning / licence / distribution / trust decisions. | prose |
| [`last-task.md`](../../../last-task.md) | The next free `T<n>`, the next block letter, structural notes, one-line-per-task log. | one line per task |

**Why the root and not `docs/`** (this is the one structural difference from the Turing repo, which
uses `docs/`): `docs/` here is the **published GitHub Pages site** (`index.html` + assets + `llms.txt`
+ `sitemap.xml`). Internal engineering docs do not go in a directory that is served to the public.

**Task numbering — the next free `T<n>` lives in [`last-task.md`](../../../last-task.md).** Read it
before adding a task, use `T<n+1>`, then bump the counter + append a log line. T-numbers are
**non-contiguous across blocks**, so never infer the next number from a block's header range or a
`git log` scan — that mis-numbers tasks and collides with already-shipped ones.

**`last-task.md` is a terse INDEX, not a memory.** It holds the counter, the next **block letter**,
the structural notes, and a **one-line-per-task log** — nothing more. Because the roadmap is
periodically pruned of fully-shipped blocks, **`CHANGELOG.md` (not `ROADMAP.md`) is authoritative for
the real maximum block letter** — grep it to confirm, then bump the letter when you create a block.

- **A log entry is exactly ONE line**, of the form
  `- **T<n> SHIPPED** (Block X — short title) — YYYY-MM-DD.` (truncate right at the date). The full
  implementation story — files, gotchas, measurements — goes in the **commit message** and the
  **`CHANGELOG.md` one-liner**, *never* here.
- **Do not turn a log entry into an implementation report.** Multi-sentence paragraphs with
  gotchas/decisions are the anti-pattern this file exists to avoid. A genuinely reusable gotcha
  belongs in auto-memory or [`AGENTS.md`](../../../AGENTS.md), not here.

**The cross-file update rules — follow these every time:**

1. **When a task ships:** move its one-liner from `ROADMAP.md` → `CHANGELOG.md` (under its block), and
   **delete** its detailed design subsection from `IMPROVEMENTS.md`. `git log` is the history — do
   **not** leave a shipped implementation report in `IMPROVEMENTS.md` or re-accrete one. **Then run
   the user-facing-surface gate** (below) — a shipped user-visible feature that never reaches the
   README, the site, or the other four languages is a bug.
2. **When you add a new task:** add the one-liner to `ROADMAP.md` (with `deps:` and a `→ §x.y`
   pointer) and, if it needs design, add the rationale subsection to `IMPROVEMENTS.md`. Status lives
   **only** in `ROADMAP.md` — don't put ✅/📋 markers inside `IMPROVEMENTS.md` prose.
3. **Status belongs to exactly one file.** If a status marker in `IMPROVEMENTS.md` disagrees with
   `ROADMAP.md`/`CHANGELOG.md`, the roadmap files win — fix or remove the stale marker.
4. **Keep entries terse.** A task line is *what + why + pointer* (~1 sentence). Implementation detail
   goes in code/commits, not the table cell. Never put multi-paragraph release notes in a table cell.
5. **Strategy ≠ backlog.** Licence, pricing, distribution channels, cross-platform and naming/brand
   discussion goes in `STRATEGY.md`, never as a numbered task.
6. **Non-goals are binding.** `ROADMAP.md` → "Non-goals" + `IMPROVEMENTS.md` §I.5 list things
   deliberately *not* to build — check them before proposing new work. The privacy promise
   (§I.1) and the zero-dependency rule (§I.3) are the two that get accidentally violated most.
7. **Commit the instant a task finishes — before starting the next (see the ⛔ block at the top).** A
   task is not "done" until `run-commit.cmd -m "<conventional-commits title>"` has landed. Do the doc
   sync (rules 1–2, bump `last-task.md`) **in the same commit** as the code so
   `ROADMAP`/`CHANGELOG`/`last-task.md` never drift from what actually shipped. `cd` to the repo root
   before running it, and keep the `-m` title ASCII. One commit per finished task — never one commit
   covering several tasks, never an uncommitted pile at the end.
   - `run-commit.cmd` **stages everything**, so check `git status` first: an unrelated stray file
     (an editor's `.vscode/settings.json`, a scratch script) will ride along into the commit.
   - `run-commit.cmd` is a **global command on the Windows PATH**, not a file in this repo. Searching
     the tree for it comes up empty, which is not a reason to fall back to a raw `git commit` — call
     it by name from the repo root (`where run-commit.cmd` confirms it).
8. **A batch of ≥2 tasks MUST run under `/loop` — mandatory, not a suggestion.** When the ask covers
   multiple tasks (a whole phase/block, or an explicit list of `T<n>`s), drive it with the `/loop`
   skill self-paced: **exactly one task per iteration, `run-commit.cmd` at the end of that iteration
   (rule 7), then advance.** Do not implement task 2 while task 1 is uncommitted, and do not
   hand-roll a loop that defers commits to the end. Only a genuinely single-task ask skips `/loop`.

## The user-facing-surface gate

There is no separate end-user docs repo here — the user-facing surfaces are **in this repo**, which
makes them easy to forget. **Every time a task ships, after the internal doc sync (rule 1), run this
decision:**

1. **Is it user-facing?** Would someone *using* the app (looking at the tray icon, opening a window,
   installing it) do something differently because this shipped? If **no** — it's internal
   engineering, a refactor, a dev-only CLI flag, or CI — it gets **no** README/site change. Say so in
   the commit message and stop. Don't invent thin docs for internal work. (`--context` in T66–T69 is
   exactly this case: a developer-facing CLI, deliberately undocumented for users until the window
   ships.)
2. **If yes, hit all three surfaces that apply:**
   - **`lang/*.json` — all five, not just `en`.** A new user-visible string must exist in `en`,
     `pt-BR`, `pt-PT`, `fr` and `es`. `{local:Loc key}` in XAML, `L.T(...)` in code. An English string
     hardcoded in XAML is a bug, and so is a key present only in `en`.
   - **`README.md`** — the feature list and, if there is something to see, a screenshot (use the
     `preview-ui` skill to produce it; screenshots live in `docs/`).
   - **`docs/index.html`** — the marketing site block, kept consistent with the README's wording.
3. **Write for the user, not the commit.** These surfaces explain *what the feature does and how to
   use it*. They are not a changelog or a design-rationale dump — never paste `IMPROVEMENTS.md`
   rationale verbatim.
4. **Verify UI by looking.** Any task that touches a window is not done without a screenshot — the
   `preview-ui` skill, per [AGENTS.md](../../../AGENTS.md). For a localized change, screenshot at
   least one non-English language too.
5. **Trademark discipline.** User-facing text keeps the unofficial framing: not affiliated with,
   endorsed by, or sponsored by Anthropic (see [STRATEGY.md](../../../STRATEGY.md) §I).

Unlike Turing, these surfaces are in the **same repo**, so they belong in the **same commit** as the
task (rule 7) — there is no second repo to commit separately.

## Release notes

A release is not a task. When the version is bumped, `<Version>` in `ClaudeTray.csproj` is the single
source of truth — the installer, winget manifests and the update check all derive from it (see
[STRATEGY.md](../../../STRATEGY.md) §IV). Cutting a release is a `chore: release vX.Y.Z` commit of its
own, never bundled with a task.
