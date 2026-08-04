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

- 📋 **T286** (deps: —) **a window capture reports success while the page it photographed is still computing** — Capture-Window.ps1 waits 1500ms by default, the Statistics page took 25s here, and the PNG of "Computing your consumption pace…" was announced as Captured. → §XX.6
- 📋 **T291** (deps: T277 ✅) **no check asks whether a card built with no quantity actually drew no bar** — The fit check reads only text blocks and the arithmetic is asserted a layer above the window, so the collapse could come back and every capture, check and assertion would stay green. → §XX.7
- 📋 **T294** (deps: —) **no check drives the switch that changes which account the icon follows** — The Profiles case drives the report's picker, which only chooses whose numbers are drawn — so the path that rewrites the setting, re-keys the stores and drops the old account's state runs under none. → §XX.8
- 📋 **T297** (deps: —) **the folded store's line format has a second writer inside the check, kept in step by hand** — WriteStore composes the day line itself, so T287's column reached every fixture as absent — the one state a fixture cannot notice it is asserting, because absent is a legitimate reading. → §XX.9

## Block AJ — Working here — the repo's own docs and flags

## Block G — Localization

## Block D — Auth & API resilience

- 📋 **T288** (deps: —) **the stopped verdict still follows the window on the icon, so a blocked session hides behind a quiet week** — T274 split the billing verdict from the caption and left this half: 5h rejected beside 7d at 0.47 draws an on-track projection and says nowhere that work has stopped. → §XXIII.6

## Block N — System information — your plan, your install, this machine

## Block E — Reset notifications & toasts

## Block S — Settings round-trip

## Block Q — Keyboard input in the windows

## Block I — Context Load Inspector

## Block B — Packaging, self-update, CI

## Block J — Activity-aware pacing

- 📋 **T295** (deps: —) **last week's ghost curve stops at a ceiling and never shades the stretch the account was over** — T287's column is in the store and PreviousWeek still returns a curve and a coverage share only, so the stretch this week's chart shades has no counterpart on the week drawn behind it. → §XXXII.2

## Block A — Foundation — tray, icon, API, projection

- 📋 **T280** (deps: T275 ✅) **nothing on screen says when the overage spell started** — The store dates the crossing at its first reading past the threshold, and saying since when is the difference between reporting a state and reporting an event. → §XXXIII
- 📋 **T293** (deps: —) **the fields a profile switch must drop are a list kept by hand, and each new one is found by the defect it causes** — AdoptMonitored nulls three and clears a fourth by name, so a fifth is carried into the incoming account until somebody notices what it made the tray say. → §XXXV

## Block F — Statistics window (pace report)

- 📋 **T289** (deps: —) **one rule about what counts as a break in the readings is computed twice in the same file** — T275 gave MergeSpans a measured bridge and FindGaps still derives the identical median-and-floor inline, so the two can drift and only one of them has a name. → §XXXIV

## Non-goals (do NOT add as tasks)

Binding constraints — see [IMPROVEMENTS.md](IMPROVEMENTS.md) §I for the full text. Summary:

- **No tokenizer dependency, and no NuGet package in general** The single self-contained `.exe` plus
  installer story depends on it, so token counts stay estimates with a visible "≈" — §I.3.
- **Never read message content** Usage counts, model ids, flags, tool and skill *names* and the
  session `cwd` only, and no content display or export anywhere in the app — §I.1.
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
