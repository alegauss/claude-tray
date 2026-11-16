## What's new in v1.10.0

The app now speaks German and Simplified Chinese, uses far less memory while it sits in the tray, and no longer gets stuck at startup when WSL hangs.

### 🌍 Deutsch and 中文（简体）

- **Two new languages.** The menus, settings, reports, tooltips and notifications are translated into German and Simplified Chinese. The app picks them up from your Windows display language, or you can choose one in **Settings → General**. That makes seven: English, Deutsch, Español, Français, Português (Brasil and Portugal) and 中文（简体）.
- The Microsoft Store page is now offered in both languages too.

### 🪶 Lighter in the tray

- **Much less memory at idle.** The app gives back the memory its startup scan used, reads transcripts with one reusable buffer, and the installed program is no longer a compressed bundle that has to be unpacked into memory. Measured on the same machine, an idle tray dropped from about 180 MB to well under 100 MB.
- **A smaller installer:** `ClaudeTray-Setup.exe` is about 18 MB smaller.

### 🛠️ Fixes

- **A hung WSL no longer stops the app from starting.** When WSL was stuck after a reboot, the tray never showed its icon and opening it again did nothing. It now waits at most two seconds for WSL and carries on.
- **The first start after installing opens the window**, so you can see the app is running even when Windows hides a new icon in the overflow. Updates and later starts stay quiet in the tray.
- **Keyboard:** the Sessions rows, the chart strip, the Context overview, the Settings sidebar and the About cards can all be reached and opened from the keyboard.
- **Settings are saved atomically**, and a settings file that could not be read is never overwritten with defaults.
- **Profiles:** usage insights and the Context page read the profile the icon shows; a profile keeps its identity while Claude Code is rewriting its config file.
- **Sessions:** the list and any call tree you opened stay put through a refresh; deleted conversations are kept correctly behind a linked projects folder.
- **Context:** plugin skills are counted, and a project's drift no longer resets.
- Counted strings say "1 conversation" instead of "1 conversations" in every language, and dates use the Gregorian calendar everywhere.

**Install or update:** from the [Microsoft Store](https://apps.microsoft.com/detail/9P27BHWNT56D), with `winget install alegauss.ClaudeCodeTray`, or by downloading `ClaudeTray-Setup.exe` below. Installed copies offer the update from the tray menu.

---

Unofficial community project, not affiliated with, endorsed by, or sponsored by Anthropic.
