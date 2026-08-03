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

- 📋 **T192** (deps: —) **Two controls in one window carry the same automation id, so an id lookup picks by tree order** — The Statistics picker and the Settings one are both `ProfileCombo`, and the only reason the new name check reads the right one is that it reads before navigating. → §XX.1
- 📋 **T193** (deps: —) **A picker route that stops working turns an assertion into a note, and the run stays green** — T177's timing is observed only along `Combo-Select`'s UIA route, and the keyboard fallback exists precisely because that route can die. → §XX.2
- 📋 **T194** (deps: —) **The check that caught the keyboard being dead in every window runs only when somebody remembers** — `-Case Keyboard` drives `--settings-tray`, which needs no credentials and no report, and CI runs `--selftest` alone on a runner that has a desktop. → §XX.3
- 📋 **T195** (deps: —) **A full interaction run launches the app five times, three of them for the same read-only window** — `Panes`, `Profiles` and `Names` each start `--main`, wait out its first layout and first poll, and kill it, although none of them changes anything the next would see. → §XX.4
- 📋 **T196** (deps: —) **The row rule now governs thirty-odd controls and is asserted on three of them** — `-Case Names` reads one ComboBox, one switch and one Slider on the Settings page's General panel, and five of its six panels are never visited. → §XX.5

## Block AF — Six surfaces shipped, and what nothing was checking

> Everything Block AE built came out of one session, and the recurring experience was that the tool
> which should have caught a mistake was either absent, broken, or quietly disagreeing with its twin.
> Three are gaps in verification, two are the preview tooling itself, and two are what six new
> surfaces cost a colour vocabulary and a file at its budget.
> Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §XIX — what each was found by, since none was reported.
>
> Ordered by what can ship a defect to a user today, then by what misleads whoever builds here next.

- 📋 **T186** (deps: —) **A preview added to --stats does not exist in --capture-stats, and the capture looks plausible without it** — The two flags parse their variants in separate branches, so 'overage' captured this machine's real week instead and only the numbers gave it away. → §XIX.2
- 📋 **T187** (deps: —) **--capture-stats throws when its output directory does not exist** — SaveSnapshot calls File.Create on a path nothing creates, so a capture into a new folder dies with a stack trace after the window has already rendered. → §XIX.3
- 📋 **T188** (deps: —) **Two toasts wear the same clay for opposite news** — Surprise says quota came back early and the new one says you started paying, because T184 took the colour the icon and the chart use for being past the quota. → §XIX.4
- 📋 **T189** (deps: —) **The chart's own series-building is unreachable from --selftest** — FillCurve is private and the drawing is WPF, so the rule that an absent overage reading is not a plotted zero is held by one screenshot and nothing else. → §XIX.5
- 📋 **T190** (deps: —) **The Settings row that reports extra usage in use has never been rendered** — It needs a profile carrying an overage reading and no fixture builds one, so the only branch of that row which shows a percentage shipped unseen. → §XIX.6
- 📋 **T191** (deps: —) **AGENTS.md is at its line budget, so this block's new files and flags are on no map** — The budget is a ceiling meant to come down and the file sits at 400 of 400, so the repo's own map silently omits whatever ships next. → §XIX.7

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
