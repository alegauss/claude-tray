# Claude Code Tray — Roadmap (active backlog)

> **Single source of truth for task status.** Flat, one line per task.
> Only **unshipped** work lives here (📋 designed · 💭 idea · ⏳ partial · 🛠 in-progress).
> Shipped work is moved to [CHANGELOG.md](CHANGELOG.md); design rationale (the *what/why* per task)
> lives in [IMPROVEMENTS.md](IMPROVEMENTS.md); positioning/distribution decisions live in
> [STRATEGY.md](STRATEGY.md).
>
> **How to pick work:** lowest-numbered task in a block whose `deps` are all shipped.
> The `→` pointer is the section in IMPROVEMENTS.md with the full design.
>
> **Next free task number lives in [last-task.md](last-task.md)** — read it before adding a task.
> Maintenance rules: the `roadmap-docs` skill.

| Symbol | Meaning |
|---|---|
| 📋 | Designed but not started |
| 💭 | Idea worth exploring; needs design |
| ⏳ | Partial — direction is right, more work remains |
| 🛠 | In progress |

## Block AE — Extra usage is money, and the tray is asleep for it

> A field report the tray had no way to answer: *"quando estava usando o perfil da VILT, apesar de estar
> em 100% de uso, ainda funcionava, porque estava com uso extra ativado — mas no Claude Tray isto não
> aparece evidente, nem no gráfico, nem no tray, em lugar nenhum"*. Right on every count. The overage
> reading **is** fetched — `ApiClient` has parsed `anthropic-ratelimit-unified-overage-utilization` since
> the first version — and then it reaches one conditional line of tooltip and stops: never stored, never
> charted, never notified, and never changing what any other number on screen *means*. Measured on the
> reporter's machine: 1,403 stored readings on the work profile, **178 of them at 97–99% weekly**, in a
> file whose line format has no column an overage figure could go in. That account has
> `hasExtraUsageEnabled: true`; the other profile on the same machine has it `false`; the app behaves
> identically for both.
> Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §XVIII.
>
> Ordered by what is losing data today, then by what every string here is waiting on, then by what a
> user would actually see.

- 📋 **T179** (deps: —) **The overage reading is fetched, shown once and thrown away** — `UsageSample` is `(T, Util5h, Reset5h, Util7d, Reset7d)` and `UsageHistory.Append` takes five numbers, none of them overage; `PaceSnapshot` carries the same four, so the Statistics window could not draw the series even if it wanted to. Every other task in this block is downstream of the store, which is why it goes first. The trap is `ApiClient`'s own, one layer down: a missing field must read as **unknown, not 0** — the header-less-200 guard exists precisely because a 0 parsed as a real reading fires a phantom reset and wipes the burn history, and an append-only JSONL that gained a column is exactly where that mistake is cheap to make. → §XVIII.1
- 📋 **T180** (deps: —) **The poll goes to sleep exactly when the spending starts** — `BlockedUntilUnix` idles the timer until the window's reset for anything ≥ `AtLimitThreshold` (0.995), on the stated reasoning that "usage is blocked and consumption is frozen until that window resets". For an account with extra usage enabled that sentence is false: the session keeps working and keeps billing, and the tray stops looking — for up to seven days on the weekly window. The only stretch in which a percentage is a *price* is the one stretch the app records nothing about. The idle belongs to *there is nothing left to observe*, not to a threshold. → §XVIII.2
- 📋 **T181** (deps: —) **Nobody has established what the overage percentage is a percentage of** — it is printed beside two numbers that mean "of your included window", and the app has never confirmed what 100% of *this* one would mean, nor what `anthropic-ratelimit-unified-5h-status` reads while overage is being consumed (it is displayed verbatim in the tooltip and read by nothing). Until that is captured from a real account mid-overage, every string the rest of this block wants to put on screen is a guess. A probe that records the unified headers verbatim — names and values only, no message content (§I.1) — is what T182–T184 are waiting on. → §XVIII.3
- 📋 **T182** (deps: T181) **The icon and the tooltip say "blocked" where the account is paying** — one threshold drives the red fill, the "at limit" sentence and the sleep, and it models two states where there are three: inside the included quota, past it and billing, and genuinely stopped. `ClaudeInfo.ExtraUsage` is already on the model, already parsed per profile, and is consulted by a single text row in Settings. Making it a modelled state is the task; the design problem is that red is already spoken for by *danger*, so "you are now spending money" has to be legible at 16×16 without becoming a second alarm — and the Settings row has the matching gap, stating that extra usage is *enabled* while saying nothing about whether it is being *used*. → §XVIII.4
- 📋 **T183** (deps: T179, T181) **The Statistics window has no overage anywhere** — the weekly chart draws a curve flattening against a ceiling and a projection announcing the window is exhausted, while the quantity that actually matters keeps climbing off-chart. Once T179 stores the series there is a real design choice — a third line on the weekly chart, or a pane of its own — under a real constraint: an overage curve has no cap to be a percentage *of*, so it cannot share an axis with two windows that do without one of them lying. → §XVIII.5
- 💭 **T184** (deps: T181) **Nothing marks the moment the included quota ends and the meter starts** — resets are notified because they are good news; the one transition that costs the user money is silent. A one-shot toast on the first reading with overage above zero is the obvious shape, and `ToastTheme.Context` already established a card with no celebration. Needs design, and it inherits two hard constraints: the T87 non-goal means a new notification needs its own justification rather than the reset channel's, and the "profiles are contexts, not quota pools" non-goal means the one thing it must never imply is that some other account still has quota. → §XVIII.6

## Block AD — The window can be read now, and what that turned up

> Block AA ended with the first check that *reads* the Statistics window rather than photographing it,
> and reading it found something no screenshot could: the whole tab body was outside the accessibility
> tree (fixed in T165). Four things came out of that half-hour and belong to none of AA's tasks — one is
> the same defect one layer down, one is a gap in the check that found it, and two are ways the new check
> can mislead the next person to run it. All four exist because *nobody had ever asked the window what it
> was showing* — every earlier verification asked a picture.
> Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §XVII.
>
> Ordered by what a user is affected by today, then by what protects the rest.

- 📋 **T175** (deps: —) **The two controls a screen reader most needs named are the two with no accessible name** — dumping the window's control view (T165) shows `RefreshButton`, `CloseButton` and the three nav destinations carrying their labels, and `ProfileCombo` and `MethodInfo` carrying `''`. The picker's label is a *separate* `TextBlock` beside it and the ⓘ is a glyph-only `ToggleButton`, so neither has anything to derive a name from: the two controls that switch the whole report and open the note explaining it announce as an unnamed combo box and an unnamed button. Both strings already exist in `lang/*.json` (`stats.profile`, the method-note header) — this is `AutomationProperties.Name`, not new copy. → §XVII.1
- 📋 **T176** (deps: T165) **The assertion that the panes are readable at all sits behind a two-profile skip** — `-Case Profiles` is the only thing that would notice the tab body leaving the UIA tree again, and it skips entirely on a machine with one profile, which is most machines and every CI runner. The pane-in-the-tree property has nothing to do with profiles: it wants to be asserted wherever the window opens, before the round trip that needs two. Splitting it out also gives the one-profile machine a real check instead of a stated skip. → §XVII.2
- 📋 **T177** (deps: T166) **T166's whole claim is a timing, and no check makes it** — "coming back is instant" was measured with a scratch script (status line at 162 ms and panes at 961 ms without the cache; never shown and 12 ms with it) and the script is gone. The property is cheap to assert and sharply defined: on the switch *back* to a profile seen seconds ago, the status line must never appear at all. Same precedent as T142 and T165 — the check that found something belongs in the repo, not in a scratch directory. → §XVII.3
- 📋 **T178** (deps: T165) **"Read NOTHING" is reported for a window that was talking the whole time** — `Read-ProfileStop` waits out `stats.computing` and, on timeout, fails with *"no panes and no status line after 25s"*. On the first real run that message was wrong in the way that matters: the status line was up for all 25 s saying *"Computing your consumption pace…"*, and the true fault was elsewhere entirely. A timeout must report what it last saw and distinguish *slower than the deadline* from *nothing on screen* — the second is the failure the script exists for, and conflating them cost a debugging session on the check's own first outing. → §XVII.4

## Block AB — What Block Z's own work left behind

> Block Z made the app say what it knows: the method note stopped overstating its evidence (T159), the
> away-week gate started firing on an ordinary machine (T160), the slug encoding and the settings round
> trip got their first assertions (T161, T162), and the straight-line projection began explaining itself
> (T163). Four things surfaced *while building it* that are not part of any of those tasks — three of them
> only visible in a screenshot or a CI log, none of them reported by anybody. Two are one number and one
> paragraph the block itself grew; two are about the checks that were supposed to protect it.
> Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §XV.
>
> Ordered by what is wrong on screen today, then by what protects the rest.

- 📋 **T167** (deps: —) **Numbers interpolated into localized strings bypass the window's own invariant formatter** — `StatisticsPage.Format.cs` routes every number through `Fmt` (13 call sites: `Pct`, `Tps`, tokens, bytes) precisely so the report reads the same in every locale, and the method note's five interpolations use bare `$"{x:0.#}"`, which takes the OS culture. Measured on a pt-BR machine with `--lang en`: *"4,7 weeks of local transcripts"* and *"there are 2,1 so far"* in the same popup as `1,319 tok/s` and `40%`. One window, two conventions. → §XV.1
- 📋 **T168** (deps: —) **The method note's composition is UI code, so the rules T159 and T163 added cannot be asserted** — which paragraphs a report yields is now a real decision (shaped vs. measured vs. thin, and the away clause only when a week was dropped), it lives inline in `Render`, and `--selftest` cannot reach it. As a pure function of `(report, activity)` returning the ordered key list, every rule the block just introduced becomes a check: never shaped *and* thin, thin only when the shape was declined for thinness, no away clause at zero. → §XV.2
- 📋 **T169** (deps: —) **A skip that fires on every run is a check that does not exist** — T161's probe guard skips when the temp path holds a character the encoding cannot reconstruct, which is every CI runner whose profile is an 8.3 short name (`RUNNER~1`), so the two `TryProbe` assertions may never have run outside a dev machine. Canonicalising the temp root to its long form removes the skip entirely; and the summary counts skips without naming them, so lost coverage reads as a green run. → §XV.3
- 💭 **T170** (deps: —) **The method note is ~1,000 characters of 12px prose in one unstructured paragraph** — 238 + 352 + 422 for the shaped branch, 1,080 for the measured one, and Block Z added to it twice (T159 a clause, T163 a whole sentence). T113 moved it behind an ⓘ because six lines pinned under the panes was a lot of screen; it is now twelve lines behind a click, and the next task will make it thirteen. Needs design: structure (a line per number? a note per tab?) before another sentence lands. → §XV.4

## Block AC — The tray reports the switch it performed, not the switch the machine got

> A field report against shipped Block T, arriving as a question rather than a bug: *"mudei para o
> Pessoal, mas se eu digito `/usage`, aparece VILT Group"*. The tray was right and so was `/usage` —
> they were answering different questions. Picking a profile by hand always moves the icon and re-keys
> the stores; it writes `CLAUDE_CONFIG_DIR` **only** when "Usar o perfil escolhido em todo o Windows"
> is on, and that switch is off by default (deliberately, T145). With it off the pick does half its
> job and reports success for the whole. Three distinct ways the choice fails to reach a new session,
> and the tray currently distinguishes none of them.
> Design: [IMPROVEMENTS.md](IMPROVEMENTS.md) §XVI.
>
> Ordered by what is wrong for every user today, then by what makes the result checkable.

- 📋 **T171** (deps: —) **A hand pick with the Windows-wide switch off does half its job and says nothing** — `SyncEnvironmentToPin` returns on its first line when `SyncEnvironmentProfile` is false and the tray never owned the variable, so `SetMonitoredProfile` moves the icon, re-keys the stores and saves, while `CLAUDE_CONFIG_DIR` is never touched. Both halves are correct in isolation and the user sees only the half that happened. The pick must state what it does and does not change, with the affordance to turn the other half on — silence is the defect, not the default. → §XVI.1
- 📋 **T172** (deps: —) **Nothing on screen answers "which profile will the next session actually use?"** — the icon, the submenu check mark and the Statistics picker all report `MonitoredConfigDir`, which is what the tray *watches*; what Claude Code *obeys* is the user-scope `CLAUDE_CONFIG_DIR`, and the Settings row that shows it (T145) is one line on a page nobody opens mid-doubt. Reading it back beside the pick, and marking the two as disagreeing when they do, turns an investigation into a glance. → §XVI.2
- 📋 **T173** (deps: —) **`Adopt` returns "accepted", never "written", so no failure of the write is observable** — T149 moved the registry write and its `WM_SETTINGCHANGE` sweep to the thread pool and settled the bookkeeping up front, which is the right trade for the UI thread and leaves nothing that ever reads the result. Measured, the window is small (`SetEnvironmentVariable(User)` returns in 87 ms; the value is readable at 108 ms), so this is not a spinner — it is a read-back once the queue drains, and a divergence that becomes visible state instead of a caught-and-dropped exception. → §XVI.3
- 📋 **T174** (deps: T173) **A machine-wide write is the one action with no feedback at all** — the icon moving is feedback for the icon, not for the environment; the effect of the write is invisible until the *next* process starts. `ToastWindow` already exists for events worth noticing and `ToastTheme.Context` already established a card without the celebration. A switch toast fires on T173's confirmation, naming the profile and the effective directory, and carrying the sentence the "no switching a running session" non-goal requires. **No quota bar and no confetti**: animating one account's remaining quota into another's would say the one thing the "profiles are contexts, not quota pools" non-goal forbids. → §XVI.4

## Non-goals (do NOT add as tasks)

Binding constraints — see [IMPROVEMENTS.md](IMPROVEMENTS.md) §I for the full text. Summary:

- **No tokenizer dependency**, and no third-party NuGet package in general — the single
  self-contained `.exe` + installer story depends on it. Token counts stay estimates with a visible "≈".
- **Never read message content** from transcripts. Usage counts, model ids, flags, tool/skill *names*
  and the session `cwd` only. No content display or export anywhere in the app.
- **No network** beyond the usage API and GitHub Releases. No telemetry, analytics or crash reporting.
- **Not a memory editor, not a Claude Code config manager.** Hooks, MCP servers, permissions and
  instruction files are *measured*, never edited — measure, advise, hand the edit to Claude. **T77
  settled this: no write path at all.** The archive-with-undo was considered and dropped in favour of
  a generated cleanup prompt (see [IMPROVEMENTS.md](IMPROVEMENTS.md) §I.4).
- **Never swap, copy or move a credential or config file to switch accounts.** Block O switches
  profiles by launching Claude Code with a different `CLAUDE_CONFIG_DIR`; the file-juggling
  alternative races Claude Code's own refresher (measured: an 8h access token, a 5-day refresh token,
  `.credentials.json` rewritten on refresh, and `.claude.json` rewritten constantly), splits identity
  across two files, and would make the tray the thing that broke somebody's login. §I.4 has no
  exception, and credential material is the last place to invent one.
- **No switching the account of a running session.** The environment is fixed when the process
  starts; a profile change applies to the *next* session. The UI says so rather than implying
  otherwise.
- **Profiles are contexts, not quota pools.** No string anywhere may suggest changing accounts
  because one hit its limit. Monitoring two subscriptions with each one's own token is reading what
  you own; nudging somebody to hop when a window maxes out is limit circumvention wearing a
  convenience costume, and it would contradict the README's own terms section.
- **Don't swap the UI stack.** WinForms owns the tray icon, WPF owns windows, both on one STA thread.
  No imperative `Dock=Top` stacking; no hardcoded hex for theme-able surfaces.
- **No second source of truth for the version** — `<Version>` in `ClaudeTray.csproj` only.
- **No usage annotation on memory files** (settled by dropping T85). T75 annotates skills and agents
  because an invocation is a real tool call; a recall is the harness injecting a file into the
  conversation, and the app never reads message content (§I.1). The task was parked pending a
  structured signal, and the signal was then looked for rather than waited on: across 530 local
  transcripts (195,451 lines) the harness records four kinds of provenance — `attributionSkill`,
  `attributionAgent`, `attributionMcpServer`, `attributionMcpTool` — and sixteen attachment kinds
  including `skill_listing` and `agent_listing_delta`, and **none of them concerns a memory**. No
  `memor*`/`recall*` field exists at all; the only memory paths recorded are in
  `file-history-snapshot`, which tracks files Claude *wrote* — the opposite signal, and one that would
  flag the memories being maintained as the ones in use. So memory rows stay blank, and a wrong "never
  used" — the one error an advisor must not make — stays impossible by construction. Reopen only if a
  memory analogue of `attributionSkill` appears; the re-check is written down on `UsageEvidence`.
- **No activity-aware tray notification** (settled by T87). The nudge threshold is the wall-clock
  verdict and stays that way: the shaped projection changed what the *chart* says, not what is worth
  interrupting somebody for, and a second, softer notification channel would need its own
  justification rather than inheriting this one's.
- **No live hint on the tray icon** (settled by dropping T101). An animating icon draws the eye
  continuously, and — decisively — it would need a transcript tail running for the whole session.
  T99 deliberately made the tail *window-owned* so a closed Statistics window watches nothing;
  a permanent tail to power an ambient nicety would undo the one property that keeps the feature
  free when nobody is looking. See [CHANGELOG.md](CHANGELOG.md) Block K.
- Pricing/distribution/positioning discussion goes in [STRATEGY.md](STRATEGY.md), never as a
  numbered task.
