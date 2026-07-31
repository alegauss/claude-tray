# Claude Code Tray — Design rationale (IMPROVEMENTS)

> The **what/why** behind *unshipped* work. Status lives **only** in [ROADMAP.md](ROADMAP.md) and
> [CHANGELOG.md](CHANGELOG.md) — never put ✅/📋 markers in this file's prose.
>
> **When a task ships, delete its design subsection here.** `git log` is the history. Letting
> shipped implementation reports accrete in this file is the single failure mode it exists to avoid.
>
> Sections are Roman-numbered and referenced from the roadmap as `→ §III`.

| § | Subject |
|---|---|
| [§I](#i--house-constraints) | House constraints (binding, app-wide) |
| [§III](#iii--measured-baseline-context-load) | Measured baseline for the context feature (kept: it is data, not design) |
| [§IV](#iv--activity-aware-pacing-block-j) | Activity-aware pacing (Block J) |
| [§V](#v--live-throughput-block-k) | Live throughput (Block K) |
| [§VI](#vi--system-information-block-n) | System information (Block N) |
| [§VII](#vii--profiles-block-o) | Profiles — several Claude Code logins on one machine (Block O) |

> Block I's design sections (§II) are gone: every one of them shipped, and `git log` plus
> [CHANGELOG.md](CHANGELOG.md) are the history. §III stays because it is a **measurement** — the
> numbers the rule thresholds and the base-overhead constant were calibrated against.

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

## §IV — Activity-aware pacing (Block J)

### §IV.0 The problem, stated precisely

The weekly projection is **not** blind to idle time. `UsageReport.Fill` derives it from the average
pace since the window opened — `elapsed * (1 - util) / util` — and that average already has every
idle hour so far diluted into it. Adding "average idle time" as a separate correction would
double-count something already priced in.

What the average cannot represent is **where** the idle sits. A straight line spends quota at 03:00
Sunday at exactly the rate it spends it at 15:00 Tuesday, which produces two concrete errors:

1. **Impossible landings.** The projection can place the exhaustion marker inside a stretch where the
   user is reliably asleep — observed on a real chart at *Fri 03:59*. A limit cannot be reached while
   nothing is running, so that timestamp is wrong by construction, not merely imprecise.
2. **Asymmetric remainders.** The projection is only sound when the *remaining* window has the same
   active/idle mix as the elapsed one. It does not, whenever the window ends on a partial day (it is
   19:52 — roughly three active hours left today, not twenty-four), when a weekend or holiday falls in
   the remainder, or when the window opened mid-night (the 02:00 reset boundary the API hands out).

The fix is to project along a **shape**, not a slope.

### §IV.6 Where Block J stopped

An activity-aware **tray notification** is explicitly *not* part of this block. The nudge threshold is
the wall-clock verdict and stays that way (T87 settled this); a second, softer notification channel
would need its own justification.

### §IV.7 Keeping the profile warm (T91)

`ActivityProfile.Load` is called from one place: `UsageReport.ComputePace`, which runs when the
Statistics window is open. That has two consequences nobody would choose deliberately. A user who
never opens the window never rebuilds the grid, so the projection they *do* see — through the tray
icon's verdict, indirectly — is shaped by a profile that may be weeks stale. And the first open on a
new install lands the full ~15s sweep in front of the first chart.

The tray already solves this exact problem for context (T79: sample on launch, then every 6h, off the
poll cadence because a warm scan is cheap and weekly drift doesn't need per-minute sampling). The same
timer should own the profile. Nothing else changes: `Load` already returns a stale cache immediately
and refreshes behind it, so the window keeps reading whatever is on disk.

### §IV.8 Incremental transcript sweep (T92)

The rebuild reads every `*.jsonl` under `~/.claude/projects` newer than the 12-week cutoff — measured
at 15s over 93,856 requests on the dev machine — and almost all of that work is repeated: a transcript
that hasn't been written since yesterday's sweep produces exactly the hourly counts it produced then.

`ContextUsage` already has the pattern: a per-file cache keyed by path + size + mtime. Here the cached
value is smaller still — the set of (day, hour) buckets that file touched. Only changed and new files
get read, which turns the daily refresh into something that could run on launch without a thought
(and makes T91 cheap enough to be uncontroversial).

Worth keeping the cold path honest: a `--activity --refresh` must still be able to force the full
sweep, or a cache bug becomes unfalsifiable.

### §IV.9 Prefer the measured grid (T93)

The transcript grid has a structural blind spot the UI currently has to apologise for: *usage from
another machine or from claude.ai counts against the same limit but leaves no transcript here.* The
folded aggregate does not have that blind spot — it is built from the rate-limit utilization itself,
which counts every request against the limit whoever made it and wherever.

So the measured grid is not merely a second opinion to validate against (its role in T88), it is the
better source once there is enough of it. The transcript grid keeps two jobs: bootstrapping the first
weeks, when the store has nothing, and covering hours the app wasn't running to observe.

Blend rather than switch — a hard cutover at three weeks would visibly jump the projection on an
arbitrary day. Weight per bucket by measured coverage, so hours the store knows well are taken from
the store and hours it barely saw stay with the transcripts.

When this ships, the method-note sentence about the local-transcript blind spot has to change with it
— it stops being true to the degree the measured grid dominates, and a stale disclaimer is its own
kind of wrong.

### §IV.10 Intensity, not just presence (T94)

`projected = util + rate × Σ p` treats every active hour as costing the same. It does not: a morning
of agent work and an evening of one-line questions are both "active", and the model spends them
identically. The error is bounded (the rate is calibrated on this window's own average) but it is
systematic — a week whose remaining hours are the heavy kind is under-projected exactly when the
warning matters most.

The folded store already holds what is needed: mean spend per *active* hour per bucket. Expressed
relative to the overall mean it becomes a unitless intensity `i_h ≈ 1`, and the projection becomes
`Σ p_h · i_h`. Deliberately sequenced after T93: intensity is only meaningful once the measured grid
is trusted, and stacking two model changes at once would make a regression impossible to attribute.

Guard against overfitting the same way the profile does: clamp `i_h` to something like [0.5, 2] and
shrink it toward 1 by observation count, so a single 3am incident doesn't create a "3am is 4× heavy"
bucket.

### §IV.11 Holidays shouldn't teach the model (T95)

Every observed week votes with equal weight (times recency decay). A week on holiday therefore votes
"these hours are idle" as confidently as a working week votes the opposite, and with a 12-week horizon
two weeks off in the sample pull every bucket down by a sixth. The flat prior softens this but doesn't
address it: the prior is about *thin* evidence, not *unrepresentative* evidence.

A week whose total activity is far below the median week (say under a quarter of it) is not evidence
about which hours are worked — it is evidence that the person was away. Dropping such weeks from both
the numerator and the denominator leaves the shape untouched and the coverage count honestly reduced.
`--activity` should print how many weeks were excluded, because silently discarding a sixth of the
input is exactly the kind of thing that looks like a bug later.

### §IV.12 A self-check for the pacing math (T96)

Block J introduced arithmetic with genuine edge cases and the repo has no test surface at all — every
verification in the block was a screenshot or a CLI read-out by a human. Some of the properties are
cheap to assert and expensive to lose:

- a *flat* profile must make the staircase reproduce the straight average-pace line exactly (this is
  the whole "degrades correctly" claim, and it is one line of arithmetic away from being false)
- folding must be idempotent, and folding the same day twice must not double its spend
- the resume advice must never propose an hour whose projected close exceeds its own target
- the ghost must stay hidden below its coverage and total gates
- `ExpectedActiveHours` over a whole week must equal the sum of the grid

A test project would mean a third-party test framework, which the non-goals rule out for a repo whose
single-self-contained-exe story is a feature. An in-app `--selftest` that builds synthetic profiles and
windows, asserts these properties and exits non-zero on failure costs nothing at runtime, ships inside
the same binary, and can run in CI as one line.

Block K doubled the surface this has to cover, and every one of these was checked by hand against a
synthetic tree exactly once: the tail's cursor (a partial line is held until its newline, a shrunk
file restarts without re-reporting, a primed offset skips to a character boundary) and the rate's
kernel (sustained R reads as R, a single burst decays linearly to a true zero at exactly W, the
smoothed value never exceeds the weighted one, a paused caller resumes with an empty strip, and the
per-project rates sum to the headline because the kernel is linear). Those are properties, not
observations — which is precisely what a self-check is for, and nothing currently stops an edit from
breaking one silently.

---

## §V — Live throughput (Block K)

### §V.0 Where Block K stopped

The block's premise held: the throughput row's window average is immobile *by construction* (it
divides by the whole elapsed window), and the fix was a second metric on a second clock, read from
the append-only transcripts rather than from a faster API poll. T97–T100 shipped that — tail, rate,
strip, attribution — and the API cadence was never touched.

**T101 was dropped**, which §V.5 explicitly allowed. The reason turned out sharper than the original
"costs battery and attention": T99 made the tail *window-owned*, so a closed Statistics window tails
nothing and the whole feature is free when nobody is looking. A tray hint would require a tail
running for the entire session to power it. That trade is not worth an ambient nicety, and the ruling
now lives in the roadmap's Non-goals.

### §V.6 What this block will not do

- **No new notification channel.** Same ruling as §IV.6 — a live rate is a display, not an alarm, and
  "you are burning fast right now" as a toast needs its own justification.
- **No content, ever.** Tailing reads the same fields the sweep does (§I.1). Session *names*, prompts
  and tool output stay unread, and "which project" means the `cwd`, not what is in it.
- **No API cadence change.** If a task in this block starts arguing for a shorter `RefreshSeconds`,
  it has drifted: the whole point is that the live signal is local.
- **No tray-icon animation** — T101, dropped. See §V.0 and the roadmap's Non-goals.

### §V.7 One response, counted once, everywhere (T102)

Claude Code writes **one `assistant` line per content block** of a single API response — a thinking
block and a tool_use block are two lines — and every one of them repeats that response's
`message.usage` verbatim, with the same `requestId`. `UsageReport.ScanTokens` sums per line, so it
counts such a response twice.

Why this has gone unnoticed: the burn-up curve built from those samples is **rescaled to the live
utilization**, so the endpoint is right no matter how badly the samples are inflated. What is not
protected is the *shape* — the inflation is not uniform, it tracks how many content blocks a response
had, which tracks heavy tool use. The same samples also feed `WindowPace.MeasuredActiveHours` (the
denominator T87 calibrates the projection against) and `ActivityProfile`.

`TryParseSample` already emits the id for T97; this is a de-duplication set in the sweep, and a
before/after on the same window to quantify what the shape was off by.

### §V.8 The sweep is O(all history) (T103)

`TranscriptTail` lists every `*.jsonl` under `~/.claude/projects` on each sweep and opens only the
ones whose mtime is recent. Measured today: 602 files, 17ms, metadata-only. That is cheap, and it is
also the wrong shape — the count only ever grows, and the sweep runs every 3s for as long as the
window is open.

Two candidate fixes, and the cheaper one should be measured first: enumerate only the *directories*
whose mtime moved (one stat per project rather than per transcript), or trust the watcher's reported
paths once the tree passes some size, keeping the full sweep as an occasional reconciliation. Either
way the floor sweep must stay whole often enough that a lost watcher cannot silently strand a file.

### §V.9 A line with no reading (T104)

Height means something now — T110 rules and labels the ceiling, T116 draws a rate you can follow — but
the charts still answer only in shapes. Which second is that rise, which project, and how many tokens
actually landed in it: none of the three is available anywhere, and **per-sample hover** is the one
interaction a time-series should never ship without.

Two things make it more valuable than when it was first written. The chart has its own tab and 180px of
plot, so a hit target is worth aiming at; and the line draws a *rolling* rate while `LiveRate` still
keeps the raw per-second buckets beside it, so a hover can report both — "this second carried 12k
tokens; the rate through it was 800/s" — which is exactly the distinction the rate view smooths over.

### §V.10 Two readers of one lossy encoding (T105)

A `projects/<slug>` name is the session's root path with every non-alphanumeric character replaced by
`-`, which is lossy: `d--Git-acme-claude-tray` cannot be split back into a folder name, and
`…-shio-2026-3` naïvely reads as "3". T100 recovers the real folder by walking the recorded `cwd` up
to the ancestor whose encoding equals the slug — exact, because it verifies rather than guesses.

`ContextScanner` needs the same answer and gets it differently (`CwdFromTranscripts` takes the raw
`cwd`, which is the working directory *of that turn* and moves with any `cd`). One of these is right.
It should be the only one.

### §V.12 What the cache re-read costs (T107)

`LiveRate` already separates cache reads from real work, and the measured ratio on ordinary traffic is
startling: **~30,000 tok/s of cache read against ~150 tok/s of real work**. Excluding it from the
headline is right — it barely weighs on the limit and would drown the signal — but the number itself
is a finding the app currently computes and shows nobody.

It is the per-turn price of a large eager context, which makes it the missing link to the Context Load
Inspector: §III measures the context load once, statically, and this measures what re-reading it
actually costs, live. The design question is where it belongs — a reading in the live row, or a
finding in the Context window that cites it — and it should not become a fourth number nobody asked
for.

### §V.13 The screenshot fixture belongs with the fixtures (T108)

`--stats live` renders a hand-shaped synthetic three minutes so the published screenshot is
reproducible; the shaping lives inline in `StatisticsWindow.RenderDemoLive`. Two tasks now depend on
it, and `ContextFixture` is where this repo already keeps deterministic stand-in data. Pure
housekeeping — no behaviour change — and worth doing before a third caller appears.


---

## §VI — System information (Block N)

### §VI.1 A fixture account, so the page can be published (T121)

Every other window in this repo has a screenshot in the README and on the site. The System
information page cannot have one from a real machine: masking the holder hides the name and the local
part of the email, but the **organization name and the mail domain are the reading itself** — that is
the point of the row — and on this developer's machine the organization is a client. The same reason
`ContextFixture` exists (real project names are client names) applies one directory up.

What is needed is a synthetic `.claude.json` / `.credentials.json` pair behind `--settings System
--sample`, built the way `ContextFixture` builds its tree: written to a throwaway directory, never
near the real one, and pointed at through the same `CLAUDE_CONFIG_DIR`-style seam `ClaudeAccount`
already resolves. Two accounts are worth fixing, because they are the two layouts the page renders —
a **personal Max 20x** login (no organization: the row collapses) and a **Team seat** (organization
plus role). A third case, no OAuth account at all, needs no fixture: an empty directory produces it.

Only then do the README section and the site block get their image; until they do, both describe the
page in words, which is why T120 shipped without a shot.

---

## §VII — Profiles (Block O)

### §VII.0 What is actually on disk, and what "switching" can mean

Claude Code is **single-account per config dir**. `.claude.json` carries exactly one `oauthAccount`,
`.credentials.json` exactly one `claudeAiOauth`, and `/login` overwrites rather than appends. There is
no list of subscriptions to enumerate, so "several accounts on one Windows" can only mean one of three
things, and they are not equally real:

1. **Several config dirs** (`CLAUDE_CONFIG_DIR`) — the supported pattern, and the one this block
   models. Each dir is its own credentials, account, projects, transcripts, settings, MCP servers,
   permissions and plugins.
2. **Several Windows user accounts** — a different `%USERPROFILE%`, unreadable across users by ACL.
   Out of scope deliberately: the app must not go looking inside another user's profile.
3. **Traces of a previous account in the same dir** — `clientDataCacheSlots` retains slots carrying an
   `org` value (two distinct ones observed on the development machine), and `.claude.json.backup` may
   hold an older `oauthAccount`. Both are undocumented internals with opaque keys, and neither yields a
   plan or an address for the other account. **Not a basis for anything.**

And "switching" splits into three axes that cost wildly different amounts:

| Axis | Feasible | Cost |
|---|---|---|
| Which profile the tray **monitors** | yes | pure read (plus T125's store keying) |
| Which profile a **new** session opens in | yes | one env var on the launch path the tray already has |
| The account of an **already running** session | **no** | the environment is fixed at process creation |

The third is a hard no rather than a "later", which is why it sits in Non-goals: nothing about a GUI
changes a live process's environment.

### §VII.5 Auto-follow, and the 16px problem (T126)

`TranscriptTail` reports each turn within ~250ms of it landing. With N profiles it also knows *which*
config dir the turn landed in, so the icon can follow the profile actually being worked in and nobody
clicks "switch" at all. A manual override stays, because a following icon that guesses wrong is worse
than a static one.

The unsolved part is the icon. At the real tray size the number already fills the glyph, so there is no
room for a label: the profile has to read from the tooltip and the menu, with at most a small
per-profile colour dot — and that dot has to survive being looked at beside the projection colour, which
already carries meaning. Nothing here gets promised before it has been rendered and screenshotted. It is
not the animated tray hint T101 was dropped for: a static per-profile mark needs no permanent tail
(T125's polling already knows the profiles) and does not animate.

---

## §VIII — Project layout (Block P)

### §VIII.0 The problem, and the four things that must not move

The repo root holds **57 entries**: 33 `.cs`, 4 `.xaml`, the icon, the manifest, the csproj, the
installer script, five release scripts, and ten markdown/config files. A flat listing that size carries
no information — it does not say which files draw the tray, which are windows, which read `~/.claude`,
which talk to the API. The only map is the file-map table in `AGENTS.md`, and a hand-maintained table
is a weaker guarantee than a directory that is correct by construction.

The target:

```
src/Context/    ContextScanner ContextUsage ContextRules ContextReport ContextPrompt
                ContextHistory ContextNudges ContextFixture TokenEstimate
src/Usage/      ApiClient UsageReport UsageHistory HourlyUsage BurnTracker
                LiveRate LiveChart UsageInsights ActivityProfile ActivityShape TranscriptTail
src/Profiles/   ClaudeAccount ProfileStore Settings
src/Core/       Localization SafeWalk
src/Tray/       Program TrayContext IconRenderer Updater
src/Cli/        the headless printers (T132)
src/Ui/         SettingsWindow StatisticsWindow ContextWindow ToastWindow (+ SettingsRow)
build/          installer.iss build*.cmd update-release.* update-winget.ps1 winget/
```

Root keeps exactly what has to be there or is read by a human first: `ClaudeTray.csproj`,
`app.manifest`, `ClaudeTray.ico`, `lang/`, `docs/`, `scripts/`, `.github/`, `README`, `AGENTS`,
`LICENSE` and the five roadmap docs.

Four decisions are **binding for the whole block**, because each one is a way the restructure could
quietly cost more than it returns:

1. **The namespace stays flat `ClaudeTray`** — no folder-derived namespaces. Folder namespaces would
   add a `using` to most of the 33 files and churn every cross-type reference, turning a set of pure
   renames into a diff nobody can review. The folders exist for the person running `ls`; the compiler
   does not need to agree with them. (`LiveRate.cs` is the one file without the file-scoped namespace
   declaration — worth normalising while it moves.)
2. **`lang/` never moves.** The embedded resource name is derived from the path: `lang\en.json` becomes
   `ClaudeTray.lang.en.json`, which is exactly the string `Localization.cs` matches at startup. Under
   `src/lang/` every resource is silently renamed and `L` finds nothing — a runtime failure a green
   build does not catch, in the one subsystem every string in the app goes through.
3. **The five roadmap docs stay at the root**, per the `roadmap-docs` skill: `docs/` is the published
   GitHub Pages site, not a place for internal engineering docs.
4. **The csproj stays at the root and should need no edit.** The SDK globs `**/*.cs` and `**/*.xaml`,
   so new folders are picked up for free. If a move turns out to require a csproj change, that is the
   signal the move is wrong (case 2 above is exactly this) — not an invitation to hand-write
   `<Compile Include>` items.

And one rule about scope: **every Block P task is rename + reference fix, with zero behaviour change.**
The value of this block is that its diffs are boring and its risk is a build error rather than a bug.
A file that gets "improved while it was open" destroys that property — the improvement becomes a task in
whichever block owns that subsystem.

### §VIII.1 The non-UI sources move (T129)

28 files, five folders, one commit: splitting the mechanical move by family would leave the repo
half-migrated between commits for no gain in safety, since the namespace is flat and the compiler is
the gate either way. The grouping follows the seams the code already has — `Context*` is one subsystem
with one entry point, the usage/pacing family is another, `ClaudeAccount`/`ProfileStore`/`Settings` are
the profile model Block O built, and `Localization`/`SafeWalk` are the two helpers with no subsystem of
their own.

`AGENTS.md`'s file map is part of this task, not a follow-up: it gets grouped by folder, and it gains
the one rule that keeps the root from filling up again — **a new file goes in the folder of the
subsystem it belongs to; the root is for the project, not for code.**

The gate is a build plus the headless smoke set (`--context`, `--activity`, `--profiles`, `--tail`),
because those paths touch every moved family and cost seconds.

### §VIII.2 The windows move (T130)

Separate from T129 because the failure mode is different. A `.xaml` compiled as a `Page` gets a
generated `InitializeComponent` whose `Uri` is **derived from the file's path**, so moving markup
rewrites generated code — and anything that had hardcoded a `pack://application:,,,/…` path to a window
or a resource dictionary would break in a way the build does not necessarily catch. Measured before
planning this: the repo hardcodes **no** `pack://` URI and has no `ResourceDictionary Source=` in any
window, so the generated code absorbs the move on its own.

That is a prediction, not a proof, so the gate is not a green build: it is a `preview-ui` screenshot of
all four windows (Settings across its pages, Statistics, Context, Toast), per the AGENTS visual
verification loop.

### §VIII.3 The build and release scripts move (T131)

Seven root entries exist to *ship* the app, not to build it in a dev loop, and they are the ones a
reader of the root never wants: `installer.iss`, `build.cmd`, `build-installer.cmd`,
`update-release.cmd`, `update-release.ps1`, `update-winget.ps1`, `winget/`.

Three things hold references that must move with them:

- `installer.iss` resolves `bin\Release\…\publish`, `SetupIconFile=ClaudeTray.ico` and `OutputDir=dist`
  against **its own** directory — all three become `..\`-relative.
- `.github/workflows/build.yml` runs `ISCC.exe installer.iss` and `./update-winget.ps1` from the repo
  root. CI is the only consumer that cannot be fixed by reading an error message locally, so it is the
  part to change first and watch.
- `README.md` (build/release section) and `STRATEGY.md` §III quote the paths, and README is a
  user-facing surface: somebody building from source follows those lines literally.

The `.cmd` files already resolve their siblings through `%~dp0`, so they keep working once they move
together — that is why they move as one group rather than one at a time. `scripts/` is deliberately
left alone: it holds the screenshot/capture helpers, which are a dev tool, not the release path.

### §VIII.4 `Program.cs` is two programs (T132)

2,524 lines and 130 KB in one file, and the split is already visible in the structure: `Main` dispatches
on `args`, then ~1,000 lines of **headless printers** (`PrintContext`, `PrintActivity`,
`PrintMeasuredActivity`, `PrintGrid`, `PrintLive`, `PrintTail`, `PrintProfiles`, `PrintFindings`,
`PrintPrompt`, `PrintUsage`, `PrintCalibration`, `WriteContextReport`, plus `SimulateReset`,
`CaptureToast`, `RenderTest`, `MakeIcon`) run and exit, and ~1,080 lines of `TrayContext` — menu, poll
timer, settings apply, tooltip, icon render — are the actual resident app. The two share almost
nothing but the process.

So: `Tray/Program.cs` keeps `Main`, the dispatch table and `ArgValue`; `src/Cli/` gets one file per flag
family (context, activity, live/tail, profiles, fixtures/captures) with the small formatting helpers
(`Kb`, `Clip`, `Spark`, `Shade`, `Mode`, `StateWord`) beside their users; `Tray/TrayContext.cs` gets the
resident app. Moved verbatim — this task must not "clean up" a printer on the way past (§VIII.0).

A pleasant side effect worth stating because it is the actual argument: the headless CLI is where every
new subsystem gets its first entry point, and it is currently the part of the codebase hardest to open.

### §VIII.5 One window, several surfaces, one file (T133)

`ContextWindow.xaml.cs` is 1,369 lines and **7 types**; `StatisticsWindow.xaml.cs` is 1,269 lines and
carries the session tab, the week tab, the throughput tab, the activity shape, the method popup and now
the profile selector. Both grew a tab at a time, and each tab's handlers, formatting and state are
interleaved with the others' — the file is where a change to the throughput chart has to be found among
the projection code.

`partial class` files per surface (`StatisticsWindow.Throughput.cs`, `StatisticsWindow.Week.cs`, …) keep
one class and one XAML while giving each surface a file; the helper types that are not the window move
out to their own files under the folder that owns them (a chart model belongs with `Usage/`, not with a
window). No XAML changes, so the screenshot gate is a regression check rather than a review.

### §VIII.6 Six settings pages in one markup file (T134)

`SettingsWindow.xaml` is 1,019 lines: ~170 lines of shared styles (the settings-row pattern, the
read-only value pair) followed by six visibility-toggled page `Grid`s. It is the file most likely to be
edited by whoever adds a setting, and finding a page in it means scrolling past five others.

Marked as an idea rather than designed, because the obvious move has a real cost. A `UserControl` per
page separates the pages but puts each one's styles *out of scope* unless they are hoisted into a shared
`ResourceDictionary` — which is a second file to keep in sync and a level of indirection the current
single file does not have. The alternatives worth weighing before touching it: a `ResourceDictionary` per
page with the pages staying in one window; or leaving the markup alone and only splitting the
**code-behind** (T133's approach), which addresses most of the same pain with none of the styling risk.
Whichever wins, the six pages each need a screenshot afterwards, in at least one non-English language.
