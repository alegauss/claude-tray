namespace ClaudeTray;

/// <summary>
/// One row of the Sessions pane grouped by day, week or month (T392): the period, how many conversations
/// touched it, its turns, its tokens and their list-price equivalent — the conversation list's columns,
/// summed over every turn that landed inside it.
///
/// <para>Public for the binding reason <see cref="SessionListRow"/> carries. No title and no prompt: a
/// period is many conversations, and §I.1's exception is about recognising one.</para>
/// </summary>
public sealed class PeriodListRow
{
    internal PeriodListRow(PeriodRow row, Period period)
    {
        DateTime start = row.Start.ToDateTime(TimeOnly.MinValue);
        // In the window's language and its order of fields (T263): a day by its weekday and date, a week
        // by its first day, a month by its own name.
        Label = period switch
        {
            Period.Month => start.ToString("Y", L.DateCulture),
            Period.Week => L.T("stats.sessions.weekOf", Dates.MonthDay(start)),
            _ => start.ToString("ddd", L.DateCulture) + " " + Dates.MonthDay(start),
        };
        Sessions = Nums.Of(row.Sessions, "N0");
        Turns = Nums.Of(row.Calls, "N0");
        // Cache reads out, as in every token figure this pane shows.
        Tokens = TokenEstimate.Format((int)Math.Min(int.MaxValue,
            row.Bits.Input + row.Bits.Output + row.Bits.CacheCreate));

        ListPrices.Equivalent price = row.Price;
        Money = price.PricedTokens > 0 ? L.T("stats.sessions.money", Nums.Of(price.Dollars, "0.00")) : "";
        MoneyTip = price.Complete
            ? L.T("stats.sessions.periodTip", ListPrices.Read.ToString("yyyy-MM-dd"))
            : L.T("stats.sessions.periodPartial", ListPrices.Read.ToString("yyyy-MM-dd"));
    }

    public string Label { get; }
    public string Sessions { get; }
    public string Turns { get; }
    public string Tokens { get; }
    public string Money { get; }
    public string MoneyTip { get; }
}
