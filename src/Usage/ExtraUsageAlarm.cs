namespace ClaudeTray;

/// <summary>
/// The "you have started paying" transition, as a state machine that can be driven without a tray (T290).
///
/// <para><b>Why it left <c>TrayContext</c>.</b> The rule is a sequence — a seed, a rise, a latch, a release
/// — and every part of it lived inside a method only a resident tray can call, so nothing asserted any of
/// it end to end. What did get asserted were the two predicates underneath (T184, T276), and the defect
/// this type exists to close sat in neither: the notifier fetched its own seed from
/// <see cref="UsageHistory"/>, and the poll appends the reading it is about to judge <em>before</em> calling
/// it, so the "previous" reading handed back was that same poll's own. A first poll therefore compared
/// itself with itself, found no rise, and stayed quiet — and every poll after it read a spell already under
/// way. One missed alert per restart, in exactly the case the alert exists for.</para>
///
/// <para><b>So the seed is a constructor argument, not a lookup.</b> The ordering hazard is not fixed by
/// moving two calls into the right order, which the next edit can undo silently; it is removed by making
/// the reading an input this type cannot obtain for itself. Build it where the process starts, from the
/// history as it stood before the first poll wrote to it, and there is no moment at which it could read
/// the wrong thing.</para>
/// </summary>
internal sealed class ExtraUsageAlarm
{
    private double? _lastExtra;
    private bool? _lastInUse;

    /// <summary>Whether this spell has already been announced. One latch for both routes, so an account
    /// whose figure does climb after the boolean fired is told once and not twice (T276).</summary>
    private bool _announced;

    /// <param name="before">The last reading taken <b>before this process started</b>, or <c>null</c> when
    /// the history holds none. It is what stops a tray launched in the middle of an overage spell from
    /// reading that spell as its beginning — and passing this poll's own reading is the defect T290 fixed,
    /// so the caller reads the store before it appends to it.</param>
    public ExtraUsageAlarm(UsageSample? before)
    {
        _lastExtra = before?.Extra;
        _lastInUse = before?.InUse;

        // A spell this process walked in on is already announced, as far as this process is concerned.
        // The two readings alone only keep the *boolean* route quiet — it sees true beside true and finds
        // no rise — while the figure route would still read a mid-spell 0.0 → 0.03 as a beginning and
        // announce the middle of a spell as its start. The seed says which state the account was in, so it
        // is the thing that knows there is nothing left to announce about it. An absent reading arms
        // nothing either way: it is not an observation of spending any more than it is one of quiet.
        _announced = before is { } s && (s.InUse is true || s.Extra > 0);
    }

    /// <summary>
    /// Take one reading, and answer whether it is the moment to announce.
    ///
    /// <para>Called on every poll whatever the notification setting says, because the answer depends on the
    /// readings that came before and a spell the user was not told about is still a spell that happened:
    /// skipping the call while the toggle is off would leave the next reading comparing against one taken
    /// an unknown time ago. Whether the announcement is <em>shown</em> is the caller's decision.</para>
    ///
    /// <para>Two routes to one announcement (T276): <c>overage-in-use</c> rising, which is the header the
    /// crossing actually moves, and the overage figure rising, for an account whose utilization does climb.
    /// They share <see cref="_announced"/>, released only by a reading measured back inside the quota — so
    /// one spell is at most one announcement, and the next spell can still have its own.</para>
    /// </summary>
    public bool Note(double? extra, bool? inUse)
    {
        double? previous = _lastExtra;
        bool? previousInUse = _lastInUse;
        _lastExtra = extra;
        _lastInUse = inUse;

        // State, not a decision — the latch describes the spell, so it is released before anything reads it.
        if (QuotaStates.BackInsideQuota(inUse, extra)) _announced = false;
        if (_announced) return false;

        if (!QuotaStates.StartsSpending(previousInUse, inUse)
            && !QuotaStates.StartsSpending(previous, extra)) return false;

        return _announced = true;
    }
}
