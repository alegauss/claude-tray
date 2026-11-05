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

## Block AI — Verification — the checks that prove a change

## Block B — Packaging, self-update, CI

## Block AO — What a stranger meets first

## Block C — Settings window (WPF Fluent)

## Block G — Localization

## Block I — Context Load Inspector

## Block L — Scan resilience

## Block Q — Keyboard input in the windows

## Block AJ — Working here — the repo's own docs and flags

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
T174 refused a quota bar animating one account's remaining quota into another's, on the grounds that
it says the forbidden sentence without a string.

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
Trabalho"*, with the inference that `/usage` must be lying. It was not. The session was on the work
account and the tray was showing the personal one: **both correct about different questions**, and
nothing in the app connects them.

The first theory was wrong instructively. The suspicion was a race, the write being asynchronous
since T149; measured (T173), it is not. The variable had never been written *at all* until the
Windows-wide switch was ticked, three minutes after the editor that asked the question had started.
**A spinner verifies the click; what needs verifying is the result.**

Three failure modes hide behind the one symptom, and separating them comes first:

- **A — the write is never attempted.** `SyncEnvironmentProfile` is off (the default), so
  `SyncEnvironmentToPin` returns immediately. This is the one that happened. → §XVI.1
- **B — the write is attempted and its outcome is never read.** `Adopt` returns *accepted*, by design.
  → T173
- **C — the write lands and the next process still misses it.** A child inherits its parent's environment
  block at creation; the editor's parent was an Explorer from six hours earlier, and Explorer's refresh on
  `WM_SETTINGCHANGE` is a courtesy, not a guarantee. → T172

C bounds the block: **no registry check proves the next process will see the value.** Hence T172
displays the effective value continuously rather than asserting it once at the pick.

## §XVIII — Extra usage is money, and the tray is asleep for it (Block AE)

The report indicts the whole feature: *"apesar de estar em 100% de uso, ainda funcionava, porque
estava com uso extra ativado — mas no Claude Tray isto não aparece evidente, nem no gráfico, nem no
tray, em lugar nenhum"*. The user worked straight through a number the app called a ceiling.

This is a block and not a missing feature because **none of the data is missing**:

| Signal | Where it is read | What is done with it |
|---|---|---|
| `anthropic-ratelimit-unified-overage-utilization` | `ApiClient.FetchAsync` → `UsageData.Extra` | one tooltip line, only when `> 0.001` |
| `anthropic-ratelimit-unified-overage-reset` | → `UsageData.ResetExtra` | the countdown on that same line |
| `hasExtraUsageEnabled` (`.claude.json` → `oauthAccount`) | `ClaudeAccount` → `ClaudeInfo.ExtraUsage` | one text row on the System page |
| `anthropic-ratelimit-unified-5h-status` | `UsageData.Status` | printed verbatim in the tooltip; read by nothing |

Three of the four predate the Statistics window. The gap is not acquisition: **nothing downstream
believes the account can be past 100% and still working** — T179 has the measurement.

Two constraints bind. The **privacy promise** is not in tension: rate-limit headers are quota
metadata, §I.1 restricts *transcripts*, and this block adds no transcript reading and no endpoint.
The **"profiles are contexts, not quota pools"** non-goal is in tension throughout — every task
makes overage more visible, and the next sentence, *this account is out, the other has room*, is the
one the roadmap forbids. T184 binds hardest; T174 found the answer's shape: a receipt, not a reward.

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
forbids — §I.7, and T174 has the shape of the answer: a receipt, not a reward.

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
spending — §I.7 and T174. And a price table right about which models the overage even bills, since
pricing turns the account was never billed for is worse than saying nothing. That was the gate, and
the instruction was to measure the list first.

**Measured 2026-08-04, and the third fails.** `.claude.json` caches
`tengu_usage_overage_included_models` under `cachedGrowthBookFeatures`, reading `["Fable", "Fable
5"]`. Eight days of transcripts here are 34,595 Opus turns, 195 Sonnet and **zero Fable** — so under
one reading of the flag ("these bill as overage") the estimate for a spell that demonstrably cost
money is $0.00, and under the other ("these stay included") it is the whole $5,834 notional. One
sample cannot tell them apart, which is a shape one sample cannot map. Two smaller cracks in the
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

That is not the single-sample vocabulary §XXIII.4 declines to map. The name has taken two values in
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

## XCII The site is a workspace, not a page

The page shipped as one `docs/index.html`, and for a year that was the right size. What it grew into
was the argument for a workspace, and T368 built one.

Nine hundred lines of hand-written markup carried the copy, the palette and the composition in one
file, so a claim could not be reviewed apart from the markup displaying it. The committed
`sitemap.xml` listed one URL — correct when the site was one scroll, still one entry after the page
had seven sections nothing linked to. There was no dark counterpart because there was no light mode:
the palette was written once, in hex, in a `<style>` block. And the head asked Google Fonts for two
families on every load, on the page whose own privacy section claims the product sends nothing
anywhere.

None of those was a defect anybody reported. They are what a single file cannot hold, which is why
the answer was a workspace rather than a rewrite of the file — and why the shape is
[freewilly](https://github.com/alegauss/freewilly)'s, so the sites under `alegauss.github.io` are
maintained the same way instead of each carrying its own conventions.

T369 finished the shape by drawing what the other three have and this one did not: a band closing
the hero and opening the footer, whose motif is the product's own subject. Here that is the icon —
bars rising from a floor, with the even-pace line ruled across them.

## CVI What the alternatives do that this app does not

In October 2026 the app was read against the two tools listed beside it — Claude Code Usage Monitor
and ccusage, both terminal programs — and against CodexBar, the menu-bar app that leads the category
on macOS. The subsections are the gaps that survived the non-goals. Each is filed under the block
that owns the capability, not under this comparison, because parity is not the bar: a gap is here
only because somebody using this app would act differently with it.

Left out on purpose, so they are not filed again: polling Anthropic's status page for incidents (a
network call §I.2 does not allow); other agents and providers — Codex, Gemini, Cursor — which is a
positioning question for STRATEGY.md, not a task; hook-driven "Claude is waiting" notifications,
because a hook is a write to Claude Code's settings (§I.4) and its payload carries message text
(§I.1); and macOS, Linux or mobile builds.

### CVI.9 How much the status line actually saves (T398)

T390 lets a tray tick take Claude Code's status-line reading instead of calling the API, under four
refusals in `StatusLineFeed.Substitute`: an API reading under an hour old, no spending past the
quota, both statuses `allowed`, both windows under 0.9. Two numbers were assumed rather than read:

1. **How often the status line runs while somebody works.** Claude Code documents event-driven runs
   with a 300ms debounce, but not their spacing across a working hour. If most five-minute ticks
   find a reading younger than the interval, the saving is real; if the payload goes quiet between
   responses, few ticks are covered and the hourly floor dominates.
2. **Whether its percentages match the headers on the same minute.** Both come from the same
   account's rate-limit state, but the payload is what Claude Code last received, and that may lag
   the API by a response.

How to measure, with no new mechanism: paste the Settings row's entry into one profile, work a day,
then compare `statusline\<dir-key>.json`'s rewrite times against the poll cadence, and pair the
status-line lines in `usage-history.jsonl` (`s:1`) with the API lines (`s:0`) around them. A gap of
more than a point in either window, or a share of covered ticks under a third, is the finding.
Either one changes `ApiAtLeastEverySeconds` or the 0.9 floor, not the design.

## CVII Profiles

Accounts, profiles and the machine-wide config dir: how the tray finds them, which one the icon
follows, and what it writes outside its own folder.

## CVIII Packaging, self-update, CI

### CVIII.2 Submitting the Store package (T412)

T409 built the Store package and measured it on a copy installed from its layout.
`build\build-msix.cmd` makes the unsigned `dist\ClaudeTray.msix` under the Alegauss reservation
(Store ID 9P27BHWNT56D), whose family name matches Partner Center's. Inside it the updater stands
down, startup is the manifest's `startupTask`, the status line runs the `ClaudeTray.exe` alias, and
the user-scope `CLAUDE_CONFIG_DIR` write reaches shells outside the package, so no restricted
capability is needed.

The package passed certification and the Store lists the app
(apps.microsoft.com/detail/9P27BHWNT56D), so the submission and the sign-in policy question are
behind it. What is left is the owner's: **a week on the Store build** in place of the winget one.
They cannot run side by side: they share the `Local\ClaudeTray.SingleInstance` mutex, and the
package's `%LocalAppData%` is redirected, so its settings start fresh.

Done means a week of use on the Store build held the startup task, the alias and a profile switch.

## CX What a stranger meets first

The first minutes of somebody who has never seen the app: what the first launch shows, what an icon
in the overflow says, and what the app says on a machine it cannot read anything from yet.

## CXI What the November 2026 audit found

The defects the 2026-11-05 audit confirmed against the code: six scanners over the source
partitions, each finding then checked by an adversarial verifier, and the survivors grouped so that
one subsection is one fix that ships in one commit. The baseline was green when they were found
(build with warnings as errors, `--selftest --quick`, the site's typecheck and tests, `roadkeep
lint`), so none of these is caught by an existing check; where a check was meant to hold the
property, the subsection says why it did not.

Several share one cause and are grouped by it rather than by file: a transcript or store opened with
a share mode that a concurrent writer refuses, a child process read to the end before its timed
wait, a counted string with one form. Where a shipped task promised the property and the code no
longer keeps it, the subsection names that task as a regression rather than as new design.

### CXI.4 A second rate card (T422)

`UsageInsights.Price` (UsageInsights.cs:36) hardcodes opus and unknown at 5/25, sonnet 3/15, haiku
1/5 and a 0.1x cache read; `ListPrices` (ListPrices.cs:48-52), which T346 and T401 keep current, has
opus-5-5 at 4/20, sonnet-5-5 at 2/10, haiku-5-5 at 0.10/0.50 and a 0.05x cache read. The comment
above `Price` says it is the one table for the app.

`Price` weights the insights shares and every cost figure on the Context page
(ContextScanner.cs:124, TrayContext.cs:330, ContextPage.Gauge.cs:138, :241), so on this machine's
Opus 5.5 transcripts the cache-read weight is double what the Sessions list uses. `Price` should
read `ListPrices`, and a selftest should hold the two to one source.

### CXI.5 A token refresh and the connection per poll (T423)

`ApiClient` reads `.credentials.json` with `File.ReadAllText` (ApiClient.cs:287). An open that
collides with Claude Code rewriting the file, or a parse of a half-written one, throws `IOException`
or `JsonException`, neither of which `IsTransient` names (:261-263), so the poll returns `Transient
= false` and TrayContext.cs:1253-1264 replaces the last good reading at once. Whether Claude Code
rewrites in place was not established; the classification is wrong either way.

Each `ApiClient` owns a `new HttpClient` (:130) and is not disposable, and one is built per other
profile per tick (TrayContext.cs:1104) and on every menu open (:706). The pool closes idle
connections, so this is a fresh TLS handshake per fetch and lingering sockets rather than an
unbounded leak. One shared client fixes it.

### CXI.6 The --profiles line that never prints (T424)

ProfilesCli.cs:62 builds the continuation line as `"             " + (p.IsWsl ? ... : null) ??
ClaudeAccount.ActionFor(...) switch { ... } + $"   cwd {workDir}"`. C# parses that as `("   " + x)
?? (...)`, and the left side is a non-null string whatever `x` is, so the right side never runs.

For a Windows profile the line is whitespace; for a WSL one the cwd is missing. The three
`CLAUDE_CONFIG_DIR` cases, including the warning that an inherited dir would select another profile,
and the missing-cwd note never reach the read-out that T144 made the app's statement of what a
launch does. Parenthesise the ternary, and give `--profiles` a selftest that reads the line back.

### CXI.7 The --context read-out's exits and its filter (T425)

T261 made `ReadOut.Failed` the rule for this surface: a read-out that could not start exits 1
(ReadOut.cs:7-10, :26), and `SessionsCli` follows it. `ContextCli` misses three sites. A fixture
that fails to build under `--sample` prints and returns (ContextCli.cs:42). `WriteContextReport`
catches the write failure and only prints (:211-214). `no project matching` is printed and returns 0
(:102, :282).

The filter matches `ContextProject.Name`, the leaf folder (ContextScanner.cs:144-146), with
`FirstOrDefault` (ContextCli.cs:97, :274). The scanner's own comment notes three projects named
`2026.3` here, so `--context 2026.3` shows one of them and says nothing about the others. An
ambiguous filter should refuse and list the candidates by their full path.

### CXI.8 Child processes on the profile path (T426)

`WslProfiles.Running` calls `StandardOutput.ReadToEnd()` before `WaitForExit(10_000)`
(WslProfiles.cs:97-98), so the bound is reached only after the child has exited; stderr is
redirected and never read (:91). It runs under `lock (Gate)` (:74) and is reached on the UI thread
from `RefreshWatched` (TrayContext.cs:693, via ClaudeAccount.Candidates :592), so a `wsl.exe` that
never returns freezes the menu that was just opened. The result is cached, so it bites on a cache
miss.

`QueryAuthStatusAsync` cancels its reads after 30 s (ClaudeAccount.cs:436-438) but nothing kills
`cmd` or `claude`, and disposing the `Process` does not stop it; stderr (:427) is never drained, so
a chatty CLI can time out a healthy check. Both need a read that runs alongside the wait and a kill
on timeout.

### CXI.9 Timed waits that cannot time out (T427)

`SelfTestCli.PowerShell()` reads `StandardOutput.ReadToEnd() + StandardError.ReadToEnd()` and only
then calls `WaitForExit(60_000)` (SelfTestCli.cs:787-788). The stdout read blocks until the child
closes stdout, so the 60 s bound documented for the `link` script run cannot fire, and a child that
fills stderr first deadlocks both sides.

`CasesRun.Profiles()` has the same order (CasesRun.cs:330-331), never reads the stderr it redirects
(:319), ignores the wait's result and kills nothing, so a hung `--profiles` blocks the whole case
run instead of producing the absence the harness is built to report. Both want the two streams read
asynchronously and a kill when the wait expires.

### CXI.10 Checks that claim more than they ran (T428)

The resume-hour pair is gated on `advised.HasAdvice` (SelfTestCli.Usage.cs:78), which is what
`ActivityShape.Build` produced, and the skip is on the allowed list (SelfTestCli.cs:67-68). A build
that never advises passes with an allowed skip, which is the T161 shape AGENTS.md forbids; T218
cannot turn it red because it is allowed. Gate on the fixture's precondition instead.

`CasesRun` answers a desk that cannot observe with `Assert.True(true, ...)` and `return`
(CasesRun.cs:82). xUnit prints the message only on failure, so a blind runner is green with zero
cases run, though AGENTS.md says a hole is never a pass (T201).

`ObservingTray` writes nothing, which the counted assertion holds, but it reads the real `~/.claude`
tree and the shared caches (SelfTestCli.Docs.cs:1410-1415). AGENTS.md says a real profile's stores
are never read; the sentence should say what the suite does.

### CXI.11 Three anchors the design-system move dropped (T429)

The "In depth" nav item points at `#features` (site-content.ts:60), which is the Why section's id
(Why.tsx:6); the depth-page index is `SiteFeatureIndex`, whose id is `features-index`
(Landing.tsx:65-66).

The privacy feature sets `anchor: "privacy"` (features.ts:355), and its depth page links back to
`/claude-tray/#privacy` (Feature.tsx:16), but nothing under `site/src` carries `id="privacy"`, so
the link lands at the top. The same section is the only one using `className="wrap reveal"`
(Privacy.tsx:7); `.wrap` is defined neither in `index.css` nor in the design system's CSS, so the
banner runs full width while every neighbour uses `vg-site-wrap`. All three date from the T389 move.
A site test that every in-page `href="#x"` resolves to an id would hold them.

### CXI.12 Two privacy promises the site does not keep (T430)

The consent banner says nothing is loaded until the visitor chooses (Consent.tsx:85). The footer
renders `Ad` (Chrome.tsx:71, Ad.tsx:16), whose `useAds()` appends `https://ads.japode.com/v1/ads.js`
on mount (ads.ts:40-43, :59-60), before any choice. The lint test guards only `googletagmanager.com`
(lint.test.mjs:46-57). The sentence is what is false; whether this loader needs consent is a
separate decision, since ads.ts:3-5 says it uses no identifiers.

The privacy depth page says the one-token API call every five minutes is all that leaves the machine
(features.ts:365, repeated at site-content.ts:804). §I.2 names two endpoints, the usage API and
GitHub Releases, and the site's own Why card says the app checks Releases (site-content.ts:200). The
cadence is also stale: an idle tray and the status-line feed both call less often
(build/release-notes.md:15-16).

### CXI.13 The About page's licence (T431)

`settings.about.disclaimer` reads "Released under the MIT License." in every `lang/*.json` (en:412,
es/fr/pt-BR:408, pt-PT:409). T11 moved the project to Apache-2.0: `LICENSE`, README.md:1020 and the
winget locale manifests all say so. Fix the five strings, and add a one-claim-in-two-places check
that the licence named in `lang/en.json` matches the one `LICENSE` declares.

### CXI.14 Counted strings with one form (T432)

Strings that take a count through `L.T` with a single form: `stats.sessions.count`
(StatisticsPage.Sessions.cs:304), `context.fix.count` (ContextPage.Gauge.cs:133,
ContextPage.xaml.cs:439), `insights.summary` (TrayContext.cs:652), `settings.sys.scopes`
(SettingsPage.System.cs:158), `context.footer`, `context.project.detail`, `context.all.rowDetail`
and `context.observed.parts` (ContextPage.xaml.cs:341, :345, ProjectRow.cs:72, :77), and
`stats.tasks.promptChars` (SessionListRow.cs:57). Each renders "1 conversations", "1 things", "1
requests" and so on.

`stats.live.session1`/`sessionN` and `scaleTipClipped1`/`N` (StatisticsPage.Throughput.cs:213,
StatisticsPage.Format.cs:54) choose by `== 1` in code; their output is right in the five shipped
languages but they sit outside the pair rule.

The plural checks (SelfTestCli.Lang.cs:94-146) catch `(s)` shapes and look only at keys already
ending in `.one`/`.many`, so none of these is red. `L.N` takes one count, so `insights.summary` and
the other two-count strings need splitting into parts.

### CXI.15 Release notes and installer text (T433)

T418 made `build/release-notes.md` the single source of a release's text: `update-release.cmd`
passes it with `--notes-file` (:81) and `update-winget.ps1` requires it (:109-119). When `build.yml`
runs on a tag that has no release yet, it creates one with `gh release create ... --generate-notes`
(build.yml:72), so the release body and the winget manifest's `ReleaseNotes` say different things
for one version. The normal flow creates the release first, so only the fallback is affected.

`installer.iss` offers English in `[Languages]` (:42-43) but writes the task descriptions and groups
(:46-47), the uninstall icon's name (:54) and the finish-page run text (:65) as Portuguese literals,
with no `[CustomMessages]` or `{cm:}` lookup. An English install shows "Iniciar ... com o Windows"
and a Start-menu entry named "Desinstalar".

### CXI.16 Two dev tools that report the wrong outcome (T434)

`scripts/Encode-Clip.ps1` sets `$ErrorActionPreference = "Stop"` (:31), which Windows PowerShell
does not apply to a native exit code, and never reads `$LASTEXITCODE` after its three `ffmpeg` calls
(:50, :57, :59), so a failed encode still prints `wrote <file>` beside a stale or empty output.

`--settings-tray` and `--main` end with a bare WinForms `Application.Run()` (Program.cs:568, :600)
beside a WPF application set to `OnExplicitShutdown` (:560, :579), and neither `PageWindow` nor
`MainWindow` exits anything on `Closed`. Closing the window leaves a windowless process running. The
case harness kills what it launches, so only a person or a script that waits on the process meets
it.

### CXI.17 What a Context rescan throws away (T435)

`StartScan` calls `ShowStatus("scanning")` unconditionally (ContextPage.xaml.cs:207), which
collapses `MasterPane` and `DetailPane` (:316-322) on every Rescan, every revisit (:123) and every
watcher refresh (:155-156); `Apply` then replaces `ProjectList.ItemsSource` (:271), so the scroll
resets. Statistics stopped exactly this blink in T118 (StatisticsPage.xaml.cs:371-378).

The selection to keep is read as `SelectedItem?.Slug` (:261), which is `""` for the All projects row
(ProjectRow.cs:53); `Matches("")` is false, so the overview is replaced by the heaviest project
(:278).

A debounced rescan that fires while a scan runs returns at `if (_scanning) return;` (:204) after
`_liveRefresh` was already set at :155, so the edit is never picked up and the running scan is
labelled "updated automatically" whether or not it saw the change. It should queue one more scan.

### CXI.18 The Sessions pane on every poll (T436)

Each poll runs `UpdateSnapshot` -> `Reload` -> `Render` -> `LoadSessions`
(StatisticsPage.xaml.cs:217-229, :582). `LoadSessions` nulls `ItemsSource`, shows "Reading your
transcripts..." and rebuilds every row (StatisticsPage.Sessions.cs:98-102), so a call tree the user
opened (T329) closes and the list blinks every five minutes by default. The comment says once per
reload rather than per poll, but a reload happens on every poll. The list should rebuild only when
the index changed, and keep each row's open state across a rebuild.

### CXI.19 The second Save in one visit (T437)

T229 made Save carry over any field the page did not change, by comparing the page's values with
`_opened`, the snapshot taken when the page opened (SettingsPage.xaml.cs:26, :56, :288;
Settings.cs:349-354). `_opened` is readonly and set once, and the page is rebuilt only on Cancel
(MainWindow.xaml.cs:171-187). So after a first Save commits a page edit, a menu flip of that field,
and a second Save, the page value still differs from `_opened` and is written back over the menu's.
Renew the snapshot after each Save; a selftest of two Saves around a menu change would hold it.

### CXI.20 Controls the keyboard cannot reach (T438)

The six Settings sidebar items are `Border`s with only `MouseLeftButtonUp`
(SettingsPage.xaml:181-252), so a keyboard user cannot leave the General page;
keyboard.cases.json:33 already notes the item offers no Invoke. The four About link cards are the
same (:1189, :1203, :1217, :1231), with no automation name. The Sessions rows
(StatisticsPage.xaml:98, StatisticsPage.Sessions.cs:72), the live chart's project link (:668) and
the Context overview's heaviest-project rows (ContextPage.xaml.cs:516-530) open on a mouse-up only.
There is no `KeyDown` handler or `InputBinding` anywhere in `src/Ui`.

`ToastWindow`'s close button has `Content="✕"` and no `AutomationProperties.Name`
(ToastWindow.xaml:91), the shape T175 fixed elsewhere. T142 did not reach these. They want to be
buttons (or carry focus, a peer and Enter/Space), and the `keyboard` case should Tab to each.

### CXI.21 The error banner's colours (T439)

`ErrorBanner` sets `Background="#332B1416"`, `BorderBrush="#66E33B30"` and the glyph's
`Foreground="#E86A5E"` (StatisticsPage.xaml:339-347), with no comment justifying them. They were
tuned on a dark card; on a light one the wash is murky and the salmon glyph has poor contrast. UI
convention 3 and §I.5 say a theme-able surface takes `{DynamicResource}` Fluent brushes; the
critical-severity system brushes cover this case.

### CXI.22 Dates outside the Dates path (T440)

T263 routed dates through `Dates` because a fixed pattern pins the field order. `SessionListRow`
(:93) and the Sessions peak line (StatisticsPage.Sessions.cs:343) still format `"MMM d, HH:mm"` with
`L.DateCulture`, so French reads "août 3, 14:10"; `Dates.MonthDayTime` exists (Dates.cs:40). The
DateOrder check (SelfTestCli.Lang.cs:699-752) tests `Dates` itself, not its callers.

`ToString("yyyy-MM-dd")` with no culture runs on the OS calendar: a probe on .NET 10 gives
2569-11-05 under th-TH and 1448-05-25 under ar-SA, for `DateOnly` too. The tray never sets an
invariant culture, so this reaches SourceRows.cs:80 and :130, SessionListRow.cs:128-129,
PeriodListRow.cs:33-34 and StatisticsPage.Sessions.cs:79. Pass the invariant culture, or route them
through `Dates`.

### CXI.23 Skill usage that says never (T441)

Three faults in `ContextUsage`, each feeding the `never-invoked` advice (ContextRules.cs:421).

`CountFile` opens with `File.ReadLines` and returns empty counts on failure (ContextUsage.cs:267,
:277), yet `FilesRead` still increments (:213), `Complete` is set because every file was read
(:220), and the zero is cached under the walk-time fingerprint (:203-204). T261 promised that a
partial read is reported as one. T419 fixed the share mode (src/Core/SharedRead.cs) and keeps a
failed read out of the cache; what is left here is that a failed open is never counted as read, nor
complete.

`Named` keeps the full `input.skill` value, while `Candidates` yields only the frontmatter name and
the label tail (:99-105, :318), so a `plugin:skill` call, which this machine's transcripts do carry,
never matches.

`WhenUtc` is the transcript's mtime (:190, :279), so every call in a recently touched file counts as
within 30 days and as last used today. That error leans toward more use, never toward a false never.

### CXI.24 A stable project's drift (T442)

`ContextHistory.Record` skips a value equal to the last one (ContextHistory.cs:73-75), and `Trend`
keeps only samples from the last 60 days (:96-101). A project unchanged for longer has no sample in
the window, so `Trend` returns null, which reads as never recorded; when it finally changes it has
one point and no baseline, so the drift row stays hidden (ContextPage.Gauge.cs:176-178). `Trend`
should carry the newest sample before the window in as the baseline.

### CXI.25 A torn read of .claude.json (T443)

`ClaudeAccount.TryParse` returns null on any failure (ClaudeAccount.cs:892-900), so `AccountUuid` is
null (:229-236) and `ProfileStore.KeyFor` falls back from `acct-...` to `dir-...`
(ProfileStore.cs:93-96). `RefreshWatched` runs on every menu open, sees a new key, and resets
`MonitoredAccount` and the other profiles' data (TrayContext.cs:724-734), dropping the burn tracker,
the extra-usage alarm and the last good reading, and filing readings under the other folder until
the next good read flips it back.

The path is verified; whether Claude Code rewrites `.claude.json` non-atomically was not. An
unreadable file should keep the last known identity rather than read as a different one.

### CXI.26 An unreadable settings.json (T444)

`Settings.Load` catches every exception and returns defaults (Settings.cs:285-292). `Save` writes in
place through `File.WriteAllText` (:369, StoreFile.cs:47), unlike `UsageHistory`, which writes a
temp file and moves it (UsageHistory.cs:179-181). A crash mid-save leaves a truncated file; the next
launch loads defaults and the first quiet save (`NoteDayOfUse`, TrayContext.cs:138-139) writes them
over the registered profiles, the monitored dir and the level lists. T20 clamps bad values, not a
bad file. Save atomically, and on a parse failure keep the file aside instead of overwriting it.

### CXI.27 A status line set up on a removed exe (T445)

`StatusLineFeed.Classify` (StatusLineFeed.cs:217) recognises any `ClaudeTray ... --statusline`
command as the app's own, and the Claude Code settings page (SettingsPage.ClaudeCode.cs:174-185)
then reports it set up and hides the command to paste. On this machine both profiles'
`settings.json` run `%LOCALAPPDATA%/ClaudeTray/ClaudeTray.exe --statusline`, and that exe was
removed when the Store build replaced the Inno install (T412): the profile histories hold 1894 and
95 API readings and no status-line reading, and the feed's directory holds one reading from
2026-10-27.

This is also why the day of readings T398 waits for cannot start. Classify should check that the
command's exe resolves, and the page should offer the alias form when it does not.
