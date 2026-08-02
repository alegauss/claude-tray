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
| [§XII](#xii--what-the-self-check-found-block-v) | What the self-check found (Block V) |

> Block I's design sections (§II) and Block J's (§IV) are gone: every one of them shipped, and
> `git log` plus [CHANGELOG.md](CHANGELOG.md) are the history. §III stays because it is a
> **measurement** — the numbers the rule thresholds and the base-overhead constant were calibrated
> against.
>
> Section numbers are **never reused**: §II, §IV–§XI have all been retired, so a new block takes the
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

## §XII — What the self-check found (Block V)

Building `--selftest` (T96) was the first time the pacing and live-rate arithmetic was written down as
properties rather than looked at on a chart, and writing them down turned up four things: three gaps
in what the check itself covers or guards, and one number that had been wrong on screen since T98
(T150, shipped — the live rate's 1.7% kernel bias). None of them was found by a user report, which is
the point: they were found by having to state what "correct" means.

### §XII.2 A week away shouldn't teach the measured grid either (T152)

T95 drops weeks the user was away from the **transcript** grid's vote. The measured grid (T88, blended
in by T93) still counts every folded hour, so the same holiday still votes "these hours are idle" —
and as the measured share grows, that becomes the *dominant* vote rather than a softened one.

It was deferred out of T95 for a stated reason: a low-activity week in the folded store is ambiguous
between "away" and "the app was closed". That ambiguity is resolvable rather than fundamental, and the
store already holds what resolves it — an hour with no reading is *unknown* and is already outside the
denominator, so a week can be judged only once it is known to have been **observed**. The test
therefore needs two conditions where the transcript one needed a single one: a week is away when it is
well covered (a majority of its hours have readings) *and* its active-hour count falls under
`AwayFraction` of the median covered week's. Weeks that fail the coverage test are not evidence either
way and must keep contributing exactly what they contribute today. `--activity --measured` should
print the exclusion the same way `--activity` prints the transcript one, and for the same reason:
silently discarding a sixth of the input is what looks like a bug three months later.

### §XII.3 Two properties the self-check still doesn't cover (T153)

§IV.12 listed the tail's **primed offset** ("a primed mid-line cursor skips to a character boundary")
and the rate's **zero-fill** ("a paused caller resumes with a truthful gap rather than a stale
plateau") among the properties that justified building `--selftest`. Neither is asserted: the fixture's
transcripts are a few hundred bytes, so the first sweep always starts at offset 0 and `NeedsAlign` is
never exercised, and there is no way to feed `LiveRate` a turn without a real `TranscriptTail` raising
its event.

Both are cheap once named. The priming path needs a fixture larger than `PrimeBytes` (256 KB) so the
cursor genuinely starts mid-line — a few hundred synthetic turns, written once. The zero-fill needs a
seam: `LiveRate.Add` is private and reached only through `tail.Appended`, so making it internal (the
same visibility the fixtures already rely on) lets a check push a burst, skip thirty seconds of
`Tick`, and assert the strip came back empty instead of holding its last value. A property listed as
the reason a feature exists and then left unasserted is worse than one nobody wrote down.

### §XII.4 The self-check only runs when a release is cut (T151)

`build.yml` is `workflow_dispatch` only, so `--selftest` — now wired into it — runs when somebody
decides to publish. A broken invariant therefore surfaces at the least convenient moment available,
with an installer half-built behind it.

The check takes 2.3 seconds and needs no secrets, no signing and no Inno Setup. Running it on push and
on pull requests as its own small job (build, run, done) turns "the release is blocked" into "the
commit is red", which is the entire value of having written the properties down. Deliberately a
separate job rather than a condition on the existing one: the release workflow must stay manual, and a
guard that can only run by publishing is not a guard.

