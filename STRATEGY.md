# Claude Code Tray — Strategy & positioning

> Business / positioning / distribution decisions: what this project *is*, how it reaches people,
> and what it will never be. **Not a backlog** — nothing here is a numbered task, and a pricing,
> distribution or naming discussion belongs here rather than in [ROADMAP.md](ROADMAP.md).
>
> This file records decisions **already made and visible in the repo** (README, LICENSE, installer,
> winget manifests, CI, the docs site). It is deliberately short; speculative product strategy is
> not invented here.

## §I — What this is

A native **Windows tray** monitor for Claude Code rate-limit usage: a crisp, DPI-aware icon with
burn-rate projection, a pace report, and a local 24h breakdown. .NET 10, `win-x64`, shipped as a
single self-contained `.exe`.

**Unofficial / community project.** Not affiliated with, endorsed by, or sponsored by Anthropic.
"Claude" and "Claude Code" are trademarks of Anthropic; the tool reads only the usage data Claude
Code already stores on the user's own machine. This framing is stated in the README, on the docs
site, and must survive any future marketing copy.

## §II — Why .NET rather than Python

The usage number is drawn as a **vector** (`GraphicsPath` with an outline), **at the exact size the
tray requests** (`SM_CXSMICON`), with `PerMonitorV2` DPI awareness. No downscaling a 64px bitmap, so
the number stays crisp at 125–200% scaling (20–32px icons). This is the product's whole visual
premise and the reason the stack is what it is — see [AGENTS.md](AGENTS.md) for the three-layer
split.

## §III — Licence and openness

**Apache-2.0.** Source-available and buildable from source (`build\build.cmd`,
`build\build-installer.cmd`, documented in the README). No paid tier, no license keys, no accounts.

## §IV — Distribution

Two channels, both in place:

1. **winget** — `winget install alegauss.ClaudeCodeTray`. Manifests live in `build/winget/` (with an
   en-US and a pt-BR locale) and are updated automatically by CI on release.
2. **GitHub Releases** — `ClaudeTray-Setup.exe`, an Inno Setup **per-user** install (no admin).
   Installed copies then **self-update** from Releases via `Updater`.

Version discipline: `<Version>` in `ClaudeTray.csproj` is the single source; the installer, the
manifests and the update check all derive from it. Releases are tagged `vX.Y.Z`.

## §V — Trust as the product

The privacy promise is not a feature, it is the reason a developer is willing to point this at
`~/.claude`: the app reads only usage counts, model ids, flags, tool/skill names and the session
`cwd` — **never message content**, save one amended exception that is visible on screen: the opening
prompt of a conversation, truncated, on the Sessions list and nowhere else (§I.1) — and talks to
nothing but the usage API and GitHub Releases. No telemetry, no analytics, no crash reporting.

Every feature that touches `~/.claude` inherits this, and it is restated in user-facing docs on
purpose. See [IMPROVEMENTS.md](IMPROVEMENTS.md) §I.1–§I.2 for the binding engineering form.

## §VI — Reach

The marketing surface is the GitHub Pages site served from `docs/` (`index.html`), which already
carries `llms.txt`, `robots.txt` and `sitemap.xml` — i.e. the site is written to be discoverable by
LLM assistants as well as search engines. Localization into five languages (en, pt-BR, pt-PT, fr,
es) is part of the same reach decision, not a nice-to-have.

## §VII — Deliberately not doing

- **No paid tier, no accounts, no license server.** Changing this would invalidate §V.
- **Not a Claude Code manager.** The app observes usage and context cost; it does not administer
  Claude Code's configuration (see [ROADMAP.md](ROADMAP.md) → Non-goals).
- **No cross-platform port.** The premise is a *native Windows tray* icon drawn with GDI+ at the
  real tray size; a Mac/Linux port would be a different product, not a build target.
- **No bundled third-party dependencies.** The single-exe install story is a distribution decision
  as much as an engineering one.
