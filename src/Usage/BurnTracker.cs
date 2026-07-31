namespace ClaudeTray;

/// <summary>Result of projecting the current usage trend against its reset deadline.</summary>
internal enum Projection
{
    Unknown, // not enough samples yet to estimate a burn rate
    Ok,      // at the current pace, usage stays under 100% until the window resets
    Danger,  // at the current pace, usage hits 100% before the window resets
}

/// <summary>
/// Observability for token usage: keeps a short rolling history of utilization samples
/// per metric (5h / 7d / extra) and estimates the burn rate (utilization per second) by
/// least-squares regression. From that it projects whether usage will reach 100% before
/// the window resets — the signal behind the tray's green/red dot.
/// </summary>
internal sealed class BurnTracker
{
    private sealed class Series
    {
        public readonly List<(double t, double u)> Samples = new();
        public double LastReset;
    }

    // ~2h of history at the 5-min poll cadence — recent enough to reflect the current pace.
    private const int MaxSamples = 24;
    // Need at least this much elapsed time between first and last sample to trust a slope.
    private const double MinSpanSeconds = 120;
    // A reset counts as "unexpected" only when the previously-reported deadline was still this
    // far in the future — comfortably beyond the slowest poll cadence (1h), so an on-time reset
    // (where the deadline ≈ now) never trips it, but the days-early anomaly does.
    private const double UnexpectedResetMarginSeconds = 2 * 3600;
    // Any real downward move in utilization (points) is worth noting — usage only climbs within a
    // window, so a fall is always good news. This floor filters out poll-to-poll rounding jitter.
    private const double MinDrop = 0.05;
    // A partial drop at least this large (e.g. 91% → 50%) is reported as a Credit; below it, the fall
    // is treated as noise and only resets the burn-rate history.
    private const double MinCreditDrop = 0.10;

    private readonly Dictionary<string, Series> _series = new();

    /// <summary>
    /// Forget every sample. Needed when the tray switches which profile the icon follows (T127): the
    /// slope is fitted over a series of utilization readings, and readings from two different accounts
    /// in one series would produce a projection about neither — including a phantom "reset" the moment
    /// the number jumps from one account's usage to the other's.
    /// </summary>
    public void Clear() => _series.Clear();

    /// <summary>How a detected downward change is classified: a full reset on its deadline
    /// (<see cref="Scheduled"/>) or earlier than due (<see cref="Unexpected"/>), or a partial
    /// mid-window drop where usage was credited back without resetting (<see cref="Credit"/>).</summary>
    internal enum ResetKind { Scheduled, Unexpected, Credit }

    /// <summary>A usage window falling — the signal behind the reset/credit tray notification. Carries
    /// the kind and before/after numbers so the event can be shown and logged with concrete values.</summary>
    internal readonly record struct ResetEvent(
        ResetKind Kind, double PrevUtil, double NewUtil, double PrevReset, double NewReset);

    /// <summary>
    /// Record a fresh utilization sample, auto-clearing history when usage falls. Returns a non-null
    /// <see cref="ResetEvent"/> when this sample shows usage dropping (it never falls on its own within
    /// a window): a collapse to ~0% is a full reset — <see cref="ResetKind.Unexpected"/> if it beat its
    /// deadline by a comfortable margin, else the routine <see cref="ResetKind.Scheduled"/> weekly reset
    /// — and a smaller-but-meaningful fall is a mid-window <see cref="ResetKind.Credit"/>.
    /// </summary>
    public ResetEvent? Record(string key, double util, double reset, double now)
    {
        if (!_series.TryGetValue(key, out Series? s))
            _series[key] = s = new Series();

        // A new window started if the reset timestamp jumped, or utilization fell (usage only climbs
        // within a window). Either way, the old trend no longer applies.
        bool resetMoved = reset > 0 && s.LastReset > 0 && Math.Abs(reset - s.LastReset) > 1;
        double prevUtil = s.Samples.Count > 0 ? s.Samples[^1].u : 0;
        double drop = s.Samples.Count > 0 ? prevUtil - util : 0;
        bool dropped = drop > MinDrop;

        // Classify any real fall. A collapse to ~0% is a full reset (early ⇒ Unexpected, on-time ⇒
        // Scheduled). A smaller fall that stays above zero is a Credit — usage recalculated/returned
        // mid-window (e.g. 91% → 50%) — provided it clears the credit floor.
        ResetEvent? evt = null;
        if (dropped && s.LastReset > 0)
        {
            if (util < 0.05)
            {
                ResetKind kind = s.LastReset - now > UnexpectedResetMarginSeconds
                    ? ResetKind.Unexpected
                    : ResetKind.Scheduled;
                evt = new ResetEvent(kind, prevUtil, util, s.LastReset, reset);
            }
            else if (drop >= MinCreditDrop)
            {
                evt = new ResetEvent(ResetKind.Credit, prevUtil, util, s.LastReset, reset);
            }
        }

        if (resetMoved || dropped)
            s.Samples.Clear();

        s.LastReset = reset;
        s.Samples.Add((now, util));
        if (s.Samples.Count > MaxSamples)
            s.Samples.RemoveAt(0);

        return evt;
    }

    /// <summary>
    /// Project the named metric: verdict, seconds until usage would hit 100% at the current
    /// pace, and the burn rate as a fraction of the limit per hour (for display).
    ///
    /// When <paramref name="windowSeconds"/> &gt; 0 the verdict is decided by the "pace line"
    /// instead of the regression: utilization is compared against the linear ideal consumption
    /// since the window started (elapsed = window − timeToReset). This is robust to short
    /// bursts because it averages over the whole elapsed window rather than the last ~2h. The
    /// ETA / burn rate are still derived from the regression for display.
    /// </summary>
    public (Projection verdict, double exhaustSeconds, double burnPerHour) Project(
        string key, double util, double reset, double now, double windowSeconds = 0)
    {
        // Regression-based burn rate and ETA (drives the verdict unless a pace-line window
        // is supplied below).
        Projection verdict = Projection.Unknown;
        double exhaust = 0, burnPerHour = 0;

        if (_series.TryGetValue(key, out Series? s) && s.Samples.Count >= 2 &&
            s.Samples[^1].t - s.Samples[0].t >= MinSpanSeconds)
        {
            // Least-squares slope (utilization per second), x re-based to the first sample.
            double n = s.Samples.Count, t0 = s.Samples[0].t;
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            foreach ((double t, double u) in s.Samples)
            {
                double x = t - t0;
                sx += x; sy += u; sxx += x * x; sxy += x * u;
            }
            double denom = n * sxx - sx * sx;
            if (denom > 0)
            {
                double burnPerSec = (n * sxy - sx * sy) / denom;
                if (burnPerSec <= 1e-9)
                {
                    // Flat or declining usage: nothing will be exhausted on its own.
                    verdict = Projection.Ok;
                    exhaust = double.PositiveInfinity;
                }
                else
                {
                    double remaining = Math.Max(0.0, 1.0 - util);
                    exhaust = remaining / burnPerSec;          // seconds until 100%
                    double untilReset = reset > 0 ? reset - now : double.PositiveInfinity;
                    verdict = exhaust < untilReset ? Projection.Danger : Projection.Ok;
                    burnPerHour = burnPerSec * 3600;
                }
            }
        }

        // Pace-line verdict for windows of known length: danger if we are already above the
        // share of the budget that even, constant consumption would have used by now. The ETA
        // is the rule-of-three projection from the average pace since the window started:
        // util reached in `elapsed` → 100% reached in elapsed × 100/util, so the time left to
        // hit 100% is elapsed × (1 − util) / util.
        if (windowSeconds > 0 && reset > 0)
        {
            double untilReset = reset - now;
            double elapsed = Math.Max(0.0, windowSeconds - untilReset);
            double expected = Math.Clamp(elapsed / windowSeconds, 0.0, 1.0);
            verdict = util > expected ? Projection.Danger : Projection.Ok;

            if (util >= 1.0)
                exhaust = 0;
            else if (util > 0)
                exhaust = elapsed * (1.0 - util) / util;
            else
                exhaust = double.PositiveInfinity;
        }

        // A window at (or rounding to) 100% is already exhausted — there is nothing left to project,
        // so the verdict is always Danger (red). Without this, usage pinned flat at 100% has a ~0
        // burn slope and the regression path would call it "on track" (green).
        if (util >= 0.995)
            return (Projection.Danger, 0, burnPerHour);

        return (verdict, exhaust, burnPerHour);
    }
}
