---
name: dev-flags
description: The complete catalogue of Claude Code Tray's own CLI flags — every preview, capture, headless read-out and fixture the .exe exposes, with what each one is for. Use when you need a flag that AGENTS.md's short list does not name, when you are unsure which entry point shows a surface, or when you add a flag and need to know where it is written down.
---

# Dev flags (Claude Code Tray)

Every flag the app's own `.exe` exposes. **This file is a reference, consulted rather than read** —
which is why it lives here and not in [AGENTS.md](../../../AGENTS.md), whose whole content is loaded
on every turn against a line budget (T191). AGENTS.md keeps the handful of flags that carry a *rule*;
everything else is here, complete.

Run any of them with `dotnet run -- <flag>` from the repo root, or against a built binary at
`bin\Debug\net10.0-windows\win-x64\ClaudeTray.exe`. With no flag at all the tray app itself starts.

## Build and publish

```
dotnet build -c Debug                 # fast compile check
dotnet run -c Release                 # build + run the tray app
dotnet publish -c Release             # single self-contained .exe -> bin\Release\net10.0-windows\win-x64\publish\
```

## The windows, as previews

Three different hosts, and the difference matters — see UI convention 7 in AGENTS.md.

```
--main [dest]                         # the WHOLE window as the tray opens it: nav strip + destination
                                      #   (Statistics | Context | Settings), under the WinForms pump the
                                      #   tray uses. Use this to look at the shell.
--settings [page]                     # just the Settings page, no nav strip, under a WPF pump. Any page
                                      #   name works: General, Display, "Claude Code", Notifications,
                                      #   System, About.
--settings System --sample [--reveal] # ...over the synthetic AccountFixture profiles instead of this
                                      #   machine's, unmasked with --reveal. Any published shot of that
                                      #   page must use it.
--settings-tray [page]                # ...hosted the way the TRAY hosts it (WinForms pump). The only
                                      #   preview that can see a keyboard bug.
--stats [variant] [modifiers]         # just the Statistics page on a synthetic reading. The variants and
                                      #   modifiers are one table, src\Cli\StatsPreviews.cs, which
                                      #   --capture-stats reads too; pass a name it does not know and it
                                      #   prints the whole catalogue and exits 1 (T186).
--stats [variant] --sample            # ...with the profile picker filled from AccountFixture instead of
                                      #   this machine, which is what --capture-stats --sample publishes.
--context --window [slug|name|all]    # just the Context Load page, optionally opened on one project
--context --window --sample --lang en # ...over the bundled fixture, in English (for screenshots)
--context --window <slug> --scroll    # ...scrolled to the source table
--context --window <slug> --simulate  # ...with the 3 heaviest sources ticked (the what-if)
--context --window <slug> --demo-history  # ...with a synthetic drift series behind it
--simulate-reset [variant]            # a toast card on screen: unexpected (the default early weekly reset)
                                      #   | scheduled | credit | session | context | extra. One table with
                                      #   --capture-toast (T198), so a name it does not know prints the
                                      #   catalogue and exits 1 rather than showing the default card.
```

## The captures (off-screen, deterministic)

Prefer these over `scripts\Capture-Window.ps1`: that one copies the pixels **on screen** inside the
window's rectangle, so any app that steals focus or sits on top lands in the file. The exception is a
popup — its own top-level window, which `RenderTargetBitmap` over a page's content cannot see.

```
--capture-settings <out.png> [page] [scroll=<dip>] [profile=<n>] [--sample] [--reveal]
--capture-stats [outBase] [variant] [modifiers] [profile=<n>[,<n>]] [--sample]
                                      # all three tabs -> <outBase>-5h.png / -7d.png / -throughput.png.
                                      #   profile=1,0 walks the picker one settle apart (the T164 round
                                      #   trip). Refuses a variant it cannot show rather than capturing
                                      #   the default one (T186).
                                      #   Whose name sits above the chart (T197): --sample fills the picker
                                      #   from AccountFixture, the default variant from this machine, and
                                      #   a synthetic variant on its own gets no picker at all. So any
                                      #   PUBLISHED capture of a fixture week needs --sample, and profile=
                                      #   is refused where there is no picker to walk.
--capture-toast <variant> <out.png>   # one toast card + shadow + confetti, transparent background.
                                      #   BOTH arguments required (T198): a lone path used to be read as
                                      #   the variant and land the default card in the working directory.
                                      #   Variants are one table, src\Cli\ToastPreviews.cs, which
                                      #   --simulate-reset reads too; an unknown name prints the catalogue
                                      #   and exits 1.
--render [dir]                        # tray-icon PNGs at 16/20/32 px, plus the accent mark sheet
--makeicon [ClaudeTray.ico]           # regenerate the multi-resolution app icon
--social [docs\social-preview.png]    # regenerate the social card
```

Every one of these creates the directory it is given (T187), so a fresh output folder is fine.

**Where an omitted output path goes** (T198). A default output belongs somewhere git ignores, never in the
working directory — which for anyone running these is the repository root, where `run-commit.cmd` stages
everything. So `--capture-stats`, `--render` and `--context-report` default under `docs\_preview\`;
`--capture-toast` has no default at all. The two exceptions are deliberate: `--makeicon` and `--social`
each default to their own **tracked** artifact, so the default means "regenerate the committed file".

## The headless read-outs

No window, no screen — the arithmetic and the readings as text.

```
--selftest [--quick]                  # the whole self-check over synthetic inputs, exit 1 on failure.
                                      #   --quick skips the tail sections, which wait on real sweeps.
--profiles [dir …] [--check]          # every profile discovery finds, its auth, its config-dir action and
                                      #   its icon accent; extra dirs are treated as registered ones, and
                                      #   --check also asks `claude auth status --json` per profile. Also
                                      #   prints each profile's last turn and where auto-follow points.
--probe [--live] [--all]              # the rate-limit headers VERBATIM: the recorded capture log first,
                                      #   then one live call. --live skips the log, --all reads every
                                      #   profile's. The instrument for T181 — what the overage
                                      #   percentage denominates — which only an account in overage can
                                      #   answer. Quota metadata only: no message content, no token.
--insights                            # the 24h usage breakdown: requests, sessions, per-model share
--tail                                # every assistant turn as it lands, with what the sweep cost
--live                                # the rolling tok/s with its per-project sparklines
--activity [--numbers|--refresh|--measured|--fold] [--root <dir>]
                                      # the weekly activity shape behind the projection as a 24x7 grid;
                                      #   --measured is the same week out of the folded hourly store and
                                      #   --fold folds every complete day into it now
--context [slug|name]                 # what a session costs before you type: every project, or one
                                      #   source by source (eager/lazy, bytes, ~tokens)
--context --all                       # every project in full detail
--context --calibrate                 # the estimate against transcript-measured session zero, and the fit
--context --check                     # the rule engine's findings, grouped by severity
--context --usage                     # which skills/agents were actually invoked (90d)
--context --skills                    # expand the folded skill/agent index instead of one summary row
--context --prompt [project]          # the cleanup prompt the window copies
--context --no-cache                  # force a cold scan, skipping the %LocalAppData% cache
--context --root <dir>                # scan a fixture tree instead of ~/.claude
--context --sample                    # build and scan the bundled fixture, where every rule fires
--context-report [out.md]             # the whole picture as one markdown file
```

## Two flags that go with anything

```
--lang <code>                         # render this run in en | pt-BR | pt-PT | fr | es, whatever the
                                      #   saved preference is. Both tokens are stripped before anything
                                      #   else parses, so it can go anywhere. Published screenshots are
                                      #   English; use it to check a layout in the longest translation.
--sample                              # feed a surface its fixture instead of this machine's data. Any
                                      #   published screenshot of a page that shows an account, a repo
                                      #   name or a project path must use it.
```

## Adding a flag

Route it in `src/Tray/Program.cs` (`Main` is the arg dispatch and nothing else), put the work in a
`src/Cli/*Cli.cs`, and **write it down here** — plus a row in AGENTS.md's file map if it comes with a
new file. A flag nobody can find is a flag that gets written twice.
