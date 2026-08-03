using System.Globalization;

namespace ClaudeTray;

/// <summary>
/// How this app writes a <b>date</b>, which is the other half of <see cref="Nums"/>'s rule: numbers are
/// invariant, dates follow the display language — and following it means the <em>order</em> too, not only
/// the words (T263).
///
/// <para><b>The defect.</b> Every date on the Statistics page was built from a custom pattern:
/// <c>"MMM d"</c> and <c>"d/M"</c>. A custom pattern pins the arrangement while the culture supplies only
/// the month name, so the axis read <em>"début août 3"</em> in French — a French sentence with English word
/// order inside it, which is worse than not translating at all. Measured over the five shipped cultures:
/// English's own <c>MonthDayPattern</c> is <c>MMMM d</c> and the other four are <c>d MMMM</c> or
/// <c>d 'de' MMMM</c>, so four of five were wrong.</para>
///
/// <para>And <c>"d/M"</c> was wrong in the other direction, for the one audience the published screenshots
/// are taken for: it renders <c>3/8</c> in every culture including <c>en-US</c>, whose short date puts the
/// month first, so the weekly chart's day dividers said 3/8 for 3 August and an American read 8 March.</para>
///
/// <para><b>The rule: take the order from the culture, never from a literal.</b> The System page already
/// did — it formats with the standard <c>d</c> and <c>g</c> specifiers and comes out right in all five.
/// What is here is the two shapes a standard specifier does not offer: a month and day without the year,
/// and a compact numeric day and month for an axis label.</para>
/// </summary>
internal static class Dates
{
    private static CultureInfo C => L.DateCulture;

    /// <summary>
    /// A month and a day, month named and abbreviated, in the order this culture puts them: "Aug 3",
    /// "3 août", "3 de ago". Derived from the culture's own <c>MonthDayPattern</c> with the full month
    /// name narrowed to the short one — an axis label and a status line are both tight, and narrowing a
    /// pattern keeps the arrangement while a rewritten pattern is what lost it.
    /// </summary>
    public static string MonthDay(DateTime local) =>
        local.ToString(C.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM"), C);

    /// <summary>A month and a day with the time after it: "Aug 3, 16:26" / "3 août, 16:26".</summary>
    public static string MonthDayTime(DateTime local) => MonthDay(local) + ", " + Time(local);

    /// <summary>
    /// The same two fields as digits, for a label drawn inside a plot where a month name will not fit:
    /// "8/3" for English, "3/8" for the other four. Order and separator both from the culture — the single
    /// <c>d</c> and <c>M</c> are deliberate, because <c>03/08</c> is two characters nobody has room for.
    /// </summary>
    public static string DayMonthDigits(DateTime local)
    {
        string shortDate = C.DateTimeFormat.ShortDatePattern;
        int m = shortDate.IndexOf('M', StringComparison.Ordinal);
        int d = shortDate.IndexOf('d', StringComparison.Ordinal);
        string sep = C.DateTimeFormat.DateSeparator;
        // A pattern naming neither (nothing shipped does) falls to day-first rather than throwing: the
        // point of this type is that a date is readable, and an unreadable pattern is not worth a crash.
        return local.ToString(m >= 0 && (d < 0 || m < d) ? $"M{sep}d" : $"d{sep}M", C);
    }

    /// <summary>Clock time. No order to get wrong, and named here so a call site has one place to go.</summary>
    public static string Time(DateTime local) => local.ToString("HH:mm", C);
}
