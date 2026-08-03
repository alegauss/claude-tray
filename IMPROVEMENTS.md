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
| [§XIX](#xix--six-surfaces-shipped-and-what-nothing-was-checking-block-af) | Six surfaces shipped, and what nothing was checking (Block AF) |
| [§XX](#xx--the-interaction-check-grew-two-cases-and-nobody-runs-it-block-ag) | The interaction check grew two cases, and nobody runs it (Block AG) |

> Block I's design sections (§II), Block J's (§IV), Block V's (§XII), Block Z's (§XIII), Block AA's
> (§XIV) and Block AD's (§XVII) are gone: every one of them shipped, and `git log` plus
> [CHANGELOG.md](CHANGELOG.md) are the history. §III stays because it is a **measurement** — the
> numbers the rule thresholds and the base-overhead constant were calibrated against.
>
> Section numbers are **never reused**: §II, §IV–§XIV and §XVII have all been retired, so a new block
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

## §XIX — Six surfaces shipped, and what nothing was checking (Block AF)

Block AE was built in one session, and the same thing kept happening: the tool that should have caught
a mistake was absent, broken, or disagreeing with its twin. None of these was reported by anybody, and
none is a Block AE task — they are what building it cost.

Three are gaps where a rule exists and nothing holds it (§XIX.1, §XIX.5, §XIX.6): a string reaching
one language, a chart rule held by a screenshot, a Settings branch never rendered. Two are the preview
tooling misleading whoever runs it (§XIX.2, §XIX.3) — and one of those produced a plausible-looking
capture of the wrong thing, which is worse than a crash. Two are what six new surfaces cost the things
around them (§XIX.4, §XIX.7): a colour that now means two opposite things, and a map at its ceiling.

The pattern worth naming, because it is the one that recurs: **each was found by doing the task, not by
checking it.** The capture crash surfaced because a directory happened not to exist; the argument
divergence because a chart's numbers looked wrong; the colour collision because the same hex was typed
twice in one afternoon. A verification loop nobody exercises is indistinguishable from one that passes.

### XIX.1 Five files, one pair of eyes

Block AE added nineteen keys across `en`, `pt-BR`, `pt-PT`, `es` and `fr`, every one by hand, in a
repository whose own rule is *new user-visible strings go into all five*. The rule is right and
nothing enforces it: a key that lands only in `en` falls back silently, so the app is correct in
English and quietly untranslated everywhere else until somebody opens that screen in that language.

`--lang <code>` is the stated verification, and it is a person remembering. This block ran it for
three languages and never for the two Portuguese variants — exactly the sampling that lets a gap
live.

The check belongs in `--selftest`, which is already this repository's test suite: load the five
embedded resources, assert the key sets match, name what is missing. Two details decide whether it
is worth having. It must compare **placeholders** too — `{0}` present in one language and absent in
another is a formatted string that silently drops a number, which no key-set comparison sees. And it
must name the offending key rather than report a count, because a count is something people learn to
live with.

What it must not become is a translation-*quality* check. Whether the Portuguese reads well is not
something an assertion can hold, and pretending otherwise would make it untrustworthy in the one way
that matters.

### XIX.2 Two flags, one preview, no overlap

`--stats <variant>` and `--capture-stats <out> <variant>` are the interactive preview and the
screenshot of the same window, and they parse their variants in two separate `if` chains. A variant
added to one is not merely missing from the other: it is **ignored**, and the capture falls through
to the default sample.

Measured while building T183. `--capture-stats … overage` produced a chart of this machine's real
38% week, correctly rendered, with no error and no warning. It was caught only because the number on
the card was not the number the fixture specifies — had the synthetic value happened to resemble the
real one, a screenshot of the wrong thing would have shipped as evidence of the right one.

That is the sharp end: a crash is self-reporting, and this is a preview that lies quietly. Anything
that captures the window for verification has to be showing what the flag names, or the loop this
repository leans on for UI work is worth nothing on exactly the surfaces that are new.

The shape is one variant table both entry points read, so adding a preview is one edit and the two
cannot drift. A cheaper stopgap — refusing an unrecognised variant instead of silently defaulting —
would have turned this into an error message rather than a wrong picture.

### XIX.3 A capture that dies after it has drawn

`StatisticsPage.SaveSnapshot` calls `File.Create` on the path it is given, and nothing creates the
directory above it. Capturing into a folder that does not exist yet throws
`DirectoryNotFoundException` — from a `DispatcherTimer` tick, so it surfaces as an unhandled
exception with a WPF stack, after the window has already rendered and the expensive part is done.

Every sibling does the opposite: `PreviewCli.RenderTest` opens with `Directory.CreateDirectory`, and
`UsageHistory.Append` creates its own parent on every write. This one call site is the exception.

It is a one-line fix and it goes on the list for the same reason as §XIX.2: what a person is doing
when they hit it is *verifying a change*, and a stack trace in the middle of that reads as "the
change broke the app" rather than "the folder was new". The cost is a wrong diagnosis on the surface
where somebody is already suspicious.

Worth doing once for the whole family — `--capture-settings` and `--capture-toast` take output paths
too, and neither has been tried against a fresh directory.

### XIX.4 The same clay for opposite news

T184 gave the extra-usage toast the brand clay, deliberately: the tray icon's bar (T182) and the
weekly chart's second axis (T183) already wear it for *past the included quota*, and one fact should
look the same everywhere it appears.

The collision is that `ToastTheme.Surprise` — the weekly limit resetting **early**, unambiguously
good news — has used the same clay since the toasts shipped, as the `_ =>` fallback of the gradient
switch. So the notification colour vocabulary now says clay for both "you got quota back ahead of
schedule" and "you have started paying", which are as close to opposite as this app has.

Both claims are individually reasonable and that is what makes it a design decision rather than a
bug to squash: either the app-wide clay yields on the toast surface, or Surprise moves. Surprise
moving is the smaller change and the better one — its clay was a default rather than a choice, while
T182 and T183 picked theirs for a stated reason, and *Bonus* (violet) already shows the good-news
family has room. But it repaints a shipped notification, so it is worth saying out loud rather than
doing quietly.

Whichever way it goes, the published `notify-surprise.png` and `notify-extra.png` are then two cards
of the same colour on one page, which is the form the reader actually meets.

### XIX.5 A chart rule held by one screenshot

T183's series obeys a rule that matters: a stored reading carrying **no** overage figure is skipped,
not plotted as zero. Reading absent as zero would draw a floor along the bottom of the chart that
nobody measured — the same `absent ≠ zero` distinction T179 built the store around, one layer up.

Nothing asserts it. `FillCurve` is `private` inside `UsageReport`, reached only through
`ComputePace`, which wants a real profile and its transcripts; the drawing itself is WPF and
off-limits to a headless run. So the rule is held by the fixture screenshot taken once while
building it — and a fixture that contains no nulls cannot demonstrate the null path at all, which
means even that screenshot does not show it.

The same shape as §XV.2: a real decision living where `--selftest` cannot reach. The fix is the same
too — lift the series-building to something callable with a `List<UsageSample>` and a window, and
assert the three cases directly (nulls skipped, a measured zero kept, `ExtraMax` over the kept
points only). None of that needs a profile, a chart, or a screen.

It is worth doing beyond overage: `Curve` and `Gaps` are built in the same method and asserted by
nothing either.

### XIX.6 The branch that shipped unseen

T182 appends to the System page's extra-usage row whether the allowance is *in use* — "Enabled — in
use now (42%)" — read from the profile's own stored history rather than a live call.

Only the empty branch has ever been rendered. Showing the percentage needs a profile whose
`usage-history.jsonl` carries an overage figure, and there is none: this machine's accounts are
`org_level_disabled`, and the work profile's history predates the field, so it takes the null path.
`AccountFixture` builds two config directories and no stores at all, so `--settings System --sample`
cannot reach it either.

The layout risk is small — the string is about as long as the token-expiry value in the row three
below it — and small is not the same as checked. This repository's own rule is that a UI change is
not done without a screenshot, and this branch has none.

What it actually asks for is the missing half of `AccountFixture`: a fixture that writes a short
`usage-history.jsonl` for the profile it invents. That unlocks this row, and also gives `--stats` a
deterministic overage series without the `PreviewDemoOverage` seam T183 had to add to the page.

### XIX.7 A map at its ceiling

`AGENTS.md` carries a declared budget — 400 lines, 42,000 bytes — and sits at exactly 400. Block AE
added `src/Usage/HeaderProbe.cs`, `src/Usage/QuotaState.cs` and `src/Cli/ProbeCli.cs`, plus
`--probe`, `--stats overage` and `--capture-toast extra`. None is in the file map or the dev-helper
list, because adding them costs lines the budget does not have.

This was met head-on: correcting the plugin-scope sentence there had to be rewritten twice to land
net-zero, and the operational detail moved to the `roadmap-docs` skill instead. That was the right
trade for one sentence and it does not scale — the map now silently omits whatever ships next, and a
map with holes is worse than a long one, because it is read as complete.

`roadkeep.toml` calls the number "a ceiling to come down, not a target", so the answer is not
raising it. The file's own §I.5 list, the release process and the flag catalogue are all candidates
to move or compress — the flag list in particular is a reference, consulted rather than read, and
reference material is what a per-turn budget is least willing to pay for.

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

### XX.1 Two ProfileCombos, and the check that reads the right one by luck

`ById $win 'ProfileCombo'` has two candidates in `--main`: the Statistics page's picker and the one
on the Settings page's Claude Code panel. `FindFirst` returns whichever the tree reaches first, so
which control a check drives depends on which destinations have been built — and a page is built on
its first visit and then kept collapsed, so the answer changes with the route a run took.

Nothing is wrong today, and that is the defect: `-Case Names` reads the Statistics picker *before*
it navigates to Settings, and a comment saying so is the whole guarantee. `-Case Profiles` is safe
only because it never leaves Statistics. The first case that visits Settings and then looks the
picker up by id will silently drive the other control and go on passing.

Sharing an id is wrong on its own terms too — an automation id is a control's identity, and anything
scripting the window has the same ambiguity a check does. Give each its own, update the lookups, and
state the rule: an `x:Name` here is unique across the window, not per page.

### XX.2 The fallback route quietly un-asserts the switch-back timing

`Combo-Select` tries `SelectionItemPattern.Select()` and falls back to arrow keys on a focused
closed ComboBox. The fallback is deliberate — a dead UIA path would otherwise report a red build for
a defect in the script — and T177's status-line observation is wired to the UIA route only, because
the keyboard route walks the selection through every index on the way and each intermediate stop is
a switch of its own.

So the timing assertion is written to say *not checked* when the route was the fallback. That is
honest, and it is also the exact shape T169 and T176 both name: on the day `Select()` starts
throwing, the check reports a pass on everything else and drops T166 silently, in an Info line
nobody reads.

Two candidate answers. Make the keyboard route reach the target in one hop, so the observation is
valid on both paths — `Home`/`End` plus a known count, or typing the label. Or keep the fallback and
make the downgrade loud: count assertions that did not run and refuse to print `OK - every
interaction check passed` when any did not.

### XX.3 The one case CI could run, and does not

`check.yml` builds and runs `--selftest` on `windows-latest`. It runs no interaction case at all, so
the loop that exists because T135 survived every screenshot ever taken depends on a person choosing
to run it.

Three of the five cases genuinely cannot go there: `Panes`, `Profiles` and `Names` need a rendered
report, which needs credentials and this machine's transcripts. `Menu` needs the notification area.
`Keyboard` needs none of it — it launches `--settings-tray`, clicks a sidebar item, types into a
TextBox, Tabs, and drives a Slider with an arrow key, all against an unconfigured install.

What has to be true for it to work on a runner: a desktop session exists (it does on the hosted
Windows images), the synthesised input reaches a window on a headless-but-present desktop, and the
run is bounded so a hang fails rather than burns the job. Worth measuring before committing to it —
and if the input does not arrive, that answer is itself worth writing down where the next person
will find it.

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
