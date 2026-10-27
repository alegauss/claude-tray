namespace ClaudeTray;

/// <summary>The calendar unit a total is taken over (T392).</summary>
internal enum Period { Day, Week, Month }

/// <summary>One calendar period's work: what the session list's columns say, summed over every turn that
/// landed inside it. <paramref name="Start"/> is the period's first local day.</summary>
internal sealed record PeriodRow(DateOnly Start, TokenBits Bits, int Calls, int Sessions,
                                 IReadOnlyDictionary<string, TokenBits> PerModel)
{
    /// <summary>At API list prices — an equivalent, not a bill (§I.7), and partial where a model is one
    /// the rate table does not know.</summary>
    public ListPrices.Equivalent Price => ListPrices.Of(PerModel);

    /// <summary>The models that answered, heaviest first.</summary>
    public IEnumerable<string> Models => PerModel.Where(kv => kv.Key.Length > 0)
                                                 .OrderByDescending(kv => kv.Value.Total).Select(kv => kv.Key);
}

/// <summary>
/// What a day, a week or a month came to (T392) — folded from the quarter-hour split the session index
/// keeps, so it costs no transcript read.
///
/// <para><b>A turn counts on the local day it landed</b>, not on the day its conversation started: a
/// session that runs past midnight is split across the two days, which is the one thing a row of the
/// session list cannot show. The index stores UTC quarter hours, and every offset in use is a multiple of
/// fifteen minutes, so no bucket straddles a local midnight whatever zone this runs in.</para>
///
/// <para><b>Only what is on disk.</b> Claude Code deletes old transcripts, so the oldest period is
/// wherever the files start; <see cref="DataStarts"/> says where that is, so a short history reads as
/// short rather than as quiet weeks before it. Periods with no turn are not listed.</para>
/// </summary>
internal static class PeriodTotals
{
    /// <summary>The periods that hold any turn, newest first.</summary>
    /// <param name="zone">The zone a turn's local day is taken in — <see cref="TimeZoneInfo.Local"/>
    /// outside a check.</param>
    /// <param name="firstDay">The day a week opens on.</param>
    public static List<PeriodRow> Fold(IEnumerable<SessionRow> rows, Period period, TimeZoneInfo zone,
                                       DayOfWeek firstDay = DayOfWeek.Monday)
    {
        var acc = new Dictionary<DateOnly, (Dictionary<string, long[]> Models, int Calls, HashSet<string> Sessions)>();
        foreach (SessionRow r in rows)
        {
            if (r.Quarters is null) continue;
            foreach (QuarterSpend q in r.Quarters)
            {
                DateOnly start = StartOf(LocalDay(q.Quarter, zone), period, firstDay);
                if (!acc.TryGetValue(start, out var slot))
                    acc[start] = slot = (new Dictionary<string, long[]>(StringComparer.Ordinal), 0, new HashSet<string>(StringComparer.Ordinal));
                if (!slot.Models.TryGetValue(q.Model, out long[]? m)) slot.Models[q.Model] = m = new long[6];
                m[0] += q.Bits.Input; m[1] += q.Bits.Output; m[2] += q.Bits.CacheCreate;
                m[3] += q.Bits.CacheRead; m[4] += q.Bits.CacheCreate1h; m[5] += q.Bits.CacheCreate5m;
                slot.Sessions.Add(r.Project + "\0" + r.Session);
                acc[start] = slot with { Calls = slot.Calls + q.Calls };
            }
        }

        var list = new List<PeriodRow>(acc.Count);
        foreach (var (start, slot) in acc)
        {
            var perModel = slot.Models.ToDictionary(
                kv => kv.Key, kv => new TokenBits(kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3], kv.Value[4], kv.Value[5]),
                StringComparer.Ordinal);
            var sum = new TokenBits(perModel.Values.Sum(b => b.Input), perModel.Values.Sum(b => b.Output),
                                    perModel.Values.Sum(b => b.CacheCreate), perModel.Values.Sum(b => b.CacheRead),
                                    perModel.Values.Sum(b => b.CacheCreate1h), perModel.Values.Sum(b => b.CacheCreate5m));
            list.Add(new PeriodRow(start, sum, slot.Calls, slot.Sessions.Count, perModel));
        }
        list.Sort((a, b) => b.Start.CompareTo(a.Start));
        return list;
    }

    /// <summary>The first local day any turn on file landed, or null when there is none.</summary>
    public static DateOnly? DataStarts(IEnumerable<SessionRow> rows, TimeZoneInfo zone)
    {
        long? first = null;
        foreach (SessionRow r in rows)
            if (r.Quarters is not null)
                foreach (QuarterSpend q in r.Quarters)
                    if (first is null || q.Quarter < first) first = q.Quarter;
        return first is { } f ? LocalDay(f, zone) : null;
    }

    private static DateOnly LocalDay(long quarter, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            DateTimeOffset.FromUnixTimeSeconds((long)(quarter * SessionIndex.Quarter)), zone).DateTime);

    /// <summary>The first day of the period <paramref name="day"/> falls in.</summary>
    public static DateOnly StartOf(DateOnly day, Period period, DayOfWeek firstDay) => period switch
    {
        Period.Week => day.AddDays(-(((int)day.DayOfWeek - (int)firstDay + 7) % 7)),
        Period.Month => new DateOnly(day.Year, day.Month, 1),
        _ => day,
    };

    /// <summary>A period's name as the read-out prints it: the day, the week's first day, or the month.
    /// Digits only, so no culture decides a word in it.</summary>
    public static string Label(DateOnly start, Period period) => period switch
    {
        Period.Month => $"{start.Year:D4}-{start.Month:D2}",
        Period.Week => $"wk {start.Year:D4}-{start.Month:D2}-{start.Day:D2}",
        _ => $"{start.Year:D4}-{start.Month:D2}-{start.Day:D2}",
    };
}
