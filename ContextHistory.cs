using System.Text.Json;

namespace ClaudeTray;

/// <summary>One recorded measurement of a project's eager context: unix seconds, tokens, bytes.</summary>
internal readonly record struct ContextSample(long T, int Eager, long Bytes);

/// <summary>
/// A project's drift: what its eager context is now, what it was a week ago, and the shape in between.
/// </summary>
/// <param name="Points">Chronological samples inside the window, for the sparkline.</param>
/// <param name="DeltaTokens">Change against the oldest sample at least a week old (0 if there isn't one).</param>
internal sealed record ContextTrend(
    List<ContextSample> Points, int DeltaTokens, long DeltaBytes, bool HasBaseline, int SpanDays)
{
    public int Current => Points.Count > 0 ? Points[^1].Eager : 0;
    /// <summary>Enough distinct samples to draw a line rather than a dot.</summary>
    public bool CanDraw => Points.Count >= 2;
}

/// <summary>
/// An append-only log of each project's eager context over time, written to
/// <c>%LocalAppData%\ClaudeTray\context-history.jsonl</c> — the same shape and the same best-effort
/// discipline as <see cref="UsageHistory"/>.
///
/// It exists because bloat does not arrive in one lump. A memory here, a paragraph in `AGENTS.md`
/// there, and six weeks later every session starts 4k tokens heavier with nobody able to say when it
/// happened. A single measurement cannot show that; only the trend can.
///
/// Sampling is deliberately coarse — at most one line per project per day, and only when the number
/// actually moved. Weekly drift is what matters, so a per-minute series would be noise plus a large
/// file. One compact object per line: <c>{"t":…,"s":"slug","e":7412,"b":123456}</c>.
/// </summary>
internal static class ContextHistory
{
    /// <summary>Six months is plenty to see a trend and still bounded; pruned lazily on append.</summary>
    private const int RetentionDays = 180;
    private const int TriggerDays = 200;

    /// <summary>The drift window the UI reports on ("this week").</summary>
    public const int WeekDays = 7;

    /// <summary>How far back the sparkline reaches.</summary>
    public const int SparkDays = 60;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClaudeTray", "context-history.jsonl");

    /// <summary>
    /// Record every project's eager context, skipping the ones already recorded today at the same
    /// value. Best-effort: a failure here must never disturb a scan.
    /// </summary>
    public static void Record(ContextScan scan, DateTime nowUtc)
    {
        try
        {
            long now = new DateTimeOffset(nowUtc, TimeSpan.Zero).ToUnixTimeSeconds();
            Dictionary<string, ContextSample> latest = LatestPerSlug();
            var lines = new List<string>();

            foreach (ContextProject p in scan.Projects)
            {
                int eager = scan.EstimatedSessionZero(p);
                long bytes = p.Bytes;

                // Only when something moved: the file is a record of change, not a heartbeat. The
                // same-day check keeps a day of repeated opens from writing a line each time even if
                // the numbers wobble.
                if (latest.TryGetValue(p.Slug, out ContextSample prev) &&
                    (prev.Eager == eager && prev.Bytes == bytes || SameDay(prev.T, now)))
                    continue;

                lines.Add(FormattableString.Invariant(
                    $"{{\"t\":{now},\"s\":{JsonSerializer.Serialize(p.Slug)},\"e\":{eager},\"b\":{bytes}}}"));
            }

            if (lines.Count == 0) return;
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllLines(path, lines);
            PruneIfStale(path, now);
        }
        catch { /* history is best-effort */ }
    }

    /// <summary>The drift for one project, or null when nothing has been recorded for it yet.</summary>
    public static ContextTrend? Trend(string slug, DateTime nowUtc)
    {
        try
        {
            long now = new DateTimeOffset(nowUtc, TimeSpan.Zero).ToUnixTimeSeconds();
            long from = now - SparkDays * 86400L;

            var points = new List<ContextSample>();
            foreach ((string s, ContextSample sample) in Read())
                if (sample.T >= from && string.Equals(s, slug, StringComparison.OrdinalIgnoreCase))
                    points.Add(sample);
            if (points.Count == 0) return null;

            points.Sort((a, b) => a.T.CompareTo(b.T));
            return Build(points, now);
        }
        catch { return null; }
    }

    /// <summary>
    /// Build a trend from samples already filtered to one project. The baseline is the newest sample
    /// that is still at least a week old, so "this week" means the last seven days rather than
    /// "since whenever the first sample happens to be".
    /// </summary>
    internal static ContextTrend Build(List<ContextSample> points, long now)
    {
        long weekAgo = now - WeekDays * 86400L;
        ContextSample? baseline = null;
        foreach (ContextSample s in points)
            if (s.T <= weekAgo) baseline = s;

        ContextSample newest = points[^1];
        int spanDays = (int)Math.Round((newest.T - points[0].T) / 86400.0);

        return baseline is { } b
            ? new ContextTrend(points, newest.Eager - b.Eager, newest.Bytes - b.Bytes, true, spanDays)
            : new ContextTrend(points, 0, 0, false, spanDays);
    }

    /// <summary>
    /// A synthetic trend for previews: a project that gained a little context most weeks. Used by the
    /// screenshot path only — real drift takes weeks to accumulate, and a feature that cannot be
    /// looked at cannot be verified.
    /// </summary>
    internal static ContextTrend Demo(int currentEager, long currentBytes, long now)
    {
        var points = new List<ContextSample>();
        int[] steps = { 0, 180, 190, 640, 700, 705, 1_150, 1_400, 1_980, 2_040, 2_600 };
        for (int i = 0; i < steps.Length; i++)
        {
            long t = now - (steps.Length - 1 - i) * 5 * 86400L;   // one sample every five days
            int eager = currentEager - (steps[^1] - steps[i]);
            long bytes = currentBytes - (steps[^1] - steps[i]) * 4L;
            points.Add(new ContextSample(t, Math.Max(0, eager), Math.Max(0, bytes)));
        }
        return Build(points, now);
    }

    // ---------------------------------------------------------------- storage

    // Slug and sample are yielded together rather than paired through a side table keyed by the
    // sample: two worktree siblings can legitimately record the same tokens and bytes in the same
    // second, and a value-keyed map would silently merge them.
    private static IEnumerable<(string Slug, ContextSample Sample)> Read()
    {
        string path = FilePath;
        if (!File.Exists(path)) yield break;

        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0) continue;
            ContextSample sample;
            string slug;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement r = doc.RootElement;
                sample = new ContextSample(
                    r.TryGetProperty("t", out JsonElement t) ? t.GetInt64() : 0,
                    r.TryGetProperty("e", out JsonElement e) ? e.GetInt32() : 0,
                    r.TryGetProperty("b", out JsonElement b) ? b.GetInt64() : 0);
                slug = r.TryGetProperty("s", out JsonElement s) ? s.GetString() ?? "" : "";
            }
            catch { continue; }   // a truncated line costs one sample, nothing more

            if (sample.T == 0 || slug.Length == 0) continue;
            yield return (slug, sample);
        }
    }

    private static Dictionary<string, ContextSample> LatestPerSlug()
    {
        var latest = new Dictionary<string, ContextSample>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach ((string slug, ContextSample s) in Read())
                if (!latest.TryGetValue(slug, out ContextSample prev) || s.T >= prev.T)
                    latest[slug] = s;
        }
        catch { /* an unreadable history just means everything looks new */ }
        return latest;
    }

    private static bool SameDay(long a, long b)
        => DateTimeOffset.FromUnixTimeSeconds(a).UtcDateTime.Date ==
           DateTimeOffset.FromUnixTimeSeconds(b).UtcDateTime.Date;

    // Rewrite only once the oldest line is well past retention, so a normal append stays one write.
    private static void PruneIfStale(string path, long now)
    {
        try
        {
            var kept = new List<string>();
            bool stale = false;
            long cutoff = now - RetentionDays * 86400L;
            long trigger = now - TriggerDays * 86400L;

            foreach (string line in File.ReadLines(path))
            {
                if (line.Length == 0) continue;
                long t;
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(line);
                    t = doc.RootElement.TryGetProperty("t", out JsonElement v) ? v.GetInt64() : 0;
                }
                catch { continue; }

                if (t < trigger) stale = true;
                if (t >= cutoff) kept.Add(line);
            }

            if (stale) File.WriteAllLines(path, kept);
        }
        catch { /* pruning is best-effort */ }
    }
}
