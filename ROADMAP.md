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

## Block AE — Extra usage is money, and the tray is asleep for it

> A field report against an account working past 100%: the overage reading is fetched, reaches one
> conditional line of tooltip, and changes nothing else on screen.
> Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §XVIII — the report, the four signals already parsed, and
> the two constraints that bind every task here.
>
> Ordered by what is losing data today, then by what every string here is waiting on, then by what a
> user would actually see.

- ⏳ **T181** (deps: —) **Nobody has established what the overage percentage is a percentage of** — It is printed beside two figures meaning *of your included window*, and no reading of a real account mid-overage exists to settle what 100% of this one would be. → §XVIII.3
- 💭 **T184** (deps: T181) **Nothing marks the moment the included quota ends and the meter starts** — Resets are notified because they are good news, and the one transition that starts costing the user money passes without a word. → §XVIII.6

## Block AD — The window can be read now, and what that turned up

> The first check that *reads* the Statistics window instead of photographing it found the whole tab
> body outside the accessibility tree (fixed in T165). Four things came out of that half-hour, all of
> them because nobody had ever asked the window what it was showing.
> Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §XVII.
>
> Ordered by what a user is affected by today, then by what protects the rest.

- 📋 **T175** (deps: —) **The two controls a screen reader most needs named are the two with no accessible name** — `ProfileCombo`'s label is a separate `TextBlock` and `MethodInfo` is a glyph-only toggle, so both announce unnamed while every other control in the window reads. → §XVII.1
- 📋 **T176** (deps: T165 ✅) **The assertion that the panes are readable at all sits behind a two-profile skip** — `-Case Profiles` is the only thing that would notice the tab body leaving the UIA tree again, and it skips whole on a one-profile machine and every CI runner. → §XVII.2
- 📋 **T177** (deps: T166 ✅) **T166's whole claim is a timing, and no check makes it** — Coming back to a profile seen seconds ago must never show the status line at all, and the probe that measured it was a scratch file that is now gone. → §XVII.3
- 📋 **T178** (deps: T165 ✅) **"Read NOTHING" is reported for a window that was talking the whole time** — `Read-ProfileStop` reports no panes and no status line after 25s without saying what it last saw, so a window that was slow and one that was blank fail alike. → §XVII.4

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
