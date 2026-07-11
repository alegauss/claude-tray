using System.Text.Json;

namespace ClaudeTray;

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
    /// Minimum usage (percent) the 5-hour window must have reached before its reset to be worth a
    /// notification — a reset from a barely-touched session is skipped. Defaults to 2%.
    /// </summary>
    public int SessionResetMinPercent { get; set; } = DefaultSessionResetMinPercent;

    /// <summary>
    /// Minimum usage (percent) the weekly window must have reached before its routine reset to be
    /// worth a notification. Defaults to 5%.
    /// </summary>
    public int ScheduledResetMinPercent { get; set; } = DefaultScheduledResetMinPercent;

    /// <summary>Which usage window the tray displays: "5h", "7d", or "extra".</summary>
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

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClaudeTray", "settings.json");

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

    public void Save()
    {
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
        if (string.IsNullOrWhiteSpace(ClaudeCodeDirectory))
            ClaudeCodeDirectory = DefaultDirectory;
        if (Array.IndexOf(ValidMetrics, Metric) < 0)
            Metric = DefaultMetric;
        if (!L.IsValidPreference(Language))
            Language = DefaultLanguage;
    }
}
