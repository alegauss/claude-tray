# Microsoft Store listing — Claude Code Tray (T409)

What Partner Center asks for when the package is submitted, kept beside the manifest so a submission and
the code it describes are reviewed together. Paste each block into the field it names. Keep the wording
in step with the README: the pitch, the privacy facts and the unofficial disclaimer are the same claims.

## Product name

Claude Code Tray

## Short description

Your Claude Code limit in the Windows tray: red before you run out, and a weekly forecast paced to the
hours you actually work.

## Description

Claude Code Tray shows how much of your Claude Code 5-hour session and weekly limit you have used, as a
crisp icon in the Windows notification area.

- The icon is the number: your session or weekly usage, drawn sharp at any display scale.
- Red means slow down: when your current pace would use the limit up before it resets, the icon turns
  red and a notification says when, while there is still time to change course.
- A forecast that knows your week: the weekly projection is paced to the hours you actually work, not a
  straight line through nights and weekends.
- Statistics, per-session cost at API list prices, and a context audit of what Claude Code loads before
  your first prompt — all computed on your own machine.
- Several Claude Code profiles, work and personal, one click apart.

It reads the sign-in Claude Code already keeps on this computer, so there is no API key to paste and no
setup.

This is an unofficial, community-built app. It is not affiliated with, endorsed by, or sponsored by
Anthropic. "Claude" and "Claude Code" are trademarks of Anthropic, PBC.

## Privacy policy URL

https://alegauss.github.io/claude-tray/features/privacy/

## Privacy — what the app does with data (for the privacy questions and the notes to certification)

- The app sends no telemetry, analytics or crash reports, and nothing to the developer.
- To read your usage it makes one minimal request to the Anthropic API on each poll (every 5 minutes by
  default, less when idle), using the OAuth sign-in Claude Code already stores in
  `%USERPROFILE%\.claude\.credentials.json`, and reads only the rate-limit headers of the reply. When
  Claude Code's status line feeds the app, that request is skipped.
- From Claude Code's local transcripts it reads token counts, model ids, flags, tool and skill names and
  the working directory — never message content.
- Everything it stores stays in its own app data folder on this computer.

## Notes for certification

The app is a notification-area (system tray) utility: after launch it shows an icon, not a window. Click
the icon to open the main window; right-click it for the menu. Without Claude Code signed in on the test
machine the icon shows a "not authenticated" state, which is the expected behaviour — the app needs a
Claude Code sign-in to have anything to show.

It starts with Windows through the package's startup task, which the user can turn off in Settings >
Apps > Startup; the app's own Settings page links there.

The `ClaudeTray.exe` execution alias exists so Claude Code's status-line command can run the app from any
shell.

## Reusing the Claude Code sign-in (the policy question)

The app does not ask for, store or transmit credentials of its own. It reads the OAuth token Claude Code
already keeps on the user's machine, only to make the same minimal request Claude Code itself makes, so
it can show the user their own limits. The README sets out how this sits with Anthropic's terms
(section "Authentication & Anthropic's terms"). Whether that is acceptable for the Store is the owner's
answer to give, not this file's.
