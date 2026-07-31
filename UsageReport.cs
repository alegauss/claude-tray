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

    // Per-type token totals in this window, from the transcripts. Kept separate so the throughput
    // (tokens/sec) breakdown can show where the tokens went. Cache reads are tracked but excluded
    // from the headline rate — they're huge (the whole context re-read each turn) and barely weigh
    // on the rate limit, so folding them in would drown out the real work.
    public long InputTokens;
    public long OutputTokens;
    public long CacheCreationTokens;
    public long CacheReadTokens;

    // Average throughput over the elapsed window (tokens/second), excluding cache reads. This is the
    // "average over the window" definition: real work / time since the window opened, so it dips when
    // the session sat idle. 0 until the window has measurable elapsed time.
    public double TokensPerSecond =>
        ElapsedSeconds > 0 ? (InputTokens + OutputTokens + CacheCreationTokens) / ElapsedSeconds : 0;

    // Burn-up curve: x = fraction of the window elapsed (0–1), y = cumulative utilization (0–1).
    // Runs from (0,0) to (ElapsedFraction, Util).
    public List<(double frac, double cum)> Curve = new();

    // Spans of the curve where no live reading was logged — an API outage (e.g. a 403) or the app not
    // running. Each is the pair of curve points bracketing the gap, so the chart can redraw exactly
    // that stretch of the usage line in red instead of a normal (misleadingly smooth) segment. The
    // start fraction also fixes the "unavailable since …" time. Empty when the readings are continuous.
    public List<(double f0, double cum0, double f1, double cum1)> Gaps = new();

    // Distinct clock hours inside the elapsed window that contained at least one request. This is what
    // the activity-aware projection calibrates against — quota per *active* hour, not per wall-clock
    // hour — so a window that sat idle all morning isn't paced as if it hadn't.
    public double MeasuredActiveHours;

    // The activity-aware projection (T87), or null to keep drawing the straight average-pace line:
    // weekly window only, and only when the profile has enough weeks behind it. See ActivityShape.
    public ActivityShape? Shape;

    // Last week's burn-up on the same axes (T89), or null when too little of it was observed to be
    // worth drawing. Weekly window only — the question it answers ("is this week worse than the
    // last?") isn't one a 5-hour sitting has.
    public HourlyUsage.GhostWeek? Ghost;

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

    /// <summary>The weekly activity shape the projection follows, when there is enough of one.</summary>
    public ActivityProfile? Activity;

    public string? Error;
}

/// <summary>Token counts of one assistant turn, split by type so throughput can be broken down.</summary>
internal readonly record struct TokenBits(long Input, long Output, long CacheCreate, long CacheRead)
{
    public long Total => Input + Output + CacheCreate + CacheRead;
}

internal static class UsageReport
{
    private const double SessionSeconds = 5 * 3600;
    private const double WeekSeconds = 7 * 86400;

    /// <summary>Build the pacing report for both windows from the live snapshot plus the transcripts.</summary>
    /// <param name="profile">Which profile the report is about — its stores *and* its transcripts.
    /// Defaults to the monitored one. Before T128 the transcript scan and the activity profile always
    /// read <c>~/.claude/projects</c>, so monitoring a non-default profile (T127) would have shaped one
    /// account's curve with another account's work.</param>
    public static PaceReport ComputePace(DateTime nowUtc, PaceSnapshot snap, ProfileRef? profile = null)
    {
        var r = new PaceReport { ComputedLocal = nowUtc.ToLocalTime() };
        ProfileRef p = profile ?? ProfileStore.MonitoredRef;
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
                List<UsageSample> hist = UsageHistory.Load(p.Key, earliest);
                List<(double t, TokenBits bits)> samples =
                    Directory.Exists(p.ProjectsDir) ? ScanTokens(p.ProjectsDir, earliest, now) : new();

                FillCurve(r.Session, samples, hist, s => (s.Util5h, s.Reset5h), now);
                FillCurve(r.Weekly, samples, hist, s => (s.Util7d, s.Reset7d), now);
            }

            // Shape the weekly projection around when this machine is actually used. Cached daily and
            // refreshed off-thread, so this costs a file read on all but the first call of the day.
            // The 5-hour window is deliberately left alone: it is one sitting, not a habit.
            r.Activity = ActivityProfile.Load(nowUtc, profile: p);
            r.Weekly.Shape = ActivityShape.Build(r.Weekly, r.Activity, now);

            // Last week behind this week, from the folded hourly aggregate — the raw log can't reach
            // back that far (8-day retention), which is exactly why T88 exists.
            if (r.Weekly.HasWindow)
                r.Weekly.Ghost = HourlyUsage.PreviousWeek(p.Key, 
                    r.Weekly.ResetUnix - r.Weekly.WindowSeconds, r.Weekly.WindowSeconds,
                    r.Weekly.ElapsedFraction);

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

        // Verdict off the even-pace line, with NO extra tolerance — so the chip and the status message
        // agree with the projection line/tooltip and the tray icon, which all flag "won't last to reset"
        // as soon as usage passes the pace line (util > elapsed fraction ⟺ ExhaustFraction < 1). An
        // earlier margin here made the chip say "on track" while the chart already projected running out.
        w.Verdict = w.Util >= 0.995 ? PaceVerdict.AtLimit
            : w.Util > w.ElapsedFraction ? PaceVerdict.Ahead
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
    private static void FillCurve(WindowPace w, List<(double t, TokenBits bits)> samples,
        List<UsageSample> hist, Func<UsageSample, (double util, double reset)> pick, double now)
    {
        if (!w.HasWindow) return;
        double start = w.ResetUnix - w.WindowSeconds;

        var inWin = samples.Where(s => s.t >= start && s.t <= now).OrderBy(s => s.t).ToList();
        long total = 0;
        // Hours that saw work, counted as distinct UTC hour slots: whole-hour offsets make that the
        // same count as local slots, and the projection only needs how many, not which.
        var activeHours = new HashSet<long>();
        foreach (var s in inWin)
        {
            activeHours.Add((long)(s.t / 3600));
            total += s.bits.Total;
            w.InputTokens += s.bits.Input;
            w.OutputTokens += s.bits.Output;
            w.CacheCreationTokens += s.bits.CacheCreate;
            w.CacheReadTokens += s.bits.CacheRead;
        }
        w.TokensInWindow = total;
        w.RequestsInWindow = inWin.Count;
        w.MeasuredActiveHours = activeHours.Count;

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
            // Mark stretches with no logged reading (outages): look across the real samples plus the
            // live "now" point, so both past gaps and an ongoing outage (last sample → now) are caught.
            var known = new List<(double f, double c)>(real) { (w.ElapsedFraction, w.Util) };
            w.Gaps = FindGaps(known, w.WindowSeconds);
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
            cum += s.bits.Total;
            double frac = Math.Clamp((s.t - start) / w.WindowSeconds, 0, 1);
            double cu = w.Util * cum / total;
            curve.Add((frac, cu));
        }
        w.Curve = curve;
    }

    // A gap in the logged readings counts as an outage only once it's this much larger than the normal
    // cadence — so a stray missed poll (or the couple of blips we ride out before alarming) isn't drawn
    // as unavailable, while a real interruption (a persistent 403, a long app-off stretch) is.
    private const double GapFloorSeconds = 15 * 60;   // absolute floor, regardless of a fast cadence
    private const double GapCadenceFactor = 3.0;      // ...or this many times the measured poll spacing

    /// <summary>Find spans between consecutive known points whose time gap is well above the normal
    /// sample cadence, returning each as the bracketing curve points so the chart can redraw that
    /// stretch of the usage line as "unavailable". <paramref name="pts"/> must be sorted by fraction.</summary>
    private static List<(double f0, double cum0, double f1, double cum1)> FindGaps(
        List<(double f, double c)> pts, double windowSeconds)
    {
        var gaps = new List<(double, double, double, double)>();
        if (pts.Count < 2) return gaps;

        // Typical spacing = median of consecutive deltas (in seconds), a cadence estimate robust to the
        // one big outage delta that would skew a mean.
        var deltas = new List<double>();
        for (int i = 1; i < pts.Count; i++)
            deltas.Add((pts[i].f - pts[i - 1].f) * windowSeconds);
        deltas.Sort();
        double median = deltas[deltas.Count / 2];
        double threshold = Math.Max(GapFloorSeconds, GapCadenceFactor * median);

        for (int i = 1; i < pts.Count; i++)
        {
            double deltaSec = (pts[i].f - pts[i - 1].f) * windowSeconds;
            if (deltaSec > threshold)
                gaps.Add((pts[i - 1].f, pts[i - 1].c, pts[i].f, pts[i].c));
        }
        return gaps;
    }

    // Collect (unixTime, token breakdown) for every in-window assistant request with usage, from one
    // profile's transcripts (T128 — before that this always read the default config dir's).
    private static List<(double t, TokenBits bits)> ScanTokens(string projectsDir, double startUnix, double nowUnix)
    {
        var samples = new List<(double, TokenBits)>();
        DateTime cutoffUtc = DateTimeOffset.FromUnixTimeSeconds((long)startUnix).UtcDateTime;

        foreach (string file in SafeWalk.Paths(projectsDir, "*.jsonl"))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoffUtc) continue;

            foreach (string line in ReadLinesSafe(file))
            {
                if (line.Length == 0) continue;
                if (TryParseSample(line, startUnix, nowUnix, out double t, out TokenBits bits))
                    samples.Add((t, bits));
            }
        }
        return samples;
    }

    private static IEnumerable<string> ReadLinesSafe(string file)
    {
        try { return File.ReadLines(file); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Parse one transcript line into (unix time, token breakdown), or fail. Reads
    /// <c>type</c>, <c>timestamp</c>, <c>requestId</c>, <c>message.id</c>, <c>message.model</c> and
    /// <c>message.usage</c> only — never content (§I.1) — which is why <see cref="TranscriptTail"/>
    /// reuses it rather than writing a second parser with a second chance of reading too much.</summary>
    private static bool TryParseSample(string line, double startUnix, double nowUnix, out double t, out TokenBits bits)
        => TryParseSample(line, startUnix, nowUnix, out t, out bits, out _, out _);

    /// <inheritdoc cref="TryParseSample(string,double,double,out double,out TokenBits)"/>
    /// <param name="id">The response this line belongs to (<c>requestId</c>, falling back to
    /// <c>message.id</c>). Claude Code writes <b>one line per content block</b>, each repeating the
    /// same <c>usage</c>, so a caller that must not count a response twice keys on this.</param>
    /// <param name="cwd">The session's working directory, when the line carries one. This is the only
    /// way to name a project correctly: the <c>projects/&lt;slug&gt;</c> directory encodes path
    /// separators and literal dashes identically, so <c>d--Git-acme-claude-tray</c> cannot be split
    /// back into a folder name without guessing. Sanctioned by §I.1, and the same field
    /// <see cref="ContextScanner"/> already resolves paths with.</param>
    public static bool TryParseSample(string line, double startUnix, double nowUnix,
        out double t, out TokenBits bits, out string? id, out string? cwd)
    {
        t = 0; bits = default; id = null; cwd = null;
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

            var b = new TokenBits(
                Input: (long)Num(usage, "input_tokens"),
                Output: (long)Num(usage, "output_tokens"),
                CacheCreate: (long)Num(usage, "cache_creation_input_tokens"),
                CacheRead: (long)Num(usage, "cache_read_input_tokens"));
            if (b.Total <= 0) return false;

            id = root.TryGetProperty("requestId", out var rid) && rid.GetString() is { Length: > 0 } r
                ? r
                : msg.TryGetProperty("id", out var mid) ? mid.GetString() : null;

            if (root.TryGetProperty("cwd", out var cw) && cw.GetString() is { Length: > 0 } c) cwd = c;

            t = u; bits = b;
            return true;
        }
        catch { return false; }
    }

    private static double Num(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : 0;
}
