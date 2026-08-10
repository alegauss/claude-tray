namespace ClaudeTray;

/// <summary>
/// One row of the Sessions list.
///
/// <para>Public, like <see cref="ProjectRow"/> and the view models beside it, because WPF resolves a
/// <c>{Binding}</c> path by reflection over a public type — an internal one binds to nothing, and does
/// it silently.</para>
///
/// <para><b>What a row may say is the whole design (§I.1).</b> Project, clock, duration, turns,
/// tokens. Not the prompt, not a title, not a summary: a list of conversations is precisely where a
/// person wants a subject line, which makes this the surface that would erode the promise. Project
/// plus clock is enough to recognise the morning being looked for, and the session id sits on the
/// hover for matching against <c>--resume</c> — an identifier, not content.</para>
/// </summary>
public sealed class SessionListRow
{
    internal SessionListRow(SessionRow row, DateTime nowLocal)
    {
        Row = row;
        DateTime when = DateTimeOffset.FromUnixTimeSeconds((long)row.LastUnix).LocalDateTime;

        Project = row.Name;
        // Today and yesterday read as a time; anything older needs its date. A list whose first
        // column is "19:10" for a conversation last week is a list that has to be counted backwards.
        When = when.Date == nowLocal.Date
            ? when.ToString("HH:mm")
            : when.Date == nowLocal.Date.AddDays(-1)
                ? L.T("stats.sessions.yesterday", when.ToString("HH:mm"))
                : when.ToString("MMM d, HH:mm");
        Duration = Span(row.Seconds);
        Turns = Nums.Of(row.Calls, "N0");
        // Cache reads are out, as everywhere else a token figure is shown here: they are two orders of
        // magnitude larger than the work and would be the only number anyone read.
        Tokens = TokenEstimate.Format((int)Math.Min(int.MaxValue,
            row.Bits.Input + row.Bits.Output + row.Bits.CacheCreate));

        // The hover carries the identifiers, which is what makes a row actionable without making it
        // descriptive: the id is what `claude --resume` takes.
        Tip = L.T("stats.sessions.tip", row.Session,
                  row.Models.Length > 0 ? string.Join(", ", row.Models) : "—",
                  row.Agents);
    }

    internal SessionRow Row { get; }

    public string Project { get; }
    public string When { get; }
    public string Duration { get; }
    public string Turns { get; }
    public string Tokens { get; }
    public string Tip { get; }

    /// <summary>Minutes under an hour, hours over it. One unit that reads well at both ends of "three
    /// minutes" and "an afternoon" does not exist, and rounding an afternoon to 380 minutes is worse
    /// than switching.</summary>
    /// <remarks>Through <see cref="Nums"/>, like every other number this app puts in front of a reader
    /// (T216) — the first capture of this pane read "1,2 h" in the English screenshot, which is the
    /// exact defect that rule exists for. A static formatter rather than an inline interpolation so
    /// <c>--selftest</c>'s format sweep can reach it; inline is what keeps a number out of it.</remarks>
    private static string Span(double seconds)
        => seconds >= 3600
            ? L.T("stats.sessions.hours", Nums.Of(seconds / 3600, "0.0"))
            : L.T("stats.sessions.minutes", Nums.Of(Math.Max(1, Math.Round(seconds / 60)), "0"));
}
