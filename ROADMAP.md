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

- 💭 **T210** (deps: —) **The API names which window is binding and three files work it out again** — Three of them are read by nothing, and what reading representative-claim would unlock needs a second value first: five_hour from one account is a mapping whose default arm nobody has seen. → §XVIII.9
- 💭 **T211** (deps: —) **Nothing here knows the API is offering a fallback** — unified-fallback reads available and unified-fallback-percentage 0.5 on every response, and what either governs is unmeasured, so no string can say what happens at the limit. → §XVIII.10

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

- 📋 **T171** (deps: —) **A hand pick with the Windows-wide switch off does half its job and says nothing** — `SyncEnvironmentToPin` returns on its first line while `SetMonitoredProfile` moves the icon and re-keys the stores, so the user sees only the half that happened. → §XVI.1
- 📋 **T172** (deps: —) **Nothing on screen answers "which profile will the next session actually use?"** — Every indicator reports `MonitoredConfigDir`, which is what the tray watches, while what Claude Code obeys is a user-scope variable shown on a page nobody opens. → §XVI.2
- 📋 **T173** (deps: —) **`Adopt` returns "accepted", never "written", so no failure of the write is observable** — T149 settled the bookkeeping up front and queued the registry write, which is the right trade for the UI thread and leaves nothing that ever reads the result. → §XVI.3
- 📋 **T174** (deps: T173) **A machine-wide write is the one action with no feedback at all** — The icon moving is feedback for the icon, and the effect of the write stays invisible until the next process starts. → §XVI.4

## Block AI — Verification — the checks that prove a change

- 📋 **T206** (deps: —) **A screen-copied PNG carries a strip of whatever is behind the window** — GetWindowRect spans the invisible resize border and drop shadow, so the copied rectangle is wider than the window paints and every capture the script makes has other pixels down its edges. → §XX.10
- 📋 **T207** (deps: —) **Nothing asserts the preview and capture flags, only the arithmetic behind them** — All 240 self-check assertions are about stores and pacing, so the variant tables, the refusals and the output-path rules this block just added are held up by whoever next runs a flag by hand. → §XX.11
- 📋 **T214** (deps: —) **One published screenshot is a photograph nobody can retake** — Every other picture in docs comes from a capture flag, and this one is a hand-taken shot of the notification area — so T213 changed the tooltip and the README's hero image still shows the old line. → §XX.12
- 📋 **T215** (deps: —) **A character budget decides what the tooltip shows and no assertion can reach it** — Which lines survive the 127-char cap is decided inside an instance method no headless test can build, and T213 spent eight of those characters in five languages without checking the effect. → §XX.13
- 📋 **T217** (deps: —) **A capture can succeed and not contain the surface it was taken for** — A popup is its own window and closes when the script takes the foreground, so three captures of the method note reported success, named the right window, and showed no note. → §XX.14
- 📋 **T218** (deps: T169 ✅) **A run that checked less is the same colour as one that checked everything** — T169 made every skip print its name and left the policy open on purpose; the exit code still ignores them, so losing a check in CI costs nothing and shows nothing. → §XX.15

## Block AJ — Working here — the repo's own docs and flags

- 💭 **T219** (deps: —) **The turn budget is full, so a new rule is paid for by deleting an old one nobody ranked** — Writing T169's rule took AGENTS.md 612 bytes over, and the bytes came back from a sentence that happened to be duplicated — a selector that runs out before the next rule does. → §XXII.1

## Block G — Localization

- 💭 **T216** (deps: —) **One machine, two number conventions: the page decides, not the app** — T167 picked invariant for one window, while the Context page, the tray toast and the System page format the same figures with L.Culture, and the sweep it added reaches neither. → §XXI.1

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
