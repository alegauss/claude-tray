---
name: preview-ui
description: Build and visually verify the Claude Code Tray window by launching it and capturing a screenshot. Use whenever a change touches MainWindow, any of its pages (SettingsPage / StatisticsPage / ContextPage) or any windowed UI, before claiming the layout works.
---

# Preview UI (Claude Code Tray)

The cardinal rule of UI work in this repo: **never claim a window looks right without looking at it.**
WinForms/WPF layout is easy to get wrong on paper; this skill closes the loop by rendering the real
window and capturing a PNG you can read back.

## When to use

- After editing `MainWindow.xaml`, `SettingsPage.xaml` or any page's `.xaml.cs`.
- After any change that affects a windowed surface (new settings page, new dialog).
- Before telling the user a UI change is done.

## How it works

The pictures are **cases** (`cases\preview.cases.json`), run by the harness this repository's other
cases use. What draws them is the application itself: it carries `Winwright.InApp` (WW480), so a case
asks it to render its own visual tree. There is no screen in that, which is what the 451-line screen-copy
script it replaced spent most of its lines guarding against — whose window this is, what is standing over
it, whether a second instance is showing one. Those are readings the engine takes, and a picture it
cannot vouch for is refused rather than written.

One command, and the argument picks a surface:

```
preview.cmd            every preview case
preview.cmd shell      the shell on each of its three destinations (statistics, context, settings)
preview.cmd panels     every settings panel the sidebar declares (general … about)
preview.cmd context    the context load page over its own fixture
preview.cmd note       the method note, whose popup no copy of a screen can photograph
```

A word that names no tag is refused with the list there is, so a typo costs a corrected word rather
than a run of no cases that reads as a pass.

## Steps

1. **Draw it** (builds Debug first):

   ```
   preview.cmd panels
   ```

2. **Look** at the PNGs with the Read tool. They land under git-ignored `docs\_preview\<case name>\`,
   one per surface — `general.png`, `display.png`, `claude-code.png` and so on — and the command prints
   the folder. Judge the layout: alignment, spacing, overlap, theme (light/dark follows the Windows
   setting), accent colour (follows the Windows accent), and that every control rendered.

3. **Iterate**: edit the XAML, re-run, re-read — until it is right. Only then report done.

**A red is not a picture.** The case fails rather than writing something misleading: a page still saying
it is computing, a window the application would not draw, a tree that laid out to nothing. Read the
failure and fix the cause instead of re-reading a stale PNG.

To see a window by hand rather than in a file, the preview flags are still there — `dotnet run --
--main`, `--settings`, `--settings-tray ClaudeCode` — and the `dev-flags` skill is their catalogue.

## A published shot of System information must use the fixture

`--settings System` renders **this machine's** login. Masking hides the holder's name and the local
part of the address, but the organization and its mail domain *are* the reading — so any screenshot of
that page destined for the README or the site is taken over `AccountFixture` instead:

```
ClaudeTray.exe --capture-settings site\public\shots\system.png System --sample [--reveal] [profile=1] --lang en
```

`--sample` swaps in two synthetic profiles (a personal Max 20x and a Team seat, `profile=1`), and
`--reveal` opens with the holder unmasked — safe only together with `--sample`.

## A screenshot cannot see a keyboard bug

`--settings` runs a **WPF** `Application.Run`; the tray runs a **WinForms** pump and only shows the WPF
window. Those are different *input* environments, and the difference is not academic: the windows had
**no keyboard input at all** under the tray (T135) — no typing, no Tab, no Esc — while every preview and
every screenshot looked perfect, because mouse input is `WndProc`-driven and works either way.

So this loop proves *layout*, never *input*. For anything that involves typing, Tab or a shortcut, run
the interaction harness — it hosts the window under `--settings-tray` (the tray's own pump) and drives
it with UI Automation, so the result is a pass/fail rather than an impression:

```
dotnet build -c Debug
powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 -Case Keyboard
```

It navigates by clicking the sidebar, types into a `TextBox` and reads the value back through
`ValuePattern`, Tabs out of it, and drives a `Slider` with an arrow key. `-Case Menu` does the same
for the tray menu's entries. Add new checks **to that script**, not to a scratch one — its header
documents the UIA traps (no clickable point on the tray icon, collapsed WPF panes missing from the
tree, the menu not always opening) that otherwise get rediscovered every time. To see the window by
hand instead:

```
dotnet run -- --settings-tray ClaudeCode     # the window hosted the way the tray hosts it
```

## Notes

- The picture is a **render** and not a copy of the screen (WW480), so nothing has to be kept
  unobscured and the window need not hold the foreground. What is in the file is what this
  application drew.
- To preview light vs dark, toggle the Windows app theme; `ThemeMode="System"` makes the window
  follow it. There is no in-app theme switch.
- The tray icon itself is GDI+, not WPF — preview those with `dotnet run -- --render <dir>` instead
  (dumps PNGs at 16/20/32 px). See AGENTS.md.
