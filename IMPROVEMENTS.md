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

### §IV.3 Long-term hourly summary (T88)

`UsageHistory.PruneIfStale` currently discards days older than 8 outright. Folding each expiring day
into a permanent per-hour aggregate first (~168 floats per week — negligible next to the 230 KB/week
raw log) would let idle be *measured* from real utilisation deltas instead of inferred from transcript
timestamps, giving `ActivityProfile` a second, independent source to validate against — and it is the storage
T89 needs.

### §IV.4 Ghost curve (T89)

Draw last week's burn-up faintly behind the current one on the same axes. "Is this week worse than the
last?" is a question the current chart cannot answer at all, and a ghost line answers it without a
second chart, a second tab, or any new number.

### §IV.5 When to stop, when to resume (T90)

The window today reports a problem ("you run out 1d 22h before the reset"); it does not say what to do
about it. With a trustworthy shape from T87, it can: *"stop now and resume tomorrow at 09:00 and you
close the week at ~92%"*. Sequenced last on purpose — advice built on a shaky projection is worse than
no advice, so this waits until T87 has proven itself.

An activity-aware **tray notification** is explicitly *not* part of this. The nudge threshold is the
wall-clock verdict and stays that way (T87 settled this); a second, softer notification channel would need its
own justification.
