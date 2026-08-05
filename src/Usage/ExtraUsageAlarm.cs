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
    /// <summary>Whose readings these are (T292). An alarm is about one account: a rise is two readings
    /// compared, and two accounts' readings compare to nothing.</summary>
    private string _profile;

    private double? _lastExtra;
    private bool? _lastInUse;

    /// <summary>Whether this spell has already been announced. One latch for both routes, so an account
    /// whose figure does climb after the boolean fired is told once and not twice (T276).</summary>
    private bool _announced;

    /// <param name="profileKey">The account these readings belong to — <see cref="ProfileStore.Monitored"/>
    /// at the moment the alarm is built.</param>
    /// <param name="before">The last reading taken <b>before this process started</b>, or <c>null</c> when
    /// the history holds none. It is what stops a tray launched in the middle of an overage spell from
    /// reading that spell as its beginning — and passing this poll's own reading is the defect T290 fixed,
    /// so the caller reads the store before it appends to it.</param>
    public ExtraUsageAlarm(string profileKey, UsageSample? before)
    {
        _profile = profileKey;
        _lastExtra = before?.Extra;
        _lastInUse = before?.InUse;
        _announced = Spending(before?.Extra, before?.InUse);
    }

    /// <summary>Whether a reading is the account observed <em>doing</em> it — which is what makes a spell
    /// one this alarm has walked in on rather than one it can announce the beginning of.
    ///
    /// <para>The two readings alone only keep the <em>boolean</em> route quiet: it sees <c>true</c> beside
    /// <c>true</c> and finds no rise, while the figure route would still read a mid-spell 0.0 → 0.03 as a
    /// beginning and announce the middle of a spell as its start. So the latch starts set. An absent
    /// reading arms nothing either way — it is not an observation of spending any more than it is one of
    /// quiet.</para>
    ///
    /// <para>The predicate itself is <see cref="QuotaStates.Spending"/>, which is where it moved once
    /// <see cref="OverageSpell"/> needed the same question answered the same way (T280).</para></summary>
    private static bool Spending(double? extra, bool? inUse) => QuotaStates.Spending(extra, inUse);

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
    public bool Note(string profileKey, double? extra, bool? inUse)
    {
        if (!string.Equals(profileKey, _profile, StringComparison.Ordinal))
        {
            // A reading from an account this alarm has never watched, which is what a switch of the
            // monitored profile produces (T292). The caller is expected to have built a fresh alarm at the
            // switch, seeded from the incoming account's own history; this is what happens when it did not,
            // and it is deliberately the *quiet* answer rather than a repair. There is no repair available
            // here: seeding from the store at this moment reads back the line this very poll appended,
            // which is the defect T290 removed. So the reading becomes the baseline and announces nothing —
            // the same answer a process with no history gets — and a genuine crossing on this account is
            // announced by the poll after it. A forgotten call site costs one late alert, never a false one.
            _profile = profileKey;
            _lastExtra = extra;
            _lastInUse = inUse;
            _announced = Spending(extra, inUse);
            return false;
        }

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
