using System.Text;
using System.Text.Json;

namespace ClaudeTray;

/// <summary>One local day of folded readings: 24 hourly quota spends and how many readings covered
/// each hour. <paramref name="Key"/> is the local date as <c>yyyyMMdd</c>.</summary>
internal readonly record struct HourlyDay(int Key, double[] Spend, int[] Count)
{
    public DateTime Date => new(Key / 10000, Key / 100 % 100, Key % 100);

    /// <summary>Share of the weekly window spent across the whole day.</summary>
    public double DaySpend { get { double s = 0; foreach (double v in Spend) s += v; return s; } }

    /// <summary>Hours with at least one reading — coverage, not activity.</summary>
    public int Covered { get { int n = 0; foreach (int c in Count) if (c > 0) n++; return n; } }
}

/// <summary>
/// A permanent, tiny per-hour summary of quota actually spent, folded out of
/// <see cref="UsageHistory"/> before its raw lines are discarded.
///
/// <para>The raw log is pruned at 8 days, so everything older than a week is currently thrown away —
/// which is why <see cref="ActivityProfile"/> has to <em>infer</em> idle from transcript timestamps
/// rather than read it from what the limit actually recorded. Folding each expiring day into 24
/// numbers first costs about 168 floats a week, nothing next to the ~230 KB/week raw log, and turns
/// idle into something measured: an hour with readings and no spend was genuinely idle, an hour with
/// no readings is unknown (the app was off), and the two are no longer the same thing.</para>
///
/// <para>Spend is the sum of positive weekly-utilization deltas between consecutive readings,
/// attributed to the hour of the later reading. A drop in utilization is a window reset, not negative
/// work, so it contributes nothing. Coverage is counted separately precisely so a gap can't read as a
/// quiet hour.</para>
///
/// <para>One compact object per local day: <c>{"d":20260720,"s":[…24…],"c":[…24…]}</c>. Best-effort
/// throughout — a failure here must never disturb a poll.</para>
/// </summary>
internal static class HourlyUsage
{
    /// <summary>Two years of days is ~200 KB and still bounded; the point is to outlive the raw log
    /// by orders of magnitude, not literally forever.</summary>
    public const int RetentionDays = 730;

    /// <summary>An hour that moved the weekly window by at least this much was worked in. 0.1% of a
    /// week's quota is a couple of real requests — below it, a stray keepalive shouldn't count.</summary>
    public const double ActiveSpendThreshold = 0.001;

    /// <summary>Per profile (T125) — see <see cref="ProfileStore"/>. This store is permanent, so a
    /// second account folded into it would skew the measured week for good.</summary>
    public static string FilePath(string profileKey) =>
        ProfileStore.PathFor(profileKey, "usage-hourly.jsonl");

    // ---------------------------------------------------------------- folding

    /// <summary>
    /// Fold every <em>complete</em> local day in <paramref name="samples"/> into the permanent store,
    /// skipping days already folded. Called from <see cref="UsageHistory"/> just before it prunes, so
    /// no reading is discarded without being counted first. Today is deliberately left alone: it is
    /// still being written, and a half-day folded now could never be completed.
    /// </summary>
    public static void Fold(string profileKey, List<UsageSample> samples, long nowUnix)
    {
        try
        {
            if (samples.Count < 2) return;

            int today = KeyOf(Local(nowUnix));
            Dictionary<int, HourlyDay> existing = ReadAll(profileKey);
            var built = new Dictionary<int, HourlyDay>();

            samples.Sort((a, b) => a.T.CompareTo(b.T));
            for (int i = 1; i < samples.Count; i++)
            {
                UsageSample prev = samples[i - 1], cur = samples[i];
                DateTime at = Local(cur.T);
                int key = KeyOf(at);

                // Only whole days that aren't already in the store. A day is never re-folded, so a
                // second pass over the same raw lines cannot double-count it.
                if (key >= today || existing.ContainsKey(key)) continue;

                if (!built.TryGetValue(key, out HourlyDay day))
                    built[key] = day = new HourlyDay(key, new double[24], new int[24]);

                day.Count[at.Hour]++;

                // A reset (utilization dropping, or a new reset deadline) ends the accounting for that
                // step: the two readings belong to different windows and their difference is not work.
                bool sameWindow = Math.Abs(cur.Reset7d - prev.Reset7d) < 120;
                double delta = cur.Util7d - prev.Util7d;
                if (sameWindow && delta > 0) day.Spend[at.Hour] += delta;
            }

            if (built.Count == 0) return;

            foreach (HourlyDay d in built.Values) existing[d.Key] = d;
            WriteAll(profileKey, existing, nowUnix);
        }
        catch { /* the aggregate is best-effort; the next prune will try again */ }
    }

    // ---------------------------------------------------------------- reading

    /// <summary>Every folded day at or after <paramref name="fromKey"/> (<c>yyyyMMdd</c>), oldest first.</summary>
    public static List<HourlyDay> Load(string profileKey, int fromKey = 0)
    {
        var list = new List<HourlyDay>();
        foreach (HourlyDay d in ReadAll(profileKey).Values)
            if (d.Key >= fromKey) list.Add(d);
        list.Sort((a, b) => a.Key.CompareTo(b.Key));
        return list;
    }

    /// <summary>
    /// The same 168-bucket week as <see cref="ActivityProfile"/>, but built from what the rate limit
    /// actually recorded rather than from transcript timestamps — the independent second opinion the
    /// inferred grid can be checked against.
    ///
    /// An hour counts as active only if it was <em>covered</em> (at least one reading) and spent at
    /// least <see cref="ActiveSpendThreshold"/>; uncovered hours are left out of the denominator
    /// entirely, so days the app was closed dilute nothing.
    /// </summary>
    /// <returns>The grid, the number of weeks it draws on, and how many folded days were used.</returns>
    public static (double[] P, double Weeks, int Days) MeasuredProfile(string profileKey, DateTime nowLocal)
    {
        var p = new double[ActivityProfile.Buckets];
        var active = new double[ActivityProfile.Buckets];
        var observed = new double[ActivityProfile.Buckets];

        List<HourlyDay> days = Load(profileKey);
        if (days.Count == 0) return (p, 0, 0);

        int used = 0;
        double oldest = 0;
        foreach (HourlyDay d in days)
        {
            bool counted = false;
            for (int h = 0; h < 24; h++)
            {
                if (d.Count[h] == 0) continue;   // no reading: unknown, not idle

                DateTime at = d.Date.AddHours(h);
                int week = (int)((nowLocal - at).TotalDays / 7);
                if (week < 0 || week >= ActivityProfile.MaxWeeks) continue;

                double weight = Math.Pow(ActivityProfile.WeekDecay, week);
                int b = ActivityProfile.Index(at.DayOfWeek, h);
                observed[b] += weight;
                if (d.Spend[h] >= ActiveSpendThreshold) active[b] += weight;

                counted = true;
                oldest = Math.Max(oldest, (nowLocal - at).TotalDays);
            }
            if (counted) used++;
        }

        for (int b = 0; b < ActivityProfile.Buckets; b++)
            p[b] = observed[b] > 0 ? active[b] / observed[b] : 0;

        return (p, Math.Min(oldest / 7.0, ActivityProfile.MaxWeeks), used);
    }

    /// <summary>Last week's burn-up, ready to draw behind this week's.</summary>
    /// <param name="Curve">(fraction of the window, cumulative utilization), hour by hour.</param>
    /// <param name="Coverage">Share of that week's hours that had at least one reading.</param>
    /// <param name="AtSameFraction">Where it stood at the same point in the window as now.</param>
    internal sealed record GhostWeek(List<(double frac, double cum)> Curve, double Coverage,
        double AtSameFraction, double Total);

    /// <summary>Enough of the previous week must have been observed for its curve to mean anything;
    /// below this the line would mostly be flat stretches the app simply wasn't there for.</summary>
    public const double MinGhostCoverage = 0.5;

    /// <summary>A week that barely moved is a flat line at zero — true, but not worth drawing.</summary>
    public const double MinGhostTotal = 0.02;

    /// <summary>
    /// Rebuild the <em>previous</em> weekly window's burn-up from the folded aggregate: the seven days
    /// immediately before <paramref name="windowStartUnix"/>, accumulated hour by hour and expressed
    /// in the same (fraction, cumulative) space as <see cref="WindowPace.Curve"/> so the chart can
    /// draw it with the same transform.
    ///
    /// Returns null when too little of that week was observed, or when it holds almost nothing — a
    /// ghost that is really a record of the app being closed would read as a quiet week, which is the
    /// one thing it must not do.
    /// </summary>
    public static GhostWeek? PreviousWeek(string profileKey, double windowStartUnix, double windowSeconds, double nowFraction)
    {
        try
        {
            DateTime from = Local(windowStartUnix - windowSeconds);
            int hours = (int)Math.Round(windowSeconds / 3600.0);
            if (hours is < 24 or > 24 * 14) return null;

            Dictionary<int, HourlyDay> store = ReadAll(profileKey);
            if (store.Count == 0) return null;

            var curve = new List<(double, double)> { (0, 0) };
            double cum = 0, atSame = 0;
            int covered = 0;

            for (int i = 0; i < hours; i++)
            {
                DateTime at = from.AddHours(i);
                if (store.TryGetValue(KeyOf(at), out HourlyDay day))
                {
                    if (day.Count[at.Hour] > 0) covered++;
                    cum += day.Spend[at.Hour];
                }
                double frac = (i + 1) / (double)hours;
                if (frac <= nowFraction) atSame = cum;
                curve.Add((frac, Math.Min(1, cum)));
            }

            double coverage = covered / (double)hours;
            if (coverage < MinGhostCoverage || cum < MinGhostTotal) return null;
            return new GhostWeek(curve, coverage, atSame, Math.Min(1, cum));
        }
        catch { return null; }   // the ghost is decoration; never fail a report over it
    }

    /// <summary>
    /// A synthetic previous week for previews: quota spent through working hours and flat overnight,
    /// finishing a little under the current one. Used by the screenshot path only — a real ghost needs
    /// two weeks of folded history, and a feature that cannot be looked at cannot be verified (same
    /// reason <see cref="ContextHistory.Demo"/> exists).
    /// </summary>
    public static GhostWeek Demo(DateTime windowStartLocal, double windowSeconds, double nowFraction, double total)
    {
        int hours = (int)Math.Round(windowSeconds / 3600.0);
        var weight = new double[hours];
        double sum = 0;
        for (int i = 0; i < hours; i++)
        {
            DateTime at = windowStartLocal.AddHours(i);
            bool working = at.Hour is >= 9 and <= 23;
            weight[i] = !working ? 0.02 : at.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 0.35 : 1.0;
            sum += weight[i];
        }

        var curve = new List<(double, double)> { (0, 0) };
        double cum = 0, atSame = 0;
        for (int i = 0; i < hours; i++)
        {
            cum += total * weight[i] / sum;
            double frac = (i + 1) / (double)hours;
            if (frac <= nowFraction) atSame = cum;
            curve.Add((frac, cum));
        }
        return new GhostWeek(curve, 1, atSame, total);
    }

    // ---------------------------------------------------------------- storage

    private static Dictionary<int, HourlyDay> ReadAll(string profileKey)
    {
        var map = new Dictionary<int, HourlyDay>();
        try
        {
            string path = FilePath(profileKey);
            if (!File.Exists(path)) return map;

            foreach (string line in File.ReadLines(path))
            {
                if (line.Length == 0) continue;
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(line);
                    JsonElement r = doc.RootElement;
                    int key = r.TryGetProperty("d", out JsonElement d) ? d.GetInt32() : 0;
                    if (key <= 0) continue;

                    var spend = new double[24];
                    var count = new int[24];
                    ReadArray(r, "s", i => spend[i] = 0, (i, e) => spend[i] = e.GetDouble());
                    ReadArray(r, "c", i => count[i] = 0, (i, e) => count[i] = e.GetInt32());
                    map[key] = new HourlyDay(key, spend, count);
                }
                catch { /* one bad line costs one day, nothing more */ }
            }
        }
        catch { /* an unreadable store just looks empty */ }
        return map;
    }

    private static void ReadArray(JsonElement root, string name, Action<int> clear, Action<int, JsonElement> set)
    {
        if (!root.TryGetProperty(name, out JsonElement arr) || arr.ValueKind != JsonValueKind.Array) return;
        int i = 0;
        foreach (JsonElement e in arr.EnumerateArray())
        {
            if (i >= 24) break;
            clear(i);
            try { set(i, e); } catch { /* leave the hour at zero */ }
            i++;
        }
    }

    // Rewritten whole rather than appended: folding can add several days at once (a machine that was
    // off for a week), the file is a few hundred lines, and one atomic replace is easier to reason
    // about than an append plus a separate prune.
    private static void WriteAll(string profileKey, Dictionary<int, HourlyDay> days, long nowUnix)
    {
        int cutoff = KeyOf(Local(nowUnix).AddDays(-RetentionDays));
        var keys = new List<int>(days.Keys);
        keys.Sort();

        var sb = new StringBuilder(keys.Count * 300);
        foreach (int key in keys)
        {
            if (key < cutoff) continue;
            HourlyDay d = days[key];
            sb.Append(FormattableString.Invariant($"{{\"d\":{key},\"s\":["));
            for (int h = 0; h < 24; h++)
            {
                if (h > 0) sb.Append(',');
                sb.Append(FormattableString.Invariant($"{d.Spend[h]:0.#####}"));
            }
            sb.Append("],\"c\":[");
            for (int h = 0; h < 24; h++)
            {
                if (h > 0) sb.Append(',');
                sb.Append(d.Count[h].ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            sb.AppendLine("]}");
        }

        string path = FilePath(profileKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, sb.ToString());
        File.Move(tmp, path, overwrite: true);
    }

    private static DateTime Local(double unix)
        => DateTimeOffset.FromUnixTimeSeconds((long)unix).LocalDateTime;

    private static int KeyOf(DateTime local) => local.Year * 10000 + local.Month * 100 + local.Day;
}
