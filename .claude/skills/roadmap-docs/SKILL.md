---
name: roadmap-docs
description: Claude Code Tray's own shipping discipline — the one-task-one-commit rule, the user-facing-surface gate (README, the site/ copy modules, lang/*.json) a shipped feature must pass, and how a release is cut. The roadmap/changelog/rationale write path itself is NOT here: roadkeep owns it, and its own skill says which command to call. Use whenever a task is finished, before committing, or when a shipped feature might be user-visible.
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
     `preview-ui` skill produces it; screenshots live in `site/public/shots/`).
   - **the site** — a claim in `site/src/lib/site-content.ts`, and a section that renders it if it needs
     one. The site is a workspace, not a file: `cd site && npm run build && npm test` is its gate, and a
     new screenshot is picked up by the build rather than measured by hand. Keep the wording consistent
     with the README's.
3. **Write for the user, not the commit.** These surfaces explain what the feature does and how to
   use it. Never paste `IMPROVEMENTS.md` rationale verbatim.
   *What `--selftest` holds up here (T250)*: every image either surface points at exists, every
   screenshot in `site/public/shots/` is shown by one of them, and the winget id is spelled one way
   across both and the manifest. The **wording** is not checked and deliberately so — the site is marketing copy, the README
   is not, and a check demanding they match would fail on every rewrite.
4. **Verify UI by looking.** A task that touches a window is not done without a screenshot — the
   `preview-ui` skill, per [AGENTS.md](../../../AGENTS.md). For a localized change, screenshot at
   least one non-English language too.
5. **Trademark discipline.** User-facing text keeps the unofficial framing: not affiliated with,
   endorsed by, or sponsored by Anthropic (see [STRATEGY.md](../../../STRATEGY.md) §S:I).

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
| The Settings page, and what a control claims to change | **C** | `SettingsPage.*`, `Settings`, `CarryUnchangedFrom` |
| Auth, the API, and what a quota header means | **D** | `ApiClient`, `HeaderProbe`, `QuotaState`, extra usage |
| Notifications and toasts | **E** | `ToastWindow`, `ContextNudges` |
| The Statistics page and the pace report | **F** | `StatisticsPage.*`, `UsageReport`, `UsageHistory` |
| Localization | **G** | `lang\*.json`, `Localization` |
| The Context Load Inspector | **I** | `src\Context\*`, `ContextPage` |
| Activity-aware pacing | **J** | `ActivityProfile`, `ActivityShape`, `HourlyUsage` |
| Live throughput | **K** | `LiveRate`, `LiveChart`, `TranscriptTail` |
| Sessions — one conversation, drilled into | **AK** | `SessionIndex`, the Sessions page, per-task attribution |
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
changelog") and `record drop` only removes the later of two entries for one id. Write it at ship time:

```
roadkeep ship T179 --why "The overage figure has a column in the store, and a reading that carries none stays absent rather than becoming a measured zero."
```

`roadkeep.toml`'s `[ledger] symptom = false` means an entry needs no bold symptom — the symptom
belonged to the roadmap line `ship` just deleted. Needs none, not forbidden: the richer entries here
open with one, and that is the house style worth matching for anything a user would notice.

**There is one door back, and it is not a second draft.** `record amend <id> --why "…"` rewrites an
entry's sentence where it stands — not `drop` plus `add`, which would move the line to the end of its
block and show a reviewer a deletion where a word changed. Use it for a sentence that is *wrong about
the repository*, not for one you would now phrase better: T254 used it to take a path out of T151 that
`roadkeep lint` reported missing on every CI run, and that is the bar.

## A task that is decided against

**`retire` does not work in this project, and finding that out at the moment you need it is the trap.**
It writes the ledger entry for a task leaving without shipping, and it refuses here because
`roadkeep.toml` declares `[ledger] marker = false` — with no ✅ written, a 🗑 cannot be told from one.
The refusal is correct and it writes nothing. (Upstream as `RK214`; if that lands, this section goes.)

So the exit is `ship --why`, and the sentence carries the whole burden of not lying:

- **Open with the decision, not with work.** *"Measured before deciding, and the premise did not
  survive: …"* — never a sentence that reads as something built.
- **Give the evidence that settled it**, in numbers where there are numbers. That is the deliverable;
  a decision with no measurement behind it is an opinion that has taken an id.
- **Say where the conclusion now lives** — usually a non-goal (`roadkeep non-goal add`), so the same
  idea is not re-filed by the next person who has it.

T253 is the worked example: a build-configuration change that a 10s-against-11s measurement killed,
shipped as the measurement plus the non-goal it produced.

## Release notes

A release is not a task. When the version is bumped, `<Version>` in `ClaudeTray.csproj` is the single
source of truth — the installer, winget manifests and the update check all derive from it (see
[STRATEGY.md](../../../STRATEGY.md) §S:IV). Cutting a release is a `chore: release vX.Y.Z` commit of
its own, never bundled with a task.

**Before it, run the published `.exe` from outside the tree — once (T383).**

```
dotnet publish -c Release
copy bin\Release\net10.0-windows\win-x64\publish\ClaudeTray.exe %TEMP%\away\
cd %TEMP%\away && ClaudeTray.exe --selftest      # 0 failed, ~30 skipped, every skip a repo one
```

**This is the only path that exercises what an installed copy is.** `--selftest`'s second kind of claim
reads repository files and stands down where there is none — and `build.yml` publishes *into* the
checkout and runs the exe from there, so it finds `AGENTS.md` by walking up and the whole family runs.
Every other loop is inside the tree too. T383 shipped a claim that guarded its own precondition instead
of going through `Repo`: correct in a checkout, **one red assertion on every installed copy**, invisible
to CI and to every developer command. Copying the single file somewhere else is the whole test, and it
costs one publish a release already pays for.
