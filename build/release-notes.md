## What's new in v1.9.0

The app is now on the Microsoft Store. This release also adds a feed from Claude Code's status line, alerts at the usage levels you pick, a period view for sessions and support for WSL.

### 🛍️ On the Microsoft Store

- **Install from the Store:** [Claude Code Tray on the Microsoft Store](https://apps.microsoft.com/detail/9P27BHWNT56D). The Store copy updates through the Store and starts at sign-in through Windows' own startup setting.
- **Opening the app shows it.** Launching it from the Store, the Start menu or a shortcut opens the window, so you can see it is running. Launching it again while it runs brings the window back. At sign-in it still starts quietly in the tray.
- **The window tells you when the icon is hidden.** If Windows put the icon in the overflow (the **^** next to the clock), a strip at the top of the window says so. One click opens the taskbar settings where you can pin it.
- **No Claude Code yet? It says so.** On a machine without Claude Code, the tooltip, the Statistics page and the menu say it is not installed and link to the official install page. They no longer ask you to run `/login` in a program you don't have.
- The taskbar icon of the Store copy no longer sits on a coloured square.

### 📈 Fresher readings, fewer calls

- **The status line feeds the tray.** Add the entry shown in **Settings → Claude Code** to Claude Code's status line, and each terminal session hands the tray its current quota. The tray then skips the API call that reading makes redundant. Settings tells you which profiles already have it set up.
- **Quieter when you're idle.** After 30 minutes with no Claude Code activity, the tray checks every 30 minutes and right after a reset. It goes back to its normal interval as soon as you work again.
- **Each account is checked once per round**, even when several profiles point at it.

### 🔔 Alerts at your own levels

**Settings → Notifications** takes a list of levels per window, for example 50%, 80% and 95% of the week. Each level is announced once per reset, when a reading passes it. When the icon shows what's left, the levels count what's left too.

### 🗂️ Sessions by day, week or month

- **The Sessions tab groups by day, week or month**, with sessions, turns, tokens and the list-price equivalent for each period.
- **Deleted conversations keep their row for a year**, marked as deleted. Settings shows how many there are and can forget them.

### 🐧 WSL

A Claude Code signed in inside a running WSL distro shows up as a profile of its own. Its quota, sessions and context load are read, and **Open Claude Code** launches it in that distro.

### 🎛️ Smaller things

- **Both windows on the icon:** an option in **Settings → Display** splits the bar into the session on the left and the week on the right, each in its own pace colour. It is off by default.
- **For scripts:** `ClaudeTray.exe --usage --json` prints where the quota stands, with an exit code for on track, near the limit, at the limit, or no reading. `--sessions --json` lists conversations (no titles or prompts).
- **Prices:** Opus 5.5, Sonnet 5.5, Haiku 5.5, Fable 5.1 and Mythos 5.1 have their own rates. Cache reads use each model's own multiple, and Haiku 5.5 prompts over 100K tokens are priced at the higher tier.
- **Profiles:** saving Settings no longer resets `CLAUDE_CONFIG_DIR` when you only changed something else. Picking the profile the icon already shows now pins it.
- After a week of use, a card asks once whether you'd star the project on GitHub. Nothing leaves your machine to decide that, and it never shows again.

**Install or update:** from the [Microsoft Store](https://apps.microsoft.com/detail/9P27BHWNT56D), with `winget install alegauss.ClaudeCodeTray`, or by downloading `ClaudeTray-Setup.exe` below. Installed copies offer the update from the tray menu.

---

Unofficial community project, not affiliated with, endorsed by, or sponsored by Anthropic.
