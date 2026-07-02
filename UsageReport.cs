using System.Text.Json;

namespace ClaudeTray;

/// <summary>
/// A pacing report for the two rate-limit windows — the 5-hour session and the 7-day week — that
/// answers the only question that matters for staying under the limit: <em>are you spending your
/// quota evenly, or burning through it faster than the clock, so it runs out before the window
/// resets?</em>
///
/// The authoritative numbers (how much of the window is used, and when it resets) come live from the
/// Anthropic rate-limit headers via the tray (<see cref="PaceSnapshot"/>). The <em>shape</em> of the
/// burn — whether consumption was front-loaded or spread out — is reconstructed from the local
/// transcripts' token timestamps (~/.claude/projects), scaled so the curve lands exactly on the live
/// utilization. Reads only timestamps and token counts — never message content.
/// </summary>
internal enum PaceVerdict
{
    Unknown,   // no live window data yet
    Adequate,  // usage is at or below the even-pace line — comfortably on track
    Ahead,     // usage is ahead of the even-pace line — burning faster than the clock
    AtLimit,   // window is effectively exhausted (≥ ~100%)
}

/// <summary>The pacing state of one rate-limit window (5h or 7d).</summary>
internal sealed class WindowPace
{
    public string Label = "";
    public double Util;              // live utilization, 0–1
    public double WindowSeconds;     // 5h or 7d, in seconds
    public double ElapsedSeconds;    // time since the window started
    public double SecondsToReset;    // time until it resets
    public double ResetUnix;         // reset deadline, unix seconds (0 = unknown)
    public bool HasWindow;           // false when the reset time is unknown
    public PaceVerdict Verdict = PaceVerdict.Unknown;
    public double ExhaustSeconds = double.PositiveInfinity; // seconds until 100% at the average pace
    public long TokensInWindow;
    public int RequestsInWindow;

    // Burn-up curve: x = fraction of the window elapsed (0–1), y = cumulative utilization (0–1).
    // Runs from (0,0) to (ElapsedFraction, Util).
    public List<(double frac, double cum)> Curve = new();

    public double ElapsedFraction => WindowSeconds > 0 ? Math.Clamp(ElapsedSeconds / WindowSeconds, 0, 1) : 0;

    // At the average pace so far, utilization would reach 100% at this fraction of the window.
    // ≤ 1 means it runs out before the reset.
    public double ExhaustFraction => Util > 0 ? ElapsedFraction / Util : double.PositiveInfinity;

    // The even-pace target for "now": the share of quota a perfectly even user would have spent.
    public double IdealNow => ElapsedFraction;
}

/// <summary>Live rate-limit reading passed in from the tray. Utilizations are 0–1; resets are unix
/// seconds (0 when unknown).</summary>
internal readonly record struct PaceSnapshot(double Util5h, double Reset5h, double Util7d, double Reset7d);

internal sealed class PaceReport
{
    public DateTime ComputedLocal;
    public WindowPace Session = new() { Label = "5-hour session", WindowSeconds = 5 * 3600 };
    public WindowPace Weekly = new() { Label = "Week (7 days)", WindowSeconds = 7 * 86400 };
    public string? Error;
}

internal static class UsageReport
{
    private static readonly string ProjectsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "projects");

    private const double SessionSeconds = 5 * 3600;
    private const double WeekSeconds = 7 * 86400;

    // A window counts as "ahead of pace" only once it clears the even-pace line by this margin, so a
    // reading that is marginally above the ideal isn't flagged as alarming.
    private const double AheadMargin = 0.05;

    /// <summary>Build the pacing report for both windows from the live snapshot plus the transcripts.</summary>
    public static PaceReport ComputePace(DateTime nowUtc, PaceSnapshot snap)
    {
        var r = new PaceReport { ComputedLocal = nowUtc.ToLocalTime() };
        try
        {
            double now = new DateTimeOffset(nowUtc, TimeSpan.Zero).ToUnixTimeSeconds();

            Fill(r.Session, snap.Util5h, snap.Reset5h, SessionSeconds, now);
            Fill(r.Weekly, snap.Util7d, snap.Reset7d, WeekSeconds, now);

            // Scan transcripts once, over the widest window we need, then shape each curve.
            double earliest = double.PositiveInfinity;
            if (r.Session.HasWindow) earliest = Math.Min(earliest, r.Session.ResetUnix - SessionSeconds);
            if (r.Weekly.HasWindow) earliest = Math.Min(earliest, r.Weekly.ResetUnix - WeekSeconds);

            if (!double.IsPositiveInfinity(earliest))
            {
                // Real measured utilizations (preferred), plus token samples as the shaping fallback.
                List<UsageSample> hist = UsageHistory.Load(earliest);
                List<(double t, long tok)> samples =
                    Directory.Exists(ProjectsDir) ? ScanTokens(earliest, now) : new();

                FillCurve(r.Session, samples, hist, s => (s.Util5h, s.Reset5h), now);
                FillCurve(r.Weekly, samples, hist, s => (s.Util7d, s.Reset7d), now);
            }

            return r;
        }
        catch (Exception e)
        {
            r.Error = e.Message;
            return r;
        }
    }

    private static void Fill(WindowPace w, double util, double reset, double windowSeconds, double now)
    {
        w.Util = Math.Clamp(util, 0, 1);
        w.WindowSeconds = windowSeconds;

        if (reset <= 0)
        {
            w.HasWindow = false;
            w.Verdict = PaceVerdict.Unknown;
            return;
        }

        w.HasWindow = true;
        w.ResetUnix = reset;
        w.SecondsToReset = Math.Max(0, reset - now);
        double start = reset - windowSeconds;
        w.ElapsedSeconds = Math.Clamp(now - start, 0, windowSeconds);

        w.Verdict = w.Util >= 0.995 ? PaceVerdict.AtLimit
            : w.Util > w.ElapsedFraction + AheadMargin ? PaceVerdict.Ahead
            : PaceVerdict.Adequate;

        // Seconds until 100% at the average pace since the window started (rule of three):
        // util reached in `elapsed` → 100% in elapsed / util, so the time left is elapsed*(1−util)/util.
        w.ExhaustSeconds = w.Util >= 0.995 ? 0
            : w.Util > 0 && w.ElapsedSeconds > 0 ? w.ElapsedSeconds * (1 - w.Util) / w.Util
            : double.PositiveInfinity;
    }

    // A logged reading belongs to "this" window only if its reset time matches, so a mid-window
    // reset cleanly drops the previous window's samples. Reset times are stable within a window.
    private const double ResetMatchTolerance = 120;
    // Below this many in-window readings the real curve is too coarse to beat the token shaping.
    private const int MinRealSamples = 2;

    // Shape a window's burn-up curve. Preferred: the real utilizations logged over this window
    // (<paramref name="hist"/>, selected by <paramref name="pick"/>). Fallback, when there aren't
    // enough logged points yet: the token samples, scaled so the curve lands on the live utilization.
    private static void FillCurve(WindowPace w, List<(double t, long tok)> samples,
        List<UsageSample> hist, Func<UsageSample, (double util, double reset)> pick, double now)
    {
        if (!w.HasWindow) return;
        double start = w.ResetUnix - w.WindowSeconds;

        var inWin = samples.Where(s => s.t >= start && s.t <= now).OrderBy(s => s.t).ToList();
        long total = 0;
        foreach (var s in inWin) total += s.tok;
        w.TokensInWindow = total;
        w.RequestsInWindow = inWin.Count;

        // Preferred path: the real measured utilization over this window.
        var real = new List<(double frac, double cum)>();
        foreach (UsageSample s in hist)
        {
            if (s.T < start || s.T > now) continue;
            var (util, reset) = pick(s);
            if (Math.Abs(reset - w.ResetUnix) > ResetMatchTolerance) continue;
            real.Add((Math.Clamp((s.T - start) / w.WindowSeconds, 0, 1), Math.Clamp(util, 0, 1)));
        }
        real.Sort((a, b) => a.frac.CompareTo(b.frac));

        if (real.Count >= MinRealSamples)
        {
            var realCurve = new List<(double, double)> { (0, 0) };
            realCurve.AddRange(real);
            // Land exactly on the live reading, which is fresher than the last logged sample.
            realCurve.Add((w.ElapsedFraction, w.Util));
            w.Curve = realCurve;
            return;
        }

        var curve = new List<(double, double)> { (0, 0) };
        if (total <= 0)
        {
            // No history of any kind to shape with — fall back to a straight line to the live level.
            curve.Add((w.ElapsedFraction, w.Util));
            w.Curve = curve;
            return;
        }

        long cum = 0;
        foreach (var s in inWin)
        {
            cum += s.tok;
            double frac = Math.Clamp((s.t - start) / w.WindowSeconds, 0, 1);
            double cu = w.Util * cum / total;
            curve.Add((frac, cu));
        }
        w.Curve = curve;
    }

    // Collect (unixTime, tokens) for every in-window assistant request with usage.
    private static List<(double t, long tok)> ScanTokens(double startUnix, double nowUnix)
    {
        var samples = new List<(double, long)>();
        DateTime cutoffUtc = DateTimeOffset.FromUnixTimeSeconds((long)startUnix).UtcDateTime;

        foreach (string file in Directory.EnumerateFiles(ProjectsDir, "*.jsonl", SearchOption.AllDirectories))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoffUtc) continue;

            foreach (string line in ReadLinesSafe(file))
            {
                if (line.Length == 0) continue;
                if (TryParseSample(line, startUnix, nowUnix, out double t, out long tok))
                    samples.Add((t, tok));
            }
        }
        return samples;
    }

    private static IEnumerable<string> ReadLinesSafe(string file)
    {
        try { return File.ReadLines(file); }
        catch { return Array.Empty<string>(); }
    }

    private static bool TryParseSample(string line, double startUnix, double nowUnix, out double t, out long tok)
    {
        t = 0; tok = 0;
        try
        {
            using var doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("type", out var ty) || ty.GetString() != "assistant")
                return false;

            if (!root.TryGetProperty("timestamp", out var ts) || ts.ValueKind != JsonValueKind.String ||
                !DateTime.TryParse(ts.GetString(), null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out DateTime whenUtc))
                return false;

            double u = new DateTimeOffset(whenUtc, TimeSpan.Zero).ToUnixTimeSeconds();
            if (u < startUnix || u > nowUnix) return false;

            if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
                return false;
            if (!msg.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
                return false;
            if (msg.TryGetProperty("model", out var m) && m.GetString() == "<synthetic>")
                return false;

            long tokens = (long)(Num(usage, "input_tokens") + Num(usage, "output_tokens")
                + Num(usage, "cache_creation_input_tokens") + Num(usage, "cache_read_input_tokens"));
            if (tokens <= 0) return false;

            t = u; tok = tokens;
            return true;
        }
        catch { return false; }
    }

    private static double Num(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : 0;
}
