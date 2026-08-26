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

## Block P — Project layout

- 📋 **T381** (deps: —) **The suite is 9,330 lines and 76 sections in one file, in a repo whose own rule says otherwise** — The families are already named by the section titles, Run stays whole as the table of contents, and the assertion count before and after is the test. → §XCVIII.1

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
