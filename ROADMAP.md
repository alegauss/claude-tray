# Claude Code Tray — Roadmap (active backlog)

> **Single source of truth for task status.** Flat, one line per task.
> Only **unshipped** work lives here (📋 designed · 💭 idea · ⏳ partial · 🛠 in-progress).
> Shipped work is moved to [CHANGELOG.md](CHANGELOG.md); design rationale (the *what/why* per task)
> lives in [IMPROVEMENTS.md](IMPROVEMENTS.md); positioning/distribution decisions live in
> [STRATEGY.md](STRATEGY.md).
>
> **This file is written by `roadkeep`, not by hand.** The id, the `(deps: … ✅)` annotation and the
> `→ §` pointer are derived on render, and the field limits in
> [roadkeep.toml](roadkeep.toml) are refused at insertion rather than reported afterwards. The
> `roadkeep` skill says which command to call; a hand-edit is denied by the hook.
>
> **An entry is one sentence: what + why + `→` pointer**, ≤320 characters, symptom in bold and never
> named after its fix. The `→` points at the section in IMPROVEMENTS.md with the full design.
>
> **How to pick work:** `roadkeep pick` — the lowest-numbered task whose `deps` are all shipped, with
> the reason it was chosen. `roadkeep brief <id>` is everything it costs to start one, in one call.

| Symbol | Meaning |
|---|---|
| 📋 | Designed but not started |
| 💭 | Idea worth exploring; needs design |
| ⏳ | Partial — direction is right, more work remains |
| 🛠 | In progress |

## Block AG — The interaction check grew two cases, and nobody runs it

> Block AD doubled what `Check-Interaction.ps1` asserts, and every item here came out of building or
> running those cases rather than out of planning them: one is a latent id collision the new case only
> avoids by the order it reads in, two are ways a check can quietly stop asserting, and two are the cost
> of a loop that a person has to remember.
> Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §XX.
>
> Ordered by what can make a green run meaningless, then by what keeps the loop being run at all.

## Block AE — Extra usage is money, and the tray is asleep for it

> A field report against an account working past 100%: the overage reading is fetched, reaches one
> conditional line of tooltip, and changes nothing else on screen.
> Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §XVIII — the report, the four signals already parsed, and
> the two constraints that bind every task here.
>
> Ordered by what is losing data today, then by what every string here is waiting on, then by what a
> user would actually see.

## Block AB — What Block Z's own work left behind

> Four things surfaced *while building* Block Z that are part of none of its tasks and were reported by
> nobody: two the block itself grew, two about the checks meant to protect it.
> Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §XV.
>
> Ordered by what is wrong on screen today, then by what protects the rest.

## Block AC — The tray reports the switch it performed, not the switch the machine got

> A field report against shipped Block T: the tray and `/usage` named different profiles, and both were
> right about different questions. Three distinct ways the choice fails to reach a new session, and the
> tray distinguishes none of them.
> Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §XVI — the measurement, and the A/B/C split the tasks follow.
>
> Ordered by what is wrong for every user today, then by what makes the result checkable.

## Block AI — Verification — the checks that prove a change

- 📋 **T335** (deps: —) **a published capture of the Sessions pane would carry this machine's real prompts to GitHub** — --capture-stats renders that pane from the monitored profile, so the command that makes a README screenshot now makes one holding real prompts - and a screenshot cannot be un-published. → §XX.32
- 📋 **T340** (deps: T288 ✅) **a state's sentence can be squeezed out of the tooltip in one language, and only a test written for that state notices** — T222, T302 and T288 each lost a sentence to the 127-char cap in a different language, and each was locked shut by a test written for that one key. → §LXVIII
- 📋 **T343** (deps: T298 ✅) **the sessions pane is still captured mid-scan, because its wait proceeds at the deadline where the report's now refuses** — Two waits in one capture answer one deadline differently: the report refuses and writes nothing, the sessions scan proceeds and announces its placeholder. → §LXXI
- 📋 **T344** (deps: T305 ✅) **two more stores write without consulting the observing gate: the reset log appends and the profile migration moves files** — The reading that found T305 finds two more, and one moves files rather than appending - so the list of call sites needs an owner, not a fourth fix. → §LXXII
- 📋 **T345** (deps: T312 ✅) **the source-reading checks need a four-minute WPF build to answer questions about text on disk** — Seven checks open files under src and compare text, and all seven need the WPF binary built to answer - so fixing what one of them found was done against a scratch script instead. → §LXXIII
- 📋 **T347** (deps: —) **one source file is binary to git, so every change to it lands with no line diff and no blame** — A literal NUL byte at SessionIndex.cs:453 makes git call the file binary, so T330's nine-line change was reported as 1141 lines rewritten and Grep refuses to read it at all. → §LXXV

## Block AJ — Working here — the repo's own docs and flags

- 📋 **T339** (deps: —) **roadkeep lint reports two problems in this repo's own prose that no task has taken** — A check that fails every run stops being read, and a new finding lands in a wall of expected ones - T301 went over on a dep annotation it never typed, and §I.8 spends an id on an example. → §LXVII
- 📋 **T342** (deps: T294 ✅) **adding a sixth interaction case put AGENTS.md over budget, and fitting it meant compressing four unrelated rules** — Eleven edits to fit one bullet, nine of them compressing rules the task never touched - and one byte of headroom left, so a seventh case cannot be added. → §LXX

## Block G — Localization

## Block N — System information — your plan, your install, this machine

## Block E — Reset notifications & toasts

## Block S — Settings round-trip

## Block Q — Keyboard input in the windows

## Block I — Context Load Inspector

## Block B — Packaging, self-update, CI

- 📋 **T341** (deps: —) **a dotnet build with nothing changed takes about four minutes, because every Debug build lays down the whole runtime** — Measured at 3m41 and 4m28 with nothing changed, and the loop this repo runs on is build, run a flag, look at what came back - charged those minutes every turn. → §LXIX

## Block J — Activity-aware pacing

## Block F — Statistics window (pace report)

## Block AK — Sessions — what one conversation cost, and what it did

- 📋 **T333** (deps: T329 ✅) **the app can say which repo is eating the week and never which kind of work is** — Measured, 443 slash-command tasks carried 57% of all spend at a $9.55 median against $1.69 for a typed prompt, and a per-project breakdown cannot separate them. → §LXII
- 📋 **T337** (deps: T329 ✅) **a spike on the live strip has no name, and the join was designed in the direction with no data** — The strip is a 300-second ring and the list opens on seven days, so a task-to-chart highlight is blank for 142 of 143 rows - the direction with data is the chart naming the live task. → §LXV
- 📋 **T338** (deps: T336 ✅) **the row's title and prompt lines are cut mid-word with no ellipsis, running under the next column** — TextTrimming cannot fire inside a horizontal StackPanel, measured at infinite width, so the Grid column clips instead - shipped in T334, unseen because no capture followed. → §LXVI
- 📋 **T346** (deps: —) **tokens rank sessions and money explains them, and a $2 conversation reads the same as a $40 one** — T330 read the split and nothing prices it, and SessionRow aggregates the models that answered — so per-model attribution is what a list price needs first. → §LXXIV

## Non-goals (do NOT add as tasks)

Binding constraints — see [IMPROVEMENTS.md](IMPROVEMENTS.md) §I for the full text. Summary:

- **No tokenizer dependency, and no NuGet package in general** The single self-contained `.exe` plus
  installer story depends on it, so token counts stay estimates with a visible "≈" — §I.3.
- **Never read message content** Usage counts, model ids, flags, tool/skill *names* and the session
  `cwd`, plus one amended exception: what a conversation is called, truncated, on the Sessions list
  only — §I.1.
- **No network** Beyond the usage API and GitHub Releases: no telemetry, no analytics, no crash
  reporting, and every computation local — §I.2.
- **Not a memory editor, not a Claude Code config manager.** Hooks, MCP servers, permissions and
  instruction files are measured, never edited, and T77 settled it with no write path at all — §I.4.
- **Never swap a credential or config file to switch accounts.** Block O switches by launching with
  a different `CLAUDE_CONFIG_DIR`, because the file-juggling alternative races Claude Code's own
  refresher — §I.6.
- **No switching the account of a running session.** The environment is fixed when the process
  starts, so a profile change applies to the next session and the UI says so rather than implying
  otherwise.
- **Profiles are contexts, not quota pools.** No string anywhere may suggest changing accounts
  because one hit its limit, which is limit circumvention wearing a convenience costume — §I.7.
- **Don't swap the UI stack.** WinForms owns the tray icon and WPF owns the windows on one STA
  thread, with no imperative `Dock=Top` stacking and no hardcoded hex — §I.5.
- **No second source of truth for the version** `<Version>` in `ClaudeTray.csproj` is the one place
  a version lives, and everything else derives from it — §I.5.
- **No usage annotation on memory files** Settled by dropping T85: a recall is the harness injecting
  a file, and no memory analogue of `attributionSkill` exists to read — §I.8.
- **No activity-aware tray notification** Settled by T87: the shaped projection changed what the
  chart says, not what is worth interrupting somebody for.
- **No live hint on the tray icon** Settled by dropping T101: it needs a transcript tail running all
  session, undoing the one property that keeps T99 free when nobody is looking.
- **No pricing or positioning discussion as a numbered task.** It goes in
  [STRATEGY.md](STRATEGY.md), which is where a decision that is not work belongs.
- **No check outside the binary** --selftest is the suite because a test project would break the
  single-exe rule, and moving a document check out to save a measured 10s compile buys a second
  reader of the thing it compares.
- **No colour emoji on the cards** WPF text and GDI+ both draw Segoe UI Emoji monochrome base layer
  - measured on six glyphs and on a real captured card - so colour needs Direct2D interop or seven
  shipped raster assets (T227).
- **No build retry loop, and no node-reuse setting** No node-reuse setting and no build retry loop:
  Directory.Build.rsp is not honoured and MSBUILDDISABLENODEREUSE leaves nodes alive, both measured
  (T270).
- **No figure in money for an overage spell** Settled by dropping T279: which models overage bills
  is a cached GrowthBook flag reading ["Fable", "Fable 5"], and 0 of 34,790 turns here were Fable —
  its two readings differ by the whole estimate.
