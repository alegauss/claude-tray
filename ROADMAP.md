# Claude Code Tray — Roadmap (active backlog)

> **Single source of truth for task status.** Flat, one line per task.
> Only **unshipped** work lives here (📋 designed · 💭 idea · ⏳ partial · 🛠 in-progress).
> Shipped work is moved to [CHANGELOG.md](CHANGELOG.md); design rationale (the *what/why* per task)
> lives in [IMPROVEMENTS.md](IMPROVEMENTS.md); positioning/distribution decisions live in
> [STRATEGY.md](STRATEGY.md).
>
> **This file is written by `roadkeep`, not by hand.** The id, the `(deps: … ✅)` annotation and the
> `→ §` pointer are derived on render, and the field limits in
> [roadkeep.toml](roadkeep.toml) are refused at insertion rather than reported afterwards. The
> `roadkeep` skill says which command to call; a hand-edit is denied by the hook.
>
> **An entry is one sentence: what + why + `→` pointer**, ≤320 characters, symptom in bold and never
> named after its fix. The `→` points at the section in IMPROVEMENTS.md with the full design.
>
> **How to pick work:** `roadkeep pick` — the lowest-numbered task whose `deps` are all shipped, with
> the reason it was chosen. `roadkeep brief <id>` is everything it costs to start one, in one call.

| Symbol | Meaning |
|---|---|
| 📋 | Designed but not started |
| 💭 | Idea worth exploring; needs design |
| ⏳ | Partial — direction is right, more work remains |
| 🛠 | In progress |

## Block AM — Danger is said, not only coloured

## Block AN — The site on the shared design system

## Block A — Foundation (tray, icon, API, projection)

## Block D — Auth & API resilience

- ⏳ **T398** (deps: —) (requires: statusline-day) **Nobody knows how many polls the status-line feed actually saves, or whether its numbers match the API** — Nobody has measured how often the status line runs while working, or whether its numbers match the headers on the same minute. → §CVI.9
- 📋 **T445** (deps: —) **Settings says the status line is set up while its command runs an exe that was uninstalled** — Classify counts any ClaudeTray --statusline command as ours without checking its exe exists, so after the Store move both profiles feed nothing and the paste stays hidden. → §CXI.27

## Block E — Reset notifications & toasts

## Block F — Statistics window (pace report)

- 📋 **T439** (deps: —) **The Statistics error banner is painted for a dark theme and reads poorly in a light one** — ErrorBanner sets its background, border and glyph in hex rather than Fluent brushes, which §I.5 forbids on a theme-able surface. → §CXI.21

## Block H — Tray display options

## Block O — Profiles

- 📋 **T443** (deps: —) **A profile's readings and alarms reset when Claude Code is caught mid-rewrite of its .claude.json** — A torn read finds no oauthAccount, so the store key falls back from the account to the config-dir digest and RefreshWatched treats it as a profile switch. → §CXI.25

## Block AK — Sessions — what one conversation cost, and what it did

- 📋 **T436** (deps: —) **An open call tree on the Sessions pane collapses on every poll** — Render reloads the sessions list on each poll even when no row changed, which T118 stopped for the pace report and the Sessions pane of T329 brought back. → §CXI.18

## Block AI — Verification — the checks that prove a change

- 📋 **T446** (deps: —) **A repository check that throws ends --selftest with an unhandled exception and no summary** — SelfTestCli.Repo calls its assert with no try, unlike Temp, so a repository check that throws ends the process before the summary and every later section. → §CXI.28

## Block B — Packaging, self-update, CI

- ⏳ **T412** (deps: —) (requires: store-account) **The Store build has not yet replaced the winget install for a week of daily use** — The app is listed on the Store as 9P27BHWNT56D; a week on that build in place of winget, holding the startup task, the alias and a profile switch, is left. → §CVIII.2

## Block AO — What a stranger meets first

## Block C — Settings window (WPF Fluent)

- 📋 **T437** (deps: —) **A second Save in one Settings visit writes back over a toggle flipped in the tray menu** — The page's opened snapshot is not renewed after a Save, so the next merge compares against stale values and the defect T229 fixed returns on the second Save. → §CXI.19
- 📋 **T444** (deps: —) **A truncated settings.json silently resets every setting and profile to defaults** — Load swallows a parse failure into new Settings() and the next Save overwrites the file, which is written in place rather than through a temp file and a rename. → §CXI.26

## Block G — Localization

- 📋 **T440** (deps: —) **Session dates ignore the language's field order, and a Thai system calendar dates files 543 years ahead** — SessionListRow formats MMM d, HH:mm itself instead of through Dates (T263), and yyyy-MM-dd elsewhere runs on the OS culture's calendar, not the invariant one. → §CXI.22
- 📋 **T447** (deps: —) **About twenty more strings may print a count of one before a plural noun** — T432 fixed the strings its audit named, but a sweep of en.json finds about twenty more counts before a plural noun, and nothing checks the shape. → §CXI.29

## Block I — Context Load Inspector

- 📋 **T435** (deps: —) **Every Context rescan blanks the page, forgets All projects and the scroll, and drops an edit made mid-scan** — The rescan collapses the panes and rebuilds them with no selection or offset restored, and a debounced rescan arriving while one runs is discarded, not queued. → §CXI.17
- 📋 **T441** (deps: —) **The Context page calls skills never invoked that were used, and dates their last use by file time** — A transcript that fails to open counts as a complete zero, a plugin:skill call never matches the bare skill name, and WhenUtc is the file's mtime. → §CXI.23
- 📋 **T442** (deps: —) **A project whose context has not changed in 60 days loses its drift readout** — ContextHistory writes a line only on change but reads a 60-day window, so a stable project's newest line falls outside it and Trend returns null. → §CXI.24

## Block L — Scan resilience

## Block Q — Keyboard input in the windows

- 📋 **T438** (deps: —) **The Settings sidebar, About cards and session rows are mouse-only, and the toast close reads as a glyph** — They are clickable Borders, not focusable named controls, so Tab skips them and a screen reader announces the toast's close button as a bare glyph. → §CXI.20

## Block AJ — Working here — the repo's own docs and flags

## Non-goals (do NOT add as tasks)

Binding constraints — see [IMPROVEMENTS.md](IMPROVEMENTS.md) §I for the full text. Summary:

- **No tokenizer dependency, and no NuGet package in general** The single self-contained `.exe` plus
  installer story depends on it, so token counts stay estimates with a visible "≈" — §I.3.
- **Never read message content** Usage counts, model ids, flags, tool/skill *names* and the session
  `cwd`, plus one amended exception: what a conversation is called, truncated, on the Sessions list
  only — §I.1.
- **No network** Beyond the usage API and GitHub Releases: no telemetry, no analytics, no crash
  reporting, and every computation local — §I.2.
- **Not a memory editor, not a Claude Code config manager.** Hooks, MCP servers, permissions and
  instruction files are measured, never edited, and T77 settled it with no write path at all — §I.4.
- **Never swap a credential or config file to switch accounts.** Block O switches by launching with
  a different `CLAUDE_CONFIG_DIR`, because the file-juggling alternative races Claude Code's own
  refresher — §I.6.
- **No switching the account of a running session.** The environment is fixed when the process
  starts, so a profile change applies to the next session and the UI says so rather than implying
  otherwise.
- **Profiles are contexts, not quota pools.** No string anywhere may suggest changing accounts
  because one hit its limit, which is limit circumvention wearing a convenience costume — §I.7.
- **Don't swap the UI stack.** WinForms owns the tray icon and WPF owns the windows on one STA
  thread, with no imperative `Dock=Top` stacking and no hardcoded hex — §I.5.
- **No second source of truth for the version** `<Version>` in `ClaudeTray.csproj` is the one place
  a version lives, and everything else derives from it — §I.5.
- **No usage annotation on memory files** Settled by dropping T85: a recall is the harness injecting
  a file, and no memory analogue of `attributionSkill` exists to read — §I.8.
- **No activity-aware tray notification** Settled by T87: the shaped projection changed what the
  chart says, not what is worth interrupting somebody for.
- **No live hint on the tray icon** Settled by dropping T101: it needs a transcript tail running all
  session, undoing the one property that keeps T99 free when nobody is looking.
- **No pricing or positioning discussion as a numbered task.** It goes in
  [STRATEGY.md](STRATEGY.md), which is where a decision that is not work belongs.
- **No check outside the binary** --selftest is the suite because a test project would break the
  single-exe rule, and moving a document check out to save a measured 10s compile buys a second
  reader of the thing it compares.
- **No colour emoji on the cards** WPF text and GDI+ both draw Segoe UI Emoji monochrome base layer
  - measured on six glyphs and on a real captured card - so colour needs Direct2D interop or seven
  shipped raster assets (T227).
- **No build retry loop, and no node-reuse setting** No node-reuse setting and no build retry loop:
  Directory.Build.rsp is not honoured and MSBUILDDISABLENODEREUSE leaves nodes alive, both measured
  (T270).
- **No figure in money for an overage spell** Settled by dropping T279: which models overage bills
  is a cached GrowthBook flag reading ["Fable", "Fable 5"], and 0 of 34,790 turns here were Fable —
  its two readings differ by the whole estimate.
- **No second build target for the source-reading checks** T345 re-measured the loop and it is the
  machine, not the repo: 39s to 413s for one edit, and 409-742s on a v1.5.3 worktree with half the
  code. A second .csproj cannot fix a stopwatch.
