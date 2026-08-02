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
| [§XVII](#xvii--the-window-can-be-read-now-and-what-that-turned-up-block-ad) | The window can be read now, and what that turned up (Block AD) |
| [§XVIII](#xviii--extra-usage-is-money-and-the-tray-is-asleep-for-it-block-ae) | Extra usage is money, and the tray is asleep for it (Block AE) |

> Block I's design sections (§II), Block J's (§IV), Block V's (§XII), Block Z's (§XIII) and Block AA's
> (§XIV) are gone: every one of them shipped, and `git log` plus [CHANGELOG.md](CHANGELOG.md) are the
> history. §III stays because it is a **measurement** — the numbers the rule thresholds and the
> base-overhead constant were calibrated against.
>
> Section numbers are **never reused**: §II, §IV–§XIV have all been retired, so a new block takes the
> next unused numeral rather than the next free-looking one. A `→ §V` in an old commit message must
> keep pointing at what it pointed at.

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

## §XVII — The window can be read now, and what that turned up (Block AD)

Every verification loop this repo had before T165 was a **picture**: `Capture-Window.ps1`,
`--capture-settings`, `--capture-stats`, the `preview-ui` skill. T142 added one that *drives* the UI, and
T165 added the first that drives the **Statistics window** and reads its numbers back. Half an hour of
doing that turned up more than the task was about, and the four items here share a cause: a picture can
only be wrong about what it shows, while the accessibility tree can be wrong about *whether the window
exists at all* — and nothing had ever asked it.

The headline finding is already fixed: the segmented-tab `ControlTemplate` never named its content host,
WPF looks it up by `GetTemplateChild("PART_SelectedContentHost")`, and without the name the entire selected
pane was absent from the UI Automation tree. Every number, chart legend, projection sentence and the live
headline. It shipped in T111 and survived to T165 because a screenshot does not use that tree, and neither
did anything else in this repo. What follows is the same lesson at three smaller scales.

### §XVII.1 Two unnamed controls, and they are the two that matter (T175)

The control-view dump taken while building T165 reads, verbatim:

```
ProfileCombo   ComboBox   off=False  ''
PanesBody      Tab        off=False  ''
MethodInfo     Button     off=False  ''
RefreshButton  Button     off=False  'Refresh'
CloseButton    Button     off=False  'Close'
```

`RefreshButton` and `CloseButton` announce themselves because a WPF `Button` derives its accessible name
from its `Content`. `ProfileCombo` has no content to derive from — its label is a *separate* `TextBlock`
sitting beside it in the card, which the automation tree has no way to associate — and `MethodInfo` is a
glyph-only `ToggleButton` whose content is the Segoe MDL2 codepoint for ⓘ. So the control that switches
which account the entire report is about, and the control that opens the note explaining every number in
it, both announce as *"combo box"* and *"button"*.

`PanesBody` is not the same problem: a `TabControl`'s name is carried by its items, and those read
correctly (*"5-hour session"*, *"Week (7 days)"*, *"Throughput"*).

This is `AutomationProperties.Name`, not new copy — `stats.profile` already exists in all five languages
and the method note already has a header string. The task is to bind them, and to decide whether the rule
generalises: a control whose label lives in a *neighbouring* element is the pattern to look for, and the
Settings page is full of exactly that shape (`SettingsRow` puts a label and a control side by side).

### §XVII.2 The pane-in-the-tree check is trapped behind a two-profile precondition (T176)

`-Case Profiles` is now the only thing in the repo that would notice the tab body leaving the accessibility
tree again — and it opens by asking `--profiles` for a count and skipping, loudly but completely, below
two. That is right for the round trip, which cannot exist with one profile. It is wrong for the property
that made the round trip readable, which has nothing to do with profiles at all.

The practical effect is that the check protecting the regression T165 just fixed does not run on a
single-profile machine, which is most machines and every CI runner — the same shape as T169's skip in Block
AB, arriving from the opposite direction. Splitting the assertion out (the window opens; the panes are in
the tree; a used %, a reset caption and a live headline can be read) gives the one-profile machine a real
check where it currently gets a stated skip, and leaves `-Case Profiles` to be about the switch.

### §XVII.3 The timing T166 claims is asserted nowhere (T177)

T166's entire claim is a timing: coming back to a profile seen seconds ago must not blank the panes. It was
verified properly — a probe that switches and then polls the tree every 60 ms, run against a build with the
cache and one without: *status line at 162 ms, panes back after 961 ms* without, *status line never shown,
panes readable after 12 ms* with. Both numbers are in the T166 changelog entry, and the script that produced
them was a scratch file.

That is the situation T142 and T165 were both created to end. The property is cheap and sharply defined —
on the switch **back**, `StatusText` must never become visible — and it belongs beside the round-trip
assertions that already drive the same picker. Worth stating in the check itself: this is the one assertion
in the file that would fail on a *slow* machine for a correct reason if it were written as a deadline, so
it must be written as "the status line was never observed", not as "the panes returned within N ms".

### §XVII.4 A timeout that reports the opposite of what it saw (T178)

`Read-ProfileStop` polls for either the panes or a settled status line, treating `stats.computing` as
"still in flight", and on expiry returns `nothing` — which the caller reports as
*"read NOTHING - no panes and no status line after 25s"*.

On the first real run of the case that message was false in the way that costs the most time. The status
line was up for the whole 25 seconds, saying *"Computing your consumption pace…"*, and the actual fault was
the missing `PART_SelectedContentHost` — nothing to do with timing, and nothing the message pointed at.
Diagnosing it took a separate throwaway script to dump the tree, which is precisely the work the script is
supposed to have already done.

Two things to fix, and they are one task because the second without the first is still misleading: the
timeout must report **what it last saw** (the status text, or that the tree held neither), and it must
distinguish *the window was working and did not finish in time* from *the window showed nothing at all*.
The first is a slow machine or a cold transcript cache; the second is the failure this script exists for.
Only the second should read as the same kind of failure as an empty menu read (T142's founding trap).

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

### §XVIII.1 A reading with nowhere to be written (T179)

`UsageSample` is `(T, Util5h, Reset5h, Util7d, Reset7d)`, the JSONL line is `{"t","u5","r5","u7","r7"}`,
and `PaceSnapshot` carries the same four. The overage figure survives one render of one tooltip and is
gone. Measured in the tray's own store, on the reporter's machine:

| Measurement | Value |
|---|---|
| Stored readings, work profile (`acct-8ba439df4d4b`) | **1,403** |
| Of those, weekly utilization at 97–99% | **178** |
| Peak weekly utilization recorded | **0.99** |
| Overage figures in the file | **0** — the line format has no field for one |
| Longest run at 0.99 weekly | ~30 min of 180 s polls, 5h window at 0.02 and climbing |
| `hasExtraUsageEnabled`, `~/.claude` vs `~/.claude-pessoal` | `true` vs `false` — no behavioural difference |

That last run is *work past the included limit*, sampled 178 times and recorded nowhere. It goes first
because it is the only irreversible task: no later one recovers those readings. The peak stopping at 0.99
is the luck that kept T180's defect out of this data set.

The trap is `ApiClient`'s own, one layer down. A header-less HTTP 200 parsing as utilization 0 is read by
the burn tracker as usage collapsing, firing a phantom reset and wiping the history; an older line with no
overage field is that hazard in the store rather than the wire. *Absent* must stay distinguishable from
*zero* — zero is a real reading, and the one whose first departure §XVIII.6 notifies on.

### §XVIII.2 The one stretch that matters is the one that is not polled (T180)

`BlockedUntilUnix` is careful, correct code resting on a premise that has an exception nobody applied:

> When a limit is hit, usage is blocked and consumption is frozen until that window resets, so polling on
> the normal cadence just burns API calls to read the same 100%.

True without extra usage. False with it — and the tray *knows which* it is looking at, because
`ClaudeInfo.ExtraUsage` was parsed for the System page. The session does not stop, so the value being read
is not "the same 100%": the interesting number has moved to another header, and the tray sleeps for up to
seven days rather than reading it.

The consequence compounds with §XVIII.1. The chart does not show a flat line through the overage period —
it shows **nothing at all**, because the sampler that would write the points is idle. The one stretch
where a user spends real money is the one with no data, and it will look, later, like the app was closed.

The fix gates the idle on its actual precondition rather than on a threshold. Cadence is a genuine
question: the idle exists because re-reading a frozen number is waste, and the API-cost story (the
Settings estimate, multiplied per profile) is a real commitment. So this is not "remove the idle" — it is
"an account that can still spend is never blocked", with the same cost accounting applied to whatever
state replaces it.

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

### §XVIII.4 Two states modelled where there are three (T182)

`AtLimitThreshold = 0.995` drives three behaviours — the icon's alarming red, the tooltip's "already
maxed" sentence, and the sleep — and all three encode one binary: under the limit, or stopped. The third
state has been in the model all along, used by one line of `SettingsPage.System.cs`.

The states are **inside the included quota** (today's normal), **past it and billing** (this block), and
**genuinely stopped** (no extra usage, or the allowance itself exhausted). The app draws the first and the
third, and it draws the second as the third.

The design problem is the icon, and it is real at 16×16. Red is spoken for: it means *danger*, warming as
usage approaches 100%, which is a pace signal. "You are spending money" is not more urgent than that — it
is a different kind of fact, and a hotter red would say *worse* where it means *other*. T147's accent band
established that the icon can carry a second, categorical channel without disturbing the first, and that
is the shape worth exploring; the constraint is that somebody who deliberately enabled extra usage and is
comfortably using it must not be alarmed every time they glance at the taskbar.

The Settings page has the matching gap and belongs to the same task: the System row says extra usage is
*enabled*, a property of the account, and nothing about whether it is being *consumed* — which is the
question somebody opening that page mid-doubt has.

### §XVIII.5 A chart that cannot draw the thing being asked about (T183)

The weekly chart's story is *here is your curve, here is the pace line, here is where it meets 100%*. For
an account in overage that story ends before the interesting part: the curve flattens against the ceiling,
the projection reports the window exhausted, and the quantity that is still moving is not on the canvas.

Once T179 stores the series this becomes an honest design question rather than a plumbing one, and it has
a sharp constraint. The two existing series are fractions of a fixed cap, which is what lets them share a
0–100% axis and what makes the gridlines mean something. Overage — pending T181 — may have no comparable
cap. Putting an uncapped series on a capped axis is how a chart tells its first lie, so the choice is
between a second axis, a separate pane, or a presentation that is not a percentage at all.

Worth stating what this is *not*: it is not a second projection. The weekly projection answers "when do
you run out", and for an account in overage the answer has already happened. Projecting *spend* forward is
a different feature with different obligations (it would be the app making a claim about money), and it is
deliberately outside this task.

### §XVIII.6 The transition nobody is told about (T184 — idea)

The tray notifies on resets — good news, opt-in per window, with a floor so a trivial reset does not ping.
It has no notification for the one transition that costs the user money, and that asymmetry is the whole
observation: the app interrupts you to say *you got something back* and stays silent when *you started
paying*.

The shape is a one-shot on the first reading with overage above zero after a stretch at zero — the
`absent ≠ zero` distinction §XVIII.1 exists to preserve, and what §XVIII.2's polling gap would otherwise
hide. `ToastWindow` is the mechanism; `ToastTheme.Context` already proved a card can carry a
non-celebratory fact.

It stays an idea because two constraints have to be settled before it is designed, not after.

**It needs its own justification.** The T87 non-goal is explicit that a second notification channel argues
for itself rather than inheriting the reset channel's argument. This one looks strong — a real state
change, once, about the user's own money, never on a timer — but "looks strong" is what that non-goal
exists to interrogate.

**It must not become a hop suggestion.** It fires when a user is most receptive to *the other profile
still has quota*, and that sentence is forbidden. §XVI.4 refused the same temptation in its stronger form,
and the reasoning transfers: the constraint is violated by implication, not only by wording, so a toast
mentioning the account at all is near the line.
