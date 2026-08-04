namespace ClaudeTray;

/// <summary>
/// The Statistics previews, as one table both entry points read: <c>--stats &lt;variant&gt;</c> opens the
/// window and <c>--capture-stats &lt;out&gt; &lt;variant&gt;</c> renders the same page off-screen to PNGs.
///
/// <para><b>Why a table (T186).</b> The two flags used to parse their variants in two separate <c>if</c>
/// chains, so a variant added to one was not merely missing from the other — it was <em>ignored</em>, and
/// the capture fell through to the default sample. <c>--capture-stats … overage</c> produced a chart of
/// this machine's real 38% week, correctly rendered, with no error and no warning; it was caught only
/// because the number on the card was not the number the fixture specifies. A screenshot is what UI work
/// here is verified by, so a preview that quietly shows something else is worse than one that crashes.</para>
///
/// <para>Two rules keep them from drifting again. Every preview is one row here, so adding one is one
/// edit and both flags gain it. And an unrecognised token is <b>refused</b> with the list of what exists
/// (<see cref="Resolve"/>) instead of falling through to the default, so a typo is an error message
/// rather than a wrong picture.</para>
/// </summary>
internal static class StatsPreviews
{
    /// <summary>The 403 body a past-due subscription returns, verbatim, for the previews that show the
    /// error banner. Not localized: it is the upstream message the tray displays as received.</summary>
    internal const string PastDue = "Your subscription payment is past due. Please pay your overdue " +
        "invoice to restore access, or reach out to your company admin.";

    /// <summary>One preview. <c>Snapshot</c> takes the current unix second, so every fixture's resets sit
    /// the same distance from whenever it runs; returning null means "no live reading", which is the
    /// signed-out path. <c>Report</c>, when set, hands the page a whole hand-built report instead
    /// (<see cref="PreviewCli.BuildGapDemoReport"/>) once it has loaded.</summary>
    internal sealed record Variant(
        string Name,
        string What,
        Func<long, PaceSnapshot?> Snapshot,
        bool Live = false,
        bool Ghost = false,
        bool Overage = false,
        bool OverSpell = false,
        bool Thin = false,
        bool MethodOpen = false,
        bool Error = false,
        Func<long, PaceReport>? Report = null)
    {
        /// <summary>Whether <c>--capture-stats</c> can show this one. The method popup is its own
        /// top-level window and the capture is a <c>RenderTargetBitmap</c> over the page's content, so it
        /// would render a PNG of the page with no popup in it — the exact silent lie T186 is about.</summary>
        internal bool Capturable => !MethodOpen;
    }

    // The default reading both flags fall back to: 3h of a 5h session elapsed with 72% spent (ahead of
    // pace), 4d of the week elapsed with 38% spent (on track), so both verdicts render at once.
    private static PaceSnapshot? Sample(long now) => new PaceSnapshot(0.72, now + 2 * 3600, 0.38, now + 3 * 86400);

    private static readonly Variant[] All =
    {
        new("", "the default: a 5h session burning ahead of pace, a week comfortably on track", Sample),

        new("idle", "the not-using-Claude state: a fresh 5h session at 0%, a week still carrying usage, " +
                    "so the window opens on the weekly tab",
            now => new PaceSnapshot(0.0, now + 5 * 3600, 0.38, now + 3 * 86400)),

        new("shape", "the activity-aware weekly projection running out before the reset, so the " +
                     "staircase's landing marker and the usually-idle bands are drawn",
            now => new PaceSnapshot(0.0, now + 5 * 3600, 0.74, now + 3 * 86400)),

        new("overage", "a week whose included quota ran out part-way through and which kept working and " +
                       "billing: the clay series on a second axis (T183)",
            now => new PaceSnapshot(0.0, now + 5 * 3600, 1.0, now + 2 * 86400,
                                    Extra: 0.47, ResetExtra: now + 2 * 86400),
            Overage: true),

        new("overage-noamount", "the spell as it was actually measured (T275): the API said the account " +
                                "was past its included quota for a stretch of the week while the overage " +
                                "figure stayed at 0, so the band is drawn and there is no second axis",
            now => new PaceSnapshot(0.0, now + 5 * 3600, 1.0, now + 2 * 86400,
                                    Extra: 0, ResetExtra: now + 2 * 86400),
            OverSpell: true),

        new("live", "a deterministic synthetic three minutes in the throughput strip, instead of the real " +
                    "tail — which cannot be screenshotted twice the same way",
            Sample, Live: true),

        new("error", "the API-error state (a 403 payment-past-due): charts from the last known local " +
                     "data, with the banner, and any real gaps drawn as unavailable spans",
            Sample, Error: true),

        new("history", "the offline fallback: no live reading, so the snapshot is rebuilt from the last " +
                       "one persisted in usage-history.jsonl, exactly as a signed-out launch does",
            _ => UsageHistory.Latest(ProfileStore.Monitored) is { } h
                ? new PaceSnapshot(h.Util5h, h.Reset5h, h.Util7d, h.Reset7d, h.Extra, h.ResetExtra)
                : null),

        new("gapdemo", "a hand-built report with a past data gap that has since recovered: no banner, but " +
                       "the outage still marked red on the curve (add 'ongoing' for the banner too)",
            _ => null, Report: PreviewCli.BuildGapDemoReport),

        new("method", "the window with the method note already open — the popup is its own top-level " +
                      "window, so this is the path that gets it on screen for a screen-copy capture",
            Sample, Live: true, MethodOpen: true),

        new("thin", "the same note, reporting as if the local history were still too thin to shape the " +
                    "projection (T163) — a paragraph a confident machine can never show again",
            Sample, Live: true, MethodOpen: true, Thin: true),
    };

    // Tokens that name no preview of their own but modify whichever one was asked for. `ghost` and `live`
    // are also rows above (a preview *is* that flag on the default reading); naming them here as well is
    // what makes `shape ghost` and `overage live` legal on both flags rather than on one of them.
    private static readonly (string Name, string What)[] Modifiers =
    {
        ("ghost", "draw a synthetic previous week behind the curve (a real ghost needs two folded weeks)"),
        ("live", "feed the throughput strip the deterministic three minutes"),
        ("remaining", "state the figures as quota remaining rather than used"),
        ("ongoing", "gapdemo only: show the error banner too, i.e. mid-outage rather than recovered"),
        ("refresh", "--capture-stats only: snapshot while a fresh reading is still recomputing (T118)"),
    };

    /// <summary>The table itself, for the self-check that every row it declares still resolves and that
    /// an invented name does not (T207). Exposed rather than duplicated: a list of names written out in
    /// the check would be the hand-maintained copy T203 has just finished removing elsewhere.</summary>
    internal static IReadOnlyList<Variant> Catalogue => All;

    /// <summary>The modifier rows, same reason.</summary>
    internal static IReadOnlyList<(string Name, string What)> ModifierRows => Modifiers;

    /// <summary>A variant with its modifiers folded in: what the caller sets on the page.</summary>
    internal sealed record Choice(Variant Variant, bool Ghost, bool Live, bool Remaining, bool Ongoing, bool Refresh)
    {
        internal string? Error => Variant.Error || (Ongoing && Variant.Report is not null) ? PastDue : null;
    }

    /// <summary>
    /// Resolve the bare words of a preview command into one <see cref="Choice"/>. The first token naming a
    /// row wins as the variant; the rest must be modifiers or rows, and <b>anything else is refused</b> —
    /// returning null after printing what exists, so the caller exits non-zero rather than rendering the
    /// default and calling it the thing that was asked for.
    /// </summary>
    /// <param name="words">The command's bare words: no <c>--flags</c>, no <c>name=value</c> pairs, and
    /// without the output path of <c>--capture-stats</c>.</param>
    /// <param name="capturing">True for <c>--capture-stats</c>, which additionally refuses a variant it
    /// cannot actually show (<see cref="Variant.Capturable"/>).</param>
    internal static Choice? Resolve(IEnumerable<string> words, bool capturing)
    {
        string[] given = words.ToArray();

        foreach (string w in given)
            if (Find(w) is null && !Modifiers.Any(m => Same(m.Name, w)))
            {
                Console.WriteLine($"unknown preview '{w}'. Refusing rather than showing the default one, " +
                                  "which is how a screenshot of the wrong thing gets taken (T186).");
                PrintCatalog();
                return null;
            }

        Variant v = given.Select(Find).FirstOrDefault(f => f is not null) ?? All[0];
        if (capturing && !v.Capturable)
        {
            Console.WriteLine($"--capture-stats cannot show '{v.Name}': the method note is its own " +
                              "top-level window and this capture is a RenderTargetBitmap over the page's " +
                              $"content, so the PNG would not contain it. Use --stats {v.Name} with " +
                              @"scripts\Capture-Window.ps1.");
            return null;
        }

        bool Has(string m) => given.Any(w => Same(w, m));
        return new Choice(v,
            Ghost: v.Ghost || Has("ghost"),
            Live: v.Live || Has("live"),
            Remaining: Has("remaining"),
            Ongoing: Has("ongoing"),
            Refresh: Has("refresh"));
    }

    /// <summary>Build the page one <see cref="Choice"/> asks for, fed and flagged. The caller still owns
    /// the window, the profile list and (for a capture) the settle timer.</summary>
    internal static StatisticsPage Build(Choice c, long now)
    {
        var page = new StatisticsPage(c.Variant.Snapshot(now), c.Remaining, c.Error)
        {
            PreviewDemoLive = c.Live,
            PreviewDemoGhost = c.Ghost,
            PreviewDemoOverage = c.Variant.Overage,
            PreviewDemoOverSpell = c.Variant.OverSpell,
            PreviewDemoThin = c.Variant.Thin,
            PreviewMethodOpen = c.Variant.MethodOpen,
        };
        // A whole hand-built report replaces the computed one, which can only happen once the page has
        // loaded — the same order the interactive preview used before this table existed.
        if (c.Variant.Report is { } build)
            page.Loaded += (_, _) => page.PreviewReport(build(now), c.Error);
        return page;
    }

    private static Variant? Find(string word) => All.FirstOrDefault(v => v.Name.Length > 0 && Same(v.Name, word));

    private static bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static void PrintCatalog()
    {
        Console.WriteLine();
        Console.WriteLine("previews (--stats <variant> / --capture-stats <out> <variant>):");
        foreach (Variant v in All)
            Console.WriteLine($"  {(v.Name.Length > 0 ? v.Name : "(none)"),-9}{(v.Capturable ? " " : "*")} {v.What}");
        Console.WriteLine("  * --stats only; --capture-stats cannot see a popup window");
        Console.WriteLine();
        Console.WriteLine("modifiers (any number, in any order):");
        foreach ((string name, string what) in Modifiers)
            Console.WriteLine($"  {name,-10} {what}");
    }
}
