namespace ClaudeTray;

/// <summary>
/// When the current overage spell began, read off the store (T280).
///
/// <para><b>Why the store and not a remembered moment.</b> A spell outlives the process watching it: the
/// tray restarts, and a field holding "this is when I first saw it" would then date the crossing at the
/// launch. <see cref="UsageHistory"/> already has the readings — <c>ix</c> beside <c>ux</c> since T275 —
/// so the crossing is a line in a file rather than something to keep in memory, and a tray started in the
/// middle of a spell reads back the same answer as one that watched it begin.</para>
///
/// <para><b>What makes an answer available at all.</b> A spell is an <em>event</em>, so it is only datable
/// when the crossing was observed: the reading before the run has to be one measured back inside the quota.
/// A run reaching the oldest line on file, or one preceded by a reading carrying neither header, gets
/// <c>null</c> — the store's own beginning is not a crossing, and absent is not zero (T179, T276). The
/// caller shows nothing rather than a duration counted from the day the log starts.</para>
///
/// <para><b>A gap inside a run is not a break.</b> Readings stop while the tray is closed, and the
/// alternative to carrying on through the gap would be to invent a return to the quota nobody measured.
/// So the duration is "since the crossing this app can see", which is what the readings support.</para>
///
/// <para>Static and pure over a list, for <see cref="QuotaStates"/>'s reason: a tray cannot be constructed
/// under <c>--selftest</c>, and the run this walks is exactly the kind of thing that goes unasserted when
/// it can only be reached through one.</para>
/// </summary>
internal static class OverageSpell
{
    /// <summary>
    /// The unix second of the first reading of the spell the <em>newest</em> reading belongs to, or null
    /// when the account is not spending now, or when the crossing is not on file.
    /// </summary>
    /// <param name="readings">One profile's readings, <b>oldest first</b> — the order
    /// <see cref="UsageHistory.Load"/> returns them in.</param>
    public static long? StartedAt(IReadOnlyList<UsageSample> readings)
    {
        int i = readings.Count - 1;
        if (i < 0 || !Spending(readings[i])) return null;

        while (i > 0 && Spending(readings[i - 1])) i--;

        // The reading before the run decides whether there is an event to date. At the start of the file
        // there is none, and one carrying neither header is not an observation of quiet — both are a spell
        // that may well have begun before anything here was watching.
        if (i == 0) return null;
        UsageSample before = readings[i - 1];
        if (!QuotaStates.BackInsideQuota(before.InUse, before.Extra)) return null;

        return (long)readings[i].T;
    }

    /// <summary>The same answer for one profile's own series. Reads the whole retained log, which is the
    /// file the poll around it is already appending to.</summary>
    public static long? StartedAt(string profileKey) => StartedAt(UsageHistory.Load(profileKey, 0));

    private static bool Spending(UsageSample s) => QuotaStates.Spending(s.Extra, s.InUse);
}
