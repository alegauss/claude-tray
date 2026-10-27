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

    /// <summary>How far two reset stamps may drift and still name one cycle — a reset header moves by
    /// seconds between responses, and two different cycles are hours apart.</summary>
    public const double SameCycleSeconds = 300;

    /// <summary>The statuses a substitute may carry from the last API reading (T405).</summary>
    private static bool Carriable(string status) => status is "allowed" or "allowed_warning";

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
        // `allowed_warning` is carried too (T405): the API sends it from 75% of the week, and refusing it
        // meant the status line saved nothing for exactly the accounts close to their limit — measured on
        // 2026-10-27, a fresh reading at 78% of the week and every tick still calling the API. It stays true
        // while the window is the same cycle, which the reset check below holds; `rejected` never is.
        if (!Carriable(api.Status) || !Carriable(api.Status7d)) return null;
        if (r.Util5h >= NearLimit || r.Util7d >= NearLimit) return null;
        // A reset already in the past is a window Claude Code would have dropped; its number is last cycle's.
        if (r.Reset5h <= now || r.Reset7d <= now) return null;
        // And the same cycles as the API reading the statuses are carried from — a window that reset since
        // has statuses nobody has read yet.
        if (Math.Abs(r.Reset5h - api.Reset5h) > SameCycleSeconds || Math.Abs(r.Reset7d - api.Reset7d) > SameCycleSeconds)
            return null;

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

    /// <summary>
    /// The <c>statusLine</c> member a person pastes into a profile's <c>settings.json</c> — shown and copied,
    /// never written (§I.4). Composed by the serializer, so a path's quotes and escapes are JSON's rather
    /// than hand-typed; forward slashes, because Claude Code runs the command through a shell that may be
    /// Git Bash, and quoted only where a space would split it.
    /// </summary>
    public static string Snippet(string exePath)
    {
        string exe = exePath.Replace('\\', '/');
        string command = (exe.Contains(' ') ? $"\"{exe}\"" : exe) + " --statusline";
        string body = JsonSerializer.Serialize(new { type = "command", command },
                                               new JsonSerializerOptions
                                               {
                                                   WriteIndented = true,
                                                   // The default encoder writes a quote as ": valid,
                                                   // and unreadable on a page meant to be read.
                                                   Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                                               });
        return "\"statusLine\": " + body.Replace("\r\n", "\n");
    }

    /// <summary>What a profile's <c>settings.json</c> says about the status line (T404).</summary>
    internal enum Setup { Missing, Ours, Other }

    /// <summary>
    /// Whether a profile already runs this app's status line — read, never written (§I.4), so the Settings
    /// row can say it is done instead of asking for a paste that already happened. Ours is any command that
    /// runs ClaudeTray with <c>--statusline</c>, wherever the exe lives; anything else is somebody's own
    /// status line, which pasting would replace. A file that cannot be read says Missing.
    /// </summary>
    public static Setup SetupIn(string configDir)
    {
        try
        {
            string path = Path.Combine(configDir, "settings.json");
            return File.Exists(path) ? Classify(File.ReadAllText(path)) : Setup.Missing;
        }
        catch { return Setup.Missing; }
    }

    /// <summary>The same answer from a settings file's text — pure, for <c>--selftest</c>. Comments and
    /// trailing commas are accepted, because a hand-edited settings file has both.</summary>
    internal static Setup Classify(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip,
            });
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("statusLine", out JsonElement sl)
                || sl.ValueKind != JsonValueKind.Object)
                return Setup.Missing;
            string command = sl.TryGetProperty("command", out JsonElement c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() ?? ""
                : "";
            return command.Contains("ClaudeTray", StringComparison.OrdinalIgnoreCase)
                   && command.Contains("--statusline", StringComparison.OrdinalIgnoreCase)
                ? Setup.Ours
                : Setup.Other;
        }
        catch (JsonException) { return Setup.Missing; }
    }

    /// <summary>What the status bar shows: both windows, in the tray's own words and number style.</summary>
    public static string Line(Reading r) =>
        $"{L.T("menu.metric.5h")} {Nums.Pct(r.Util5h)} · {L.T("menu.metric.7d")} {Nums.Pct(r.Util7d)}";
}
