namespace ClaudeTray;

/// <summary>
/// One localized fragment of the method note: the resource key, and the arguments that string takes.
/// An argument is either a number already through <c>Num</c> (a <see cref="string"/>) or a nested
/// <see cref="NoteFragment"/> — a clause that has to sit <em>inside</em> a sentence rather than beside
/// it, because where it goes is a property of the language and not of the count.
/// </summary>
internal sealed record NoteFragment(string Key, params object[] Args);

/// <summary>One headed paragraph of the method note, as the popup binds it. <c>public</c> because WPF
/// resolves binding paths by reflection over public types only.</summary>
public sealed record NoteLine(string Heading, string Body);

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

        // The Sessions pane's money column is deliberately NOT a paragraph here (T346). Measured before
        // deciding: this note's worst shape already stands at 1,302 of its 1,350-character budget in
        // French, so the room left for a fifth surface is 48 characters — not a sentence. And the budget
        // is not the only argument: this note is about every number in the window, while the list-price
        // equivalent is about one column. It has its own ⓘ beside the list, which is where a reader
        // looking at that column is already looking.
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

    /// <summary>
    /// Which surface each paragraph is about — the heading it is filed under in the popup (T170).
    /// </summary>
    /// <remarks>
    /// The note is not merely long, it is <em>unstructured</em>: measured across all five languages the
    /// worst case is 1,276 characters (fr, mostly-measured) and <b>61–62% of it is constant in every
    /// language</b> — <c>stats.methodNote</c> and <c>stats.methodNote.live</c> are always shown and never
    /// vary. The paragraph that is actually about <em>this machine's evidence</em> is the other 39%, and
    /// it sits in the middle of the wall, which is why nobody finds it.
    /// <para>So the headings, keyed to the surfaces in the order the window shows them. Nothing is cut:
    /// the "another machine or claude.ai leaves no transcript here" disclaimer is a privacy-adjacent
    /// honesty about a blind spot, and the T163 paragraph is the one actionable reading in the window.
    /// Moving the 61% to the README was the other candidate and is refused — it would put the definition
    /// of "Used" and the blind-spot disclaimer where nobody reading the number will look, which is the
    /// thing T113 decided against. A heading makes them skippable, which is what moving them was for.</para>
    /// <para>Explicit, with no catch-all arm: a fragment added later gets <c>null</c> and a red
    /// <c>--selftest</c>, rather than silently filing itself under the weekly projection.</para>
    /// </remarks>
    private static readonly Dictionary<string, string> Headings = new(StringComparer.Ordinal)
    {
        ["stats.methodNote"] = "stats.methodNote.h.numbers",
        ["stats.methodNote.shape"] = "stats.methodNote.h.weekly",
        ["stats.methodNote.shapeMeasured"] = "stats.methodNote.h.weekly",
        ["stats.methodNote.thin"] = "stats.methodNote.h.weekly",
        
        ["stats.methodNote.live"] = "stats.methodNote.h.live",
    };

    /// <summary>The heading key a fragment is filed under, or null when nothing files it.</summary>
    internal static string? HeadingFor(string fragmentKey) => Headings.GetValueOrDefault(fragmentKey);

    /// <summary>The note as the popup shows it: a headed paragraph per fragment, in window order.</summary>
    /// <remarks>An unfiled fragment gets no heading rather than its own key: <c>L.T</c> of a key it does
    /// not know returns the key, and the fragment keys <em>are</em> the note's own strings, so the
    /// obvious fallback rendered a whole 251-character paragraph as a bold heading. Seen while watching
    /// the heading assertion fail, which is the argument for breaking a check on purpose.</remarks>
    internal static IReadOnlyList<NoteLine> MethodNoteLines(PaceReport r, bool demoThin) =>
        MethodNoteParts(r, demoThin)
            .Select(f => new NoteLine(HeadingFor(f.Key) is { } h ? L.T(h) : "", Say(f)))
            .ToList();

    /// <summary>A fragment as text: <c>L.T</c> and concatenation, nested clauses resolved first. The whole
    /// of what <c>Render</c> is left doing with the note.</summary>
    private static string Say(NoteFragment f) =>
        f.Args.Length == 0
            ? L.T(f.Key)
            : L.T(f.Key, f.Args.Select(a => a is NoteFragment nested ? Say(nested) : a).ToArray());
}
