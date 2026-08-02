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
| [§XIV](#xiv--the-picker-switches-profiles-the-window-has-to-switch-with-it-block-aa) | The picker switches profiles; the window has to switch with it (Block AA) |
| [§XV](#xv--what-block-zs-own-work-left-behind-block-ab) | What Block Z's own work left behind (Block AB) |

> Block I's design sections (§II), Block J's (§IV), Block V's (§XII) and Block Z's (§XIII) are gone:
> every one of them shipped, and `git log` plus [CHANGELOG.md](CHANGELOG.md) are the history. §III
> stays because it is a **measurement** — the numbers the rule thresholds and the base-overhead
> constant were calibrated against.
>
> Section numbers are **never reused**: §II, §IV–§XIII have all been retired, so a new block takes the
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

## §XIV — The picker switches profiles; the window has to switch with it (Block AA)

The Statistics window has reported on a chosen profile since T128, and the picker has been correct about
*which* profile it names since the day it shipped. What T164 found is that naming it is not the same as
becoming it: a switch left three pieces of the previous profile behind, and the shape of all three is the
same — state whose lifetime is "the window" where it should have been "the profile on screen".

The report that opened the block is the round trip, and the round trip is the point: *view a profile,
switch away, switch back, and the chart is a different chart.* Nothing about a single switch looked
wrong, which is exactly why this survived — see §XIV.1.

### §XIV.1 Nothing drives the picker, so a switch is checked one direction at a time

Every capture this repo has taken of the profile feature renders **one** profile: `--capture-stats out
shape profile=1` selects an index and snapshots it. That is enough to check the thing T128 built, and
structurally incapable of catching what T164 fixed — all three defects need a *second* switch to become
visible, and two of them only when the second switch goes back to where it started.

T164 widened the seam (`profile=1,0` walks a list, one full settle per step) and the check is still a
person comparing two PNGs. The precedent for closing that gap is T142: `Check-Interaction.ps1` drives the
real window through UI Automation and exits non-zero unless every assertion passed, which is what turned
"the keyboard works, I tried it" into something CI-shaped. A `-Case Profiles` is the same move —

- select index 1, then 0, settling between them, through the real `ComboBox` rather than the preview seam;
- read back the **used %**, the reset caption and the live headline at each stop;
- assert the two readings of the same profile are equal, that the middle one differs from both, and that
  the live headline never reads "unavailable" after a switch (the one defect a percentage cannot see);
- and keep T142's rule that **reading nothing is a FAIL** — a collapsed pane is absent from the UIA tree,
  which is precisely the state a broken switch produces.

The window is per-machine, so the case has to skip cleanly with **one** profile registered — and the skip
must state the precondition it failed, per the T161 rule: a `Skip` that could hide the property it guards
is worse than no check.

### §XIV.2 A profile switch blanks the panes it could have kept

T164 clears the last-rendered pace on the way out, and that is the right default: keeping it means the
previous account's curves sit under this account's name for the length of a transcript scan, which is the
same misattribution the task exists to remove — only briefer. The cost is that a switch now shows
"computing…" over the whole pane, Throughput tab included, and that is the exact blink T118 removed for
the poll refresh. T118's reasoning does not carry over (it is about a refresh of the *same* profile), but
the ugliness does.

Keeping the last report **per profile** would make the round trip instant *and* correct — the case the
field report is about is the one a cache serves best, since the user is going back to something the window
computed sixty seconds ago. The reason this stays an idea rather than a task is that a cached report is a
stale one the moment its profile is polled again, and this app's whole claim is that a number on screen is
a number that was measured. So the design question is not "how to cache" but **what makes a cached view
honest**: whether the footer's "Atualizado …" timestamp is enough on its own, what invalidates an entry
(its own poll landing, certainly — but the transcripts move continuously and the curve is built from
them), and whether a report older than some age should be shown at all rather than recomputed behind it.
Answer those and it is a small change; skip them and it is T118's blink traded for a subtler lie.

---

## §XV — What Block Z's own work left behind (Block AB)

Block Z was five tasks about what the app *says* and what protects what it says. Building it surfaced four
things that belong to none of them, and the reason to write them down as a block rather than as stray
fixes is that all four are the same failure of attention: **the thing you are looking at is not the thing
you are checking.** Two were on screen in the very screenshots taken to verify the block (a number in the
wrong locale, a paragraph that has quietly become twelve lines); two are in the checks written to protect
it (a decision that lives where no assertion can reach it, a skip that may never have run outside one
machine). None was reported by anybody, and none of them would be.

### §XV.1 One window, two number conventions (T167)

`StatisticsPage.Format.cs` exists to make the report locale-independent: `Pct`, `Tps`, the token and byte
helpers all end in `.ToString(…, Fmt)` with `Fmt = CultureInfo.InvariantCulture`, thirteen call sites of
deliberate consistency. The method note's five interpolations do not go through any of them — they are
bare `$"{value:0.#}"`, which formats with `CurrentCulture`.

The result is measurable and was sitting in both verification screenshots of T159 and T163: on a pt-BR
machine run with `--lang en`, the English popup reads *"4,7 weeks of local transcripts, 2 weeks away
excluded"* and *"there are 2,1 so far"*, eight lines above `≈ 1,319 tok/s` and `40%` — the same window
stating numbers in two conventions at once. It is not a localization bug (the *strings* are correct in five
languages); it is the one place the window's own rule was not applied.

Two things make this worth a task rather than a two-line patch. First, the fix has to pick the rule
deliberately: `Fmt` everywhere is what the rest of the window does and what keeps a screenshot comparable
across locales, but a decimal point in a Portuguese sentence is *also* wrong to a reader — and the app has
no precedent for a per-language numeric culture, since `L.Culture` is used only for dates (`DateFmt`).
Whichever way it goes, it should be one helper the note cannot bypass, not five call sites corrected once.
Second, nothing prevents the sixth interpolation: the rule belongs in a place the compiler or `--selftest`
can point at, which is the same argument §XV.2 makes about the note's structure.

### §XV.2 A note whose composition no assertion can reach (T168)

Which paragraphs the method note contains stopped being a formatting detail during Block Z. There are now
four inputs (shaped or not, mostly-measured or not, thin or not, weeks excluded or not) and a real rule
over them, most of it written *this week*: thin appears only when the shape was declined for **thinness**
and never at the limit or with nothing spent (T163); the away clause appears only when a week was actually
dropped (T159); shaped and thin are mutually exclusive by construction. All of it is inline in
`Render`, in a WPF page, behind a `PaceReport` — so `--selftest` cannot see any of it, and the only
verification these rules have ever had is that somebody looked at two screenshots.

The seam is small: a pure function from `(PaceReport, ActivityProfile?)` to the ordered list of resource
keys (plus their arguments), with `Render` doing nothing but `L.T` and concatenation. Then every rule above
is a check of the kind T161 and T162 just added — cheap, and each one confirmed to fail first. The T163
rule is the one worth pinning hardest, because getting it wrong produces a *plausible* sentence: telling
somebody to keep the tray running another week when the real reason there is no shaped projection is that
they are already at the limit.

This is also where §XV.1's rule would live: a note assembled by one function is a note whose numbers go
through one formatter.

### §XV.3 A skip that fires every time is a check that does not exist (T169)

T161 gated its two `TryProbe` assertions on a precondition — every segment of the temp path must be
spelled in the alphabet the slug encoding preserves — after the first version of that guard turned a build
with backtracking deliberately removed into two green skips instead of a red check. The rule that came out
of it is in [AGENTS.md](AGENTS.md), and it is the right rule. The guard, however, is still a guard that
some machines will fail *every single time*: a Windows CI runner's profile directory is an 8.3 short name
(`RUNNER~1`), whose `~` the encoding maps to `-`, and no directory called `RUNNER-1` exists. On any such
machine the two assertions over the lossiest function in the app have never run.

The fix removes the skip rather than documenting it: resolve the temp root to its **long** form before
encoding it (walk the path and take each segment's real name — `DirectoryInfo.FullName` does not expand
8.3, and `GetLongPathName` is a P/Invoke this repo would rather avoid, so the walk is the zero-dependency
route). The precondition guard stays for the genuinely unrepresentable case (a space or a dot in a segment
the walk cannot fix), because §I.3-shaped honesty about what a check covers is the whole point.

The second half is about reading a green run. `Skip` prints its name and reason, and the summary prints
`N skipped` — but nothing distinguishes *"this run's synthetic window straddled a DST change"* (rare, and
the reason that mechanism exists) from *"this environment skips these two checks on every run forever"*.
Naming the skipped checks in the summary — beside the counts, where the exit code is read — costs three
lines and makes lost coverage legible. Whether CI should go further and **fail** on a skip it did not
expect is a judgement to make with the list in hand, not before.

### §XV.4 The note is twelve lines behind a click (T170 — idea)

Measured: the shaped branch of the method note is 238 + 352 + 422 = 1,012 characters, the mostly-measured
branch 1,080, the thin one 913 — one unstructured paragraph of 12px prose, twelve lines in the captures at
560px. T113 moved this text behind an ⓘ precisely because six lines pinned under the panes was too much
screen for something read once; the click did not make the text shorter, and Block Z added to it twice.

It is an idea and not a design because the obvious moves are both wrong. Cutting sentences loses claims
that are load-bearing — the "another machine or claude.ai leaves no transcript here" disclaimer is a
privacy-adjacent honesty about a blind spot, and the T163 paragraph is the one actionable reading in the
window. And splitting the popup per tab (5h / week / throughput) would put the *method* somewhere other
than where T113 decided it belongs, one click from every number it describes.

Worth exploring: whether the note is a paragraph at all or a short list keyed to the numbers it explains
(one line per figure, in the order the window shows them); whether the parts that never change belong in
the README instead, with the popup keeping only what is specific to *this* machine's evidence; and what
the real limit is — a note nobody finishes reading is not more honest than a shorter one, it is less.
