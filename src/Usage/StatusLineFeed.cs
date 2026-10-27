using System.Text.Json;

namespace ClaudeTray;

/// <summary>
/// The reading Claude Code already hands its status-line command, kept so a poll can be skipped (T390).
///
/// <para><b>What the payload is.</b> For a Pro or Max login, the JSON on the status-line command's stdin
/// carries <c>rate_limits.five_hour</c> and <c>rate_limits.seven_day</c>, each a <c>used_percentage</c> from 0
/// to 100 and a <c>resets_at</c> in unix seconds — the two numbers <see cref="ApiClient"/> spends a Haiku
/// call to read. It runs on Claude Code's own events (300ms debounce), so while somebody is working it is
/// fresher than any poll, and while nobody is it stops.</para>
///
/// <para><b>Two processes, one writer per file.</b> <c>--statusline</c> runs on every status refresh and
/// exits; it writes only <see cref="PathFor"/>, one small file per config dir, overwritten whole. The
/// tray reads that file on its tick and, when <see cref="Substitute"/> allows, treats it as the poll —
/// so <c>usage-history.jsonl</c> keeps one writer at poll cadence instead of gaining a line per keystroke.
/// Keyed by config dir rather than by account because the status-line process knows only the
/// <c>CLAUDE_CONFIG_DIR</c> it inherited, and reading <c>.claude.json</c> for the account on every refresh
/// is cost the status bar would feel.</para>
///
/// <para><b>Why the poll stays.</b> The payload has no overage figure and no per-window status, appears
/// only after a session's first response, and drops a window once it resets. So a status-line reading
/// stands in for the API only inside the quota, well short of the limit, while the last real reading is
/// recent — and everything it cannot carry is carried from that reading rather than invented.</para>
/// </summary>
internal static class StatusLineFeed
{
    /// <summary>One status-line reading. Utilizations 0–1, times unix seconds, as in
    /// <see cref="UsageSample"/>.</summary>
    internal readonly record struct Reading(double T, double Util5h, double Reset5h, double Util7d, double Reset7d);

    /// <summary>Where the API's own warning starts: <c>5h-surpassed-threshold</c> arrived as 0.9 beside the
    /// first <c>allowed_warning</c> (T281). From here on the statuses only the API sends are the news, so a
    /// reading at or past it is fetched rather than substituted.</summary>
    public const double NearLimit = 0.9;

    /// <summary>How old the last real API reading may be before one is fetched regardless. Everything a
    /// substitute carries — overage, statuses — comes from that reading, and an hour is as long as a
    /// carried "not spending" is allowed to stand unasked.</summary>
    public const double ApiAtLeastEverySeconds = 3600;

    /// <summary>The feed file for one config dir — the same digest a profile without an account is keyed
    /// by, so the two cannot disagree about which directory is which.</summary>
    public static string PathFor(string configDir) =>
        Path.Combine(Settings.DataDir, "statusline", ProfileStore.DirKeyFor(configDir) + ".json");

    /// <summary>The two windows out of a status-line payload, or null when either is absent — before a
    /// session's first response, on an API-key login, or after one window has reset and been dropped. A
    /// reading with one window cannot stand in for a poll that reads two.</summary>
    public static Reading? Parse(string json, double now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("rate_limits", out JsonElement limits)
                || limits.ValueKind != JsonValueKind.Object)
                return null;
            if (Window(limits, "five_hour") is not { } h5 || Window(limits, "seven_day") is not { } d7)
                return null;
            return new Reading(now, h5.Util, h5.Reset, d7.Util, d7.Reset);
        }
        catch (JsonException) { return null; }
    }

    private static (double Util, double Reset)? Window(JsonElement limits, string name)
    {
        if (!limits.TryGetProperty(name, out JsonElement w) || w.ValueKind != JsonValueKind.Object) return null;
        if (!w.TryGetProperty("used_percentage", out JsonElement pct) || pct.ValueKind != JsonValueKind.Number) return null;
        if (!w.TryGetProperty("resets_at", out JsonElement at) || at.ValueKind != JsonValueKind.Number) return null;
        return (pct.GetDouble() / 100.0, at.GetDouble());
    }

    /// <summary>Overwrite one config dir's feed file. Through a temp file and a move, so the tray never
    /// reads half a line; best-effort, because a status bar must print whatever happens here.</summary>
    public static void Write(string configDir, Reading r)
    {
        // T239: an observing process reads the store and adds nothing to it.
        if (ProfileStore.Observing) return;
        try
        {
            string path = PathFor(configDir);
            StoreFile.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + "." + Environment.ProcessId + ".tmp";
            StoreFile.WriteAllText(tmp, FormattableString.Invariant(
                $"{{\"t\":{(long)r.T},\"u5\":{r.Util5h:0.####},\"r5\":{(long)r.Reset5h},\"u7\":{r.Util7d:0.####},\"r7\":{(long)r.Reset7d}}}"));
            StoreFile.Move(tmp, path, overwrite: true);
        }
        catch { /* the line still prints; the tray polls as it always did */ }
    }

    /// <summary>The newest reading written for one config dir, or null when there is none.</summary>
    public static Reading? Read(string configDir)
    {
        try
        {
            string path = PathFor(configDir);
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement o = doc.RootElement;
            var r = new Reading(Num(o, "t"), Num(o, "u5"), Num(o, "r5"), Num(o, "u7"), Num(o, "r7"));
            return r.T > 0 ? r : null;
        }
        catch { return null; }
    }

    private static double Num(JsonElement o, string name) =>
        o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    /// <summary>
    /// The reading a poll may take instead of calling the API, or null when it must call.
    ///
    /// <para>Every condition is a thing the payload cannot say: that the account is not spending past its
    /// quota, that no window is near the limit the API warns at, that the statuses carried over are still
    /// the ones it would send. The result is the last API reading with the four numbers replaced — never a
    /// reading assembled from defaults — and marked <see cref="UsageData.FromStatusLine"/> so the stores
    /// can refuse to write what it only carries.</para>
    /// </summary>
    /// <param name="maxAgeSeconds">How young the status-line reading must be: the poll interval, so a
    /// reading older than the tick it replaces is never taken for it.</param>
    public static UsageData? Substitute(Reading? feed, UsageData? lastApi, double lastApiUnix, double now,
                                        double maxAgeSeconds)
    {
        if (feed is not { } r || lastApi is not { Error: null } api) return null;
        if (now - r.T < 0 || now - r.T >= maxAgeSeconds) return null;
        if (now - lastApiUnix >= ApiAtLeastEverySeconds) return null;
        if (QuotaStates.Spending(api.ExtraUtil, api.ExtraInUse)) return null;
        if (api.Status != "allowed" || api.Status7d != "allowed") return null;
        if (r.Util5h >= NearLimit || r.Util7d >= NearLimit) return null;
        // A reset already in the past is a window Claude Code would have dropped; its number is last cycle's.
        if (r.Reset5h <= now || r.Reset7d <= now) return null;

        return new UsageData
        {
            Session5h = r.Util5h, Reset5h = r.Reset5h,
            Week7d = r.Util7d, Reset7d = r.Reset7d,
            Status = api.Status, Status7d = api.Status7d,
            Extra = api.Extra, HasExtra = api.HasExtra, ResetExtra = api.ResetExtra,
            StatusExtra = api.StatusExtra, ExtraDisabledReason = api.ExtraDisabledReason,
            ExtraInUse = api.ExtraInUse,
            FromStatusLine = true,
        };
    }

    /// <summary>What the status bar shows: both windows, in the tray's own words and number style.</summary>
    public static string Line(Reading r) =>
        $"{L.T("menu.metric.5h")} {Nums.Pct(r.Util5h)} · {L.T("menu.metric.7d")} {Nums.Pct(r.Util7d)}";
}
