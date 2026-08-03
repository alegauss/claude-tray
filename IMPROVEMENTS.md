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

### XX.20 A read-out flag whose whole promise is that it does nothing

`--probe --recorded` promises to make no call, and `--probe --live --recorded` promises to refuse
rather than silently pick a half. Both are rules, and both are held up today by whoever last ran the
flag by hand.

That is a shape this repository has already decided is worth a check. T186 and T198 assert that a
preview flag refuses an unknown variant rather than rendering the default, because the failure is
silent: a screenshot of the wrong thing looks exactly like a screenshot of the right one. The
failure here is quieter still. A `--recorded` that made a call would print what it prints now with
one extra block, and the only evidence would be a request spent against the very account the flag
exists to stop spending against.

The refusal is pure and can be asserted directly once the argument decision is separable from the
run — today they are one method, and the first thing it does after the guard is load the settings
file. Splitting the decision out is most of the work and is worth doing for its own sake: what
`--probe` does with `--live`, `--recorded`, `--all` and with none of them is four outcomes decided
by three booleans, and not one of them is a value anything can currently look at.

Whether the no-call promise itself can be asserted without a seam in front of the network is the
open part.

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

### XXII.6 The part of a row that is a claim about another file

`CHANGELOG.md`'s index gives each block a row, and a row carries one thing that is not about the
ledger at all: `(active — see ROADMAP)`, which claims that block still has open work.

T223 asserted the two facts a row states about the ledger — that its letter has a heading, and that
its anchor still resolves to that heading — and stopped there. The marker was left as it was found,
and it was already wrong in both directions: `AE` and `AG` have nothing open and carry no marker,
`AI` has nothing open and carries one, and `AJ` acquired one in T219 and kept it through the block
emptying three tasks later.

It is the smallest of the three and the one with the most direct consequence, because it is the only
part of the index that answers *should I look here*. A row claiming a block is active when it is not
sends the next reader to a heading with nothing under it; a row silent about an active block reads
as a theme that finished, which is the habit the theme table exists to break.

The reading is derivable from a file `--selftest` already opens: a block is active exactly when
`ROADMAP.md` carries an open task line under its heading. What to settle is whether the check
*requires* the marker or only refuses a wrong one — the older rows were written before the convention
existed, and a check that rewrites history is a check that fails on arrival.

## XXIII What the API says about permission, and what the app infers instead (Block D)

Every signal this app has about whether an account may spend past its included quota is inferred
from a file or from an effect. The API states it directly, on every response, and nothing here has
ever been able to read the statement — because until a second account was probed there was only one
sample and it could not be told apart from a default.

### XXIII.1 The refusal the display is not allowed to see, and the reading that changes that

`QuotaStates.Resolve` answers the icon's colour and the tooltip's sentence from two signals: the
overage utilization, and `hasExtraUsageEnabled` read out of `.claude.json`. It deliberately does not
take the overage window's own status, and `Allows` says why: one reading, of one account, inside its
quota and with the flag set, sent `overage-status: allowed` — so nothing could tell *you are
permitted* apart from a value every response carries regardless, and believing it wrongly would put
a clay bar and "extra usage is paying" in front of somebody whose work had stopped. The display was
to keep inferring until a reading arrived that told the two apart.

T211 took that reading. A second account on this machine answers `overage-status: rejected`, carries
no overage utilization or reset at all, and sends a header nobody here had seen:
`overage-disabled-reason: org_level_disabled`. The status is not a constant, and the refusal names
its own cause.

What that does not settle is the affirmative — `allowed` from an account inside its quota still
cannot be distinguished from a default, so the asymmetry stands: a status may buy a poll and may not
paint a screen. What it settles is the negative, which is the dangerous direction. A local flag
reading enabled while the organisation has disabled it produces exactly the sentence T182 wrote to
prevent. Both accounts here agree today, so this is latent rather than live.

`overage-disabled-reason` is the second half of the task: the only signal measured so far that could
turn *you have stopped* into a sentence naming why, which no surface can say at all.

## XXIV This machine's install, read from a file another program writes (Block N)

The System information page reports the machine rather than the app, so every figure on it is read
out of files Claude Code owns and this app only opens. What those files are allowed to contain is
therefore not this app's decision, and a count taken over them is a claim about their shape.

### XXIV.1 A key count is a claim about a file this app does not write

`ClaudeAccount` reports a profile's project count as the number of keys under `projects` in
`.claude.json`. That is a count of *entries*, and the page presents it as a count of directories.

On this machine the two differ. One profile's file carries 39 keys and 37 directories: `d:/Git/x`
and `D:/Git/x` both appear, twice over, differing only in the case of the drive letter. Windows
paths are case-insensitive, so those are one folder that Claude Code recorded under two spellings —
a shell that lower-cased the drive on one launch is enough to produce it, and nothing in either
program is wrong for having written it.

The fix is to count what the number claims to count, which means folding the keys under the same
comparison the filesystem uses before counting them. `OrdinalIgnoreCase` is the whole of it for the
drive letter, and it is also not quite enough in general — a path reached through a different
spelling of the same directory is a wider problem, and this task is deliberately only the part that
is measurable here.

Worth noting where this does *not* reach. `ProjectSlug` owns the `projects/<slug>` encoding and the
transcripts live in directories named by it, so a second spelling produces a second slug directory
and the scans that walk those directories are counting real folders. This is the config file's own
map, read once for one number on one page.

## XXV Toast cards — what the card actually draws (Block E)

Two things the toasts got wrong that no capture ever objected to, both found by looking at a card
rather than at the code that built it.

### XXV.1 A card's emoji is drawn by whichever font WPF reaches first

Captured while adding T174's card: the new laptop glyph came out black, and so did the ones that
shipped with Block E - docs/notify-surprise.png, committed long before, has the same monochrome
popper. So this is not the new card, it is every card, and nobody ever looked at the glyph rather
than the layout.

The codepoints in use are emoji-default, which rules out the variation-selector explanation and
points at the font instead: the Emoji TextBlock names none, so it inherits the window's Segoe UI
stack, and neither family in it has colour glyphs. WPF resolves the run against Segoe UI Symbol and
draws a perfectly good outline.

The fix is a font on that one TextBlock, not a per-card exception list - the seven cards pick their
emoji freely and every one should render the same way. Worth a check of its own, because the failure
mode is a glyph that draws correctly and is simply the wrong one, which is exactly what a capture
certifies without complaint.

### XXV.2 A capture certifies that a card rendered, not that it fits

Found in the pt-BR capture of T174's card: the title wrapped to a second line, the caption slid
under the bottom edge, and the flag reported success. T174 fixed that one card by sizing it to its
content, which is a fix for that card and not for the class.

The other six still carry a fixed Height picked against the English wording, and every string on
them can be retranslated tomorrow. The clipping is invisible to everything the repo runs: the
self-check never builds a window, and the capture writes whatever WPF arranged.

What is missing is an assertion, not a layout. After the settle timer, ask each card whether any
text block's desired size exceeds what it was given, and refuse the capture rather than writing a
PNG of the defect. Cheap, because the window already exists by then, and it covers all five
languages when driven with the language flag.

## XXVI One setting, two places that change it (Block S)

The tray menu and the Settings page now both write fields the page believes it owns, and only one of
the two knows the other exists.

### XXVI.1 A field two surfaces write needs to say which write is newer

T162 settled that the tray owns some fields and the page owns the rest, and marked the tray's with
an attribute so no hand-written list could go stale. That split assumed each field has exactly one
writer. Two do not.

FollowActiveProfile has had a menu toggle since T126 and a page control since it shipped;
SyncEnvironmentProfile joined it in T171, which put the machine-wide switch in the Profile submenu
on purpose. Neither is TrayOwned, and neither can be: the page has a real control for each, and
CarryTrayOwnedFrom would overwrite an edit the user just made there.

So the revert is live in both directions and needs no unusual sequence - open Settings, flip the
toggle in the menu, press Save. Measured against T171's switch this also silently un-writes the
environment variable, because the reconcile runs off the value Save restored.

The shape wanted is a decision about which write is newer, not another list. A per-field stamp, or
the page reading the live model for exactly the fields it shares, are both plausible; what must not
happen is a third category of ownership, since two were already one too many to keep straight.

## XXVIII Input, focus, and being readable (Block Q)

What a control ANNOUNCES, as against what it draws. A picture cannot see an accessible name and
neither can any check, so this is the block where a surface that renders perfectly and tells
assistive technology nothing gets filed.

### XXVIII.1 A menu entry announces its text and nothing else

Every entry in the Profile submenu was dumped through UI Automation while T230 was being written.
`HelpText` is empty on all of them, and the only pattern an entry carries besides `Invoke` is
`Toggle` — and that one only while the item is **checked**. An unchecked `ToolStripMenuItem` exposes
no `TogglePattern` at all, so "off" and "not a toggle" are one reading.

Two consequences, and the first is the one that matters. `ToolTipText` is where T171 says what a
pick actually reaches — *"the tray only"* against *"the tray and the Windows user environment"* —
and where T172 explains that a session already running keeps what it started with. Those sentences
exist because the icon moving reads as "the machine is now on this profile" and that is only
sometimes true. WinForms does not map `ToolTipText` to `accHelp`, so a screen reader announces the
entry's text and nothing else: the person who most needs the sentence about scope is the one who
cannot reach it.

The second is smaller and follows from the same gap: a menu toggle's OFF position cannot be asserted
by anything, so T230's checks are written to claim only which entry is On. A switch that stopped
turning on is caught; one that stopped turning off is not.

Both are one question — what a `ToolStripItem` tells UI Automation — and the answer is its
`AccessibleObject`: `AccessibleDescription`/`Help` for the tooltip, and a toggle state that is
reported in both positions. Block Q is where this lives rather than the profile blocks, because it
is the same property `PART_SelectedContentHost` and T175's row names were: a control that renders
correctly and announces nothing.
