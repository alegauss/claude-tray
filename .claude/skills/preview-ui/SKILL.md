---
name: preview-ui
description: Build and visually verify a WPF window in Claude Code Tray by launching it and capturing a screenshot. Use whenever a change touches SettingsWindow.xaml / .xaml.cs or any windowed UI, before claiming the layout works.
---

# Preview UI (Claude Code Tray)

The cardinal rule of UI work in this repo: **never claim a window looks right without looking at it.**
WinForms/WPF layout is easy to get wrong on paper; this skill closes the loop by rendering the real
window and capturing a PNG you can read back.

## When to use

- After editing `SettingsWindow.xaml` or `SettingsWindow.xaml.cs`.
- After any change that affects a windowed surface (new settings page, new dialog).
- Before telling the user a UI change is done.

## How it works

The app exposes a deterministic preview entry point so the window can be shown **without** clicking
through the tray menu:

```
ClaudeTray.exe --settings      # opens just the Settings window, standalone
```

`scripts\Capture-Window.ps1` launches that, waits for the first paint, makes itself
per-monitor-DPI-aware (critical on 150–200% displays or the capture is offset/scaled), copies the
window's rectangle to a PNG, and kills the process.

## Steps

1. **Build** (Debug is fine and fast):

   ```
   dotnet build -c Debug
   ```

2. **Capture** (default output is `docs\_preview\settings.png`, which is git-ignored):

   ```
   powershell -ExecutionPolicy Bypass -File scripts\Capture-Window.ps1
   ```

   To preview a different window/args or output path:

   ```
   powershell -ExecutionPolicy Bypass -File scripts\Capture-Window.ps1 -AppArgs "--settings" -Out "docs\_preview\foo.png"
   ```

3. **Look** at the PNG with the Read tool (`docs\_preview\settings.png`) and judge the layout:
   alignment, spacing, overlap, theme (light/dark follows the Windows setting), accent color
   (follows the Windows accent), and that every control rendered.

4. **Iterate**: edit the XAML, rebuild, recapture, re-read — until it's right. Only then report done.

## A published shot of System information must use the fixture

`--settings System` renders **this machine's** login. Masking hides the holder's name and the local
part of the address, but the organization and its mail domain *are* the reading — so any screenshot of
that page destined for the README or `docs/` is taken over `AccountFixture` instead:

```
ClaudeTray.exe --capture-settings docs\system.png System --sample [--reveal] [profile=1] --lang en
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

- The screenshot copies from the screen, so keep the window unobscured during capture; the script
  brings it to the foreground, but a modal/topmost overlay could still cover it.
- To preview light vs dark, toggle the Windows app theme; `ThemeMode="System"` makes the window
  follow it. There is no in-app theme switch.
- The tray icon itself is GDI+, not WPF — preview those with `dotnet run -- --render <dir>` instead
  (dumps PNGs at 16/20/32 px). See AGENTS.md.
