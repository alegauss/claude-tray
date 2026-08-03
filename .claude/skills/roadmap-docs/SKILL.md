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

**First check that the tool is actually here.** The plugin install is **per repo**, so a roadkeep
installed for another project supplies this one with no `mcp__roadkeep__*` tools, no `roadkeep`
skill and — the one that matters — **no hook**: nothing denies a hand-edit to a governed file and
nothing runs `lint` at the turn's end. `roadkeep` on PATH or `python -m roadkeep.cli <command>` is
the fallback **when the package is importable at all** (a checkout or a pip install — the plugin
cache is not on `sys.path`); check with `python -c "import roadkeep"` before assuming it. Either way
the discipline is then yours to keep: never hand-edit the four files, and **run `roadkeep lint`
yourself before committing**, because nothing else will.

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

## A block is a theme, and a theme is reused

**Reuse a block. Do not open one per batch of work.** The ledger's own column is headed *Theme*: a
block names a **capability of this app** — the context inspector, the profiles, the pacing, the checks
— and every task about that capability files under it, whenever it is found. That is how roadkeep's
own backlog runs on six blocks and Shio's on sixteen, both years old, while this one reached **AH**:
`AB`, `AD`, `AF` and `AH` are each literally *"what the previous block turned up"*, which is a batch,
not a theme. Those headings are history in the ledger and cannot be renamed — they are **not** the
pattern to copy. Before `roadkeep add` the question is *which theme is this*, never *which letter is
next*.

| Theme | Block | What files under it |
|---|---|---|
| Tray icon, tooltip, the poll loop, the projection | **A** | `IconRenderer`, `TrayContext`, `BurnTracker` |
| What the icon and the menu are allowed to show | **H** | the tray display options |
| Packaging, installer, self-update, CI | **B** | `build\`, `.github\workflows\`, `Updater` |
| The Settings page, and what a control claims to change | **C** | `SettingsPage.*`, `Settings`, `[TrayOwned]` |
| Auth, the API, and what a quota header means | **D** | `ApiClient`, `HeaderProbe`, `QuotaState`, extra usage |
| Notifications and toasts | **E** | `ToastWindow`, `ContextNudges` |
| The Statistics page and the pace report | **F** | `StatisticsPage.*`, `UsageReport`, `UsageHistory` |
| Localization | **G** | `lang\*.json`, `Localization` |
| The Context Load Inspector | **I** | `src\Context\*`, `ContextPage` |
| Activity-aware pacing | **J** | `ActivityProfile`, `ActivityShape`, `HourlyUsage` |
| Live throughput | **K** | `LiveRate`, `LiveChart`, `TranscriptTail` |
| System information — this machine's install | **N** | `ClaudeAccount`, the System page |
| Profiles — several logins on one machine | **O** | `src\Profiles\*`, `ProfileStore`, `EnvironmentProfile` |
| Repo layout — where a file lives | **P** | the `src\` folders, `build\`, `scripts\` |
| Input, focus, and being readable | **Q** | `WpfInputBridge`, keyboard, UI Automation names |
| The window shell and its navigation | **Y** | `MainWindow`, page hosting, `PageWindow` |
| Verification — the checks that prove a change | **AI** | `--selftest`, `Check-Interaction.ps1`, previews, captures, fixtures |
| Working here — the repo's own docs and flags | **AJ** | `AGENTS.md`, the skills, `dev-flags`, `file-map`, the budgets |

Verification and the repo's own docs are this project's two most active themes and had no row of their
own, which is the gap the batch letters were filling. **Both letters are now declared** — T202 was
`AI`'s first task to ship and T219 was `AJ`'s — so every check, capture or fixture task files under
`AI`, and every task about this repo's own docs, skills, flags or budgets under `AJ`.

**A block empties; it does not close.** `roadkeep pick --block I` answering *"nothing is open in Block
I"* means that theme is quiet today, not finished — the next context task reopens it. Two consequences,
so neither arrives as a surprising refusal. The roadmap is pruned of fully-shipped blocks, so a reused
letter usually has **no roadmap heading** and `add --block I` refuses with *"no heading declares Block
I"*: `roadkeep block add I --title "<the title the ledger already uses>"` then writes it into the
roadmap **alone**, skipping the ledger that already declares it — so copy that title **verbatim** from
`CHANGELOG.md`'s block table, because nothing checks that two files spell one block the same way. And
`IMPROVEMENTS.md` numbers its sections in one `§I…` sequence that never reuses a number, so a reused
block accumulates several `§` sections over its life; that is expected, and `section add` places each
one under the task's own block.

**A new letter is only for a theme the table has no row for** — and then it is named for the
**capability**, never for the batch that found it or the block it came out of: *"Verification — the
checks that prove a change"*, not *"What Block AF's own captures turned up"*. Z was the last single
letter, so the scheme continues **AA, AB, …**, and **[`CHANGELOG.md`](../../../CHANGELOG.md), not
`ROADMAP.md`, is authoritative for the real maximum letter** — grep it before opening one. **Add the
row to the table above in the same commit**, or the next task has nothing to reuse and the drift starts
again.

**Declare the block in the ledger before shipping its first task.** `roadkeep ship` refuses with
*"no heading declares Block X"* and writes nothing: naming a block is editorial, so no other write
invents the heading. `roadkeep block add X --title "<title>"` is the one that does, in every governed
file organised by blocks at once — so the heading itself is never hand-typed. What it does **not**
write is the row in `CHANGELOG.md`'s block table: add that by hand in the same commit as the first
task, with the same title, marked `(active — see ROADMAP)` while tasks remain open. That row is the
**one** hand-edit to a governed file the discipline allows, and `roadkeep lint` must pass after.

**Forgetting it is now a red build (T223).** `--selftest` reads the ledger and asserts both directions —
a heading with no row, a row naming no heading — plus that each row's `#anchor` still derives from the
heading it points at, so rewording a heading without its row fails too. **The `(active — see ROADMAP)`
marker is asserted with them (T244)**, and it is required rather than merely permitted: present exactly
when the roadmap carries a task line under that block, so the commit that empties a block clears its
marker and the commit that reopens one sets it. The theme table above is checked
one way only: a letter it names must be declared in the ledger. It is deliberately **not** required to
carry a row per heading, because it lists the themes meant to be *reused*, not every letter that ever
shipped — demanding one would argue for the sprawl it exists to stop.

## `ship --why`, or live with it

`ship` copies the roadmap line's `why` into the ledger by default — and that line states a **problem**,
because that is what a roadmap line is for. A ledger entry states an **outcome**: what now works.
`--why` is the only chance to say so. **`amend` refuses a shipped id** ("it is already in the
changelog") and `record drop` only removes the later of two entries for one id, so there is no second
pass over an entry's wording, ever. Write it at ship time:

```
roadkeep ship T179 --why "The overage figure has a column in the store, and a reading that carries none stays absent rather than becoming a measured zero."
```

`roadkeep.toml`'s `[ledger] symptom = false` means an entry needs no bold symptom — the symptom
belonged to the roadmap line `ship` just deleted. Needs none, not forbidden: the richer entries here
open with one, and that is the house style worth matching for anything a user would notice.

## Release notes

A release is not a task. When the version is bumped, `<Version>` in `ClaudeTray.csproj` is the single
source of truth — the installer, winget manifests and the update check all derive from it (see
[STRATEGY.md](../../../STRATEGY.md) §IV). Cutting a release is a `chore: release vX.Y.Z` commit of
its own, never bundled with a task.
