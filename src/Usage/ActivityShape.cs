namespace ClaudeTray;

/// <summary>
/// The weekly projection, spent along <see cref="ActivityProfile"/>'s shape instead of along a
/// uniform slope: flat through the hours this machine is usually idle, sloped through the hours it
/// is usually working.
///
/// <para>The straight line it replaces is not wrong about <em>how much</em> idle there is — the
/// average pace since the window opened already has every idle hour so far diluted into it. It is
/// wrong about <em>where</em> the idle sits, which produces two concrete errors: an exhaustion marker
/// landing at 03:59 on a Friday (a limit cannot be reached while nothing is running), and a misread
/// of any window whose remaining active/idle mix differs from its elapsed one — a partial day, an
/// approaching weekend, a window that opened at 02:00.</para>
///
/// <para>Calibration is per <em>active</em> hour, not per wall-clock hour:
/// <c>rate = util / active_hours_measured_in_this_window</c>, then
/// <c>projected = util + rate × Σ p over the remaining hours</c>. This degrades correctly: where the
/// remainder's mix matches the elapsed one it reproduces the straight line exactly, and it only
/// diverges where the mix genuinely differs.</para>
///
/// <para>T94 makes the unit an <em>ordinary</em> active hour rather than any active hour: the sums
/// become <c>Σ p·i</c>, and the measured rate is divided by the elapsed span's own mean intensity so
/// it is expressed in the same unit it is later multiplied back into. Where the grid is flat — no
/// folded store yet, or a week with no heavy and light hours to tell apart — every factor is 1 and
/// the arithmetic is byte-for-byte the T87 one.</para>
///
/// <para>Weekly window only — a 5-hour window is a single sitting, and a habit grid has nothing to
/// say about it. Built only when the profile has enough weeks behind it
/// (<see cref="ActivityProfile.Confident"/>); a confident-looking staircase drawn on one week of data
/// is worse than an honest line.</para>
/// </summary>
internal sealed class ActivityShape
{
    /// <summary>
    /// Below this much measured activity, the window's own active hours are too thin to divide by
    /// (three requests in one hour would imply a rate for the whole week), so the profile's own
    /// expectation for the elapsed span stands in.
    /// </summary>
    public const double MinMeasuredActiveHours = 2.0;

    /// <summary>An hour worked in fewer than this share of weeks reads as idle on the chart.</summary>
    public const double IdleThreshold = 0.15;

    /// <summary>Shorter idle runs are not banded — a speckle of one-hour bands is noise, not a night.</summary>
    public const double MinBandHours = 2.0;

    /// <summary>The projection, from the current point to the reset. Same (frac, cum) space as
    /// <see cref="WindowPace.Curve"/>, so the chart draws it with the same transform.</summary>
    public List<(double frac, double cum)> Curve = new();

    /// <summary>Stretches ahead that are usually idle, as window fractions — the faint amber bands.</summary>
    public List<(double f0, double f1)> IdleBands = new();

    /// <summary>Where the projection reaches 100%; &gt; 1 when it doesn't before the reset.</summary>
    public double ExhaustFraction = double.PositiveInfinity;

    /// <summary>Unix seconds of that landing point (0 when it doesn't run out).</summary>
    public double ExhaustUnix;

    /// <summary>Projected utilization at the reset, clamped to 1.</summary>
    public double EndCum;

    /// <summary>Expected active hours between now and the reset — the quantity quota is spent over.</summary>
    public double ActiveHoursAhead;

    /// <summary>Quota fraction spent per active hour, the calibrated rate.</summary>
    public double RatePerActiveHour;

    /// <summary>Expected <em>ordinary-hour equivalents</em> ahead — <c>Σ p·i</c> (T94). Equal to
    /// <see cref="ActiveHoursAhead"/> when the intensity grid is flat.</summary>
    public double IntensityHoursAhead;

    /// <summary>The rate re-expressed per ordinary active hour, which is the unit
    /// <see cref="IntensityHoursAhead"/> is counted in. This, not <see cref="RatePerActiveHour"/>, is
    /// what the projection multiplies by.</summary>
    public double RatePerOrdinaryHour;

    /// <summary>Mean intensity of the elapsed span — how heavy this window's hours have been so far,
    /// relative to an ordinary one. The divisor that keeps the calibration and the projection in the
    /// same unit; 1 when there is no intensity grid.</summary>
    public double ElapsedIntensity = 1;

    /// <summary>Weeks of coverage behind the profile, for the wording that hedges on it.</summary>
    public double CoverageWeeks;

    /// <summary>True when the rate was calibrated against hours actually measured in this window,
    /// false when it fell back to the profile's expectation for the elapsed span.</summary>
    public bool MeasuredCalibration;

    /// <summary>Unix seconds of the earliest hour you could resume at and still finish the week under
    /// the limit — 0 when there is nothing to advise. See <see cref="Advise"/>.</summary>
    public double ResumeUnix;

    /// <summary>Where the week would close if work stopped now and resumed at <see cref="ResumeUnix"/>.</summary>
    public double ResumeEndCum;

    /// <summary>There is a concrete "stop now, resume then" to offer.</summary>
    public bool HasAdvice => ResumeUnix > 0;

    public bool RunsOut => ExhaustFraction <= 1;

    /// <summary>Aim to close the week just under the limit rather than exactly at it — advice that
    /// lands on 100.0% is advice to get blocked. Public because <c>--selftest</c> asserts against the
    /// real constant: a check that hardcodes 0.98 stops being a check the day the target moves.</summary>
    public const double AdviceTarget = 0.98;

    /// <summary>
    /// Build the staircase, or null when the shape shouldn't be trusted here — no window, nothing
    /// spent yet, already at the limit, a thin profile, or too little activity to calibrate against.
    /// Null is the signal to keep drawing today's straight line.
    /// </summary>
    public static ActivityShape? Build(WindowPace w, ActivityProfile? p, double nowUnix)
    {
        if (p == null || !p.Confident || !w.HasWindow) return null;

        double ef = w.ElapsedFraction;
        if (ef <= 0 || w.Util <= 0 || w.Util >= 0.995) return null;

        double start = w.ResetUnix - w.WindowSeconds;
        DateTime startLocal = Local(start);
        DateTime nowLocal = Local(nowUnix);
        DateTime resetLocal = Local(w.ResetUnix);
        if (resetLocal <= nowLocal) return null;

        double elapsedP = p.ExpectedActiveHours(startLocal, nowLocal);

        // Prefer what this window actually did over what the profile expects it to have done: a week
        // spent on holiday should not be paced as if its usual hours had been worked.
        double elapsedActive = w.MeasuredActiveHours;
        bool measured = elapsedActive >= MinMeasuredActiveHours;
        if (!measured) elapsedActive = elapsedP;
        if (elapsedActive < 0.25) return null;

        // The rate is measured in plain active hours but spent against ordinary-hour equivalents, so
        // it has to be converted between the two units — by the elapsed span's own mean intensity,
        // which is the only estimate available of how heavy the hours behind `util` were. Exactly 1
        // on a flat grid, so this factor cannot move a projection it has no evidence to move.
        double elapsedPi = p.ExpectedIntensityHours(startLocal, nowLocal);
        double elapsedIntensity = elapsedP > 0 && elapsedPi > 0 ? elapsedPi / elapsedP : 1;

        var shape = new ActivityShape
        {
            CoverageWeeks = p.CoverageWeeks,
            MeasuredCalibration = measured,
            RatePerActiveHour = w.Util / elapsedActive,
            ElapsedIntensity = elapsedIntensity,
            RatePerOrdinaryHour = w.Util / elapsedActive / elapsedIntensity,
            ActiveHoursAhead = p.ExpectedActiveHours(nowLocal, resetLocal),
            IntensityHoursAhead = p.ExpectedIntensityHours(nowLocal, resetLocal),
        };

        double Frac(DateTime local) => Math.Clamp(
            (new DateTimeOffset(local).ToUnixTimeSeconds() - start) / w.WindowSeconds, 0, 1);

        double cum = w.Util;
        shape.Curve.Add((ef, cum));

        DateTime cursor = nowLocal;
        double? bandFrom = null;

        // One step per wall-clock hour to the reset — at most 168 for a week; the guard only stops a
        // nonsense window (a corrupt reset time) from spinning.
        for (int guard = 0; guard < 2 * ActivityProfile.Buckets && cursor < resetLocal; guard++)
        {
            DateTime nextHour = cursor.Date.AddHours(cursor.Hour + 1);
            DateTime end = nextHour < resetLocal ? nextHour : resetLocal;
            double f0 = Frac(cursor), f1 = Frac(end);
            double pr = p.At(cursor);

            if (pr < IdleThreshold) bandFrom ??= f0;
            else if (bandFrom is { } open) { shape.AddBand(open, f0, w.WindowSeconds); bandFrom = null; }

            double add = shape.RatePerOrdinaryHour * pr * p.IntensityAt(cursor) * (end - cursor).TotalHours;
            if (cum < 1 && cum + add >= 1 && add > 0)
            {
                shape.ExhaustFraction = f0 + (f1 - f0) * ((1 - cum) / add);
                shape.ExhaustUnix = start + shape.ExhaustFraction * w.WindowSeconds;
            }

            cum = Math.Min(1, cum + add);
            shape.Curve.Add((f1, cum));
            cursor = end;
        }
        if (bandFrom is { } tail) shape.AddBand(tail, 1, w.WindowSeconds);

        shape.EndCum = cum;

        // Past the landing point the line is flat at 100% and the bands would only decorate a stretch
        // the projection has already given up on.
        double limit = Math.Min(1, shape.ExhaustFraction);
        shape.IdleBands = shape.IdleBands
            .Select(b => (f0: b.f0, f1: Math.Min(b.f1, limit)))
            .Where(b => b.f1 > b.f0 && (b.f1 - b.f0) * w.WindowSeconds >= MinBandHours * 3600)
            .ToList();

        if (shape.RunsOut) shape.Advise(w, p, nowLocal, resetLocal);

        return shape;
    }

    /// <summary>
    /// Turn "you run out 1d 6h before the reset" into something to do about it: the earliest hour work
    /// could resume at and still close the week under the limit, assuming the same pace per ordinary
    /// active hour once it does.
    ///
    /// Only whole hours the user is normally working are offered — advice to resume at 03:00 is not
    /// advice — so the answer naturally lands on the start of a working stretch ("tomorrow at 09:00")
    /// rather than on an arbitrary minute. Skipping a night costs nothing in the model, which is why
    /// the resulting close is often comfortably under the target rather than exactly on it.
    /// </summary>
    private void Advise(WindowPace w, ActivityProfile p, DateTime nowLocal, DateTime resetLocal)
    {
        DateTime probe = nowLocal.Date.AddHours(nowLocal.Hour + 1);
        for (int guard = 0; guard < 2 * ActivityProfile.Buckets && probe < resetLocal; guard++, probe = probe.AddHours(1))
        {
            if (p.At(probe) < IdleThreshold) continue;

            double closes = w.Util + RatePerOrdinaryHour * p.ExpectedIntensityHours(probe, resetLocal);
            if (closes > AdviceTarget) continue;

            ResumeUnix = new DateTimeOffset(probe).ToUnixTimeSeconds();
            ResumeEndCum = closes;
            return;
        }
        // Nothing found: even resuming at the last working hour overshoots, so there is no honest
        // "pause until" to give and the warning stands on its own.
    }

    private void AddBand(double f0, double f1, double windowSeconds)
    {
        if ((f1 - f0) * windowSeconds >= MinBandHours * 3600) IdleBands.Add((f0, f1));
    }

    private static DateTime Local(double unix)
        => DateTimeOffset.FromUnixTimeSeconds((long)unix).LocalDateTime;
}
