namespace ClaudeTray;

/// <summary>
/// Everything the tray holds in memory <em>about the account the icon follows</em> (T293).
///
/// <para><b>Why the fields left <c>TrayContext</c>.</b> A switch of the monitored profile has to drop all of
/// this, and the way it did so was a run of statements in <c>AdoptMonitored</c> — three nulled, a tracker
/// cleared by name, an alarm rebuilt — which is a list held by whoever last read that method. T292 is the
/// evidence that this does not hold: the alarm's readings had been the outgoing account's since T184,
/// through four blocks of work on that very notification, and were found only by giving them a name and
/// asking whose they were. T280 then added a fifth and had to be told about the list by hand.</para>
///
/// <para>So the list becomes an object and the switch becomes one assignment. A field added here is dropped
/// by a switch because there is nowhere else for it to live — which is a guarantee the compiler keeps
/// rather than a rule a reviewer has to remember. Building it is also the only way to <em>ask</em> the
/// question: three of the fields below — <see cref="LastRefresh"/>, <see cref="ConsecutiveErrors"/> and
/// <see cref="AutoOpenedForAuth"/> — were never on the old list and are the outgoing account's just as much
/// as the reading is. Two of those were live defects, described on their own summaries.</para>
///
/// <para><b>What is deliberately not here.</b> <c>_otherData</c> is keyed per profile on purpose (T137) and
/// holds the accounts the icon is <em>not</em> following, so folding it in would delete readings the Profile
/// submenu draws. And the <c>ApiClient</c> is a handle rather than state: it is rebuilt from the profile
/// list by the same call that decides who the monitored profile is, so it cannot be stale in the way a
/// remembered reading can.</para>
///
/// <para><b>The fields do not share one lifetime</b>, which is why this has a constructor rather than being
/// a record of nulls: most start empty, and <see cref="ExtraAlarm"/> is <em>seeded from the incoming
/// account's own history</em>. That seeding is why the caller matters — see
/// <see cref="ExtraUsageAlarm"/> for the ordering it must be built in (T290): before the next poll appends
/// to the log it reads, or the alarm compares this poll's reading with itself.</para>
/// </summary>
internal sealed class MonitoredAccount
{
    /// <summary>Whose state this is — <see cref="ProfileStore.Monitored"/> at the moment it was built. Kept
    /// so a reading can be attributed rather than assumed, which is the check <see cref="ExtraUsageAlarm"/>
    /// makes on every call (T292).</summary>
    public string Key { get; }

    /// <summary>The latest reading, or null before this account's first poll has returned — which is also
    /// what the icon reads as "connecting".</summary>
    public UsageData? Data;

    /// <summary>When <see cref="Data"/> was taken, for the tooltip's <c>⟳</c> suffix. The outgoing account's
    /// clock time under the incoming account's percentages is a reading dated to a poll that never happened
    /// on it.</summary>
    public DateTime? LastRefresh;

    /// <summary>The last successful live reading, kept so the Statistics window can still draw its charts
    /// from local data (usage-history + transcripts) during an API outage. The chart marks the unavailable
    /// stretch itself, from the gap in the logged readings (see <c>UsageReport.WindowPace.Gaps</c>).</summary>
    public PaceSnapshot? LastGood;

    /// <summary>When the current overage spell was first measured, or 0 when this account is not spending
    /// past its quota or the crossing is not on file (T280).</summary>
    public long SpellSince;

    /// <summary>Transient API failures since this account's last good poll — the count that decides whether
    /// a blip is ridden out or shown as an error.
    ///
    /// <para><b>It was a live defect to leave behind (T293).</b> Two timeouts on the outgoing account left
    /// the counter at the tolerance, so the very first transient failure on the incoming one was drawn as a
    /// red error icon instead of being ridden out — an account whose own polls had never failed once.</para>
    /// </summary>
    public int ConsecutiveErrors;

    /// <summary>Whether the one-shot "open Claude Code so it can be signed into" has already fired for this
    /// account's signed-out spell.
    ///
    /// <para><b>Also a live defect (T293).</b> Switching from a signed-out account to another signed-out
    /// account found this already set, so the prompt that exists for exactly that moment was suppressed:
    /// the user picked the account they wanted to log into and nothing offered to.</para>
    /// </summary>
    public bool AutoOpenedForAuth;

    /// <summary>
    /// Whether a poll of <em>this</em> account is in flight (T304).
    ///
    /// <para><b>Why it lives here and not on the tray.</b> The rule is one poll per account, not one poll per
    /// process: a switch means the fetch already in flight is the outgoing account's, and the incoming
    /// account's first poll must start at once rather than wait behind it. On the tray that would be a third
    /// mechanism to write and remember. Here it is free — a switch replaces this whole object (T293), so the
    /// incoming account arrives with the flag clear and the outgoing poll goes on holding its own.</para>
    /// </summary>
    public bool Polling;

    /// <summary>Whether somebody <em>asked</em> for a reading while one was in flight, so the poll runs once
    /// more when it lands (T304). A timer tick sets nothing — the number it would fetch is the number already
    /// being fetched — but a click on <b>Refresh now</b> that quietly did nothing is the user being ignored,
    /// and that is the whole difference between the two callers.</summary>
    public bool PollAgain;

    /// <summary>This account's utilization history, and the projection over it. A fresh one rather than a
    /// cleared one, so there is no <c>Clear()</c> for a switch to forget to call.</summary>
    public BurnTracker Burn { get; } = new();

    /// <summary>The "you have started paying" transition for this account, seeded from its own history.</summary>
    public ExtraUsageAlarm ExtraAlarm { get; }

    /// <param name="profileKey">The account this state belongs to — <see cref="ProfileStore.Monitored"/> at
    /// the moment of the switch, which is why the caller builds this <em>after</em> re-keying the stores and
    /// before the next poll.</param>
    public MonitoredAccount(string profileKey)
    {
        Key = profileKey;
        // Best-effort, as the history is everywhere else it is touched: an unreadable one means no seed, not
        // no tray and not a poll that throws.
        UsageSample? before = null;
        try { before = UsageHistory.Latest(profileKey); } catch { /* no seed is a valid seed */ }
        ExtraAlarm = new ExtraUsageAlarm(profileKey, before);
    }
}
