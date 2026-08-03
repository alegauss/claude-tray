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

### XVIII.11 The sentence that never fits is the one that matters most

Measured with the read-out T214 added, in all five languages. In the billing state the tooltip
carries four readings — profile or session, week, extra usage, status — and the fourth is the one
that only exists here. It costs about 26 characters in English and 34 in French, and it is spent
before any sentence is considered.

What is left is 17 characters in English against a compact form of 23, and 25 in French against 29.
Neither form fits anywhere. T182 split this line in two on purpose, because 'you have stopped' and
'you are paying to carry on' are opposite pieces of news and the tooltip used to give the first for
both — and the second of those two is the one that is now never shown at all. `atlimit` renders its
sentence; `extra` renders none, and the difference between them on screen is a percentage that reads
the same.

T215 made the budget an assertable property and shed the unwatched window when the readings alone
overrun, which bought French 27 characters and still was not enough. So this is not budget
mechanics, which is why it is filed rather than fixed there.

Three shapes, and they trade differently. The compact form can get **shorter** — 'extra usage is
paying' has fewer words in it than 23 characters, but the words are T182's and shortening them is a
decision about what the news is. The overage line and the sentence can **merge**, since both are
about the same fact. Or the sentence can outrank the window the icon is not about, which the
shedding order already has a place for.

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

### XX.16 The flag that cannot reach the process it drives

Found while verifying T202. With a tray resident, `-Case Menu -UseRunning` is the route the script
itself suggests, and against a pt-BR tray under the default `-Lang en` it produced four FAILs —
'Open' missing, 'Open Claude Code' missing, 'Profile' missing, three nav destinations missing — for
labels that were every one of them on screen, in Portuguese.

Nothing was broken. `-Lang` is passed as `--lang` on the command line, and `-UseRunning` exists
precisely because there is no command line: it attaches to a process somebody else started. So the
parameter is silently inert on the one path that most needs it, and the failure it produces is
indistinguishable from the defect the case exists to catch.

The shape is this block's own, arriving from the other side: not a check that is green while
asserting nothing, but a check that is red while nothing is wrong. Both teach the same thing about
its colour.

Two honest fixes and they answer different questions. The script can **read** the tray's language
rather than assume it — the saved preference is in the settings file the app already writes, and
matching what the process actually uses is the accurate answer. Or `-UseRunning` can **refuse** a
`-Lang` that was given explicitly, which is cheap, needs no file read, and says out loud that this
combination cannot be honoured — the same shape as T205's refusal.

Worth settling with it: whether the labels a case matches should come from the running process at
all, since every one of them is already resolved by `L` inside the app.

### XX.17 Nine points cannot cover a window

T199 decides what lands in the file by asking who owns the pixels, which is the right property:
being in the foreground is a proxy the script cannot even insist on. It asks at nine points — the
centre, the four quarter positions, and since T206 four more a fifth of the way in from each edge's
midpoint.

Nine points is a sample, not a cover, and the gap is not theoretical. The capture taken to verify
T217 — the one the script certified, named the right window for, and reported as a correct copy —
carries two windows of another process across its lower-right corner. Every sample missed them.

What makes this worth a task rather than more samples: adding points moves the threshold without
changing the shape, and the number that finally covers a window is the number of pixels in it. The
property is about a region, and it is being asked about coordinates.

Cheaper answers exist. The **z-order** above the target can be enumerated — walk the windows in
front of it and intersect their rectangles with the one about to be copied, which answers for the
whole area in one pass and names the intruder. Or the copy can be compared against an off-screen
render of the same window where one exists, which is a different guarantee and only available for a
page.

Also worth settling: whether a foreign window overlapping the **edge** of a capture should fail it
or crop it. T206 made the copied rectangle the painted frame, so an edge overlap is now inside real
content rather than inside the border it used to be.

## XXI Numbers in prose — one convention, or a stated split (Block G)

Two surfaces of this app answer the same question differently, and T167's sweep reaches only one of
them. What files here is the rule that picks a convention, and the check that holds every surface to
it.

### XXI.1 Two conventions, and no rule that picks one

T167 settled the question for one window and, in settling it, showed the app answers it both ways.
Grepped over every surface that puts a number in a sentence:

- **Invariant** — `StatisticsPage` (through `Num`), `SettingsPage.General`'s four projections,
  `TokenEstimate.Format`.
- **`L.Culture`** — `ContextPage.Gauge` and `ContextPage.xaml.cs` (per-request cost, scan elapsed),
  `ContextText` (every file size), `TrayContext`'s context toast, and `SettingsPage.System`'s
  extra-usage percentage.

So on a pt-BR machine the Statistics window says `4.7` and the Context page says `0,336` — each
right by its own file's rule, and the app has none. Not a translation bug: the strings are correct
in five languages.

Two things make it a task rather than a sweep. **Which convention is not obvious**, and T167's
reasoning does not settle it: invariant won there because twelve neighbouring formatters already
were, and because a published chart screenshot has to mean the same thing anywhere. Neither argument
reaches a file size in a sentence of Portuguese prose, where a reader has better claim to a decimal
comma than a screenshot has to portability — and `L.Culture` is already what the dates use. The
likely answer is not "invariant everywhere" but a stated split: **invariant for anything read
against a chart or published, `L.Culture` for prose**, written down once so a new call site has
somewhere to look.

And the check reaches one page. `--selftest` runs `StatisticsPage`'s twelve formatters under two
cultures; the surfaces above are swept by nothing, so whichever rule is chosen is held up by nobody.
That sweep already derives its formatters by reflection, so pointing it at more types is most of the
work; the rest is deciding what it may find.

## XXII Working here — what earns a byte of AGENTS.md (Block AJ)

AGENTS.md is loaded every turn and is at its budget, so the file is zero-sum: what goes in now
displaces something, and nothing records which rules have earned the bytes they cost.

### XXII.1 What earns a byte of a file read every turn

Hit while shipping T169. Its rule belongs in `AGENTS.md` — a skip that fires every run is a check
that does not exist — and writing it took the file to 41,612 bytes against a 41,000 budget. The rule
went in at three drafts instead of one, and the bytes were found by deleting a sentence duplicated
between the file map and the *Arithmetic verification* section. That deletion was free. The next one
will not be.

The budget is right and is not the problem: the file is loaded every turn, and roadkeep enforcing it is
the only reason anyone noticed. The problem is what a full budget does to the incentive. The file is
**zero-sum**, so every new rule is a negotiation against an old one, and nothing records which rules have
earned their bytes. The cheapest thing to cut is whatever the editor has not needed lately, which is a
worse selector than "what has caused a defect".

Two candidates the block turned up. **Reference material is still in here**: the `src/` file map is
the largest section and reads like a table of contents, while T191 already moved the flag catalogue
out to `dev-flags` for that reason. And several rules carry their whole discovery story where the
rule alone would do.

What would settle it is a stated test for what stays: a rule earns its bytes if getting it wrong has
produced a defect **and** the rule cannot be asserted in `--selftest` instead. T167's
number-convention rule is now a check, not a paragraph; T192's id-uniqueness rule is a check. That
test predicts a smaller file, and it says which paragraphs go to a skill rather than which ones go
away.

### XXII.2 The index of themes is the one thing nothing checks

`CHANGELOG.md` opens with a table mapping every block letter to its theme, and the `roadmap-docs`
skill carries the same mapping for choosing where new work files. Blocks **AC**, **AG** and **AJ**
have headings in the ledger and no row in that table; AG has five shipped tasks under it.

The rule exists and is written down — the skill says the row is added by hand in the same commit as
the block's first task, and calls it the one hand-edit to a governed file the discipline allows.
What is missing is anything that notices when it is not done. `roadkeep lint` passes: the table is
prose to it.

Why it matters more than tidiness. That table is what the next task is filed against. A letter that
is missing from it reads exactly like a letter that does not exist, which is how this repository
reached AH by opening a block per batch of findings instead of reusing the theme — the habit the
skill's own table was written to stop. An index with holes in it argues for a new letter every time.

The check is the same move T207 made against `dev-flags`: the ledger is a file in the repository,
its block headings are derivable, and its table rows are a list. Both directions are cheap — a
heading with no row, and a row naming no heading — and `--selftest` already reads repository files
for exactly this kind of claim.

Worth settling with it: whether the skill's theme table is a third source to check against, or
whether it should point at the ledger's rather than repeat it.
