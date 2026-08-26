namespace ClaudeTray;

/// <summary>
/// One row of the linking plan on the Claude Code settings page (T370): an entry of a config dir, what
/// would happen to it, and the one figure a reader can check.
///
/// <para><b>The plan is the surface, and the script is the artifact.</b> §XCIII.1 settled that: what
/// belongs on the page is ten rows and four verdicts, not four hundred lines of PowerShell. So this is
/// the whole reading a person makes their decision on, and the script is what the button then produces.
/// </para>
///
/// <para><b>Localized, where <see cref="ProfileLink"/> is not.</b> The catalogue's <c>Why</c> and its
/// <c>Unit</c> are English, because their audience is the emitted script; these three strings are the
/// window's, so they come from <c>lang</c>. That split is also why <c>Detail</c> never renders the
/// catalogue's unit noun: "union by session uuid" would be half-translated on four of the five
/// languages. A count needs no noun — <c>{0} to copy over</c> reads correctly in all of them, and the
/// unit is in the script where it is already English.</para>
///
/// <para><c>public</c> for the reason every view model beside it is: WPF resolves a <c>{Binding}</c> path
/// by reflection over public types only, and an internal one binds to nothing, silently.</para>
/// </summary>
public sealed class LinkPlanRow
{
    /// <summary>The entry's own name — <c>projects</c>, <c>history.jsonl</c>. Never translated: it is a
    /// path on disk, and a reader comparing this list against an Explorer window needs the same word.</summary>
    public string Name { get; init; } = "";

    /// <summary>The verdict as one word: merged, adopted, not offered, never.</summary>
    public string Verdict { get; init; } = "";

    /// <summary>What that means for this entry, in a phrase — the count where there is one.</summary>
    public string Detail { get; init; } = "";

    /// <summary>Whether this row is one the script acts on. The two refusals and the two absences are
    /// dimmed rather than dropped, because an entry that is simply missing from the list reads as an entry
    /// the app has no opinion about — which is how somebody links the credentials file by hand.</summary>
    public bool Acts { get; init; }

    /// <summary>The plan as rows, in the catalogue's order. <c>internal</c> where the properties above are
    /// public: binding reads the properties by reflection, and only they have to be reachable that way —
    /// the factory takes an <c>internal</c> type and cannot be public even if it wanted to be.</summary>
    internal static List<LinkPlanRow> From(ProfileLink.Plan plan) =>
        plan.Steps.Select(Of).ToList();

    private static LinkPlanRow Of(ProfileLink.Step s) => new()
    {
        Name = s.Entry.Name,
        Acts = Links(s) && s.OnPrimary && !s.AlreadyLinked,
        Verdict = L.T(s.Entry.Verdict switch
        {
            ProfileLink.Verdict.Merge => "settings.cc.linkMerged",
            ProfileLink.Verdict.Adopt => "settings.cc.linkAdopted",
            ProfileLink.Verdict.Withheld => "settings.cc.linkWithheld",
            _ => "settings.cc.linkNever",
        }),
        Detail = DetailOf(s),
    };

    private static bool Links(ProfileLink.Step s) =>
        s.Entry.Verdict is ProfileLink.Verdict.Merge or ProfileLink.Verdict.Adopt;

    /// <summary>
    /// What happens to this entry, in the order the questions actually resolve: the two refusals first,
    /// because a verdict of Never is true whatever the directories hold; then the two states of the disk
    /// that override the verdict; then the verdict itself.
    /// </summary>
    private static string DetailOf(ProfileLink.Step s)
    {
        if (s.Entry.Verdict == ProfileLink.Verdict.Never) return L.T("settings.cc.linkNeverDetail");
        // The withheld row carries the figure the decision is about (T373): "your decision" on its own is
        // what this task existed to fix, and the number is the shortest honest form of the answer. Not read
        // and nothing-to-decide are separate answers from a measured zero, on the same rule as the counts.
        if (s.Entry.Verdict == ProfileLink.Verdict.Withheld)
            return s.Widening switch
            {
                null or { Error.Length: > 0 } => L.T("settings.cc.linkWithheldDetail"),
                { Empty: true } => L.T("settings.cc.linkWithheldSame"),
                { } w => L.N("settings.cc.linkWithheldGrants", w.Granting),
            };
        if (s.AlreadyLinked) return L.T("settings.cc.linkAlready");
        if (!s.OnPrimary) return L.T("settings.cc.linkAbsent");
        if (s.Entry.Verdict == ProfileLink.Verdict.Adopt) return L.T("settings.cc.linkWhole");
        // A merge, and the count is the figure worth showing where there is one. Null is not zero: it is
        // history.jsonl, which could only be counted by opening it (§I.1).
        if (s.ToCopy is not { } n) return L.T("settings.cc.linkByLine");
        return n > 0 ? L.T("settings.cc.linkCopy", Nums.Of(n)) : L.T("settings.cc.linkCopyNone");
    }
}
