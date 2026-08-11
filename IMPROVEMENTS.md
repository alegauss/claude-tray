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
| [§XX](#xx-verification--the-checks-that-prove-a-change-block-ai) | Verification — the checks that prove a change (Block AI) |
| [§XXI](#xxi-numbers-in-prose--one-convention-or-a-stated-split-block-g) | Numbers in prose — one convention, or a stated split (Block G) |
| [§XXII](#xxii-working-here--what-earns-a-byte-of-agentsmd-block-aj) | Working here — what earns a byte of AGENTS.md (Block AJ) |
| [§XXIII](#xxiii-what-the-api-says-about-permission-and-what-the-app-infers-instead-block-d) | What the API says about permission, and what the app infers instead (Block D) |
| [§XXIV](#xxiv-this-machines-install-read-from-a-file-another-program-writes-block-n) | This machine's install, read from a file another program writes (Block N) |
| [§XXV](#xxv-toast-cards--what-the-card-actually-draws-block-e) | Toast cards — what the card actually draws (Block E) |
| [§XXVI](#xxvi-one-setting-two-places-that-change-it-block-s) | One setting, two places that change it (Block S) |
| [§XXVIII](#xxviii-input-focus-and-being-readable-block-q) | Input, focus, and being readable (Block Q) |
| [§XXIX](#xxix-the-context-load-inspectors-own-read-out-block-i) | The Context Load Inspector's own read-out (Block I) |
| [§XXX](#xxx-dates--the-words-are-translated-and-the-order-is-not-block-g) | Dates — the words are translated and the order is not (Block G) |
| [§XXXI](#xxxi-the-release-path-from-the-machine-it-is-run-on-block-b) | The release path, from the machine it is run on (Block B) |
| [§XXXII](#xxxii-the-activity-read-out-as-a-picture-block-j) | The activity read-out, as a picture (Block J) |
| [§XXXIII](#xxxiii-since-when-not-just-that-it-is-happening-block-a) | Since when, not just that it is happening (Block A) |

> Block I's design sections (§II), Block J's (§IV), Block V's (§XII), Block Z's (§XIII), Block AA's
> (§XIV), Block AD's (§XVII) and Block AF's (§XIX) are gone: every one of them shipped, and `git log` plus
> [CHANGELOG.md](CHANGELOG.md) are the history. §III stays because it is a **measurement** — the
> numbers the rule thresholds and the base-overhead constant were calibrated against.
>
> Section numbers are **never reused**: §II, §IV–§XIV, §XVII, §XIX and §XXVII have all been retired, so a new block
> takes the next unused numeral rather than the next free-looking one. A `→ §V` in an old commit
> message must keep pointing at what it pointed at.

---

## §I — House constraints

Binding for every task. These are product decisions, not preferences — a task that violates one is
wrong even if it works.

### §I.1 Privacy promise

The app reads only what Claude Code already stores locally, and from transcripts **only** usage
counts, model ids, flags, tool/skill *names*, the session `cwd` and the **one bounded exception**
below — never message content otherwise. This is the app's whole trust model. Any new reader of
`~/.claude` inherits it.

**The exception (T334, T336): what a conversation is called — its generated `aiTitle`, or its opening
prompt where there is no title — capped at `SessionIndex.PromptChars` characters, on the Sessions
list and nowhere else.** It exists because a list of conversations that cannot name one is a list
nobody can search. The title is the *narrower* half and is preferred wherever it exists (529 of this
machine's 664 transcripts): it is derived from content rather than being content. The prompt carries
the rest.

Bounded on every axis that can be bounded: two fields, one surface, truncated before either is
stored so the cache cannot hold what the app may not show, never exported, never in a toast, a
tooltip elsewhere, a report or a capture published from this repo. Every other reader of a transcript
stays under the sentence above, and `UsageReport.TryParseSample` — the parser that *is* the promise —
still never returns content; the two readers that do are named for what they read, so an audit has
one file to open.

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
- **No content display or export** beyond §I.1's one amended exception — sizes, names, frontmatter,
  timestamps, counts. Nothing is exported at all, in any case: the exception is a screen, not a file.
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

### XX.1 An instrument that says what it ignores

`--probe` prints the rate-limit headers verbatim, and T211 added the other half: which names each
account is sent, and which it never is. Both are about the *response*. Neither says anything about
the **app**, and the space between them is where a header arrives for months unread.

Of the fourteen names received today, `ApiClient` reads four families. `overage-in-use`,
`5h-surpassed-threshold`, `upgrade-paths`, `representative-claim` and `fallback-percentage` reach no
field. That was established by grepping the source — which is the work a read-out exists to make
unnecessary, and it is how the header deciding whether work has stopped went unread through the
release that shipped the state it decides.

The names the parser reads sit in one method, so the read-out can be driven from the parser rather
than from a table beside it; a second table is a second thing to forget. Mark each printed header
read or unread, and have `--selftest` assert the two against each other: a name the parser reads
that the probe calls unread is a defect in the read-out, and a name nothing reads is permitted but
has to be impossible to miss in `--probe --all`.

### XX.2 A summary the check cannot see

T278 made the marking assertable: `ApiClient.NamesRead` enumerates the parser, `HeaderProbe.IsRead`
and `Unread` are pure, `ProbeCli.Marked` returns lines, and `--selftest` holds all four against a
fixture of the fourteen names one real account sends. The one thing it did not make assertable is
the sentence a reader actually sees first — `readership — 9 of 17 name(s) on file reach a field in
this app, 8 reach none` — which is built inline and written to `Console`.

So the count and the marks below it are derived from the same set today and nothing keeps them that
way. A refactor that recounts, filters or dedupes on the summary line alone produces a read-out
whose header disagrees with its own body, and every check in the section passes: they are all asked
of the classifier, never of the summary. This is the same defect shape as T245's inlined `--probe`
decision and T271's unnamed axis — a fact stated only as output.

The fix is the shape the file already uses twice: a pure function returning the summary, printed by
the caller. It is small, and the reason to file it rather than fold it into T278 is that T278's own
commit is what demonstrates the pattern being incomplete here.

### XX.3 An instrument whose own read-out is unreadable

A WinExe's console starts on the OEM code page, and this repository has known it for a long time:
twelve entry points open with `try { Console.OutputEncoding = UTF8; } catch { }`, two of them
carrying a comment that names the symptom outright. `ProbeCli` is not one of the twelve.

So the read-out most dense in non-ASCII is the one that loses it. Every em dash in `readership — 9 of 17
name(s)` prints as `?`, and so does every `§` in the three pointers the command exists to send a reader
to — `§XVIII.9`, `§XVIII.10`, `§XVIII`. Measured on this machine's own logs, one `--probe --recorded
--all` run mangles well over a hundred characters, and a pointer is the one kind that cannot be guessed
back from context: `see IMPROVEMENTS ?XVIII.9` names no section.

The fix is one line copied a thirteenth time, and that is the reason to file it rather than paste
it. The line is a property of *being a console read-out*, not of any one flag, so it belongs where
the CLI is dispatched — once, before the verb runs — with the per-file copies removed. Twelve
remembered and one forgot; the next flag added has the same coin flip, and `--selftest` cannot
assert what a caller must remember to write.

### XX.4 A list derived by walking the only path it has

T278 replaced a table of header names with the parser enumerating itself: `ReadHeaders` takes a
lookup, `FetchAsync` hands it the response, and `NamesRead` hands it one that records the name and
answers `null`. That is what makes the read-out impossible to forget to update — and it is exact
only because `ReadHeaders` is a flat object initializer with no branch in it.

Nothing states that. A read added as `get("…-in-use") is "true" ? get("…-overage-reset") : null` is
perfectly ordinary code, and under the recording lookup the second name is never asked for: it would
be marked UNREAD, in the very read-out that exists to say which names reach a field, with all
twenty-one of the section's checks green. The failure is silent and it points the wrong way — a
header the app reads, reported as one nothing does.

The property is that the enumeration is total, and this repository already has the idiom for
asserting it: a source scan, as `FlagCatalogue` does for the flags and `ConsoleCodePage` now does
for the code page. Every `anthropic-ratelimit-` literal in `ApiClient.cs` is a name the parse
mentions, so the count of them against `NamesRead.Count` holds the enumeration total without asking
anything about branches — and a conditional read fails it the moment it is written, rather than the
month somebody trusts the mark.

### XX.5 Three scanners, one of them tested

`--selftest` now reads this repository's own sources in three places. `FlagsRead` (T248) matches the
*comparison* — `Contains("--x")`, `== "--x"`, `is "--x"` — so a flag quoted in a paragraph is not a flag
the app accepts, and that property is asserted against synthetic source rather than against the tree.
The two scans added since match a bare literal and then subtract prose by hand.

Both paid for it immediately. `ConsoleCodePage` (T283) failed on its first run against its own
summary, which names the setter in order to explain what it counts; it now drops lines that start
with `//`, which is right about a whole-line comment and blind to a trailing one. `ParserNames`
(T284) needed that rule *and* a second — a name may not end in a dash — because the paragraphs above
the parse write the family as `anthropic-ratelimit-unified-*`. Each guard is correct about the case
that produced it and nothing holds either against a case that has not happened yet.

T248 already established what this costs: a scan whose exclusion is approximate fails silently, by
asserting less, and still passes. The shape that does not is matching the construct — a setter is an
assignment, a header name is an argument to the parse — and the fixtures are synthetic, so the check
that the exclusion works does not wait for the tree to contain a violation. Whether that lands as
one shared scanner or three that each match their own construct is the open question; the property
is that no scan here decides what is code by pattern-matching what is prose.

### XX.6 A capture that photographed the spinner and said Captured

`Capture-Window.ps1` verifies **which** window it copied — T199's fix, and the success line names
the title and pid so a wrong one is caught by reading it. It does not verify that the window has
anything on it yet, and the two failures look identical to whoever is reading the output.

Measured 2026-08-04 while shipping T275: `--stats overage-noamount` on a machine with 213 recent
transcript files took about 25 seconds to build its report. At the default `-WaitMs 1500` the script
copied the page mid-load — heading, subtitle, "Computing your consumption pace…", nothing else — and
printed `Captured 1738 x 1885`. Two variants captured that way are near-identical for the same
reason, so comparing them proves nothing. It was caught only because the picture was looked at.

The script cannot know when a given page is done; it can know that a page still showing its own
loading text is not a picture of anything. The string is already in `lang/*.json`, so the check is a
read of the accessibility tree for it, not a longer wait. A longer wait is the wrong answer twice:
it slows every capture, and it still reports success on the page that needed longer still.

`-WaitMs` stays, for the caller who knows better. What changes is that reaching it with a page still
loading is a **failure**, named, writing no file — the same shape as the wrong-window refusal.

### XX.7 A bar nobody asks about

T277 gave `ToastWindow` a rule — a card may not draw a meter for a quantity its reading does not
carry, and a null figure collapses the block. Three things now watch the extra-usage card and none
of them watches that. `--selftest` asks the arithmetic, that `ExtraUsageBar(0)` is null, which is a
claim about a number and not about a window. `--check-toasts` builds every card in every language
and asks whether it fits, and `Overflow()` walks `TextBlock`s only: the bar is a `Border`, so a card
that drew one anyway fits perfectly well. And `--capture-toast extra-bare` writes a PNG somebody has
to look at, which is the check T277 itself leaned on and the one that never runs again.

So the collapse can come back — a theme test restored, a null coalesced away at a call site — while
the build stays green, the captures stay green, and the defect returns as the exact picture the task
removed: a full allowance drawn behind a sentence saying the quota is spent.

The property is cheap once a card exists. `QuotaBlock.Visibility` answers it directly, and
`--check-toasts` already builds one card per variant per language. What it needs is the expected
answer per row, which a preview knows because it knows whether its reading carries a quantity — so
the check becomes *the bar is present exactly where the row says it is*, which also holds the two
profile cards that have been bar-less since T174 and have never once been asked.

### XX.8 The other picker

`Check-Interaction.ps1 -Case Profiles` walks a `ComboBox` 0 → 1 → 0 and reads the report at each
stop, and it is the only check that drives a profile control at all. It is not this one. That picker
chooses whose numbers the Statistics page *draws*; `AdoptMonitored` changes which account the tray
icon follows, writes `MonitoredConfigDir` to settings, re-keys the stores, drops the outgoing
account's in-memory state and takes the new account's token. Two controls, similar names, and only
the harmless one is driven.

What rides on the undriven path is not small. T292 was a defect on it, found by reading rather than
by running; the automatic arm (T145's follow-active-profile) reaches it without anybody clicking;
and it is the one action in the app that changes what a *different* process will do. The check that
exists proves the report follows the picker, which is precisely the claim that covers none of that.

The case is buildable here: this machine discovers two profiles, and the tray menu already names
them. Drive the menu item, then assert what a switch is supposed to have done — the icon's profile
changed, the report follows it, the setting on disk names the new directory, and the reading on show
is the incoming account's rather than the outgoing one's. Below two profiles it is **DEGRADED**, not
skipped, on the rule Profiles already keeps. Worth pairing with T293, which changes what "drops the
outgoing account's state" means.

### XX.9 Two writers of one line, and the fixture is the quiet one

`SelfTestCli.WriteStore` builds `usage-hourly.jsonl` lines by hand — `d`, `s`, `c`, appended in a
`StringBuilder` of its own — because the checks that need a folded week need one that was never
folded. That is the right fixture and the wrong writer: `HourlyUsage.WriteAll` is the other one, and
nothing holds the two in step.

T287 is what showed the cost. It added a fourth column, and every fixture went on writing three — so
`FoldedWeek`, `FoldedWeeks` and `IntensityDay` all describe days folded before the column existed.
That happens to be a legitimate state and a useful one to test, which is exactly why it is
dangerous: a fixture that silently means *unknown* asserts nothing about the column, and the check
that reads it still passes. A fixture missing a column nobody notices is a check that has quietly
narrowed.

What is wanted is one writer. `WriteAll` is private and takes a dictionary and a clock, and the
fixtures already hold `HourlyDay` values — so the fixture path can go through the real writer with
an `internal` seam, the way `FillCurve` was opened up for T189, rather than through a copy of the
format that only ever drifts one way.

### XX.30 The other capture, and the same spinner

§XX.6 is about `Capture-Window.ps1`, which copies pixels from the screen and cannot know when a page
is done. `--capture-stats` is the other capture — off-screen, inside the app, and the one AGENTS.md
tells a reader to prefer — and it has the identical defect for the opposite reason: it *can* know.

Measured while shipping T295: five runs of `--capture-stats … overage-noamount ghost`, two of which
wrote a PNG holding the heading, the subtitle and `Computing your consumption pace…`, and printed
`wrote …-5h.png / -7d.png / -throughput.png` for all three. The second one was caught the way the
first was: by looking at the picture. A capture that lands on the placeholder is not slower
evidence, it is no evidence, and the read-out says the same word either way.

The fix is not a longer wait, for the reason §XX.6 gives, and here it does not even need the
accessibility tree: the page builds its report on a task and swaps the placeholder out when it
resolves, so the capture path can wait on that and refuse — named, writing no file — when it does
not arrive. The `refresh` modifier deliberately captures mid-recompute (T118), so the refusal
belongs behind it: what that flag asks for is exactly what every other run must stop calling a
success.

### XX.31 The state that was reasoned about and never looked at

T299 is about a week whose over-quota bits and whose curve disagree: the fold drops the delta of any
pair straddling a window reset, so a week the tray saw in pieces draws a total that is a floor and
can carry hours marked over while its line peaks below the ceiling. The fix pins the clay mark at
100% and adds a sentence to the ghost's tooltip saying the line is a floor.

Both were verified by `--selftest`, against hand-built `GhostWeek` values. Neither has been *seen*.
No `--stats` variant produces the state, because producing it means a folded store whose spend is
short of what its own bits imply — and the demo ghost is deliberately coherent, with its spans
derived from its curve so a screenshot cannot show the two disagreeing.

This is the shape T264 named: a preview whose whole reason for existing is a state no machine can be
put into, and the only kind of state that gets shipped on reasoning alone. What it costs is a
variant that hands the page a ghost built the wrong way round — bits past the ceiling, curve short
of it — which is three lines beside `FillDemoOverSpell` plus its row in the catalogue and the flag
doc. What it buys is the one question an assertion cannot answer: whether a clay mark floating a
centimetre above the line it belongs to reads as *the header said so* or as a drawing bug.

### XX.32 A published shot of the Sessions pane must come from a fixture (T335)

`--capture-stats` renders the Sessions pane from the monitored profile, and since T334 that pane
carries the opening prompt of every conversation. So the one command that produces a screenshot for
`README.md` and `docs/index.html` now produces one holding this machine's real prompts, and the
repository is public.

The precedent is already here and was written for exactly this: `--capture-settings System` renders
this machine's login, so a published shot of it is taken over `AccountFixture` instead, and the
`preview-ui` skill says so in its own section. §I.1's amendment permits the prompt **on the user's
own screen**; it says nothing about a picture of somebody's screen in a git history, which is a
different promise and a worse one to break — a screenshot cannot be un-published.

The consequence is visible today: the published `docs/statistics-sessions.png` is the pre-T334
capture, so the README describes a column its own screenshot does not show. That is the honest state
of it, and it is the state this task closes.

**What it needs.** A session fixture in the shape `ContextFixture` and `AccountFixture` already
establish: a synthetic `projects/` tree, deterministic clocks, a fan-out under one conversation,
obviously invented prompts, and one row long enough to exercise the truncation. Then a modifier on
the Statistics preview table — the one `--stats` and `--capture-stats` both read — so a published
capture comes from the fixture while a capture of the real thing stays available for looking at.

The check `--selftest` can hold is the one that matters and it is cheap: a published capture of this
pane must not be produced from a real profile. What it cannot check is whether somebody committed a
PNG anyway, which is why the fixture has to be the easy path and not the careful one.

## XXI Numbers in prose — one convention, or a stated split (Block G)

Two surfaces of this app answer the same question differently, and T167's sweep reaches only one of
them. What files here is the rule that picks a convention, and the check that holds every surface to
it.

## XXII Working here — what earns a byte of AGENTS.md (Block AJ)

AGENTS.md is loaded every turn and is at its budget, so the file is zero-sum: what goes in now
displaces something, and nothing records which rules have earned the bytes they cost.

## XXIII What the API says about permission, and what the app infers instead (Block D)

Every signal this app has about whether an account may spend past its quota is inferred from a file
or from an effect. The API states it directly, and until 2026-08-04 nothing here could read the
statement: `overage-status: allowed` arrives on an account inside its quota whose flag is already
set, so one sample cannot be told from a default.

A second kind of sample now exists. On the monitored account
`anthropic-ratelimit-unified-overage-in-use` was **absent** on nine consecutive readings inside the
quota — including one at 0.91 eighteen minutes before — and arrived as `true` on the first reading
past it, at 5h `1.02` with `5h-status: rejected`. Not a value every response carries, which is the
one property `Allows` needed and could not find.

Two constraints from §XVIII carry over. Rate-limit headers are quota metadata, so §I.1 is not in
tension; the "profiles are contexts, not quota pools" non-goal is, because every task here makes
overage more visible and *this account is out, the other has room* is the sentence the roadmap
forbids — §I.7, and §XVI.4 has the shape of the answer: a receipt, not a reward.

What binds the whole block: the amount is still unstated. The overage window has a utilization that
read `0.0` throughout the spell and a reset on a calendar month; no header says what 100% of it
amounts to. So every task here reports **that** it is happening, or **when**, or an estimate marked
as one — never a share of a denominator nothing measured.

### XXIII.1 The header that says it is happening

`ApiClient` parses four header families — the three windows' utilization, reset and status, plus
`overage-disabled-reason` — and `overage-in-use` is in none of them. The name appears nowhere in the
source, so the one statement the API makes about the state this app is trying to infer is received
on every response and dropped.

The work is a field on `UsageData`, the same name in the capture log, and the post in
`QuotaStates.Resolve` that `hasExtraUsageEnabled` holds today. That order is the point, and its
reasoning is already written: the flag is read out of a file Claude Code writes, and T224 put the
API's refusal above it for exactly that reason. The affirmative earns the same rank now that a
reading distinguishes it — above the file, below an observed figure, and still below a measured
refusal, since nothing has ever sent `rejected` and `in-use: true` together.

What does not change: `Allows` keeps failing to affirm on `overage-status`, because that header is
the one that could not be told from a default. This adds a second signal beside it rather than
reinterpreting the first, and `--selftest`'s quota section is where the two are held apart.

### XXIII.2 A window has a scope, and this fact has none

`CurrentQuotaState` resolves from `d.Metric(_metric)`, and the comment above it defends that choice
for the *caption*: the icon shows one number and the tooltip's at-limit sentence names that same
scope, so answering about another window would caption the wrong figure. Correct for the sentence,
wrong for the state.

Measured on the reporting machine with the metric on `5h`: 5h `1.02` / `rejected`, 7d `0.47` /
`allowed`. Switch the icon to the week — a display option, one menu click — and the same reading
resolves to `InQuota`: green bar, on-track projection, no billing sentence anywhere, and the account
spending money throughout.

Whether an account is paying is a property of the account, not of the window the icon happens to
show. The split to make is between the *verdict*, resolved from whichever window is rejected, and
the *caption*, which goes on naming the metric's own scope so the tooltip still cannot label the
wrong percentage. Both halves are text, so `--tooltip` is where the pairs get checked — and the pair
worth a variant of its own is the one this task exists for: a rejected session behind a week at 47%.

### XXIII.3 A spell that is over, and nothing that recorded it

`usage-history.jsonl` carries `ux` and `rx` per reading and both come from the overage figure, so
the crossing recorded `{"u5":1.02,"ux":0}` four times over and nothing else. The store is pruned at
eight days and `HourlyUsage` folds out spend and coverage before that, neither keeping a trace that
the account was past its quota at all — so a week reviewed afterwards shows a ceiling and no account
of how work continued through it.

Downstream, `UsageReport`'s week keeps `ExtraCurve` and `ExtraMax`, and T247 fixed the *opposite*
defect there: measured zeros were drawing a flat line as if it were data. So this spell cannot be
got onto the chart by relaxing that guard — a zero series is still not a series.

What the picture wants is a **stretch** rather than a curve: the interval over which the boolean was
true, shaded behind the utilization line, with the second axis appearing only when there is a figure
to rule. That needs the boolean in the store beside the figure, which is this task and why T279 and
T280 both depend on it. `--stats overage` is the preview it has to be visible in.

### XXIII.4 What the spell cost, and the list that refused to say (dropped)

The honest version of "extra usage is paying" is a number in money, and the app looked close to one:
`UsageInsights` owns the per-model `Price` table, and T275 puts the crossing in the store with a
timestamp, so the turns after it are priceable with nothing new read.

Three things had to hold. An **estimate**, carrying the visible "≈" every token count here does —
§I.3. A **receipt**: what it cost, with no suggestion and nothing that reads as a reward for
spending — §I.7 and §XVI.4. And a price table right about which models the overage even bills, since
pricing turns the account was never billed for is worse than saying nothing. That was the gate, and
the instruction was to measure the list first.

**Measured 2026-08-04, and the third fails.** `.claude.json` caches
`tengu_usage_overage_included_models` under `cachedGrowthBookFeatures`, reading `["Fable",
"Fable 5"]`. Eight days of transcripts here are 34,595 Opus turns, 195 Sonnet and **zero Fable** — so
under one reading of the flag ("these bill as overage") the estimate for a spell that demonstrably
cost money is $0.00, and under the other ("these stay included") it is the whole $5,834 notional. One
sample cannot tell them apart, which is the shape §XVIII.9 declines to map. Two smaller cracks in the
same joint: the values are display names, not the model ids `Price` matches on; and a cached
GrowthBook payload is remote experiment state, not a billing contract. §I.2 forbids the call that
would settle it.

So the figure is a non-goal, and T275 already draws the honest half: the band says **when**, the
sentence says no header states how much. A denominator the API states would reopen this.

### XXIII.5 The threshold the response names

T278's readership read-out put every arriving header under a mark, and the first thing it marked was
a name that had already moved twice. `anthropic-ratelimit-unified-5h-surpassed-threshold` is absent
on every reading inside the quota, arrives as `0.9` on the reading whose `5h-status` first says
`allowed_warning` at a utilization of 0.91, and reads `1.0` on the one that says `rejected` at 1.02.

That is not the single-sample vocabulary §XVIII.9 declines to map. The name has taken two values in
one log, each beside a status transition this app already reads, so what it announces is measured
rather than inferred: the threshold the account crossed, named by the API, on the reading it was
crossed on.

Meanwhile `Settings.FlashWarnThreshold` is `0.90` — a number this repository chose, with a comment
saying it is where the API reports `allowed_warning`. That comment is a claim about another system,
and the header is the system saying it. An account on a different plan whose warning lands elsewhere
gets a tray that flashes at the wrong moment, and nothing here would notice, because the constant
cannot disagree with a header nothing reads.

What is open is what to do with it, not what it says. Reading the value as the threshold is one
line; the design question is whether the flash follows the header when it is sent and falls back to
the constant when it is not, and how a value the app has never seen — a third threshold, a plan with
two — stays a state the tray can draw rather than a guess. Absence is the common case: three of five
readings here carry no such header at all.

### XXIII.6 The other half of the same split

T274 rescoped the state to the account and fixed the sentence for **billing**: a rejected session
behind a week at 47% now says extra usage is paying, unscoped, because 47% is not what crossed. The
`Stopped` half was deliberately left, and it is the same defect wearing the opposite outcome.

`TooltipText` gates its at-limit sentence on `metricAtLimit`, the window on the icon. So an account
whose session is `rejected` at 1.02 with no extra usage, watched on the week at 0.47, gets the
ordinary projection sentence — *✓ Week 7d projection: on track* — while nothing it does will run
until the session resets. The two window lines above it do carry 100% and 47%, so the reading is
recoverable by eye; the sentence, which exists to say what the reading means, gives the wrong
answer.

The shape of the fix is T274's, already built and already asserted: the account-scoped `Resolve`
answers `Stopped`, and the caption is kept off the sentence when the metric is not the window that
crossed. What needs deciding is the wording, and it is not the tooltip's alone —
`stats.proj.atLimit` on the Statistics page is gated the same way.

Worth checking before writing a string: whether an unscoped "work has stopped" is even true when the
*other* window is fine. It is — the API refuses the request, not the window — but the sentence has to
be one a user reads as "you are blocked now", not as "some window somewhere is full".

## XXIV This machine's install, read from a file another program writes (Block N)

The System information page reports the machine rather than the app, so every figure on it is read
out of files Claude Code owns and this app only opens. What those files are allowed to contain is
therefore not this app's decision, and a count taken over them is a claim about their shape.

## XXV Toast cards — what the card actually draws (Block E)

What the toasts get wrong is rarely visible in the code that built them. Of the defects filed here,
two were found by looking at a card, one by following the reading a card is made from back to the
poll that took it, and one by giving that reading a name and noticing whose it was.

## XXVI One setting, two places that change it (Block S)

The tray menu and the Settings page now both write fields the page believes it owns, and only one of
the two knows the other exists.

## XXVIII Input, focus, and being readable (Block Q)

What a control ANNOUNCES, as against what it draws. A picture cannot see an accessible name and
neither can any check, so this is the block where a surface that renders perfectly and tells
assistive technology nothing gets filed.

## XXIX The Context Load Inspector's own read-out (Block I)

The headless side of the inspector: what a script driving it can tell about a run, as opposed to
what a person reading the output can.

## XXX Dates — the words are translated and the order is not (Block G)

What a date reads like once the month name is French and the arrangement is still English.

## XXXI The release path, from the machine it is run on (Block B)

What cutting a release costs on a developer machine, as opposed to on a runner that has nothing else
installed.

## XXXII The activity read-out, as a picture (Block J)

What the grid shows and what the sentences around it claim, which are not the same axis.

### XXXII.1 A fold that keeps the hours and loses the spell

T275 put the boolean in `usage-history.jsonl` and the weekly chart now shades the stretch the
account was past its included quota. That store is pruned at eight days, and `HourlyUsage.Fold` is
what runs first so nothing is discarded before it is counted — spend and coverage, per local hour,
kept forever.

The fold has no column for the spell. So the guarantee holds for the two figures it was written for
and quietly does not hold for the third: a week reviewed a fortnight later shows a ceiling, the
ghost week draws a curve that stops at 100%, and nothing anywhere says the account went on working
past it and paying. §XXIII.3 named both stores as losing this; only the near one was fixed.

What the fold would keep is not a copy of the readings. An hour either carried a reading that said
the account was over or it did not, which is one bit per hour on a store that already has a slot per
hour — the same shape as coverage, and it composes with the band the same way, since a run of such
hours *is* the stretch. That also settles what a partial hour means: coverage already answers "how
much of this hour was observed", so the bit answers only "was any of it over".

Two consumers follow: `GhostWeek` can shade last week the way this week is shaded, and T280's "since
when" can survive the pruning that would otherwise make it a question only about the last eight
days.

### XXXII.2 The week behind this one, shaded the same way

T275 shades the stretch the account spent past its included quota on the current week's burn-up,
read from the readings themselves. T287 put the same fact in the permanent fold, one bit per hour,
so the week before this one can answer that question too once its raw readings are pruned.

`PreviousWeek` does not ask it. It returns a curve, a coverage share and where the week stood at
this point in its window, and the chart draws that line behind the live one — so a week that went
past its quota and went on paying draws exactly the ghost of a week that stopped at the ceiling.
Those are the two readings of one flat top, and only one of them cost money.

What the ghost needs out of the fold is spans rather than bits: a run of over-hours *is* the
stretch, and the hour grid maps onto the ghost's (fraction, cumulative) axis the same way its spend
already does. `OverKnown` is what decides whether the ghost may say anything at all — a week folded
before the column existed has no answer, and drawing nothing for it must not read as a week that
stayed inside its quota.

### XXXII.3 A reading is a fact, and a pair is a different one

`Fold` walks the readings in consecutive pairs, because spend is the positive part of the difference
between two of them and there is no first difference. It then attributes everything to the later
reading of the pair — the spend, the coverage tick, and since T287 the over-quota bit — which
quietly gives the first reading of the batch no hour at all.

Spend is right to be a fact about a pair. Coverage is not: the reading was taken, at a known hour,
and that hour was observed whatever the arithmetic beside it can say. So the oldest hour of every
fold loses a reading, and an hour whose only reading was that one comes back as *unknown* — the
state the store's whole design keeps distinct from idle. The bit T287 added lands in the same hole
and matters more there, since the hour may be the only surviving trace of a spell after the raw log
is pruned.

The fix is small and its shape is the point: the loop counts coverage and the bit for every sample
it sees, including `samples[0]`, and keeps the pair for the delta alone. Folding stays idempotent
per day, so a corrected pass over a day already in the store still changes nothing.

### XXXII.4 Two measurements of one week, and no rule for when they disagree

T295 draws the ghost's over-quota stretch on the ghost's own line, which was the right place for it
because being past the included quota means the week is at 100% and the stretch is therefore flat
against the ceiling. That is true of the *reading*. It is not guaranteed of the *curve*.

The two come from different arithmetic in the same fold. The bit is what the API said at some
reading in that hour. The curve is a running sum of positive weekly-utilization deltas, and `Fold`
discards the delta of any pair straddling a window reset — correctly, since that difference is not
work. A week the tray saw only in pieces therefore accumulates a total that is a *floor* on what was
spent, and a real store can hold a week whose bits say "over" while its own line peaks at 80%. The
chart would then draw a clay stretch across a rising middle of the plot: two honest measurements,
one picture, and a reader with no way to tell which to believe.

Nothing here is a wrong number, so the fix is not arithmetic. Either the stretch is drawn where the
claim actually lives — pinned to the ceiling, which is what the header asserted — or the ghost's
tooltip says that its own total is a floor. The first is a smaller change and the second is the more
honest sentence; what must not survive is the chart making a claim neither half of the fold
supports.

## XXXIII Since when, not just that it is happening (Block A)

The tooltip's billing sentence states a condition: *o uso extra está pagando*. Nothing anywhere says
when the spell began, and a spell is an event — it started at a reading, and that reading is in the
store once T275 lands.

Worth a line of its own because of what a user does with it. "You are paying" invites "since when",
and the answer decides whether the next hour of work is a considered choice or something found at
the end of the month. It is also the cheapest honest thing this app can say about cost while the
amount is unstated (§XXIII.4): a duration is measured, a figure would not be.

The budget is the constraint rather than the wording. `NOTIFYICONDATA.szTip` allows 127 characters
and `--tooltip` reports every variant against that cap in every language; the two billing variants
measure 114/127 and 103/127 in pt-BR, so a duration fits where a timestamp carrying a date does not.
It belongs in the same fitting ladder as the projection sentence — full form, compact form, dropped
— with the readings above it kept, and `--tooltip` is what says which of the three each language
ends up with.

## XXXIV Two derivations of one cadence (Block F)

`FindGaps` decides when a silence between two readings is an outage; `BridgeSeconds` decides when
the same silence ends an overage spell. They are the same question asked twice, and since T275 they
are also the same arithmetic written twice: `max(GapFloorSeconds, GapCadenceFactor × median delta)`,
once inline inside `FindGaps` and once as a method beside it.

Nothing is wrong today — they agree, because one was copied from the other. What is wrong is that
nothing makes them go on agreeing. A cadence estimate that became a trimmed mean, or a floor that
moved because the default poll interval changed, would reach the red outage stretch and not the clay
band, and both are drawn on the same chart, where a reader would take the disagreement for data.
This repository has the rule for exactly this shape: one reader, or two files that can differ.

The fix is small and the naming is the point. `FindGaps` takes its threshold from `BridgeSeconds`
over its own points, so there is one derivation, one place a change lands and one name to assert
against. `--selftest` already covers both behaviours separately; what it cannot do today is fail
when they part, which is what folding them gives it for nothing.

## XXXV What a switch must not carry across (Block A)

`AdoptMonitored` drops the outgoing account's numbers one by one: `_data`, `_lastGoodSnapshot`,
`_burn`, and since T292 `_extraAlarm`. The list is prose in a method body, held by whoever last read
it. T292 is the evidence that this does not hold — the alarm's readings had been the outgoing
account's since T184, through four blocks of work on that very notification, and were found only by
giving them a name and asking whose they were.

There is no assertion available for a list of this shape, and that is the point: a check would have
to know which fields are the monitored account's, which is the same knowledge the method already
fails to keep. The compiler can keep it instead. One object — call it the monitored account's
in-memory state — holding the reading, the last good snapshot, the burn tracker and the alarm, and
the switch becomes a single assignment of a fresh one. A field added to that object is dropped by
the switch because there is nowhere else for it to live.

Two things to get right rather than assume. The fields do not have one lifetime today: three are
cleared and the fourth is *rebuilt from the incoming profile's history*, so the object's constructor
takes the profile key, exactly as `ExtraUsageAlarm`'s does. And `_otherData` is not part of it — it
is keyed per profile on purpose (T137) and holds the accounts the icon is not following, so folding
it in would delete readings the submenu draws.

## XXXVI Two clay marks, and a legend that lists the lines instead

The weekly chart's legend names four things: actual usage, even pace, projection, last week. Every
one of them is a *line*. Since T275 the chart also carries a clay band — this week's stretch past
the included quota — and since T295 a clay mark at the ceiling for last week's, and the legend
mentions neither.

Both are explained, and only, by a tooltip: the band's on a hit target at its middle, the mark's on
the mark itself. That is the discoverability of a thing nobody knows is there. The band at least has
the paragraph under the chart saying extra usage is paying, which is why T275 was right not to grow
the legend for one element. Two elements in one colour, meaning the same fact about two different
weeks, is a different question — and the reader who notices them has no way to tell which week
either belongs to without finding the hover.

What is *not* wanted is four more legend entries. The clay pair is one idea — *past the quota
included in your plan* — distinguished by where it is drawn, so one entry that says so, with the
week left to the tooltip, is the whole change. It also has to survive the case that only one of the
two is on the chart, which is most of them: a legend entry for a mark nobody drew is the same defect
one step further on.

## XLIX The keys the code asks for, against the table that has them (T314)

`L.T` ends `Table(en).TryGetValue(key, out var en) ? en : key` — an unknown key is returned
verbatim, so a misspelled one renders `stats.legend.overQuota.tipBand` where a sentence belongs. The
T313 pass came within one character of it: the checks assert which key each legend state *chooses*,
comparing a C# literal to a C# literal, which passes whether or not any table holds it.

The parity check does not close this. It loads `en` and compares the other four to it, keys and
placeholders both, and its own summary says where it stops — a string hardcoded in XAML, which has
no key to be missing. The opposite direction is unstated and unchecked: `en` is treated as the
source of truth for what exists, and nothing asks whether the keys the *code* names are a subset of
it. Every failure it can find is a translation gap. This one is an English gap, and English is the
fallback, so there is nothing behind it.

The check is a file walk and a regex, which is the argument for it. Collect `L.T("…")` literals from
`src\**\*.cs` and `{local:Loc …}` from the markup, and assert each is a key `en.json` holds. Naming
the offending keys, as the parity check already does.

Two limits belong in its own summary rather than in a later surprise. A key built at run time cannot
be seen — `ApplyOverLegend` passes `g.TipKey`, and that is the shape T313 introduced — so the check
covers the literals and says so. And it is a subset check in one direction only: a key in `en` that
no call site names is dead weight, not a defect on screen, and pruning it is a different task from
this one.

## L Two prose files, one numbering sequence, and the constraints at the collision (T315)

`IMPROVEMENTS.md` opens at `§I — House constraints` and `STRATEGY.md` opens at `§I — What this is`;
both also declare a `§III`. Anchors are one namespace across a project's prose files, deliberately
and settled upstream — a pointer resolving to two resolves to neither, and `lint` reports
`section.ambiguous` at each heading. It has done so on every run here, four findings, and they were
read as permanent noise about someone else's tool. They are not. They are this repository's files,
correctly reported.

What sits at the collision is the worst possible thing to have there. `§I` in `IMPROVEMENTS.md` is
**House constraints** — the binding non-goals — and the roadmap's Non-goals block cites it ten times,
`§I` for the full text and `§I.1` through `§I.8` for the individual constraints. Every one of those
pointers reaches nothing. `brief` prints those constraints as its `not` lines from the roadmap's summary,
so the short form survives, but the full text a person is sent to read is unreachable through the tool
that is supposed to serve it. `§III` is the same defect over the measured context baseline and the licence.

Only two addresses collide: `STRATEGY.md` declares `§I`–`§VII` and `IMPROVEMENTS.md` uses `§I`,
`§III` and then `§XV` upward, so `§II` and `§IV`–`§VII` are already unambiguous. Moving the
improvements side is the wrong direction — it is the cited one, seventeen pointers against two. So
`STRATEGY.md`'s two sections take addresses this project has never declared, and the three citations
that name them move in the same commit: two in the `roadmap-docs` skill, one in the release note.

Worth checking first whether `[rules.<role>]` can give the strategy role its own anchor pattern,
which would be the answer that does not depend on picking numbers that happen to be free.

## LIX The cache TTL the token count throws away, and what a session is worth (T330)

`TokenBits` carries four longs, and `CacheCreate` is one of them. The transcript is finer: beside
`cache_creation_input_tokens` sits a `cache_creation` object splitting that number into
`ephemeral_1h_input_tokens` and `ephemeral_5m_input_tokens`, and nothing here has opened it.
Measured over this repo's transcripts: **50.5M** one-hour write tokens against **1.17M** five-minute
ones, across 18,468 lines. Claude Code writes almost entirely at the one-hour TTL.

The two are not priced alike. Against a model's base input rate a cache read is 0.1×, a five-minute
write 1.25×, a **one-hour write 2×**. Blending them wrong misses the write component by nearly two —
the same error the usage simulator made until it looked, where an assumed 0.2475 input multiplier
turned out to be **0.1335** because 98.2% of context is reads.

Read the split, and it is worth something: a session's tokens become a **list-price equivalent**,
per model. Tokens rank sessions; money explains them, and a $2 conversation beside a $40 one is a
sentence a token count does not say.

**This is not T279, and the difference is the whole permission.** That task asked what an *overage
spell actually bills*, and died because the model set is a cached flag disagreeing with observed
traffic — it needed a fact about someone's account. This needs none: it is arithmetic over tokens
already counted, at published rates.

So the wording must never drift into the other claim. A subscription exposes no dollar balance and
this app does not know what anyone pays. The label reads *"≈ $X at API list prices"*, with the
method note behind an ⓘ — Block M's pattern — and never *"cost"* bare. Rates live in a table with
the date they were read, so a stale one is visible rather than silent.

## LX Effort, the lever nothing here reads (T331)

Every assistant line carries an `effort` — checked here, 18,327 of 18,473 lines in this repo's own
transcripts name one — and no reader in this app has ever looked at it. It is a flag, not content,
so §I.1 permits it on the same footing as the model id already read beside it.

It is worth reading because effort is the largest lever on what a task costs, and it does not work
the way it sounds. Measured across 1,864 tasks: effort buys **more calls, not longer answers**. A
`high` task takes a median of 19 calls at ~650 output tokens each; an `xhigh` one takes 51, and
output per call barely moves. So two conversations of the same length, in the same repo on the same
model, differ several-fold in tokens with nothing on screen saying why.

What this task adds is small and specific: the index keeps the effort mix per session and per task,
the Sessions pane shows the dominant one, and the drill-down shows it per task. Where a session is
mixed, the mix is what is shown rather than a majority vote that hides the expensive minority.

**The temptation to resist is a recommendation.** The app must not say *"drop to medium"*: it cannot
see whether the answer was right, and effort traded for a wrong answer is not a saving. It reports
what ran and what it cost, and the reader decides. That is the same line the projection verdict
already holds — it says on track or not, never *work less*.

Two levels have no separate reading yet because nothing here has run at them: `max` and the `xhigh`
floor that `ultracode` imposes. Both are named in the table so a first sighting is legible rather
than blank.

## LXI The heaviest five hours, which is what a limit saw (T332)

The app reads a five-hour window in exactly one place and in exactly one way: the one the API
anchors, running from the reset it reports. That is the right window for *"how much is left"* and it
is the only one there has ever been. It answers nothing about a window that has already closed.

The transcripts hold every other one. Slide a five-hour frame across a day's turns and the peak is
the window a limit actually saw — measured across 32 active days, the busiest five hours hold a
median of **63%** of a day's work, so a daily total understates what the meter was pointed at.

What this adds is one reading, in the Sessions pane and per session: **the heaviest five-hour window
in the range on screen**, when it started, and what went through it. Beside it, the same figure for
the range's median day, because a peak with nothing to compare it against is a large number and not
a fact.

**It is a measurement, never a prediction.** The app does not know the plan's real allowance — no
figure for it is published, and Block D's quota state is a percentage, not a budget — so this must
not become *"you can do 1.6 of these"*. It says what the busiest window held, and the reader who has
hit a limit already knows which window it was.

Two consequences make it worth its own task rather than a column. The sweep is over the index rather
than over a live tail, so it is the first reading here that looks **backwards** across days; and it
is the natural home for *"today against your usual day"*, which the Week pane cannot say because it
sums the week away.

## LXII Which command ate the week (T333)

The strip attributes spend to a **project**, which answers *which repo is eating the week*. The
measurement that produced this block says the sharper question is *which kind of work*, and the
numbers are not close: across 1,864 tasks, **443 slash-command tasks carried 57% of all spend**, at
a median of $9.55 against $1.69 for a typed prompt — and a single command, `/loop`, accounted for
all 443 of them. A repo-level breakdown cannot see that, because the expensive command and the cheap
prompt land in the same repo.

A slash command's **name** is a name, on the same footing as the model id, the tool names and the
skill names §I.1 already permits. The prompt that follows it is not, and is not read.

So the Sessions pane gains a second grouping: by task kind — `command`, `prompt`, `continuation` —
and, within commands, by name. Per group: tasks, median and total, share of the range. That is one
table, and it is the table that says whether a week went on conversation or on automation.

**Two ways to get this wrong, both already made and corrected upstream.** A command's *expanded*
instructions arrive as a `role:user` line and are not a person asking, so counting them starts a
phantom task; and a slash command is not chrome to be filtered out with `<system-reminder>` and
`<local-command-stdout>` — filtering it away deletes the most expensive half of the data. The
segmentation task is the dep because it is where both rules live.

The reading a person takes from it is theirs to act on. The app says `/loop` was 57% of the week; it
does not say run fewer loops, for the same reason it never says work less.

## LXV The join between a spike and a task, in the direction that has data (T337)

T329's design ends with a sentence it did not build: selecting a task cross-highlights the
Throughput strip where the two overlap in time. It was left out because the premise does not hold as
stated, and approximating it would have been worse than saying so.

**The measurement.** `LiveRate` keeps `HistorySeconds = 300` — five minutes of per-second buckets,
of which the chart draws three. The Sessions list opens on seven days. So the overlap between a
selected task and anything the strip can draw is empty for every row except one: the task running
right now. A highlight that is correct and blank for 142 of 143 rows is a feature nobody sees fire,
and widening the strip to make it fire is the repair T326 already refused for a different reading.

**What is worth building instead is the inverse.** The strip's question is *what is burning right
now*, and the useful join is from the chart to the list: a spike on the strip becomes a task with a
name. That works because the spike is by definition inside the strip's window, so the task it
belongs to is the currently-running one — which the list can already identify and could scroll to
and open.

So: a click on the live chart selects the running task in the Sessions pane, rather than a selection
in the Sessions pane painting the live chart. Same join, the direction that has data behind it.

**What would make the original real** is a chart over the *session's own* timeline rather than over
the last three minutes — a different chart, not a highlight on this one, and a bigger task than
either. Filed here so the next person does not re-derive the reason.

## LXVI The row's lower lines are clipped, not trimmed (T338)

The Sessions row draws a project, the conversation's title under it, and the opening prompt under
that — and the lower two are cut mid-word at the column edge with no ellipsis, running on under the
"Last turn" heading rather than ending before it. `TextTrimming="CharacterEllipsis"` is set on every
one of them and does nothing.

The reason is where they sit. The chevron and the text stack are children of a **horizontal**
`StackPanel`, and a horizontal `StackPanel` measures its children with infinite available width. So
the inner stack reports the desired width of its longest line, the Grid column is narrower than
that, and the column clips whatever does not fit. Trimming never runs, because from the TextBlock's
own point of view nothing was ever too wide.

This shipped with T334's prompt line and was never seen. The published shot of this pane predates
that line entirely, so no capture has ever shown the defect; T336 made it two clipped lines instead
of one, which is what finally got it looked at.

**The fix** is to give the text stack a real width: the chevron and the stack become the two columns
of a `Grid`, `Auto` and `*`, instead of a horizontal `StackPanel`. A `*` column measures at the
width it actually has, so trimming fires and the ellipsis lands where the text ends.

**What is not enough** is a hard `Width` or `MaxWidth`. The window is resizable and the column is
proportional, so a number chosen once is wrong at every other size — and it would be wrong silently,
which is the same failure again.

## LXVII The two lint findings nobody owns (T339)

`roadkeep lint` has never exited clean here, and that is a problem in itself: a check whose failing
output is the normal output stops being read. Four of its findings are the known `section.ambiguous`
pair — this repo declares `§I` and `§III` in both `IMPROVEMENTS.md` and `STRATEGY.md`, which is
T315. Two more have no task at all, and this is that task.

**`why.too-long` on T301.** The line is one character over. It went over on its own: shipping T299
turned its dep annotation into `T299 ✅`, and a `✅` is two characters the author of the line never
typed. T298 hit the identical wall while T286 was being shipped, where it was a hard stop — the ship
was refused until the line was shortened, which is the tool working. T301's is the same defect
sitting where nothing forces it.

**`body.promise` on §I.8.** The section spells an id in this project's own prefix that no line
carries — `lint` names which. That reads as a number already spent, so the next `add` derives past
it: an illustration quietly consuming an id. `gaps` says where it went; the sentence then either
spells the example outside the prefix, where nothing numbers it, or names the id it actually meant.
Writing *this* section is the demonstration — `add` refused the first draft for repeating the id
while explaining it.

**What finishing this is.** Not zero findings — T315 owns the other four and is a separate argument
about anchors. It is that every finding `lint` reports is one somebody chose to leave, so a new one
is visible against a known floor instead of lost in a wall of expected noise.

## LXVIII The news that arrives in four languages (T340)

The tooltip has 127 characters. When a reading needs more, `Fit` walks the sentence's rungs and
takes the first that fits — and takes none if none does, which is silent. That silence has now cost
the same thing three times, in three different languages, on three different sentences:

- **T222.** The paying state rendered no sentence in any language: its overage reading cost about what
  the sentence needed. Fixed by merging the two.
- **T302.** Both French billing states dropped the spell line — two worded rungs costing 16 characters
  where that reading left 15. Fixed with a third rung that has no word in it.
- **T288.** The blocked sentence rendered in en, pt-BR and pt-PT and in neither es nor fr, costing 42
  and 51 against 37 and 33 left. Found by running `--tooltip` and reading it.

Each was then locked shut by a test **written for that one state**, naming that one key. So the
check that would have caught T288 did not exist until T288, and the check that will catch the next
one does not exist yet.

**What this is.** One assertion over the cross product: for every state that carries news — blocked,
billing, unspent, at-limit — and every shipped language, the composition says *something* about that
state. Not a particular string: the property is that the news arrives, which is T222's own wording
and the thing three separate tests each re-state about one cell of the grid.

**What it is not.** Not a length limit on translations. A translator who needs more words should get
them; what may not happen is the sentence disappearing without anything noticing. The rungs are the
mechanism for that and they already exist — this is only the check that they were used.

## LXIX The four minutes between a change and looking at it (T341)

Measured 2026-08-11, twice, with **nothing changed between the runs and the previous build
succeeding**: `dotnet build` took **3m41** and **4m28**. Not a first build and not a cold cache — a
no-op. Single-file edits during T288 and T291 measured between 3m46 and 7m39, and one small change
came back in 57s, so the variance is real and the floor is minutes.

That is charged against the loop this repo is built around. `AGENTS.md` documents build → run a flag
→ look at what came back, and Block AI exists to make that loop the way things get verified. A
fault-injection check on T291 — put the defect back, confirm the check goes red, take it out again —
cost two of those builds for six characters of change.

**The likely cause, named rather than assumed.** `PublishSingleFile`, `SelfContained`,
`RuntimeIdentifier` and `EnableCompressionInSingleFile` sit in the csproj's top `PropertyGroup`, so
they apply to `dotnet build` and not only to publish. `bin\Debug\net10.0-windows\win-x64` holds
**252 files, 247 of them DLLs, 155.5 MB** — the whole runtime, laid down every build. Consistent
with what was measured; still a hypothesis until a conditioned build is timed against this one.

**The trade-off is the work.** Every dev flag in this repo runs that exact `.exe`, and a
framework-dependent Debug build needs the shared runtime present to start. So the question is not
"can these move to publish" but what a developer's `.exe` should be, and the constraint that decides
it is `STRATEGY.md` §IV: publish output must not change by one byte.

**What would settle it.** Time a no-op build with the four properties conditioned to the publish
path, and diff the published `.exe` against one built before. Anything less is T253's mistake.

## LXX The case list has outgrown the every-turn file (T342)

Adding T294's case to the interaction list put `AGENTS.md` **786 bytes and 5 lines** over its
budget. Fitting it back took eleven edits, only two of them to the new bullet. The other nine
compressed rules the task never touched: **Panes/Names**, **Profiles**, the **Menu** bullet's three
refusals, the **Unchecked** rule, the exit-code sentence, the invocation comment. A `-UseRunning`
refusal was dropped from the new bullet because the script enforces it at runtime — true, and
decided by the byte count rather than by the rule.

The file now sits at **24199 of 24200 bytes**. There is one byte of headroom, so the seventh case
cannot be added at all without another round of this.

**This is the budget working, not failing.** The ceiling is declared *"a ceiling to come down, not a
target"*, and the grinding is the pressure it exists to apply. What it points at is that the
six-case list is the wrong content for a file loaded every turn. `AGENTS.md` states the test itself
— a rule earns its bytes if getting it wrong produced a defect and `--selftest` cannot assert it —
and a per-case description of a script is not a rule. It is reference material, about a script whose
own header this file calls **the full text**.

**An argument this repo has accepted twice.** T191 moved the flag catalogue to `dev-flags`; T219
moved the per-file map to `file-map`. Both times the ceiling came down with the content rather than
the room being spent.

**So: the case list moves to a skill**, the section keeps the invocation, the exit codes and the two
actual rules — reading nothing is a FAIL, and an assertion that could have run and did not is
`Unchecked` — and the ceiling comes down by what left.

## LXXI Two waits, one deadline, two answers (T343)

`SaveAllTabs` now contains two waits for two asynchronous panes, and they answer the same question
differently at the same deadline.

`WaitForReport` (T298) returns a bool, and the caller **refuses**: no file, a named reason, exit 1.
`WaitForSessions` (T328) returns void and simply proceeds — so when the transcript scan outruns its
30 seconds, `-sessions.png` is a picture of *"Reading your transcripts…"* and the run still prints
`wrote …-sessions.png` and exits 0. That is the defect §XX.6 and §XX.30 are both about, still live
on the fourth tab.

**The reasoning that put it there was about hanging, not about proceeding.** T328's comment reads
*"Bounded, because a capture that never returns is worse than one that shows the honest in-progress
state"* — and the bound is what answers that. What to do *at* the bound is a separate question, and
T298 settled it with an argument that names nothing specific to the report: a capture that lands on
the placeholder is not slower evidence, it is none, and the read-out says the same word either way.

**What makes this one harder than T298**, and worth deciding rather than copying: the sessions
placeholder spoils one PNG of four. Refusing the whole run throws away three good captures for one
bad one, and re-running costs the scan again. So the choice is between refusing the run, writing the
three and naming the one omitted, or writing four and exiting non-zero. All three are honest; the
current behaviour — four files, exit 0, one of them a placeholder — is the only one that is not.

**Whatever is chosen, the two waits should read as one rule.** Two sibling methods in one capture
disagreeing about what a deadline means is how the next pane gets the third copy of this.

## LXXII The list with no owner, read again (T344)

T305 closed one hole in the observing gate and was found by reading the list of writers rather than
by a defect report. The same reading, run again immediately afterwards, finds two more.

**`TrayContext.LogResetEvent`** appends to `%LocalAppData%\ClaudeTray\reset-events.log` and consults
nothing. It is called from `NotifyReset`, which an observing tray reaches on any poll where a window
resets — so a check run beside the user's tray writes a line into their reset log.

**`ProfileStore.Migrate`** is worse in kind. It `File.Move`s each per-profile file out of the shared
directory into the profile's own, and it is called from the path that resolves the monitored profile
— which every tray runs at startup, observing or not. On a machine where the migration has not yet
happened, an observing process **moves the user's files**. Not an append that can be trimmed away: a
rename of a store the process promised only to read.

**Neither is caught today**, and for the reason T305 already established: `ObservingTray` drives the
writers somebody listed, and these are not on the list. `Migrate` in particular cannot be driven
from that check at all without a fixture, because by the time the check runs the migration has run.

**The fix is the gate in both, then an owner for the list.** `ProfileStore.Observing`'s doc comment
says the call sites are *"a list with no owner, and the one it forgets is the one that keeps
writing"* — and it has forgotten three. The durable form is the one T293 and T297 both use: read the
source, enumerate every `File.WriteAll` / `AppendAll` / `Move` / `Delete` under the store-owning
folders, and fail one whose method does not consult the gate. A fixture opts out by name, so an
exemption is a decision somebody made rather than a call nobody looked at.
