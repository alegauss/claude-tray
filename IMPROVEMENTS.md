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

## §XVI — The tray reports the switch it performed, not the switch the machine got (Block AC)

The report is a question, not a bug: *"mudei para o Pessoal, mas se eu digito `/usage`, aparece
Trabalho"*, with the inference that `/usage` must be lying. It was not. The session was on the work account
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

### XVIII.9 Three top-level headers nobody reads, and one premise that was wrong (idea)

Beside the three window triples the response carries a fourth, unsuffixed set:
`anthropic-ratelimit-unified-status`, `-reset` and `-representative-claim`, reading `allowed`, the
5h reset and `five_hour`. Nothing parses any of them. That much is measured and still true.

What this section originally claimed — that the projection, the icon and the tooltip each re-derive
which window is binding by comparing 5h against 7d — is false, and reading the call sites is what
settled it. There is no such derivation anywhere. Every surface is scoped to `_metric`, the window
the user picked from the tray menu: the two utilization lines, the at-limit and billing sentences,
the projection, which names its own scope out loud, and `CurrentQuotaState`, whose comment states
the choice — *the metric, not the worst window*, so that a sentence never captions a figure the user
is not looking at. `BlockedUntilUnix` tests both windows against the threshold, which is not the
same question. So there was nothing to unify.

What is left is an idea, and it needs the measurement before the design. `representative-claim`
would let a surface say which limit the API itself considers binding — an answer this app currently
never asks for, and a feature rather than a fix. The vocabulary is one value from one account:
`five_hour`. A mapping written against one sample has a default arm nobody has seen, and T181 spent
a whole task refusing exactly that. The probe records every one of these three, so a second value
arrives as a log line rather than as a guess.

Reading the call sites did turn up a real mismatch one line below the ones checked — §XVIII.12 has
it.

### XVIII.10 A fallback is on offer and nothing here knows the word (idea)

Two headers on every response are entirely outside this app's vocabulary: `unified-fallback`,
reading `available`, and `unified-fallback-percentage`, reading `0.5`. Nothing parses either.

The plausible reading is the interesting one: that past some point a request is served by a smaller
model rather than refused, and that `0.5` is the threshold or the share involved. If that is what it
means, it matters to the one sentence this app exists to get right — *what happens when I hit the
limit* — because the answer would be neither "you stop" nor "you pay", but a third thing the tray
has no state for. That would sit beside the clay bar T182 shipped, not replace it.

It is an idea and not a design because both readings are guesses. `available` and `0.5` are one
sample from one account inside its quota, and a percentage with no stated denominator is the exact
mistake T181 spent a whole task refusing to make. Two headers whose meaning is inferred from their
names is how an app ends up explaining something that was never true.

What is cheap and already done: the probe keys on both — neither ends in `-utilization` or `-reset`,
so a change in either writes a line without a name being added anywhere. Nothing to build for the
measurement; this task begins when a log has something to read.

## XX Verification — the checks that prove a change (Block AI)

This project's checks are three loops with different reaches. `--selftest` asserts arithmetic on
synthetic inputs and runs on every push. `Check-Interaction.ps1` is the only thing in the repository
that reads the *running* UI, and since T194 one of its five cases runs on every push too. The
previews and captures are pictures, and a picture cannot see a key press arrive.

Everything filed here was found by *using* those loops rather than by reviewing them, which is the
pattern worth keeping: the defects a check has are not visible in its source, only in what it
reports on a machine that is not the author's.

One shape recurs and is worth naming once. A check does not usually break loudly — it stops
asserting and stays green. T192 was an id lookup with two candidates that picked by tree order; T193
was a fallback route that turned an assertion into a note; T196 was a rule governing thirty-odd
controls asserted on three. None of them ever went red. That is why the exit codes now distinguish a
degraded run from a clean one, and why an assertion that could have run and did not is named and
counted rather than mentioned.

### XX.6 The refusal is a precondition, not a failure

`-Case Menu` refuses to launch while any ClaudeTray is alive: the single-instance mutex would make
its own launch exit silently, and it would then read the *other* tray's menu and call that a pass.
Refusing is right; reporting it with `Fail` is not, and it was only defensible before T193 because
there was no third outcome to report it as.

Now there is. An already-running tray is an absent **precondition** — the same shape as fewer than
two profiles registered, or a report that never rendered, both of which became `Unchecked` and make
the run DEGRADED at exit 2. The menu refusal did not, so a developer with the tray running gets exit
1 from `-Case All`. That is every developer, because the app is meant to be resident, and what it
teaches is that red means nothing.

Which is the distinction T193 exists to draw. The change is small; what it protects is not, because
T194 has just made CI depend on these codes. Worth settling at the same time whether `-UseRunning`
should be implied rather than merely suggested, since driving the running tray is what the person
almost always wants.

### XX.7 One list of ids is derived, the other is remembered

`Check-Interaction.ps1` finds controls by string (`ById $win 'StatsStatusText'`), which no compiler
checks. T192 renamed three controls and broke one such lookup — the one behind T166's *"the status
line must never be observed"* — and a lookup that finds nothing makes that assertion pass by seeing
nothing. So `--selftest` now asserts that all fifteen ids the script drives still exist.

Fifteen, written out by hand. The list is right today because it was read off the script today, and
nothing keeps it that way: a new `ById` in a new case is absent from it, and its absence looks
exactly like a list that is complete.

The uniqueness half of that same check has the property this half lacks — it reflects over every
`IComponentConnector` type, so a page added later is covered without an edit. The fix is the same
move: derive the list rather than keep it. The script is a text file the check could read, and
`ById`/`ByIdNow` calls with a literal argument are a one-line pattern to match. What that cannot see
is an id built at runtime (`"Used$sfx"`), so those stay explicit and the check should say which kind
it is asserting.

### XX.8 The sweep can see a name, not the right name

`SettingsRow` gives its trailing control the row's header as an accessible name. T196 now reads
every control the rule is responsible for on all six panels — 23 of them — but asserts only that
each announces a non-empty, printable string that is not its own automation id. Three controls still
get an exact-label read, and those three are the only place the *correct* name is verified.

The gap is structural, not lazy. `SettingsRow` is a lookless `ContentControl`, and it reaches UIA as
nothing: there is no element between the panel and the control that says *this control and that text
are one row*. So the check cannot pair a control with its header, and a rule that gave every control
the **wrong** header would pass the sweep.

Giving the row an automation peer — a `Group` carrying `Header` as its name — closes it, and it is
worth more than the check: a screen reader currently reads a flat run of labelled controls where the
visual design is label↔control pairs. Then the sweep becomes per row, which is what §XX.5 asked for
and this settled for less.

### XX.9 The fixture is opt-in on the page that most needs it

T197 stopped the Statistics picker naming a real account on a synthetic chart, and T200 stopped a
`--sample` that could not be honoured from falling back to the real one. Neither touched the plain
path: `--capture-settings System` with no `--sample` at all renders this machine's login, correctly,
and nothing marks the result as unpublishable.

That is the shape of both fixed defects, one step earlier. The fixture exists *because* this page
gets published — masking hides the holder's name and the local part of the address, but the
organization name and the mail domain **are** the reading, and on a real machine the organization is
somebody's client. AGENTS.md states the rule and nothing in the app enforces it.

Two shapes are worth weighing. The capture could **require** `--sample` for the System page, since
no other caller has a reason to render a real account off-screen into a file; that is narrow and
breaks nothing that exists. Or a capture of real data could carry a visible watermark, which is
honest but costs a rendering path and makes the PNG useless for anything else.

The asymmetry that makes the first attractive: interactive `--settings System` *should* show the
real account, because that is a person looking at their own machine. Only the path that writes a
**file** has no such excuse.

### XX.10 The copied rectangle is wider than the window

`GetWindowRect` spans a WPF window's invisible resize border and drop-shadow margin, so the
rectangle the screen copy reads is larger than the area the window paints. Every PNG the script
produces therefore carries a strip of whatever is behind the window down its edges — measured while
verifying T199, where the popup capture came back with slivers of the editor along the left, right
and bottom.

T199 asserted that nothing covers the window's *own* pixels, which is the assertion that matters and
is what stops a wrong picture. This is the remainder: the border pixels do not belong to the window,
so no ownership check can pass on them, and sampling deliberately stays 25% inside the rect for
exactly that reason. The result looks fine at a glance and is wrong at the edges, which is the class
of defect this repository keeps finding late.

`DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` returns the visible frame instead and is the
narrow fix; it is per-monitor-DPI-correct on the same terms the script already sets up. Worth
checking against a maximized window and at 150–200%, since the margin is not symmetric.

Worth noting what this does **not** affect: `--capture-settings` and `--capture-stats` render the
page's content off-screen through a `RenderTargetBitmap`, so they have no border to include and no
such edge. This is a defect of the screen-copy path alone, which is the popup case.

### XX.11 The flag surface is the one thing no check asserts

`--selftest` holds 240 assertions and not one of them is about a CLI flag. Everything it covers is
arithmetic, stores and rules over synthetic inputs; the preview and capture surface — which is what
every visual verification in this repository goes through — is protected only by somebody running it
by hand and reading the result.

Block AH added three refusals to that surface, and each is exactly the kind of thing that regresses
in silence: `StatsPreviews` and `ToastPreviews` refusing an unknown name instead of rendering the
default, `AccountFixture.ResolveWeek` refusing an unknown week, `--capture-toast` requiring its
output path, and `--sample` that cannot be honoured stopping the run rather than falling back to
this machine's real account. That last one is a privacy guard whose failure mode is a published PNG,
and nothing would notice it being removed.

All four are cheap to assert because they are **pure**: a table lookup and a resolver that return a
value or null, no window and no file. `SelfTestCli` is the repo's test suite (§I.3 rules out a test
project), so they belong there — one section, asserting that every documented variant resolves, that
an invented name resolves to null, and that the two tables' rows match the names the skill catalogue
prints.

The last of those is the one with reach: it turns the "write it down in `dev-flags`" convention into
something a build can check, which is the only version of that convention that survives.

### XX.12 The most-seen surface is the one nothing can photograph

Every published picture in `docs/` comes from a flag: `--capture-stats`, `--capture-settings`,
`--capture-toast`, `--render`, `--social`. One does not. `docs/tooltip.png` is a hand-taken
photograph of the real Windows notification area — this machine's taskbar, this machine's icons —
and it is the README's hero image, second only to the icon itself.

So it drifts silently. T213 changed the last line of the tooltip in five languages and that picture
still says `Status: allowed`, a form the app no longer produces. Nothing failed, because nothing
looks.

The obstacle is real and worth naming, because it is why this was never built. A tray tooltip is not
a window: it is `NOTIFYICONDATA.szTip`, drawn by the shell, and it appears only when a pointer rests
over an icon that may itself be inside the overflow flyout. There is nothing to `SaveSnapshot`. Two
routes exist, and they answer different questions. A **read-out** — print the composed text for a
synthetic reading and a given metric — is cheap, needs no screen, and is what a check should assert;
it does not produce a picture. A **render** — draw the same text into the same rounded card the
shell draws — produces a publishable picture that is honest about the words while admitting it is a
mock-up of the chrome.

The read-out is the one that pays for itself: it makes the tooltip's own composition reviewable at
all, which §XX.13 is about, and it makes the picture's staleness detectable by comparison instead of
by someone remembering. Take it first, and take the render only if the README still wants a
photograph.

### XX.13 The tooltip's own composition is decided where no check can see it

`BuildTooltip` is 80 lines of real decisions on an instance method: which lines are added and in what order,
whether the profile label is present, which of three at-limit forms the verdict takes, and — the one that
actually rations — a 127-character budget that admits the projection in its **full** form, else its
**compact** form, else not at all, with a `Truncate(…, 127)` behind it as a backstop.

None of it is reachable by `--selftest`, because a `TrayContext` cannot be constructed headlessly.
That is the same shape as T182 (a three-state verdict living on the tray until `QuotaStates` was
extracted), T189 (the chart's series-building) and T168 (the method note's paragraphs) — and the fix
is the same one: the composition becomes a static over a reading, and the assertions follow.

T213 is why this is filed now rather than noted. It lengthened the status line by roughly eight
characters in five languages, which pushes the projection from its full form to its compact one at
some threshold nobody can name, in some languages before others. The German-style worst case does
not exist here, but French and Portuguese are the longest of the five and neither was measured. A
budget that decides what a user sees, spent by a task that could not check what it spent, is the
definition of a rule held up by whoever next hovers an icon.

Assert the interesting cases once extracted: that the profile label survives when the projection
cannot, that the compact form is chosen rather than the line dropped whenever it fits, that no
composed tooltip in any of the five languages exceeds 127 characters before `Truncate` sees it.
