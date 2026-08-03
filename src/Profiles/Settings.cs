using System.Reflection;
using System.Text.Json;

namespace ClaudeTray;

/// <summary>
/// Marks a setting the <b>tray</b> owns rather than the Settings page: it is changed from the menu (or by
/// the tray's own bookkeeping) and has no control on any page, so the window's snapshot of it is always
/// the older value.
///
/// <para>The declaration lives on the property because the alternative — a list of field names in
/// <c>ApplySettings</c>, four lines long and a file away from the fields it named — has shipped the same
/// defect twice: a tray-owned field missing from that list is written back stale on every Save (T126 lost
/// the monitored config dir, T155 the icon's profile). Marking the field and carrying by the declaration
/// makes the carry total for the same reason <see cref="Settings.Clone"/> makes the copy total (T162).</para>
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class TrayOwnedAttribute : Attribute;

/// <summary>
/// User-configurable settings, persisted as JSON in %LocalAppData%\ClaudeTray\settings.json.
/// Deliberately small for now — more fields will land here over time and the Settings dialog
/// grows with them. Any missing, corrupt, or out-of-range value falls back to its default, so
/// a hand-edited or older file can never crash the app.
/// </summary>
internal sealed class Settings
{
    public const int MinRefreshSeconds = 30;     // don't hammer the API
    public const int MaxRefreshSeconds = 3600;   // an hour between polls is plenty
    public const int DefaultRefreshSeconds = 300; // 5 min — the historical hardcoded cadence

    public const int MinAuthRetrySeconds = 5;       // floor on the signed-out re-check cadence
    public const int MaxAuthRetrySeconds = 300;     // 5 min is as slow as a retry needs to be
    public const int DefaultAuthRetrySeconds = 10;  // re-check every 10s while signed out

    // Minimum usage (before the reset) required to fire a routine reset notification, in percent.
    // A reset from a near-empty window is uninteresting — these floors suppress the "fresh session /
    // week" ping when you'd barely used the window. Kept low so they nudge rather than silence.
    public const int MinResetNotifyPercent = 0;
    public const int MaxResetNotifyPercent = 100;
    public const int DefaultSessionResetMinPercent = 2;    // 5h session: notify only above 2%
    public const int DefaultContextNudgeTokens = 15_000;   // above the middle of the measured 4k–22k spread
    public const int MinContextNudgeTokens = 2_000;
    public const int MaxContextNudgeTokens = 100_000;
    public const int DefaultScheduledResetMinPercent = 5;  // routine weekly: notify only above 5%

    public const string DefaultMetric = "5h";
    private static readonly string[] ValidMetrics = { "5h", "7d", "extra" };

    public const string DefaultLanguage = "auto";

    /// <summary>How often the tray polls the usage API, in seconds.</summary>
    public int RefreshSeconds { get; set; } = DefaultRefreshSeconds;

    /// <summary>Draw the usage percentage number on the tray icon (otherwise just the fill bar).</summary>
    public bool ShowPercentage { get; set; } = true;

    /// <summary>
    /// Invert the tray display to show quota <em>remaining</em> instead of <em>used</em>: the icon
    /// starts full at 100% and counts down toward 0%, and the tooltip lines read "… left". The
    /// underlying data, burn-rate projection, warning color and >=90% flash are unchanged — they
    /// still track how close you are to the limit; only the displayed number and bar height flip.
    /// </summary>
    public bool ShowRemaining { get; set; } = false;

    /// <summary>
    /// Blink the tray icon (background flashes every 500 ms) once usage crosses the near-limit
    /// warning threshold (&gt;=90%, the point where the API reports <c>allowed_warning</c>). Off by
    /// default: the constant flashing can be distracting, so it's opt-in — the warning color still
    /// tracks the limit regardless. See <see cref="FlashWarnThreshold"/>.
    /// </summary>
    public bool FlashNearLimit { get; set; } = false;

    /// <summary>Usage fraction (0 to 1) at which the near-limit flash kicks in, when enabled.</summary>
    public const double FlashWarnThreshold = 0.90;

    /// <summary>
    /// Show a tray notification on an unexpected drop in weekly usage — the counter resetting to 0%
    /// before its scheduled deadline, or a partial mid-window credit (e.g. 91% → 50%). Both are known
    /// Claude Code anomalies. Rare by nature, so enabled by default; turn it off to silence the alert.
    /// </summary>
    public bool NotifyOnUnexpectedReset { get; set; } = true;

    /// <summary>
    /// Show a (calmer) tray notification on the routine weekly reset too — the "fresh week, quota's
    /// back" ping. On by default; turn it off to silence the weekly heads-up.
    /// </summary>
    public bool NotifyOnScheduledReset { get; set; } = true;

    /// <summary>
    /// Show a tray notification when the 5-hour session window resets ("fresh session"). On by
    /// default; turn it off if the several-times-a-day heads-up is too frequent.
    /// </summary>
    public bool NotifyOnSessionReset { get; set; } = true;

    /// <summary>
    /// Nudge once when a project's startup context crosses <see cref="ContextNudgeTokens"/>. Default
    /// off, deliberately: nobody asked to be told their memory directory is growing, and an unsolicited
    /// warning about the developer's own files is exactly the kind of nagging this feature avoids.
    /// </summary>
    public bool NotifyOnContextGrowth { get; set; } = false;

    /// <summary>
    /// Say so, once, the first time a reading shows the account spending past the quota included in its
    /// plan (T184). Default <b>on</b>, like the three reset notifications and unlike the context nudge:
    /// the resets interrupt to say quota came back, and staying silent when the opposite happens is the
    /// asymmetry this exists to close. It is a fact about the user's own money, it fires at most once per
    /// spell rather than on a timer, and it can be turned off here.
    /// </summary>
    public bool NotifyOnExtraUsage { get; set; } = true;

    /// <summary>Eager-token threshold for that nudge. The measured spread across real projects is
    /// ≈4k–22k (IMPROVEMENTS §III), so the default sits above the middle of it.</summary>
    public int ContextNudgeTokens { get; set; } = DefaultContextNudgeTokens;

    /// <summary>
    /// Minimum usage (percent) the 5-hour window must have reached before its reset to be worth a
    /// notification — a reset from a barely-touched session is skipped. Defaults to 2%.
    /// </summary>
    public int SessionResetMinPercent { get; set; } = DefaultSessionResetMinPercent;

    /// <summary>
    /// Minimum usage (percent) the weekly window must have reached before its routine reset to be
    /// worth a notification. Defaults to 5%.
    /// </summary>
    public int ScheduledResetMinPercent { get; set; } = DefaultScheduledResetMinPercent;

    /// <summary>Which usage window the tray displays: "5h", "7d", or "extra". Set from the tray menu,
    /// nowhere else — see <see cref="TrayOwnedAttribute"/>.</summary>
    [TrayOwned]
    public string Metric { get; set; } = DefaultMetric;

    /// <summary>
    /// Display language for the whole UI: <c>"auto"</c> (follow the OS UI language — the default and
    /// historical behaviour) or one of the shipped language codes (see <see cref="L"/>). Read once at
    /// startup by <see cref="L.Apply"/>; because the localized strings resolve when a window is parsed,
    /// changing this prompts a restart to take effect.
    /// </summary>
    public string Language { get; set; } = DefaultLanguage;

    /// <summary>The user's home directory — the default working directory when none is chosen.</summary>
    public static string DefaultDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Working directory Claude Code opens in — for the "Open Claude Code" menu item, a tray
    /// double-click, and auto-open. Never empty: an unset or cleared value falls back to the
    /// user's home directory (<see cref="DefaultDirectory"/>).
    /// </summary>
    public string ClaudeCodeDirectory { get; set; } = DefaultDirectory;

    /// <summary>
    /// When a poll returns HTTP 401 (expired token), automatically launch Claude Code so it can
    /// refresh the OAuth token without the user having to act.
    /// </summary>
    public bool AutoOpenOnUnauthenticated { get; set; } = false;

    /// <summary>While signed out, how often to re-poll the usage API (seconds) until auth returns.</summary>
    public int AuthRetrySeconds { get; set; } = DefaultAuthRetrySeconds;

    /// <summary>
    /// The Claude Code profiles the user registered — the tray's *own* record of which config dirs to
    /// offer, what to call them and where each one opens. Discovery finds unregistered profiles on its
    /// own (see <see cref="ClaudeAccount.Discover"/>); this list is what adds one that has never been
    /// logged into, and what remembers a chosen name or working directory.
    /// </summary>
    public List<ClaudeProfile> Profiles { get; set; } = new();

    /// <summary>An upper bound so a corrupt settings file can't make the tray menu unusable.</summary>
    public const int MaxProfiles = 20;

    /// <summary>
    /// Config dir of the profile the **tray icon** shows (T127). The icon is one number and cannot be
    /// two, so one profile owns it; the rest are read in the Profile submenu. Empty — the default —
    /// means the profile a bare <c>claude</c> would use, which is the only one most machines have.
    ///
    /// <para>Moved by the Profile submenu only, since T155 took the page's "Icon profile" row out — so the
    /// tray owns it (<see cref="TrayOwnedAttribute"/>). This is the field whose missing carry-over line
    /// <em>was</em> T155's defect.</para>
    /// </summary>
    [TrayOwned]
    public string MonitoredConfigDir { get; set; } = "";

    /// <summary>
    /// Let the icon follow whichever profile a turn just landed in, instead of the one chosen by hand
    /// (T126). Off by default: an icon that moves on its own is only welcome once you have asked for it,
    /// and on a one-profile machine it would have nothing to do. Choosing a profile in the Profile
    /// submenu still wins until the next turn lands somewhere else, so this never takes the choice away
    /// — see <see cref="ProfileActivity.Pick"/>.
    /// </summary>
    public bool FollowActiveProfile { get; set; } = false;

    /// <summary>
    /// Write the **user-scope** <c>CLAUDE_CONFIG_DIR</c> when a profile is picked by hand, so the choice
    /// reaches every Claude Code session on the machine — a terminal opened by hand, an editor started
    /// from the Start menu — and not only the one the tray launches (T145, reversing T143).
    ///
    /// <para>Off by default: an update must never silently rewrite somebody's environment. And it
    /// follows the <b>pin</b>, not the icon — auto-follow moves the icon by observation, which must not
    /// rewrite a machine-wide setting; a manual pick is a decision, and only a decision writes.</para>
    /// </summary>
    public bool SyncEnvironmentProfile { get; set; } = false;

    /// <summary>Whether the tray currently owns the user-scope <c>CLAUDE_CONFIG_DIR</c>. Distinguishes
    /// "not managing it" from "managing it, and there was nothing there before" — which
    /// <see cref="EnvironmentProfileRestore"/> alone cannot, since both are null. The tray's own
    /// bookkeeping, with no control on any page (<see cref="TrayOwnedAttribute"/>).</summary>
    [TrayOwned]
    public bool EnvironmentProfileOwned { get; set; } = false;

    /// <summary>What the user-scope <c>CLAUDE_CONFIG_DIR</c> said before the tray first took it over,
    /// so switching the feature off puts it back instead of abandoning a value the app no longer
    /// manages. Null means there was none. Tray-owned, for the same reason as
    /// <see cref="EnvironmentProfileOwned"/>.</summary>
    [TrayOwned]
    public string? EnvironmentProfileRestore { get; set; }

    /// <summary>Where the app keeps its own state — <c>%LocalAppData%\ClaudeTray</c>: this file, the
    /// caches, the usage/context history and the reset log. Public so the System information page can
    /// show (and open) it without a second copy of the path.</summary>
    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeTray");

    private static string FilePath => Path.Combine(DataDir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath) &&
                JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) is { } s)
            {
                s.Clamp();
                return s;
            }
        }
        catch { /* corrupt or unreadable — fall back to defaults */ }
        return new Settings();
    }

    /// <summary>
    /// A deep copy, made by round-tripping through the same <see cref="JsonSerializer"/> that writes
    /// the file. The point is that the copy is <b>total by construction</b>: every property that
    /// persists is copied, including <see cref="Profiles"/> (new <see cref="ClaudeProfile"/> objects,
    /// so an editor mutating them in place can't reach the original), and a field added later cannot
    /// be forgotten. A hand-written field list is what silently reset <c>MonitoredConfigDir</c> and the
    /// context-growth pair on Save — the edit buffer started at the default and was written straight
    /// back over the user's value.
    /// </summary>
    public Settings Clone()
    {
        Settings copy = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this))
                        ?? new Settings();
        copy.Clamp();
        return copy;
    }

    /// <summary>Every property marked <see cref="TrayOwnedAttribute"/>, by declaration. Computed once —
    /// the set is fixed at compile time — and public so <c>--selftest</c> can assert the round trip over
    /// the same list the tray carries by, rather than over a copy of it.</summary>
    public static readonly IReadOnlyList<PropertyInfo> TrayOwned =
        typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.IsDefined(typeof(TrayOwnedAttribute)) && p.CanRead && p.CanWrite)
                        .ToList();

    /// <summary>
    /// Carry the tray-owned fields over from the live model onto this (edited) one. The other end of
    /// <see cref="Clone"/>'s round trip: the window hands back a total copy of the model it was given, so
    /// everything it does not edit is already right — <em>except</em> the fields the tray moved while the
    /// window sat open, whose snapshot in that copy is stale.
    ///
    /// <para>By the declaration and not by a list, so a new tray-owned field is carried the day it is
    /// marked rather than the day somebody notices it resetting (T162).</para>
    /// </summary>
    public void CarryTrayOwnedFrom(Settings live)
    {
        foreach (PropertyInfo p in TrayOwned) p.SetValue(this, p.GetValue(live));
    }

    public void Save()
    {
        // T239: an observing tray must not write the settings file either. Auto-follow moves
        // MonitoredConfigDir on its own and saves, so a check run beside the user's tray could repoint
        // the icon their tray follows — the settings file is one per machine, not one per process.
        if (ProfileStore.Observing) return;
        Clamp();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void Clamp()
    {
        RefreshSeconds = Math.Clamp(RefreshSeconds, MinRefreshSeconds, MaxRefreshSeconds);
        AuthRetrySeconds = Math.Clamp(AuthRetrySeconds, MinAuthRetrySeconds, MaxAuthRetrySeconds);
        SessionResetMinPercent = Math.Clamp(SessionResetMinPercent, MinResetNotifyPercent, MaxResetNotifyPercent);
        ScheduledResetMinPercent = Math.Clamp(ScheduledResetMinPercent, MinResetNotifyPercent, MaxResetNotifyPercent);
        ContextNudgeTokens = Math.Clamp(ContextNudgeTokens, MinContextNudgeTokens, MaxContextNudgeTokens);
        if (string.IsNullOrWhiteSpace(ClaudeCodeDirectory))
            ClaudeCodeDirectory = DefaultDirectory;
        if (Array.IndexOf(ValidMetrics, Metric) < 0)
            Metric = DefaultMetric;
        if (!L.IsValidPreference(Language))
            Language = DefaultLanguage;
        ClampProfiles();
    }

    // A profile is only meaningful with a config dir, so an entry without one is dropped rather than
    // shown as a nameless menu item. Labels/dirs are trimmed, duplicates by dir are collapsed, and the
    // list is capped. Nothing here touches disk — an entry whose folder is gone stays, because it may
    // be on a drive that is merely unplugged.
    private void ClampProfiles()
    {
        Profiles ??= new();
        var clean = new List<ClaudeProfile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ClaudeProfile p in Profiles)
        {
            if (p is null) continue;
            p.ConfigDir = (p.ConfigDir ?? "").Trim();
            p.Label = (p.Label ?? "").Trim();
            p.WorkDir = (p.WorkDir ?? "").Trim();
            if (p.ConfigDir.Length == 0 || !seen.Add(p.ConfigDir)) continue;
            clean.Add(p);
            if (clean.Count >= MaxProfiles) break;
        }
        Profiles = clean;
    }
}

/// <summary>
/// One registered Claude Code profile: a config dir, what to call it, and where it opens.
///
/// <para>Deliberately holds **no** account data — no address, no token, no plan. Those are read back
/// from the profile's own files on demand (<see cref="ClaudeAccount"/>), so the tray never becomes a
/// second store of somebody's credentials, and a <c>/login</c> that changes the account inside a dir
/// needs no update here.</para>
/// </summary>
internal sealed class ClaudeProfile
{
    /// <summary>Display name for the picker and the tray submenu. Empty means "use the derived one"
    /// (the organization, else Personal, else the folder name).</summary>
    public string Label { get; set; } = "";

    /// <summary>The config dir — the profile's identity as far as Claude Code is concerned; passed as
    /// <c>CLAUDE_CONFIG_DIR</c> when launching.</summary>
    public string ConfigDir { get; set; } = "";

    /// <summary>Working directory Claude Code opens in for this profile. Empty falls back to the
    /// global <see cref="Settings.ClaudeCodeDirectory"/> — which is what keeps a work account from
    /// opening inside a personal repo.</summary>
    public string WorkDir { get; set; } = "";
}
