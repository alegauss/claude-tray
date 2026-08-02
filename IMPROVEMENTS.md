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
| [§XIII](#xiii--what-the-app-knows-and-doesnt-say-block-z) | What the app knows and doesn't say (Block Z) |

> Block I's design sections (§II), Block J's (§IV) and Block V's (§XII) are gone: every one of them
> shipped, and `git log` plus [CHANGELOG.md](CHANGELOG.md) are the history. §III stays because it is a
> **measurement** — the numbers the rule thresholds and the base-overhead constant were calibrated
> against.
>
> Section numbers are **never reused**: §II, §IV–§XII have all been retired, so a new block takes the
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

## §XIII — What the app knows and doesn't say (Block Z)

Block V made the evidence behind the weekly projection correct: a week away no longer votes, in the
transcript grid (T95) or in the measured one (T152), and `EffectiveWeeks` subtracts both. Reading those
four commits back against the running app turned up the other half of the same idea. The app now knows
exactly how much evidence it has and how much it discarded — and the one place a *user* reads that
number still prints the span, while the guard that produces the measured half of it cannot fire on the
machine it was written on. The two coverage tasks are the ones that keep the next such gap from being
written at all: both are over code that has already produced shipped defects and that `--selftest`,
after 80 assertions, does not touch.

### §XIII.2 A gate that cannot fire is not a guard (T160)

T152 judges a folded week only once at least half its 168 hours carry a reading. That bar was chosen
to resolve a real ambiguity — a quiet week is "away" or "the tray was closed", and only coverage tells
them apart — and it does resolve it. It also, measured on the machine that shipped it, never opens:
`--activity --measured` reports 67 and 71 covered hours against a bar of 84, because the tray runs
about ten hours a day. Every week is `?`, nothing is ever judged, and the exclusion is unreachable in
practice. The safe direction (an unjudged week keeps its old behaviour exactly) is what makes this a
gap rather than a defect, but a guard that only fires for a machine the tray watches around the clock
is not the guard that was designed.

The question the gate is really asking is not "was this week mostly observed?" but "**did the tray run
that week the way it runs the others?**" — which is a comparison, not an absolute. A week whose
coverage reaches half the median week's was watched as usual, and a quiet one is then evidence of
somebody being elsewhere; a week at a fifth of it is the tray being off, and stays unjudged. That reads
the same store, keeps the two-verdict structure and the "unjudged weeks change nothing" property, and
it fires on an ordinary machine.

One guard has to come with it: relative alone would let a machine whose *typical* week holds fourteen
covered hours judge a holiday on seven. So a week must clear both — half the median coverage **and** an
absolute floor of roughly a day's worth of hours — or the verdict rests on a sample too small to mean
anything. `--activity --measured` already prints each week's coverage beside its verdict (T152), which
is what makes the new bar checkable on a real machine rather than argued about.

### §XIII.3 The lossiest function in the app has no assertions (T161)

`ProjectSlug` owns an encoding that cannot be inverted: every non-alphanumeric character becomes `-`,
so `claude-tray` and `claude\tray` produce the same slug. Everything the app displays about a project
goes through it, it has been the direct cause of two shipped defects (T105 — three call sites had grown
their own decoders, one encoding a different character set; T154 — three checkouts all labelled
`2026.3`), and it is *pure*: strings in, strings out, no clock, no profile, no store. `--selftest`
covers the pacing, the folded store, the transcript grid, the tail's cursor and the rate kernel, and
not one line of this.

The properties are already written down as prose in the class and simply need stating as assertions:
`Encode` agrees with what Claude Code writes for a path containing a literal hyphen; `RootFor` recovers
the root from a `cwd` that has been `cd`-ed several levels deeper, and returns null rather than a guess
when no ancestor encodes to the slug; `ShortName` is two segments, never one and never the whole path,
and degrades to the leaf at a drive root; `TryProbe` backtracks (`viglet-model-catalog` resolves even
when `viglet\model` exists) and returns only directories that exist; `Literal` and `Tail` are the
deliberately-ambiguous last resorts and must stay reachable only as such. `TryProbe` is the one that
needs a temp tree; the rest are pure and cost microseconds.

### §XIII.4 A carry-over list is a field list wearing a different hat (T162)

T141's finding was that copying a model field by field means a forgotten field is a silent reset, and
its fix was to stop copying by hand: `Settings.Clone()` is a JSON round-trip, total by construction.
The *other* end of the same round-trip is still hand-written. `ApplySettings` takes the edited model
whole and then re-carries four fields the page does not edit — `Metric`, `EnvironmentProfileOwned`,
`EnvironmentProfileRestore`, `MonitoredConfigDir` — one line each, because the window's snapshot of
them is older than the tray's. Add a fifth tray-owned field and forget its line, and every Save writes
a stale value over it: the exact defect T126 found (the monitored config dir) and T155 found again (the
icon's profile, moved by a Save that no control on the page had touched).

Four is already a list, and the list has no owner: it lives in `TrayContext`, while the fields it names
live in `Settings`, so the two can drift without anything noticing. Marking the fields themselves —
`[TrayOwned]` on the property, one loop in `ApplySettings` that carries everything so marked — puts the
declaration next to the thing being declared and makes the carry total for the same reason the copy is.
The assertion `--selftest` can then make is the one that matters: round-trip a `Settings` through the
page's copy and the tray's apply with every property set to a non-default value, and nothing may change
except what the page edits. A new field is then covered the day it is added rather than the day
somebody notices it resetting.

### §XIII.5 A straight line that doesn't say it is one (T163 — idea)

When the profile is thin the projection quietly stops following the activity shape and goes back to the
average-pace line, and the method note goes back to its generic text. Nothing tells the user that the
better projection exists, that it was declined, or that it is a matter of time rather than of
configuration — which is the one fact here somebody could act on, by leaving the tray running another
week.

It is an idea and not a design because the obvious implementation is the wrong one. "Not enough data
yet (2.1 of 3 weeks)" in the method note is accurate and reads as an apology on every open, and this app
has already ruled that a second nagging channel needs its own justification rather than inheriting one
(the T87 non-goal). Worth exploring: whether the honest place is the note at all, whether it should
appear only while the figure is actually climbing, and whether "the projection is a straight line
because …" is better said where the line is drawn than in a paragraph under it.
