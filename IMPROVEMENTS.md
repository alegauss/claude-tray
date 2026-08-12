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

## LXXXVIII An assertion with a language in it (T361)

T359's Sessions case finds the list-price note by looking for the substring `list prices` in the
accessibility tree. That is English. The note is one of the five translated strings T346 shipped, so
in the other four the case does not find it and reports the note as unreadable:

```
$ Check-Interaction.ps1 -Case Sessions -Lang pt-BR
[FAIL] the info dot opened no readable note - a popup a capture cannot see and a screen
       reader cannot either (T346)
```

**The failure is the harmless direction of a worse habit.** A red is loud. What the same mistake
buys in the other direction is an assertion that quietly matches nothing and passes — §XX.2's
defect, and the reason this file resolves every expected string through `Label` rather than typing
it. `-Lang` exists precisely so a case can be run in the language a layout or a string is suspected
in (T260); a case that only works in one language removes the flag's point.

**It would not have surfaced on its own.** `check.yml` runs the interaction suite in English.
Nothing runs it in another language on a schedule, so the case would have read as covered for as
long as nobody happened to pass `-Lang`.

**The string carries a placeholder, which is why it was skipped.** `stats.sessions.method`
interpolates the date the rates were read, so it cannot be compared whole. The part before the
placeholder is fixed in every language and is what identifies the note; the date is what the
assertion is about, and it is already checked separately as a pattern rather than a value.

**The general form is worth stating**: a literal in an assertion is a language, and this file has
one place that turns a key into the text a window will actually show.
