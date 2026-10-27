namespace ClaudeTray;

/// <summary>
/// "You have reached a level you asked about" — per bounded window, once per reset cycle (T395). The shape
/// <see cref="PaceAlarm"/> settled, for a different question: that one asks whether the pace runs out, this
/// one whether usage has passed a number the user typed, which is how somebody rationing a week works.
///
/// <para><b>What it remembers is the highest usage seen in the cycle</b>, not a latch per level. A level
/// fires on the reading that first carries usage past it; a credit that pulls usage back under it does not
/// re-arm it, because the high-water mark stays where it was until the window resets. A level added to the
/// list while usage is already above it is not news either: nothing crossed it.</para>
///
/// <para><b>Several levels crossed by one reading are one toast</b>, for the highest of them. A tray that
/// was asleep while usage went from 40% to 85% has one thing to say.</para>
///
/// <para><b>Used or left.</b> With Show remaining on, the icon counts quota left, and a level of 25 means
/// "a quarter left" — usage past 75%. The list is read the way the icon reads, at the moment of the
/// reading, so the setting and the icon count the same way.</para>
/// </summary>
internal sealed class LevelAlarm
{
    /// <summary>Per window, the cycle (its reset) and the highest usage seen inside it.</summary>
    private readonly Dictionary<string, (double Reset, double High)> _seen = new(StringComparer.Ordinal);

    /// <param name="before">The last reading taken before this process started, or null — a tray restarted
    /// mid-cycle has already been told about the levels below it, so the seed starts the high-water mark
    /// there. Read before the first poll appends, as for <see cref="PaceAlarm"/>.</param>
    public LevelAlarm(UsageSample? before)
    {
        if (before is not { } b) return;
        _seen["5h"] = (b.Reset5h, b.Util5h);
        _seen["7d"] = (b.Reset7d, b.Util7d);
    }

    /// <summary>The usage a level stands for: the level itself, or its complement when it counts quota
    /// left.</summary>
    internal static double Threshold(int level, bool remaining) => remaining ? 1 - level / 100.0 : level / 100.0;

    /// <summary>
    /// Take one reading of one window and answer the level it crossed, or null. Called on every poll even
    /// with no levels set, so a list typed in mid-cycle starts from what was already seen rather than
    /// announcing a crossing that happened hours ago.
    /// </summary>
    public int? Note(string key, double util, double reset, IReadOnlyList<int> levels, bool remaining)
    {
        double half = BurnTracker.WindowSeconds(key) / 2;
        double high = _seen.TryGetValue(key, out var s) && Math.Abs(s.Reset - reset) < half ? s.High : 0;
        _seen[key] = (reset, Math.Max(high, util));

        int? crossed = null;
        double best = -1;
        foreach (int level in levels)
        {
            double at = Threshold(level, remaining);
            if (high < at && util >= at && at > best) { best = at; crossed = level; }
        }
        return crossed;
    }
}
