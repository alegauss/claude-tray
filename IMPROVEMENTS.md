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
| [§XV](#xv--what-block-zs-own-work-left-behind-block-ab) | What Block Z's own work left behind (Block AB) |

> Block I's design sections (§II), Block J's (§IV), Block V's (§XII), Block Z's (§XIII) and Block AA's
> (§XIV) are gone: every one of them shipped, and `git log` plus [CHANGELOG.md](CHANGELOG.md) are the
> history. §III stays because it is a **measurement** — the numbers the rule thresholds and the
> base-overhead constant were calibrated against.
>
> Section numbers are **never reused**: §II, §IV–§XIV have all been retired, so a new block takes the
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

---

## §XVI — The tray reports the switch it performed, not the switch the machine got (Block AC)

The report is a question, not a bug: *"mudei para o Pessoal, mas se eu digito `/usage`, aparece VILT
Group"* — with the reasonable inference that `/usage` must be lying, since the personal account still had
quota and the work account had none. It was not lying. The running session really was on the work account,
and the tray really was showing the personal one. **Both were correct about different questions**, and
nothing in the app connects them.

What the investigation turned up is worth writing down, because the first theory was wrong in an
instructive way. The suspicion was a race — that the environment write is asynchronous (it is, since T149)
and had not landed before the editor launched. Measured on the reporter's machine, it is not:

| Measurement | Value |
|---|---|
| `Environment.SetEnvironmentVariable(…, User)` returns | **87 ms** |
| Value readable back from `HKCU\Environment` | **108 ms** |
| `HKCU\Environment` last write | 2026-08-02 **18:09:36** |
| `%LocalAppData%\ClaudeTray\settings.json` mtime | 2026-08-02 **18:09:36** — the same Save |
| `EnvironmentProfileRestore` / `EnvironmentProfileOwned` | `null` / `true` — the first `Adopt` ever saw no prior value |
| Editor process launched | 18:06:35, from an `explorer.exe` of 12:39:04 |

The variable had never been written *at all* until the moment the Windows-wide switch was ticked, three
minutes after the editor that asked the question had already started. Every profile pick before that
changed `MonitoredConfigDir` and nothing else. The async window is a tenth of a second and was never the
problem — which is why the fix is not a progress dialog. **A spinner would have verified the click; what
needs verifying is the result.**

Three failure modes hide behind one symptom, and they need separating before any of them can be fixed:

- **A — the write is never attempted.** `SyncEnvironmentProfile` is off (the default), so
  `SyncEnvironmentToPin` returns immediately. This is the one that actually happened. → §XVI.1
- **B — the write is attempted and its outcome is never read.** `Adopt` returns *accepted*, by design.
  → §XVI.3
- **C — the write lands and the next process still misses it.** A child inherits its parent's environment
  block at creation; the editor's parent was an Explorer started six hours earlier, and Explorer's refresh
  on `WM_SETTINGCHANGE` is a courtesy, not a guarantee. → §XVI.2

C is the one that bounds the whole block: **no amount of checking the registry proves the next process
will see the value.** That is why §XVI.2 is about displaying the effective value continuously rather than
asserting it once at the moment of the pick.

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

Every profile indicator in the app is fed by `MonitoredConfigDir`: the icon and its accent band (T147),
the submenu check mark, the Statistics picker. All of them answer *whose numbers am I looking at?* — a
real question, and the one the tray was built for. None answers *which account will the next session
use?*, which is the question somebody has when `/usage` surprises them. T145 put the live value on the
Claude Code settings page, and that is the right place for a setting, but it is a row on a page opened
deliberately — no help to a person who does not yet suspect there is anything to check.

The fix is to read `EnvironmentProfile.Current()` back where the choice is made and where the doubt
arises, and — the load-bearing half — to **mark the two as disagreeing when they disagree**. Agreement is
the common case and deserves no ceremony; the divergence is the whole signal.

Note what this can and cannot promise, per failure mode C. The honest phrasing is about the *variable*,
not about any future process: sessions started from a shell that has picked up the change will use it.
Claiming more would be inventing a guarantee Windows does not offer. There is a stronger source of truth
available — a live session writes its transcript under the config dir it is actually using, which is how
the original investigation was settled — and reading that would let the tray name the account a *running*
session is on. That is a larger idea than this task and is deliberately not folded into it; if it is worth
doing it is worth its own design, and it must respect §I.1 (paths and existence only, never content).

### §XVI.3 A write whose result nothing reads (T173)

T149's trade was correct and should not be reverted: the registry write plus its `WM_SETTINGCHANGE` sweep
were measured at 129 ms and 486 ms *idle*, and seconds on a working machine, so carrying that on the UI
thread is what made a profile pick freeze the tray. `Write` therefore queues to the thread pool and
`Adopt` records its bookkeeping up front and returns *accepted*. `Apply` swallows the exception with the
comment that nothing the user is waiting on depends on it.

That was true when nothing displayed the result. §XVI.2 makes something display it, and then a write that
threw is a screen that quietly disagrees with itself. `Drain` already exists for the shutdown path and is
the shape to reuse: once the queue is empty, read the variable back, compare against the intent, and give
the mismatch somewhere to go. This is bookkeeping, not UI — a completion signal per queued write, so the
outcome is available to whatever wants it, which in T174's case is the toast.

Two things this must not become. It is not a blocking wait: the caller returns at once, exactly as now.
And it is not a dialog on failure — T145's own reasoning holds, that a modal about an environment variable
is the wrong way to interrupt a menu click.

### §XVI.4 The one action with no feedback at all (T174)

Writing a machine-wide setting is the least visible thing the tray does and the only one whose effect
cannot be seen until another process starts. `ToastWindow` is already the app's answer to "an event worth
noticing", and `ToastTheme.Context` already established that a toast need not be a celebration — same
card, same slide-and-fade, no confetti, because a nudge is not good news.

A switch toast is the same category. It fires on §XVI.3's confirmation — **after** the value is read back,
never on the click — and says what was applied: the profile, the effective directory, and the sentence the
"no switching a running session" non-goal requires.

The design decision worth recording is what the toast **must not** do. `ToastWindow`'s central metaphor is
a quota bar animating from its old level to its new one, and reusing it here is the obvious move: animate
from the outgoing account's remaining quota to the incoming one's. It is also forbidden. The roadmap's
"profiles are contexts, not quota pools" non-goal says no string may suggest changing accounts because one
hit its limit, and a bar leaping from 0% to 100% on a switch says it without a string — more persuasively
than a string could, and in precisely the situation that produced this report. So this toast carries no
quota bar and no confetti; it is a receipt, not a reward. If that leaves the card looking thin, the answer
is a smaller card, not a borrowed metaphor.

This also needs its own justification rather than inheriting one, per the T87 non-goal on notification
channels. It has one, and it is narrow: this fires only on an explicit user action, only once per action,
and only to confirm a change whose effect is otherwise unobservable. Nothing ambient, nothing on a timer.
