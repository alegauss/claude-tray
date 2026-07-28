using System.Text.Json;

namespace ClaudeTray;

/// <summary>One logged rate-limit reading: the live utilizations and reset deadlines captured at a
/// single poll. Utilizations are 0–1; times are unix seconds.</summary>
internal readonly record struct UsageSample(double T, double Util5h, double Reset5h, double Util7d, double Reset7d);

/// <summary>
/// A tiny append-only log of the live rate-limit readings, written once per successful poll to
/// <c>%LocalAppData%\ClaudeTray\usage-history.jsonl</c>. It exists so the Statistics burn-up charts
/// can draw the <em>real</em> utilization measured over time, instead of inferring the shape from
/// transcript token counts (which weighs cache reads and models wrong against the limit).
///
/// One compact JSON object per line, e.g. <c>{"t":1719900000,"u5":0.72,"r5":...,"u7":0.38,"r7":...}</c>.
/// At the default 5-minute cadence this is ~230 KB per week; growth is bounded by age-based pruning
/// (older than <see cref="RetentionDays"/> days), triggered lazily so we don't rewrite every poll.
/// Every operation is best-effort: a failure here must never disrupt a poll or the report.
/// </summary>
internal static class UsageHistory
{
    private const int RetentionDays = 8;   // keep just past the 7-day window, so the weekly chart is covered
    private const int TriggerDays = 9;     // only rewrite the file once the oldest line is this stale

    private const double RetentionSeconds = RetentionDays * 86400.0;
    private const double TriggerSeconds = TriggerDays * 86400.0;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClaudeTray", "usage-history.jsonl");

    /// <summary>Append one reading. Best-effort; prunes stale lines lazily.</summary>
    public static void Append(long nowUnix, double util5h, double reset5h, double util7d, double reset7d)
    {
        try
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string line = FormattableString.Invariant(
                $"{{\"t\":{nowUnix},\"u5\":{util5h:0.####},\"r5\":{(long)reset5h},\"u7\":{util7d:0.####},\"r7\":{(long)reset7d}}}");
            File.AppendAllText(path, line + Environment.NewLine);
            PruneIfStale(path, nowUnix);
        }
        catch { /* logging is best-effort */ }
    }

    /// <summary>The most recent logged reading, or null when the log is empty/unreadable. Lets the
    /// Statistics window reconstruct a snapshot and draw its charts from local history when there's no
    /// live reading (signed out / expired token at launch), instead of a blank "connect" hint.</summary>
    public static UsageSample? Latest()
    {
        UsageSample? latest = null;
        try
        {
            string path = FilePath;
            if (!File.Exists(path)) return null;

            foreach (string line in File.ReadLines(path))
            {
                if (line.Length == 0) continue;
                // Lines are appended in time order, but a stray out-of-order line shouldn't win — keep
                // the newest by timestamp rather than blindly taking the last line.
                if (TryParse(line, out UsageSample s) && (latest is not { } l || s.T >= l.T))
                    latest = s;
            }
        }
        catch { /* a partial/locked read just means no fallback — the window shows its hint */ }

        return latest;
    }

    /// <summary>Read every logged sample at or after <paramref name="sinceUnix"/>, oldest first.
    /// Read-only — never rewrites, so it can run off-thread while the poll appends.</summary>
    public static List<UsageSample> Load(double sinceUnix)
    {
        var list = new List<UsageSample>();
        try
        {
            string path = FilePath;
            if (!File.Exists(path)) return list;

            foreach (string line in File.ReadLines(path))
            {
                if (line.Length == 0) continue;
                if (TryParse(line, out UsageSample s) && s.T >= sinceUnix)
                    list.Add(s);
            }
        }
        catch { /* a partial/locked read just yields fewer points — the chart falls back */ }

        list.Sort((a, b) => a.T.CompareTo(b.T));
        return list;
    }

    // Drop lines older than the retention window, but only once the oldest line is comfortably stale,
    // so at a steady state this rewrites at most about once a day rather than on every poll.
    private static void PruneIfStale(string path, double nowUnix)
    {
        string? first = null;
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0) continue;
            first = line;
            break;
        }
        if (first == null || !TryGetT(first, out double oldest)) return;
        if (nowUnix - oldest <= TriggerSeconds) return;

        // Nothing is thrown away before it has been counted: every complete day still in the log is
        // folded into the permanent hourly aggregate first (HourlyUsage), which is what lets idle be
        // measured rather than inferred once these lines are gone. Already-folded days are skipped
        // there, so running this on the same lines twice is harmless.
        var all = new List<UsageSample>();
        foreach (string line in File.ReadLines(path))
            if (line.Length > 0 && TryParse(line, out UsageSample s))
                all.Add(s);
        HourlyUsage.Fold(all, (long)nowUnix);

        double keepFrom = nowUnix - RetentionSeconds;
        var kept = new List<string>();
        foreach (string line in File.ReadLines(path))
            if (line.Length > 0 && TryGetT(line, out double t) && t >= keepFrom)
                kept.Add(line);

        string tmp = path + ".tmp";
        File.WriteAllLines(tmp, kept);
        File.Move(tmp, path, overwrite: true);
    }

    private static bool TryParse(string line, out UsageSample s)
    {
        s = default;
        try
        {
            using var doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;
            s = new UsageSample(
                Num(root, "t"), Num(root, "u5"), Num(root, "r5"), Num(root, "u7"), Num(root, "r7"));
            return s.T > 0;
        }
        catch { return false; }
    }

    private static bool TryGetT(string line, out double t)
    {
        t = 0;
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("t", out var v) && v.ValueKind == JsonValueKind.Number)
            {
                t = v.GetDouble();
                return t > 0;
            }
        }
        catch { /* skip unparseable line */ }
        return false;
    }

    private static double Num(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
