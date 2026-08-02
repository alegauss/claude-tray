---
name: roadmap-docs
description: Claude Code Tray's own shipping discipline — the one-task-one-commit rule, the user-facing-surface gate (README, docs/index.html, lang/*.json) a shipped feature must pass, and how a release is cut. The roadmap/changelog/rationale write path itself is NOT here: roadkeep owns it, and its own skill says which command to call. Use whenever a task is finished, before committing, or when a shipped feature might be user-visible.
---

# Shipping discipline

**The write path is not in this file.** `ROADMAP.md`, `CHANGELOG.md`, `IMPROVEMENTS.md` and
`STRATEGY.md` are written by **roadkeep**, configured in [`roadkeep.toml`](../../../roadkeep.toml):
the fields are refused at insertion, the id and the `(deps: … ✅)` annotation are derived, and a
hand-edit is denied by the hook. Which command to call — `add`, `ship`, `amend`, `status`, `pick`,
`brief`, `non-goal` — is the **`roadkeep` skill**, which ships with the tool and is the same text in
every project that adopted it. A rule stated in two files is a rule two files can disagree about, so
nothing here repeats it.

What this file holds is what roadkeep has no opinion about: **when** a commit happens, and what a
shipped feature owes the user.

## ⛔ One task, one commit (non-negotiable)

**You may NOT do more than one task before committing.** This is the single most violated rule here:

- **One task → one `run-commit.cmd`.** The moment a task is complete and validated, run
  `roadkeep ship <id>` and commit — code and docs in that one commit — **before touching the next
  task.** Finishing a task means *the commit landed*.
- **A multi-task request** (a whole block, "execute Phase 1", a list of `T<n>`s) **is not permission
  to batch.** It is a request to run tasks one at a time, committing after each. A single giant diff
  spanning many tasks is the failure this rule exists to prevent.
- **For any batch of ≥2 tasks, drive it with `/loop`** (self-paced): exactly one task per iteration,
  `run-commit.cmd` at the end of the iteration, then let the loop advance. Do not hand-roll a loop
  that defers commits.
- **Self-check before starting task N+1:** run `git status` / `git log -1`. If the previous task's
  work is not committed, stop and commit it first.
- `run-commit.cmd -m "<ascii conventional-commits title>"` from the repo root. **`-m` always**, and
  ASCII. It **stages everything**, so check `git status` first — a stray scratch file rides along.
  It is a **global command on the Windows PATH**, not a file in this repo; `where run-commit.cmd`
  confirms it, and not finding it in the tree is never a reason to fall back to raw `git commit`.

## The user-facing-surface gate

There is no separate end-user docs repo — the user-facing surfaces are **in this repo**, which makes
them easy to forget. **Every time a task ships, run this decision:**

1. **Is it user-facing?** Would somebody *using* the app do something differently because this
   shipped? If **no** — internal engineering, a refactor, a dev-only CLI flag, CI — it gets **no**
   README or site change. Say so in the commit message and stop. Don't invent thin docs for internal
   work.
2. **If yes, hit all three surfaces that apply:**
   - **`lang/*.json` — all five, not just `en`.** A new user-visible string must exist in `en`,
     `pt-BR`, `pt-PT`, `fr` and `es`. `{local:Loc key}` in XAML, `L.T(...)` in code. An English
     string hardcoded in XAML is a bug, and so is a key present only in `en`.
   - **`README.md`** — the feature list and, if there is something to see, a screenshot (the
     `preview-ui` skill produces it; screenshots live in `docs/`).
   - **`docs/index.html`** — the marketing block, kept consistent with the README's wording.
3. **Write for the user, not the commit.** These surfaces explain what the feature does and how to
   use it. Never paste `IMPROVEMENTS.md` rationale verbatim.
4. **Verify UI by looking.** A task that touches a window is not done without a screenshot — the
   `preview-ui` skill, per [AGENTS.md](../../../AGENTS.md). For a localized change, screenshot at
   least one non-English language too.
5. **Trademark discipline.** User-facing text keeps the unofficial framing: not affiliated with,
   endorsed by, or sponsored by Anthropic (see [STRATEGY.md](../../../STRATEGY.md) §I).

These surfaces are in the same repo, so they belong in the **same commit** as the task.

## Block letters

roadkeep derives the id (`roadkeep next-id`, and `add` mints it) but has no opinion about **block
letters**. Z was the last single letter, so the scheme continues **AA, AB, …**.
**[`CHANGELOG.md`](../../../CHANGELOG.md), not `ROADMAP.md`, is authoritative for the real maximum
letter** — the roadmap is periodically pruned of fully-shipped blocks. Grep it before opening a
block, and record the new letter in [`last-task.md`](../../../last-task.md).

## Release notes

A release is not a task. When the version is bumped, `<Version>` in `ClaudeTray.csproj` is the single
source of truth — the installer, winget manifests and the update check all derive from it (see
[STRATEGY.md](../../../STRATEGY.md) §IV). Cutting a release is a `chore: release vX.Y.Z` commit of
its own, never bundled with a task.
