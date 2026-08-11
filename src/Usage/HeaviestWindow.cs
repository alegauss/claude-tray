namespace ClaudeTray;

/// <summary>The busiest stretch found by a sweep: when it began and what went through it.
/// <see cref="Tokens"/> of 0 is "nothing in range", and every caller has to say so rather than
/// drawing a zero.</summary>
internal readonly record struct PeakWindow(long StartMinute, long Tokens)
{
    public bool Found => Tokens > 0;

    /// <summary>When the stretch began, in local time — the first minute that carried work, not the
    /// left edge of an arbitrary frame.</summary>
    public DateTime StartLocal =>
        DateTimeOffset.FromUnixTimeSeconds(StartMinute * 60).LocalDateTime;
}

/// <summary>
/// The heaviest five hours on record, swept out of the index rather than read from the API (T332).
///
/// <para><b>Why this is a different window from the one the app already shows.</b> Everywhere else,
/// five hours means the window the API anchors — it runs from the reset the headers report, and it
/// is the right frame for <em>how much is left</em>. It says nothing about a window that has already
/// closed. The transcripts hold every other one, and the heaviest of them is the window a limit
/// actually saw: measured across 34 active days here, the busiest five hours hold a median
/// <b>72%</b> of a day's work, so a daily total understates what the meter was pointed at.</para>
///
/// <para><b>Minute resolution, and why not finer or coarser.</b> The sweep runs over minute buckets,
/// which is the settled trade between exactness and what the per-file cache has to carry. Measured
/// over this machine's 83,581 turns: the exact per-turn peak is 23,434,297 tokens and the
/// minute-aligned sweep finds 23,430,121 — <b>0.018%</b> low, and right to the minute on when it
/// started — while storing 12,147 buckets instead of 83,581 rows. An hour-aligned frame was the
/// cheap alternative and it was 2.6% low with a start time up to an hour out, which is a caveat this
/// reading would then have had to carry on screen.</para>
///
/// <para><b>A measurement, never a prediction.</b> Nothing here knows the plan's real allowance — no
/// figure for it is published, and the quota state is a percentage rather than a budget — so this
/// says what the busiest window held and stops. It must never become "you can do 1.6 of these".</para>
/// </summary>
internal static class HeaviestWindow
{
    /// <summary>Five hours, the window the plan meters, in minutes.</summary>
    public const int Span = 5 * 60;

    /// <summary>
    /// The heaviest <paramref name="span"/>-minute stretch in <paramref name="minutes"/>, and the
    /// first minute that carried work inside it.
    /// </summary>
    /// <remarks>Two pointers over the occupied minutes rather than a scan of the range: a month of
    /// work is twelve thousand buckets and four hundred thousand empty minutes, so walking the frame
    /// minute by minute would do thirty times the work to reach the same answer.</remarks>
    public static PeakWindow Of(IReadOnlyDictionary<long, long>? minutes, int span = Span)
    {
        if (minutes is null || minutes.Count == 0) return default;

        long[] keys = minutes.Keys.Where(k => minutes[k] > 0).Order().ToArray();
        if (keys.Length == 0) return default;

        long best = 0, bestStart = 0, run = 0;
        int j = 0;
        for (int i = 0; i < keys.Length; i++)
        {
            run += minutes[keys[i]];
            // The frame is half-open: a bucket exactly `span` minutes older has left it.
            while (keys[i] - keys[j] >= span)
            {
                run -= minutes[keys[j]];
                j++;
            }
            if (run > best) { best = run; bestStart = keys[j]; }
        }
        return new PeakWindow(bestStart, best);
    }

    /// <summary>
    /// The median active day's own heaviest stretch — what the peak is compared against, because a
    /// large number with nothing beside it is not a fact.
    /// </summary>
    /// <remarks>Days are <em>local</em>, not UTC: the reader's question is about their day. Only days
    /// that carried work are counted — folding in the days a machine sat idle would halve the median
    /// and answer a question nobody asked.</remarks>
    public static long MedianDayPeak(IReadOnlyDictionary<long, long>? minutes, int span = Span)
    {
        if (minutes is null || minutes.Count == 0) return 0;

        var byDay = new Dictionary<DateTime, Dictionary<long, long>>();
        foreach (KeyValuePair<long, long> kv in minutes)
        {
            if (kv.Value <= 0) continue;
            DateTime day = DateTimeOffset.FromUnixTimeSeconds(kv.Key * 60).LocalDateTime.Date;
            if (!byDay.TryGetValue(day, out Dictionary<long, long>? bucket))
                byDay[day] = bucket = new Dictionary<long, long>();
            bucket[kv.Key] = kv.Value;
        }

        long[] peaks = byDay.Values.Select(d => Of(d, span).Tokens).Where(v => v > 0).Order().ToArray();
        if (peaks.Length == 0) return 0;
        // The lower of the two middles on an even count, so the comparison never flatters the peak.
        return peaks[(peaks.Length - 1) / 2];
    }

    /// <summary>How many local days carried any work at all — the sample the median stands on, so a
    /// caller can decline to word a comparison drawn from two days.</summary>
    public static int ActiveDays(IReadOnlyDictionary<long, long>? minutes)
    {
        if (minutes is null) return 0;
        var days = new HashSet<DateTime>();
        foreach (KeyValuePair<long, long> kv in minutes)
            if (kv.Value > 0)
                days.Add(DateTimeOffset.FromUnixTimeSeconds(kv.Key * 60).LocalDateTime.Date);
        return days.Count;
    }
}
