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

## Block AH — What Block AF's own captures turned up

> Block AF built five checks and repaired two capture flags, and every item here came out of *using*
> that tooling for a block rather than out of reviewing it. Three are the capture surface misleading
> whoever runs it, and each produces a file: one names a real account on a synthetic chart, one lands
> where nobody asked, one photographs a different application. Two are gaps the block left behind.
> Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §XXI — what each was found by, since none was reported.
>
> Ordered by what can ship a defect to a user today, then by what misleads whoever builds here next.

- 📋 **T197** (deps: —) **A synthetic capture carries a real account's name** — The capture path calls SetProfiles for every variant, so a published PNG of a fixture week names whichever account this machine monitors, in the one place a fixture exists to prevent that. → §XXI.1
- 📋 **T198** (deps: —) **A capture flag writes a PNG the caller never named** — --capture-toast reads a lone argument as the variant, so a path given as the only argument selects the default toast and lands it in the working directory, which here is the repository root. → §XXI.2
- 📋 **T199** (deps: —) **The capture script photographs another app's window** — It never verifies the window it copies belongs to the process it launched, so a request for the Statistics window returned another instance's Settings window and reported success. → §XXI.3
- 📋 **T200** (deps: T190 ✅) **One branch of the extra-usage row still cannot be shown** — The measured-zero reading needs extra usage enabled with nothing spent, and T190's fixture spends while the seat has the flag off, so 'Enabled — not in use' is rendered by nothing. → §XXI.4
- 📋 **T201** (deps: —) **Two of the five interaction cases are described nowhere** — Panes and Names have no bullet in AGENTS.md's interaction section, so the two cases a single-profile machine can run are discoverable only by reading the script. → §XXI.5

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

- ⏳ **T181** (deps: —) **Nobody has established what the overage percentage is a percentage of** — It is printed beside two figures meaning *of your included window*, and no reading of a real account mid-overage exists to settle what 100% of this one would be. → §XVIII.3

## Block AB — What Block Z's own work left behind

> Four things surfaced *while building* Block Z that are part of none of its tasks and were reported by
> nobody: two the block itself grew, two about the checks meant to protect it.
> Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §XV.
>
> Ordered by what is wrong on screen today, then by what protects the rest.

- 📋 **T167** (deps: —) **Numbers interpolated into localized strings bypass the window's own invariant formatter** — Thirteen call sites route through `Fmt` and the method note's five interpolations take the OS culture, so one window states its numbers in two conventions. → §XV.1
- 📋 **T168** (deps: —) **The method note's composition is UI code, so the rules T159 and T163 added cannot be asserted** — Which paragraphs a report yields is now a real decision living inline in `Render`, where `--selftest` cannot reach it and two screenshots are the verification. → §XV.2
- 📋 **T169** (deps: —) **A skip that fires on every run is a check that does not exist** — T161's guard skips wherever the temp path holds a character the encoding cannot reconstruct, which is every CI runner whose profile is an 8.3 short name. → §XV.3
- 💭 **T170** (deps: —) **The method note is ~1,000 characters of 12px prose in one unstructured paragraph** — T113 moved it behind an information glyph because six lines under the panes was a lot of screen, and it is now twelve lines behind a click and still growing. → §XV.4

## Block AC — The tray reports the switch it performed, not the switch the machine got

> A field report against shipped Block T: the tray and `/usage` named different profiles, and both were
> right about different questions. Three distinct ways the choice fails to reach a new session, and the
> tray distinguishes none of them.
> Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §XVI — the measurement, and the A/B/C split the tasks follow.
>
> Ordered by what is wrong for every user today, then by what makes the result checkable.

- 📋 **T171** (deps: —) **A hand pick with the Windows-wide switch off does half its job and says nothing** — `SyncEnvironmentToPin` returns on its first line while `SetMonitoredProfile` moves the icon and re-keys the stores, so the user sees only the half that happened. → §XVI.1
- 📋 **T172** (deps: —) **Nothing on screen answers "which profile will the next session actually use?"** — Every indicator reports `MonitoredConfigDir`, which is what the tray watches, while what Claude Code obeys is a user-scope variable shown on a page nobody opens. → §XVI.2
- 📋 **T173** (deps: —) **`Adopt` returns "accepted", never "written", so no failure of the write is observable** — T149 settled the bookkeeping up front and queued the registry write, which is the right trade for the UI thread and leaves nothing that ever reads the result. → §XVI.3
- 📋 **T174** (deps: T173) **A machine-wide write is the one action with no feedback at all** — The icon moving is feedback for the icon, and the effect of the write stays invisible until the next process starts. → §XVI.4

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
