namespace ClaudeTray;

/// <summary>
/// One localized fragment of the method note: the resource key, and the arguments that string takes.
/// An argument is either a number already through <c>Num</c> (a <see cref="string"/>) or a nested
/// <see cref="NoteFragment"/> — a clause that has to sit <em>inside</em> a sentence rather than beside
/// it, because where it goes is a property of the language and not of the count.
/// </summary>
internal sealed record NoteFragment(string Key, params object[] Args);

/// <summary>Part of <see cref="StatisticsPage"/> — the method note's composition, split out by T168.</summary>
internal partial class StatisticsPage
{
    /// <summary>
    /// Which paragraphs the method note is made of, and in what order — the whole decision, as a pure
    /// function of the report (T168).
    /// </summary>
    /// <remarks>
    /// Which paragraphs the note contains stopped being a formatting detail during Block Z. There are
    /// four inputs (shaped or not, mostly-measured or not, thin or not, weeks excluded or not) and a real
    /// rule over them, most of it written in one week: <c>thin</c> appears only when the shape was
    /// declined for <b>thinness</b> and never at the limit or with nothing spent (T163); the away clause
    /// appears only when a week was actually dropped (T159); <c>shaped</c> and <c>thin</c> are mutually
    /// exclusive by construction. All of it used to be inline in <c>Render</c>, in a WPF page behind a
    /// <see cref="PaceReport"/>, so <c>--selftest</c> could not see any of it and the only verification
    /// those rules ever had was that somebody looked at two screenshots.
    /// <para>The seam is this function: report in, ordered fragments out, with <c>Render</c> doing nothing
    /// but <c>L.T</c> and concatenation. The T163 rule is the one worth pinning hardest, because getting
    /// it wrong produces a <em>plausible</em> sentence — telling somebody to keep the tray running another
    /// week when the real reason there is no shaped projection is that they are already at the limit.</para>
    /// <para>It is also where §XV.1's rule lands: a note assembled by one function is a note whose numbers
    /// go through one formatter (T167).</para>
    /// </remarks>
    /// <param name="demoThin">The <c>thin</c> preview posing as a machine whose history is too short.
    /// A parameter rather than the page's own property, because a pure function is the point.</param>
    internal static IReadOnlyList<NoteFragment> MethodNoteParts(PaceReport r, bool demoThin)
    {
        ActivityProfile? activity = r.Activity;

        // The weekly projection changes meaning when it follows the activity shape, so the note has to
        // say so — including the part that can't be measured locally: usage from another machine or from
        // claude.ai spends the same quota while leaving no transcript here.
        bool shaped = r.Weekly.Shape is not null && !demoThin;

        // That disclaimer stops being true to the degree the measured grid takes over (T93), so past half
        // the grid the note says where the shape actually came from instead. A stale disclaimer is its own
        // kind of wrong: it would keep apologising for a blind spot the projection no longer has.
        bool mostlyMeasured = activity is { MeasuredShare: >= 0.5 };

        // The straight-line case says which line it is and why (T163) — but only when the shape was
        // declined for *thinness*. `Build` also returns null at the limit or with nothing spent, and
        // "keep the tray running another week" would be a lie about either.
        bool thin = !shaped && r.Weekly.HasWindow && (demoThin || activity is { Confident: false });

        List<NoteFragment> parts = new() { new NoteFragment("stats.methodNote") };

        if (shaped)
            // Both week figures are the *effective* weeks — the span minus the weeks the grid dropped as
            // time away — because that is the number the confidence gate acted on (T159).
            parts.Add(mostlyMeasured
                ? new NoteFragment("stats.methodNote.shapeMeasured",
                                   Num(activity!.MeasuredShare * 100, "0"),
                                   Num(activity.EffectiveMeasuredWeeks),
                                   WeeksAway(activity.MeasuredExcludedWeeks))
                : new NoteFragment("stats.methodNote.shape",
                                   Num(r.Weekly.Shape!.EffectiveWeeks),
                                   WeeksAway(r.Weekly.Shape!.ExcludedWeeks)));
        else if (thin)
        {
            // Under the preview the machine's own figure is past the bar, which would print "4.7 of 3" —
            // a sentence the string is never shown with. Show a figure short of the bar instead.
            double weeksSoFar = demoThin
                ? ActivityProfile.ConfidentWeeks - 0.9
                : activity?.EffectiveWeeks ?? 0;
            parts.Add(new NoteFragment("stats.methodNote.thin",
                                       Num(weeksSoFar), Num(ActivityProfile.ConfidentWeeks)));
        }

        // The live strip has the same blind spot the shaped projection discloses, and it needs saying
        // separately: a rate built from local transcripts cannot see another machine or claude.ai, so an
        // empty strip is "no local turn landed", never proof that nothing is running.
        parts.Add(new NoteFragment("stats.methodNote.live"));
        return parts;
    }

    /// <summary>The "why the figure is smaller than the history" clause, or the empty string when no week
    /// was dropped. Localized as a clause rather than assembled from words, because where it can sit
    /// inside the sentence is a property of the language, not of the count — and where it says nothing it
    /// says nothing at all: a parenthetical that always reads "(0 excluded)" trains people to stop
    /// reading the note.</summary>
    private static object WeeksAway(int weeks) =>
        weeks <= 0 ? ""
        : weeks == 1 ? new NoteFragment("stats.methodNote.away.one")
        : new NoteFragment("stats.methodNote.away.many", Num(weeks, "0"));

    /// <summary>A fragment as text: <c>L.T</c> and concatenation, nested clauses resolved first. The whole
    /// of what <c>Render</c> is left doing with the note.</summary>
    private static string Say(NoteFragment f) =>
        f.Args.Length == 0
            ? L.T(f.Key)
            : L.T(f.Key, f.Args.Select(a => a is NoteFragment nested ? Say(nested) : a).ToArray());
}
