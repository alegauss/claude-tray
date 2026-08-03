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
--settings System --sample week=<name># which stored week the personal fixture profile gets, and so which
                                      #   branch of the extra-usage row renders (T200): spending (the
                                      #   default, "in use now (42%)") | zero ("not in use") | absent (no
                                      #   reading, so just "Enabled"). An unknown name is refused with the
                                      #   catalogue, and **--sample that cannot be honoured stops the run**
                                      #   rather than falling back to this machine's real account.
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
--tooltip [variant]                   # the TRAY TOOLTIP's composed text, printed with its char count
                                      #   against the 127 cap (T214). No variant prints them all: ok
                                      #   (the default) | track | danger | remaining | profile | extra
                                      #   | extraunspent | atlimit | connecting | signedout | error. The
                                      #   two billing rows differ by whether a cent has been spent, which
                                      #   is what decides where the news is written (T222). The surface no
                                      #   capture can photograph - the shell draws it - so this is how it
                                      #   gets reviewed at all. Add --lang to read it in a translation.
--simulate-reset [variant]            # a toast card on screen: unexpected (the default early weekly reset)
                                      #   | scheduled | credit | session | context | extra | profile |
                                      #   profile-failed. One table with
                                      #   --capture-toast (T198), so a name it does not know prints the
                                      #   catalogue and exits 1 rather than showing the default card.
```

## The captures (off-screen, deterministic)

Prefer these over `scripts\Capture-Window.ps1`: that one copies the pixels **on screen** inside the
window's rectangle, so any app that steals focus or sits on top lands in the file. The exception is a
popup — its own top-level window, which `RenderTargetBitmap` over a page's content cannot see.

**Capturing a popup: pass `-Expect <surface>`** (T217). A popup is its own top-level window, so copying
the right window proves nothing about the popup being in it — three captures of the method note came
back green, named the right window, and showed no note. The preview prints a `preview-surface:` line
saying what it drew and where; `-Expect` makes the script demand that line and prove the rectangle is
inside the copy, writing nothing if either fails. The surface for `--stats method` is `method-note`.
Preview popups are held open by `PageWindow` for every popup, not per call site.

```
--capture-settings <out.png> [page] [scroll=<dip>] [profile=<n>] [--sample] [--reveal]
                                      # page System REQUIRES --sample and is refused without it (T205):
                                      #   it renders this machine's real login, and a capture is a file
                                      #   that gets published. Interactive --settings System still shows
                                      #   the real account - only the path that writes a file is refused.
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
--capture-tooltip <out.png> [variant] # the same tooltip text drawn into the shell's card, transparent
                                      #   background, same variants as --tooltip (T214). A RENDERING, not
                                      #   a photograph: card only, no taskbar and no desktop. This is what
                                      #   docs\tooltip.png comes from - it used to be a hand-taken shot of
                                      #   one machine's notification area, which is why it went a release
                                      #   out of date without anything noticing.
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
                                      #   prints each profile's last turn, where auto-follow points, what a
                                      #   pick in the menu reaches — tray only, or Windows too (T171) — and
                                      #   which profile the environment selects beside the one the icon
                                      #   draws, marked when the two DIFFER (T172), and whether the tray
                                      #   claims to own the variable and what it restores to (T173) —
                                      #   that claim against a differing registry is a write that was
                                      #   accepted and never landed.
--probe [--live] [--all]              # the rate-limit headers VERBATIM: the recorded capture log first,
                                      #   then one live call — which is itself recorded (T212), against
                                      #   the monitored profile and filed under its key. --live skips
                                      #   *reading* the log, not writing to it; --all reads every
                                      #   profile's. The instrument for T181's other half — what 100% of
                                      #   the overage window amounts to, which only an account in overage
                                      #   can answer. Each profile's log opens with its VOCABULARY (T210):
                                      #   every value each categorical header has taken, counted and dated,
                                      #   so "has a second value arrived?" — the question §XVIII.9 and
                                      #   §XVIII.10 are blocked on — is read rather than eyeballed across
                                      #   500 readings. A name not sent on every reading says so, and
                                      #   --all ends with the CROSS-PROFILE spread (T211): which names
                                      #   every account is sent and which only some — the comparison that
                                      #   measured `unified-fallback` as conditional and absent, not
                                      #   universal. Quota metadata only: no message content, no token.
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

## Running a tray beside the resident one

```
--second-tray                         # skip the single-instance mutex and run a SECOND tray, so a check
                                      #   can drive the build you just compiled without you quitting the
                                      #   app first (T237). Explicit only — never inferred, and the mutex
                                      #   is still taken when free, so ordinary launches keep exiting.
                                      #   The tooltip is tagged `[check] …` because notification-area
                                      #   icons belong to the shell, not to us: pid cannot tell two
                                      #   ClaudeTray icons apart, so the tooltip has to. Match it
                                      #   UNANCHORED — the shell's accessible name is the registration
                                      #   name followed by the live tooltip, so the tag is mid-string.
                                      #   `Check-Interaction.ps1 -Case Menu` uses this on its own when it
                                      #   finds a tray already running. Two trays poll the same profile,
                                      #   which is fine for the length of a check and not a way to run.
```

## Three flags that go with anything

```
--lang <code>                         # render this run in en | pt-BR | pt-PT | fr | es, whatever the
                                      #   saved preference is. Both tokens are stripped before anything
                                      #   else parses, so it can go anywhere. Published screenshots are
                                      #   English; use it to check a layout in the longest translation.
--sample                              # feed a surface its fixture instead of this machine's data. Any
                                      #   published screenshot of a page that shows an account, a repo
                                      #   name or a project path must use it.
--sample-env <mode>                   # answer as if CLAUDE_CONFIG_DIR said this, and WRITE NOTHING (T231).
                                      #   agrees  | it names the profile the icon follows — the ordinary
                                      #             state, which this machine is always in anyway
                                      #   other   | it names a REGISTERED profile that is not the icon's,
                                      #             which is the only way to see T172's "set in Windows"
                                      #   outside | it names a folder no profile covers — T172's own line
                                      #   unset   | no variable at all, so the default ~/.claude applies
                                      #   Stripped like --lang, applied before anything reads the variable.
                                      #   The mode picks from the profiles really registered here, so the
                                      #   fixture cannot produce a menu state that could not occur; an
                                      #   unknown one prints the catalogue and exits 1. Sampling is one-way
                                      #   for the process — that is what makes "writes nothing" true — and
                                      #   `--profiles` says out loud that it is answering off a fixture.
                                      #   `Check-Interaction.ps1 -SampleEnv <mode>` drives the tray and the
                                      #   read-out with the same mode; it is refused with -UseRunning.
```

## Adding a flag

Route it in `src/Tray/Program.cs` (`Main` is the arg dispatch and nothing else), put the work in a
`src/Cli/*Cli.cs`, and **write it down here** — plus a row in AGENTS.md's file map if it comes with a
new file. A flag nobody can find is a flag that gets written twice.
