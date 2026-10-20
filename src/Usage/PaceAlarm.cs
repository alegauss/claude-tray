namespace ClaudeTray;

/// <summary>
/// The "on pace to run out before the reset" transition, per bounded window (T388), as a state machine that
/// can be driven without a tray — the shape <see cref="ExtraUsageAlarm"/> settled for the same reason.
///
/// <para><b>The verdict is the icon's.</b> <see cref="BurnTracker.PaceLine"/> is what turns the bar red, and
/// this asks the same function: the toast says in words what the icon already shows, and is not a second
/// forecast. The activity-shaped projection is deliberately not consulted (the T87 non-goal).</para>
///
/// <para><b>Once per window per reset cycle.</b> The latch is keyed by the cycle's reset time, so a reading
/// that flickers around the pace line, or a credit that pulls the window back under it for a while, is told
/// once. A new cycle re-arms it. Cycles are told apart at half a window, not to the second: two different
/// cycles of one window are a whole window apart, and a reset header that drifts by seconds must not read as
/// a new cycle and nag.</para>
///
/// <para><b>Two readings that are red on the icon and silent here.</b> The first stretch of a window, where
/// the pace line is an average over a few minutes and 2% used three minutes in "runs out" in two hours —
/// so the alarm waits for <see cref="MinElapsedShare"/> of the window. And a window already at its limit,
/// which has run out rather than being on pace to; it latches quietly, so a reset is what re-arms it.</para>
/// </summary>
internal sealed class PaceAlarm
{
    /// <summary>The share of a window that must have elapsed before its pace is worth announcing.</summary>
    internal const double MinElapsedShare = 0.10;

    /// <summary>The windows this alarm watches. "extra" has no fixed length, so no pace line.</summary>
    internal static readonly string[] Windows = { "5h", "7d" };

    /// <summary>Per window, the reset time of the cycle already announced (or silently latched).</summary>
    private readonly Dictionary<string, double> _latched = new(StringComparer.Ordinal);

    /// <param name="before">The last reading taken <b>before this process started</b>, or <c>null</c>. A
    /// tray restarted in the middle of a red cycle has almost certainly said so already, so a seed in danger
    /// latches that cycle — the same reason <see cref="ExtraUsageAlarm"/> takes its seed, and the same
    /// ordering: read the store before this process's first poll appends to it.</param>
    public PaceAlarm(UsageSample? before)
    {
        if (before is not { } b) return;
        foreach (string key in Windows)
        {
            (double util, double reset) = key == "5h" ? (b.Util5h, b.Reset5h) : (b.Util7d, b.Reset7d);
            if (InDanger(key, util, reset, b.T)) _latched[key] = reset;
        }
    }

    /// <summary>Whether one reading of one window is past the point worth announcing: the pace line says it
    /// runs out before the reset, and enough of the window has gone by for that average to mean
    /// something.</summary>
    internal static bool InDanger(string key, double util, double reset, double now)
    {
        double window = BurnTracker.WindowSeconds(key);
        if (window <= 0 || reset <= now) return false;
        if (window - (reset - now) < MinElapsedShare * window) return false;
        return BurnTracker.PaceLine(util, reset, now, window).verdict == Projection.Danger;
    }

    /// <summary>
    /// Take one reading of one window, and answer whether it is the moment to announce.
    ///
    /// <para>Called on every poll whatever the notification setting says, like
    /// <see cref="ExtraUsageAlarm.Note"/>: a cycle the user chose not to hear about has still been seen, and
    /// turning the toggle on mid-cycle must not announce a danger that began hours ago.</para>
    /// </summary>
    public bool Note(string key, double util, double reset, double now)
    {
        if (!InDanger(key, util, reset, now)) return false;

        double halfWindow = BurnTracker.WindowSeconds(key) / 2;
        if (_latched.TryGetValue(key, out double cycle) && Math.Abs(cycle - reset) < halfWindow) return false;

        _latched[key] = reset;
        return util < 0.995;   // already out is not "on pace to run out" — the same cut Project makes
    }
}
