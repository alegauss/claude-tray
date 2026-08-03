# Claude Code Tray — Design rationale (IMPROVEMENTS)

> The **what/why** behind *unshipped* work. Status lives **only** in [ROADMAP.md](ROADMAP.md) and
> [CHANGELOG.md](CHANGELOG.md) — never put ✅/📋 markers in this file's prose.
>
> **When a task ships, delete its design subsection here.** `git log` is the history. Letting
> shipped implementation reports accrete in this file is the single failure mode it exists to avoid.
>
> Sections are Roman-numbered and referenced from the roadmap as `→ §III` — `ref_scheme = "outline"`
> in [roadkeep.toml](roadkeep.toml), because a number is never reused and an old commit's `→ §V` has
> to keep pointing where it pointed. A section is budgeted in words there too, and `roadkeep lint`
> exits 1 on a pointer that resolves to nothing.

| § | Subject |
|---|---|
| [§I](#i--house-constraints) | House constraints (binding, app-wide) |
| [§III](#iii--measured-baseline-context-load) | Measured baseline for the context feature (kept: it is data, not design) |
| [§XV](#xv--what-block-zs-own-work-left-behind-block-ab) | What Block Z's own work left behind (Block AB) |
| [§XVI](#xvi--the-tray-reports-the-switch-it-performed-not-the-switch-the-machine-got-block-ac) | The tray reports the switch it performed, not the switch the machine got (Block AC) |
| [§XVIII](#xviii--extra-usage-is-money-and-the-tray-is-asleep-for-it-block-ae) | Extra usage is money, and the tray is asleep for it (Block AE) |
| [§XX](#xx--the-interaction-check-grew-two-cases-and-nobody-runs-it-block-ag) | The interaction check grew two cases, and nobody runs it (Block AG) |
| [§XXI](#xxi--what-block-afs-own-captures-turned-up-block-ah) | What Block AF's own captures turned up (Block AH) |

> Block I's design sections (§II), Block J's (§IV), Block V's (§XII), Block Z's (§XIII), Block AA's
> (§XIV), Block AD's (§XVII) and Block AF's (§XIX) are gone: every one of them shipped, and `git log` plus
> [CHANGELOG.md](CHANGELOG.md) are the history. §III stays because it is a **measurement** — the
> numbers the rule thresholds and the base-overhead constant were calibrated against.
>
> Section numbers are **never reused**: §II, §IV–§XIV, §XVII and §XIX have all been retired, so a new block
> takes the next unused numeral rather than the next free-looking one. A `→ §V` in an old commit
> message must keep pointing at what it pointed at.

---

## §I — House constraints

Binding for every task. These are product decisions, not preferences — a task that violates one is
wrong even if it works.

### §I.1 Privacy promise

The app reads only what Claude Code already stores locally, and from transcripts **only** usage
counts, model ids, flags, tool/skill *names* and the session `cwd` — **never message content**. This
is the app's whole trust model. Any new reader of `~/.claude` inherits it.

### §I.2 No network beyond two endpoints

The usage API (`ApiClient`) and GitHub Releases (`Updater`). Nothing else — no telemetry, no
analytics, no crash reporting. Every scan and every computation is local.

### §I.3 Zero extra dependencies

The single self-contained `.exe` + installer story depends on the app having no third-party
packages. WPF's built-in Fluent theme, GDI+ and `System.Text.Json` are the toolkit. A task that
wants a NuGet package needs to justify breaking this first (see §I.5).

### §I.4 Read-only against `~/.claude`

The app observes; it does not manage Claude Code. It never edits `CLAUDE.md`, memories, skills,
hooks, MCP servers or permissions.

**Settled with T77: there is no exception.** The archive-with-undo that was under design (move files
to `memory/.archive/<date>/`) was deliberately *not* built. What shipped instead is a generated
cleanup prompt: the app writes down what it measured — paths, sizes, token estimates — and hands that
to Claude Code, which can read the files and decide. Measuring is a read; rewriting somebody's memory
directory on a size heuristic is a different risk class, and the tool that can actually read the file
should be the one to touch it. Revisit only with a concrete reason the copy-a-prompt path cannot
cover.

### §I.5 What NOT to build (binding non-goals)

- **No tokenizer dependency.** Token counts stay estimates with a visible "≈". A real
  tokenizer would mean a native/managed dependency and break §I.3.
- **Not a memory editor / not a config manager.** Measure and advise; hand the edit to Claude.
- **No content display or export**, anywhere, ever — sizes, names, frontmatter, timestamps, counts.
- **Don't swap the UI stack.** WinForms owns the tray icon (WPF has no native tray support); WPF owns
  windows. Both live on one STA thread pumped by `Application.Run(new TrayContext())`. This is
  settled — see [AGENTS.md](AGENTS.md).
- **Don't stack imperative layout.** Layout is declarative XAML with explicit rows/columns; the
  original WinForms sidebar overlapped precisely because it relied on reverse `Dock=Top` add-order.
- **No hardcoded hex for theme-able surfaces** — `{DynamicResource}` Fluent brushes only, so light,
  dark and the system accent all follow for free.
- **No second source of truth for the version.** `<Version>` in `ClaudeTray.csproj`; everything else
  derives from it.

### §I.6 Never swap a credential or config file

Block O switches profiles by launching Claude Code with a different `CLAUDE_CONFIG_DIR`, never by
moving files around. The alternative was measured and refused: the access token lasts 8 hours and
the refresh token 5 days, `.credentials.json` is rewritten on every refresh and `.claude.json` is
rewritten constantly, so a copy-and-restore scheme races Claude Code's own refresher. It also splits
one identity across two files, and the failure mode is the tray being the thing that broke
somebody's login. §I.4 has no exception, and credential material is the last place to invent one.

### §I.7 Profiles are contexts, not quota pools

No string anywhere in the app may suggest changing accounts because one hit its limit. Monitoring
two subscriptions, each with its own token, is reading what you own; nudging somebody to hop when a
window maxes out is limit circumvention wearing a convenience costume, and it would contradict the
README's own terms section. The constraint is violated by implication as readily as by wording —
§XVI.4 refused a quota bar animating one account's remaining quota into another's, on the grounds
that it says the forbidden sentence without a string.

### §I.8 No usage annotation on memory files

T75 annotates skills and agents because an invocation is a real tool call; a recall is the harness
injecting a file into the conversation, and the app never reads message content (§I.1). T85 was
parked pending a structured signal, and the signal was then looked for rather than waited on. Across
530 local transcripts (195,451 lines) the harness records four kinds of provenance —
`attributionSkill`, `attributionAgent`, `attributionMcpServer`, `attributionMcpTool` — and sixteen
attachment kinds including `skill_listing` and `agent_listing_delta`, and none of them concerns a
memory. No `memor*`/`recall*` field exists at all. The only memory paths recorded are in
`file-history-snapshot`, which tracks files Claude *wrote* — the opposite signal, and one that would
flag the memories being maintained as the ones in use. So memory rows stay blank, and a wrong "never
used" — the one error an advisor must not make — stays impossible by construction. Reopen only if a
memory analogue of `attributionSkill` appears; the re-check is written down on `UsageEvidence`.

---

## §III — Measured baseline (context load)

Taken from one real developer machine (33 project dirs, 19 with memory). Project names are withheld
deliberately — this file is published with the repo. The right-hand column is the rule (now shipped in
`ContextRules.cs`) the observation justifies.

| Observation | Value | Rule it proves |
|---|---|---|
| Heaviest memory dir | 122 files / 316 KB | size rules |
| `MEMORY.md` index (eager, every session) | 20 KB ≈ 5.4k tokens | index size |
| Index pointers vs memory files | 121 pointers / 122 files, none dead (the extra index *lines* are headers); one unindexed memory in another project | stale pointers / orphan memories |
| Largest single memory file | 18 KB | "one fact" |
| Memory dirs byte-identical to a sibling | 2 pairs | duplication |
| Project dirs whose path no longer exists | 2 (+1 that is not a path at all) | orphans |
| `type:` distribution | 93 project / 12 feedback / 2 reference / 0 user | frontmatter |
| Plugin skills available | 31 `SKILL.md`, largest 33 KB, ≈3.1k tokens of eager descriptions | the eager/lazy split — bodies are lazy, the 31 descriptions are not |
| `settings.json` | 87 KB | settings bloat |
| Base overhead (system prompt + tools + MCP) | ≈32k tokens, p25 30k / p75 34k | shown as its own gauge segment, never folded into a project's number |
| Heaviest scannable eager load | ≈22k tokens (a 43 KB `AGENTS.md` + 20 KB index) | the hero number the gauge shows |

---

## §XV — What Block Z's own work left behind (Block AB)

Block Z was five tasks about what the app *says* and what protects what it says. Building it surfaced four
things that belong to none of them, and the reason to write them down as a block rather than as stray
fixes is that all four are the same failure of attention: **the thing you are looking at is not the thing
you are checking.** Two were on screen in the very screenshots taken to verify the block (a number in the
wrong locale, a paragraph that has quietly become twelve lines); two are in the checks written to protect
it (a decision that lives where no assertion can reach it, a skip that may never have run outside one
machine). None was reported by anybody, and none of them would be.

### §XV.1 One window, two number conventions (T167)

`StatisticsPage.Format.cs` exists to make the report locale-independent: `Pct`, `Tps`, the token and byte
helpers all end in `.ToString(…, Fmt)` with `Fmt = CultureInfo.InvariantCulture`, thirteen call sites of
deliberate consistency. The method note's five interpolations do not go through any of them — they are
bare `$"{value:0.#}"`, which formats with `CurrentCulture`.

It was sitting in both verification screenshots of T159 and T163: on a pt-BR machine run with `--lang en`,
the English popup reads *"4,7 weeks of local transcripts, 2 weeks away excluded"* and *"there are 2,1 so
far"*, eight lines above `≈ 1,319 tok/s` and `40%`. Not a localization bug — the *strings* are correct in
five languages — but the one place the window's own rule was not applied.

Two things make it a task rather than a two-line patch. The fix has to pick the rule deliberately: `Fmt`
everywhere is what the rest of the window does and what keeps a screenshot comparable across locales, but
a decimal point in a Portuguese sentence is *also* wrong to a reader, and the app has no precedent for a
per-language numeric culture (`L.Culture` is used only for dates). Either way it should be one helper the
note cannot bypass, not five call sites corrected once. And nothing prevents the sixth interpolation: the
rule belongs where the compiler or `--selftest` can point at it, which is §XV.2's argument as well.

### §XV.2 A note whose composition no assertion can reach (T168)

Which paragraphs the method note contains stopped being a formatting detail during Block Z. There are now
four inputs (shaped or not, mostly-measured or not, thin or not, weeks excluded or not) and a real rule
over them, most of it written *this week*: thin appears only when the shape was declined for **thinness**
and never at the limit or with nothing spent (T163); the away clause appears only when a week was actually
dropped (T159); shaped and thin are mutually exclusive by construction. All of it is inline in
`Render`, in a WPF page, behind a `PaceReport` — so `--selftest` cannot see any of it, and the only
verification these rules have ever had is that somebody looked at two screenshots.

The seam is small: a pure function from `(PaceReport, ActivityProfile?)` to the ordered list of resource
keys (plus their arguments), with `Render` doing nothing but `L.T` and concatenation. Then every rule above
is a check of the kind T161 and T162 just added — cheap, and each one confirmed to fail first. The T163
rule is the one worth pinning hardest, because getting it wrong produces a *plausible* sentence: telling
somebody to keep the tray running another week when the real reason there is no shaped projection is that
they are already at the limit.

This is also where §XV.1's rule would live: a note assembled by one function is a note whose numbers go
through one formatter.

### §XV.3 A skip that fires every time is a check that does not exist (T169)

T161 gated its two `TryProbe` assertions on a precondition — every segment of the temp path spelled in the
alphabet the slug encoding preserves — after the first version of that guard turned a build with
backtracking deliberately removed into two green skips instead of a red check. The rule it produced is in
[AGENTS.md](AGENTS.md) and it is right. The guard is still one some machines fail *every single time*: a
Windows CI runner's profile directory is an 8.3 short name (`RUNNER~1`), whose `~` the encoding maps to
`-`, and no `RUNNER-1` exists. There, the two assertions over the app's lossiest function have never run.

The fix removes the skip rather than documenting it: resolve the temp root to its **long** form before
encoding (walk the path and take each segment's real name — `DirectoryInfo.FullName` does not expand 8.3
and `GetLongPathName` is a P/Invoke this repo would rather avoid). The precondition guard stays for the
genuinely unrepresentable case, because honesty about what a check covers is the point.

The second half is about reading a green run. `Skip` prints its name and reason and the summary prints
`N skipped`, but nothing distinguishes *"this run straddled a DST change"* from *"this environment skips
these two checks forever"*. Naming the skipped checks beside the counts, where the exit code is read,
costs three lines and makes lost coverage legible. Whether CI should **fail** on an unexpected skip is a
judgement to make with the list in hand.

### §XV.4 The note is twelve lines behind a click (T170 — idea)

Measured: the shaped branch of the method note is 238 + 352 + 422 = 1,012 characters, the mostly-measured
branch 1,080, the thin one 913 — one unstructured paragraph of 12px prose, twelve lines in the captures at
560px. T113 moved this text behind an ⓘ precisely because six lines pinned under the panes was too much
screen for something read once; the click did not make the text shorter, and Block Z added to it twice.

It is an idea and not a design because the obvious moves are both wrong. Cutting sentences loses claims
that are load-bearing — the "another machine or claude.ai leaves no transcript here" disclaimer is a
privacy-adjacent honesty about a blind spot, and the T163 paragraph is the one actionable reading in the
window. And splitting the popup per tab (5h / week / throughput) would put the *method* somewhere other
than where T113 decided it belongs, one click from every number it describes.

Worth exploring: whether the note is a paragraph at all or a short list keyed to the numbers it explains
(one line per figure, in the order the window shows them); whether the parts that never change belong in
the README instead, with the popup keeping only what is specific to *this* machine's evidence; and what
the real limit is — a note nobody finishes reading is not more honest than a shorter one, it is less.

---

## §XVI — The tray reports the switch it performed, not the switch the machine got (Block AC)

The report is a question, not a bug: *"mudei para o Pessoal, mas se eu digito `/usage`, aparece VILT
Group"*, with the inference that `/usage` must be lying. It was not. The session was on the work account
and the tray was showing the personal one: **both correct about different questions**, and nothing in the
app connects them.

The first theory was wrong instructively. The suspicion was a race, the write being asynchronous since
T149; measured (§XVI.3), it is not. The variable had never been written *at all* until the Windows-wide
switch was ticked, three minutes after the editor that asked the question had started. **A spinner
verifies the click; what needs verifying is the result.**

Three failure modes hide behind the one symptom, and separating them comes first:

- **A — the write is never attempted.** `SyncEnvironmentProfile` is off (the default), so
  `SyncEnvironmentToPin` returns immediately. This is the one that happened. → §XVI.1
- **B — the write is attempted and its outcome is never read.** `Adopt` returns *accepted*, by design.
  → §XVI.3
- **C — the write lands and the next process still misses it.** A child inherits its parent's environment
  block at creation; the editor's parent was an Explorer from six hours earlier, and Explorer's refresh on
  `WM_SETTINGCHANGE` is a courtesy, not a guarantee. → §XVI.2

C bounds the block: **no registry check proves the next process will see the value.** Hence §XVI.2
displays the effective value continuously rather than asserting it once at the pick.

### §XVI.1 A pick that does half its job and reports the whole (T171)

`SetMonitoredProfile` does two independent things: `AdoptMonitored` (icon, stores, save — always) and
`SyncEnvironmentToPin` (the machine-wide variable — only under a flag). The flag is off by default and
should stay off: T145 chose that deliberately, and an update that silently rewrote somebody's environment
would be a worse bug than this one. The defect is not the default, it is that the user is shown the half
that happened and told nothing about the half that did not.

The menu says "Profile ▸ Pessoal" and the icon changes, which reads as *the machine is now on Pessoal*. It
means *the tray is now watching Pessoal*. Those coincide only when the flag is on, and the app has never
said which regime it is in at the moment the choice is made.

So the pick states its own scope. The design constraint is that this must not become a modal on every
switch — the tray's whole manner is to stay out of the way, and §XIV's lesson about interrupting a menu
click applies. What is wanted is the difference made legible at the point of decision (the submenu item
itself can carry it) plus a route to the other half for somebody who wants it, without a trip through
Settings to discover the switch exists. The "no switching a running session" non-goal supplies the exact
claim the text may make: this applies to sessions started from now on.

### §XVI.2 The effective profile is not on screen anywhere (T172)

Every profile indicator is fed by `MonitoredConfigDir`: the icon and its accent band (T147), the submenu
check mark, the Statistics picker. All answer *whose numbers am I looking at?* — the question the tray was
built for. None answers *which account will the next session use?*, which is what somebody has when
`/usage` surprises them. T145 put the live value on the Claude Code settings page, the right place for a
setting but a row on a page opened deliberately — no help to a person not yet suspecting anything.

The fix reads `EnvironmentProfile.Current()` back where the choice is made and the doubt arises, and — the
load-bearing half — **marks the two as disagreeing when they disagree**. Agreement is the common case and
deserves no ceremony; the divergence is the whole signal.

Per failure mode C, the honest phrasing is about the *variable*, not about any future process: sessions
started from a shell that picked up the change will use it, and claiming more invents a guarantee Windows
does not offer. A stronger source of truth exists — a live session writes its transcript under the config
dir it is actually using, which is how the original investigation was settled — and reading it would name
the account a *running* session is on. That is a larger idea, deliberately not folded in; it would need
its own design, and it must respect §I.1 (paths and existence only, never content).

### §XVI.3 A write whose result nothing reads (T173)

T149's trade was correct and should not be reverted: the registry write plus its `WM_SETTINGCHANGE` sweep
measured 129 ms and 486 ms *idle*, and seconds on a working machine, so carrying it on the UI thread is
what froze the tray on a pick. `Write` queues to the thread pool, `Adopt` settles its bookkeeping up front
and returns *accepted*, and `Apply` swallows the exception because nothing the user waits on depends on
it. The window that leaves open is small — measured against the pick that produced the report:

| Measurement | Value |
|---|---|
| `Environment.SetEnvironmentVariable(…, User)` returns | **87 ms** |
| Value readable back from `HKCU\Environment` | **108 ms** |
| `HKCU\Environment` last write | 2026-08-02 **18:09:36** |
| `%LocalAppData%\ClaudeTray\settings.json` mtime | 2026-08-02 **18:09:36** — the same Save |
| `EnvironmentProfileRestore` / `EnvironmentProfileOwned` | `null` / `true` — the first `Adopt` saw no prior value |
| Editor process launched | 18:06:35, from an `explorer.exe` of 12:39:04 |

So this is not a spinner. Returning *accepted* was true when nothing displayed the result; §XVI.2 makes
something display it, and then a write that threw is a screen quietly disagreeing with itself. `Drain`
already exists for the shutdown path and is the shape to reuse: once the queue is empty, read the variable
back, compare against the intent, and give the mismatch somewhere to go. Bookkeeping, not UI — a
completion signal per queued write, so the outcome reaches whatever wants it, in T174's case the toast.

It must not become a blocking wait (the caller returns at once, exactly as now) and it must not become a
dialog on failure — T145's reasoning holds, that a modal about an environment variable is the wrong way to
interrupt a menu click.

### §XVI.4 The one action with no feedback at all (T174)

Writing a machine-wide setting is the least visible thing the tray does and the only one whose effect
cannot be seen until another process starts. `ToastWindow` is the app's answer to "an event worth
noticing", and `ToastTheme.Context` established that a toast need not be a celebration — same card, same
slide-and-fade, no confetti.

A switch toast is the same category. It fires on §XVI.3's confirmation — **after** the value is read back,
never on the click — and says what was applied: the profile, the effective directory, and the sentence the
"no switching a running session" non-goal requires.

What the toast **must not** do is the decision worth recording. `ToastWindow`'s central metaphor is a quota
bar animating from its old level to its new one, and reusing it — animating the outgoing account's
remaining quota into the incoming one's — is the obvious move and forbidden. The "profiles are contexts,
not quota pools" non-goal says no string may suggest changing accounts because one hit its limit, and a
bar leaping from 0% to 100% says it without a string, in precisely the situation that produced this
report. So: no quota bar and no confetti. If that leaves the card thin, the answer is a smaller card.

Per the T87 non-goal it needs its own justification rather than an inherited one, and it is narrow: only
on an explicit user action, once per action, only to confirm a change otherwise unobservable.

---

## §XVIII — Extra usage is money, and the tray is asleep for it (Block AE)

The report indicts the whole feature: *"apesar de estar em 100% de uso, ainda funcionava, porque estava
com uso extra ativado — mas no Claude Tray isto não aparece evidente, nem no gráfico, nem no tray, em
lugar nenhum"*. The user worked straight through a number the app called a ceiling.

This is a block and not a missing feature because **none of the data is missing**:

| Signal | Where it is read | What is done with it |
|---|---|---|
| `anthropic-ratelimit-unified-overage-utilization` | `ApiClient.FetchAsync` → `UsageData.Extra` | one tooltip line, only when `> 0.001` |
| `anthropic-ratelimit-unified-overage-reset` | → `UsageData.ResetExtra` | the countdown on that same line |
| `hasExtraUsageEnabled` (`.claude.json` → `oauthAccount`) | `ClaudeAccount` → `ClaudeInfo.ExtraUsage` | one text row on the System page |
| `anthropic-ratelimit-unified-5h-status` | `UsageData.Status` | printed verbatim in the tooltip; read by nothing |

Three of the four predate the Statistics window. The gap is not acquisition: **nothing downstream
believes the account can be past 100% and still working** — §XVIII.1 has the measurement.

Two constraints bind. The **privacy promise** is not in tension: rate-limit headers are quota metadata,
§I.1 restricts *transcripts*, and this block adds no transcript reading and no endpoint. The
**"profiles are contexts, not quota pools"** non-goal is in tension throughout — every task makes
overage more visible, and the next sentence, *this account is out, the other has room*, is the one the
roadmap forbids. §XVIII.6 binds hardest; §XVI.4 found the answer's shape: a receipt, not a reward.

### §XVIII.3 Nobody has established what the number denominates (T181)

The tooltip prints `Extra: 42%` beside `Session 5h: 42%` and `Week 7d: 42%`, and the two neighbours mean
"of the quota included in your plan". Nobody here has confirmed what the third means, and the plausible
readings differ materially: a fraction of a spend cap the user configured, of some policy ceiling, or of
nothing at all — in which case a percentage is the wrong presentation and the honest display is an amount.

The same applies to `anthropic-ratelimit-unified-5h-status`, which is read, stored on `UsageData`, printed
raw in the tooltip and consulted by no logic. Whatever it says while an account consumes overage is likely
the cleanest signal in the block, and it is unmeasured.

Hence a probe before any wording: capture the unified headers verbatim from a real account across the
transition, names and values only. It costs nothing (the call is already made), risks nothing against §I.1
(no message content), and it is the difference between the app explaining a number and inventing one.

The alternative is tempting and wrong: shipping "Extra usage: 42% of your extra-usage limit" on a
plausible guess. A tray whose whole value proposition is *this number is trustworthy* cannot afford a
label that turns out to name a different denominator.

## §XX — The interaction check grew two cases, and nobody runs it (Block AG)

Block AD doubled what `Check-Interaction.ps1` asserts: `-Case Panes` and `-Case Names` are new, T166's
timing moved out of a scratch file, and the timed-out read now says what it last saw. Five cases,
roughly a thousand lines, and this file remains the only thing in the repository that reads the
*running* UI. Everything below was found by building or running those cases, and none of it was
reported by anybody.

Two shapes recur. The first is a check that stays green after it has stopped asserting: an id lookup
with two candidates that picks by tree order (§XX.1), and a fallback route that turns an assertion into
a note (§XX.2). Both are T169's defect — a check that does not run — wearing a passing tick instead of
a skip. The second is the cost of a loop a person has to remember: a full run launches the app five
times, three of them for the same read-only window (§XX.4), and nothing runs any of it automatically
even though the case that caught T135 needs no credentials at all (§XX.3). §XX.5 is the coverage the
row rule actually has, which is three controls of the thirty-odd it now governs.

### XX.4 Three cases, one window, five launches

Each case is self-contained by design — any one can be run alone, and `-UseRunning` aside, each owns
the process it drives. That was right at two cases. At five it means `-Case All` pays the launch,
the first WPF layout pass and the wait for the first poll three times over for the *same* `--main`
window, and each of those waits is seconds, not milliseconds.

`Panes` and `Names` are strictly read-only: one reads numbers out of the tree, the other reads
accessible names. `Profiles` drives the picker, which changes what is on screen but leaves the
window in a state either of the others would accept. So one launch could serve all three.

The constraint to keep is that a case must still be runnable alone — the value of `-Case Names` is
partly that it is ten seconds when a name is what you changed. So this is a shared-window helper the
cases opt into when several run together, not a merge of the three into one.

### XX.5 The row rule's real coverage is one panel of six

`SettingsRow` gives its trailing control the row's header as an accessible name unless the control
already announces something. That rule now applies to every row on all six settings panels, and the
check reads three controls on one of them.

Two branches matter and neither is asserted. The one that must *not* fire: a row holding a field and
a labelled Button beside it — `DirectoryBox` with `Browse…`, or the profile row with a ComboBox and
two buttons — where the field takes the header and each button keeps its own text. Getting that
wrong gives three controls the same name, which is worse for a screen reader than one unnamed. And
the nesting branch: a StackPanel inside a StackPanel, which the walk handles and nothing exercises.

Cheap to close: navigate the settings sidebar and assert per row rather than per named control,
driving the panel list the page itself declares so a new panel is covered by existing code. The
stated skip when a panel does not open belongs here too.

## §XXI — What Block AF's own captures turned up (Block AH)

Block AF built five checks and repaired two capture flags, and every item here came out of *using*
that tooling for a block rather than out of reviewing it. None was reported by anybody and none is a
Block AF task.

Three are the capture surface misleading whoever runs it (§XXI.1, §XXI.2, §XXI.3), and they are
ordered first because each produces a file: one that names a real account on a synthetic chart, one
that lands in the working directory under a name nobody asked for, and one that returns a screenshot
of a different application and reports success. A capture is evidence, so a capture that is quietly
wrong is the one defect class this repository cannot afford — it is the same fault T186 was, found
again in two more places once the flags were actually exercised.

Two are gaps left by the block itself (§XXI.4, §XXI.5): the third branch of a row whose second
branch T190 just made visible, and two interaction cases that exist in the script and on no list.

The pattern Block AF named still holds, and this is the evidence for it: **each was found by doing
the task, not by checking it.** The picker leak surfaced because a fixture capture was read closely
enough to notice the name above the chart; the toast path because a file appeared in the repo root;
the window mix-up because the wrong window came back.

### XXI.1 The picker on a fixture is not a fixture

`--capture-stats` runs `statsPage.SetProfiles(ClaudeAccount.Discover(...))` for every variant, the
synthetic ones included, and the picker it fills is the real one. On the captures taken while
shipping T186 it named this machine's monitored account above a chart of an invented week.

That is the exact substitution `AccountFixture` and `ContextFixture` exist to prevent — a published
image is where a real organization name becomes somebody's client's name on a marketing page — and
here the fixture and the leak are in the same PNG. Nothing caught it, because the picker is chrome
around the thing being looked at, and the thing being looked at was correct.

The picker is there for a reason: `profile=1,0` walks it, and that is the T164 round trip. So the
question is not whether to fill it but what it should say, and three shapes are worth weighing. The
interactive `--stats` already does the simplest thing — fill it only for the default variant, since
every other one is a fixture that a switch would replace. `--sample` could fill it from
`AccountFixture` instead, which is what the Settings captures do and the only option that keeps a
two-entry picker in a published shot. Or the capture could refuse `profile=` on a synthetic variant,
which is the narrowest of the three and fixes the least.

Worth checking the other direction while here: `--capture-settings` without `--sample` renders this
machine's real System page, and nothing stops that PNG from being committed either.

### XXI.2 An output path read as a variant

`--capture-toast` takes `<variant> <outPath>`, and with one argument the path is read as the
variant: `--capture-toast D:\tmp\out.png` renders the default toast — an unrecognised variant falls
through to the early-reset one — and writes it to `toast.png` in the current directory. Measured
while checking T187: the file appeared in the repository root, where `run-commit.cmd` stages
everything, one commit away from being published.

Two separate faults, and the second is the one that costs. Reading a path as a variant is the same
defect T186 fixed for `--stats`: a positional argument that means one thing when it is recognised
and something else when it is not, with no error either way. And defaulting the *output* is worse
than defaulting the input, because the input only makes the picture wrong while the output decides
where it lands — a capture flag that writes somewhere the caller did not name is a flag that can
dirty a tree.

The shape T186 already established covers the first half: one table of variants, and a name it does
not know is refused with the catalogue printed. The second half is its own decision. Requiring the
output path is the honest option and breaks nothing that exists, since every caller in `AGENTS.md`,
the `dev-flags` skill and `scripts\` passes one. If a default is kept it belongs under
`docs\_preview\`, which is git-ignored, and never in the working directory.

Worth auditing the family while here: `--render`, `--makeicon`, `--social` and `--context-report`
all default an output path, and only `docs\_preview\` is ignored by git.

### XXI.3 A capture that does not say what it captured

`scripts\Capture-Window.ps1 -Args "--stats","overage"` returned a PNG of the **Settings** window
belonging to a different instance that happened to be open on the desktop, printed `Captured 1760 x
1200 -> …` and exited 0. Measured while shipping T186, and the only reason it was caught is that the
picture was read.

This is the same class as the defect T186 repaired, on the surface that is supposed to verify the
repair. Worse, it is the path `AGENTS.md` sends people to for the one case the off-screen capture
cannot serve — a popup is its own top-level window, so `--stats method` plus this script is the
*only* way to photograph the method note. The check that catches a lying capture is the reader
noticing, which is exactly the assumption Block AF spent seven tasks removing elsewhere.

What it has to do is name what it captured. The script launches a process; the window it photographs
must be one that process owns, and if the wait for that window times out it must fail rather than
fall back to whatever is in front. Printing the window's title and its owning process id in the
success line is worth as much as the assertion, because it makes the next wrong capture
self-reporting instead of silent.

Two adjacent facts belong here. `Check-Interaction.ps1` already refuses to run while another tray is
alive, for precisely this reason, and its `-UseRunning` escape is the shape a deliberate override
should take. And whatever this grows, `--capture-settings` and `--capture-stats` stay the default:
the value of this script is the popup case, not convenience.

### XXI.4 The third branch of the same row

`SettingsPage.System`'s extra-usage row has three branches. Absent — no stored reading, or one
predating T179 — has always been on screen. T190 rendered the second: "Enabled — in use now (42%)",
from a fixture week that spends past its included quota. The third, a **measured zero** reading
"Enabled — not in use", still cannot be produced by anything.

It needs a profile with extra usage enabled and nothing spent, and neither fixture profile is one:
the personal account now carries a week that spends, and the Team seat has `hasExtraUsageEnabled =
false`, so the row answers "Disabled" and never reaches the reading at all. That is T190's own
shape, one branch along, and it is the branch that says the thing a worried user most wants to hear.

Do not solve it by flipping the seat's flag. A seat with extra usage off is a real reading and the
published `system-account.png` documents it; changing that to reach a branch would trade one
unrendered state for another. Two options that do not: a third fixture profile — enabled, measured
zero, no organization — which also gives the picker three entries and exercises a list longer than
two; or a modifier on `--sample` that chooses which stored week the personal profile gets, the way
`StatsPreviews`'s variants choose what the chart is fed.

The second is smaller and composes with what T186 built. Whichever ships, the check is the same one
T190 used: read the row at its rendered width, in English and in the longest translation, because
"Enabled — not in use" is longer than the percentage form it replaces.

### XXI.5 Two cases the section does not mention

`Check-Interaction.ps1` accepts `-Case Keyboard | Panes | Profiles | Menu | Names | All`, and its
own header documents all five. AGENTS.md's interaction section describes three: Keyboard, Menu and
Profiles. Panes (T176) and Names (T175) are named nowhere outside the script.

The invocation list beside them had gone stale in the same way — it said "both cases" and offered
two of the five — which T191 fixed by collapsing it to one parameterised line, so that half can no
longer drift. The bullets did not get the same treatment, because a bullet is not a list of names:
each one states what its case *asserts* and when to run it, and two of them were simply never
written.

That matters more than it looks, because these bullets are how a case gets run at all. A check
nobody invokes is the premise of Block AG's own first task, and the two undocumented cases are the
two that need no second profile — the ones a single-profile machine can actually run.

The cheap version is two bullets in the shape of the existing three: what the case reads, and what
change should send you to it. The version worth considering instead is whether that section should
hold the per-case detail at all, now that the script's header does and the file is at a budget T191
lowered deliberately: the same argument that moved the flag catalogue to a skill applies to five
case descriptions. If it moves, what stays behind is the rule — reading nothing is a FAIL, and a
picture cannot prove a key press arrives.
