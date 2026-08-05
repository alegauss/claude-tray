namespace ClaudeTray;

/// <summary>
/// What the tray tooltip says, composed from an explicit reading rather than from the running tray
/// (T214).
///
/// <para><b>Why this is not a method on <see cref="TrayContext"/> any more.</b> A tooltip is not a
/// window: it is <c>NOTIFYICONDATA.szTip</c>, drawn by the shell, appearing only while a pointer rests
/// over an icon that may itself be inside the overflow flyout. There is nothing to screenshot and
/// nothing to read out of the accessibility tree — so the only way this surface can be reviewed at all
/// is to compose it away from the tray and print it. It was an instance method reading six fields of a
/// live tray, which meant the most-seen text this app produces was verified by hovering over an icon
/// and looking, and the one published picture of it drifted a whole release out of date without
/// anything noticing.</para>
///
/// <para>Everything that decides the text is in <see cref="Input"/>, so <c>--tooltip</c> prints exactly
/// what the tray would show for a given reading, in any language, with no screen and no poll.</para>
/// </summary>
internal static class TooltipText
{
    /// <summary>The Windows tray tooltip's hard limit — <c>NOTIFYICONDATA.szTip</c> is 128 wide
    /// including its terminator, so 127 characters survive. Everything below fits inside it.</summary>
    internal const int Cap = 127;

    /// <summary>
    /// Everything the composed text depends on. Deliberately values rather than the tray's own objects:
    /// the projection verdict and the quota state are each computed from several fields the tray already
    /// holds, and re-deriving them here would be a second implementation that could disagree with the
    /// icon beside the tooltip.
    /// </summary>
    /// <param name="Data">The reading, or null before the first poll has returned.</param>
    /// <param name="Metric">Which window the icon is about: <c>5h</c>, <c>7d</c> or <c>extra</c>.</param>
    /// <param name="ShowRemaining">The display option that states quota left rather than used.</param>
    /// <param name="ProfileLabel">The watched profile's name, or null when only one is watched — a
    /// percentage without an owner is a lie, and this line is kept even when the budget is tight.</param>
    /// <param name="Updated">The already-formatted "when this was read" suffix, or empty.</param>
    /// <param name="SpellSince">The unix second the current overage spell was first measured at, or 0 when
    /// the account is not spending past its quota or the crossing is not on file (T280). A moment rather
    /// than a duration, so the line ages between two polls instead of freezing at the last one — and
    /// <see cref="OverageSpell"/>, not this, decides when there is one to state.</param>
    internal sealed record Input(
        UsageData? Data,
        string Metric,
        bool ShowRemaining,
        string? ProfileLabel,
        Projection Verdict,
        double Eta,
        QuotaState State,
        string Updated,
        long Now,
        long SpellSince = 0);

    /// <summary>
    /// The tooltip for one reading, newline-separated and already within <see cref="Cap"/>.
    ///
    /// <para>The budget is the interesting part and the reason this is worth being able to read: the
    /// status line carries the refresh time and must survive whole, so a blind end-truncation would chop
    /// it mid-value. Instead each optional sentence is fitted — full form if it fits, else its compact
    /// fallback, else dropped entirely — and everything above it is kept. There are two of them now, the
    /// projection and the overage spell's duration (T280), fitted in that order because the later one
    /// qualifies news the earlier one carries.</para>
    /// </summary>
    internal static string Compose(Input i)
    {
        if (i.Data is not { } data) return L.T("tip.connecting");
        if (data.Error != null)
        {
            if (data.Unauthorized)
                return data.NeedsFullLogin ? L.T("tip.notSignedIn") : L.T("tip.willAppear");
            return L.T("tip.apiError", data.Error);
        }

        string r5 = data.Reset5h > 0 ? TrayContext.FmtCountdown(data.Reset5h - i.Now) : "--";
        string r7 = data.Reset7d > 0 ? TrayContext.FmtDays(data.Reset7d - i.Now) : "--";

        // In "remaining" mode the two bounded windows show the complement and read "… left".
        // Extra is overage (no cap to have quota "left" from), so it always shows the amount used.
        string leftSuffix = i.ShowRemaining ? L.T("tip.leftSuffix") : "";
        var lines = new List<string>();
        // With more than one profile, a percentage without an owner is a lie. The label goes first and
        // stays: the budget below drops the verbose projection form before this, which is the right
        // trade — knowing *whose* quota this is matters more than a wordier projection sentence.
        if (i.ProfileLabel is { Length: > 0 } label)
            lines.Add(L.T("tip.profile", Clip(label, MaxLabel)));
        string session = $"{L.T("tip.session")}{leftSuffix}: {TrayContext.PctShown(data.Session5h, i.ShowRemaining)}  ⟳ {r5}";
        string week = $"{L.T("tip.week")}{leftSuffix}: {TrayContext.PctShown(data.Week7d, i.ShowRemaining)}  ⟳ {r7}";
        lines.Add(session);
        lines.Add(week);
        // The overage line and the billing sentence are the same fact twice, and having both is why
        // neither fitted (T222). The reading only exists while overage is being spent, and it cost about
        // what the one sentence about that state needed — so in all five languages `atlimit` rendered its
        // sentence and `extra` rendered none, leaving the two opposite pieces of news T182 split apart
        // looking identical on screen. Merged, the sentence becomes the reading's own label: T182's words
        // are kept verbatim rather than shortened, because what they say is a decision, not a length.
        bool payingOnItsOwnLine = i.State == QuotaState.Billing && data.Extra > 0.001;
        if (data.Extra > 0.001)
        {
            string re = data.ResetExtra > 0 ? TrayContext.FmtDays(data.ResetExtra - i.Now) : "--";
            string spent = TrayContext.Pct(data.Extra);
            lines.Add(payingOnItsOwnLine
                ? $"{L.T("tip.extraPaying", spent)}  ⟳ {re}"
                : $"{L.T("tip.extra")}: {spent}  ⟳ {re}");
        }

        string scope = TrayContext.MetricLabel(i.Metric); // make clear which window the projection is about
        bool hasEta = i.Eta > 0 && !double.IsInfinity(i.Eta);
        // The projection is about reaching the limit: "100%" used == "0% left" remaining.
        string limit = i.ShowRemaining ? L.T("tip.limitLeft") : L.T("tip.limitUsed");
        // The same target as a *future event*. In "remaining" mode "0% left" mirrors the current
        // saldo lines above ("Session 5h left: 97%"), so it reads as a present value; the plain-
        // language "runs out" marks it as something that happens later. Used mode keeps the "100%"
        // percentage it always showed, which reads naturally as a ceiling you climb toward.
        string hits = i.ShowRemaining ? L.T("tip.hitsLeft") : L.T("tip.hitsUsed");
        double pct = Math.Min(1.0, data.Metric(i.Metric));
        // Whether the window the icon is about has itself reached a limit — which is the *caption's*
        // question now that the state answers about the account (T274). The overage window is excluded:
        // its 100% denominates nothing any header states (§XVIII), so "at limit" there would caption a
        // ceiling nobody measured, and with the state no longer resolved from the metric that is the
        // branch an `extra` icon at 100% would now fall into.
        bool metricAtLimit = i.Metric != "extra" && pct >= QuotaStates.AtLimitThreshold;
        // Each projection verdict has a full form and a compact fallback for when the tooltip is
        // tight (see the budget below). null => no projection line at all.
        (string full, string compact)? projection = i.State == QuotaState.Billing
            // Past the included quota and paying. Said plainly rather than "projected", because a limit
            // already reached is not a forecast — and said as *this* kind of maxed, since "you have
            // stopped" and "you are paying to carry on" are opposite pieces of news and this line used to
            // give the first for both (T182).
            //
            // Already said, on the reading above. Billing without a figure to show it on — extra usage
            // enabled and not yet spent — has no line to merge into, and there the sentence is both
            // affordable and the only thing that would say so at all.
            ? payingOnItsOwnLine
                ? null
                // The caption names the metric's own scope, or it names nothing. A week at 47% behind a
                // rejected session is billing, and "Week 7d: past your included quota" would caption the
                // wrong percentage — so the scoped form is kept for the window that actually crossed and
                // the news goes out unscoped for every other one (T274).
                : metricAtLimit
                    ? (L.T("tip.billingFull", scope), L.T("tip.billingCompact"))
                    : (L.T("tip.billingCompact"), L.T("tip.billingCompact"))
            : metricAtLimit
                ? (L.T("tip.atLimitFull", scope, limit), L.T("tip.atLimitCompact", limit))
            : i.Verdict switch
            {
                Projection.Danger => hasEta
                    ? (L.T("tip.dangerEtaFull", scope, hits, TrayContext.FmtDays(i.Eta)), L.T("tip.dangerEtaCompact", hits, TrayContext.FmtDays(i.Eta)))
                    : (L.T("tip.dangerPaceFull", scope), L.T("tip.dangerPaceCompact")),
                Projection.Ok => double.IsInfinity(i.Eta)
                    ? (L.T("tip.okTrackFull", scope), L.T("tip.okTrackCompact"))
                    : (L.T("tip.okEtaFull", scope, hits, TrayContext.FmtDays(i.Eta)), L.T("tip.okEtaCompact", hits, TrayContext.FmtDays(i.Eta))),
                _ => null,
            };

        // T280. "You are paying" invites "since when", and the answer decides whether the next hour of work
        // is a considered choice or something found at the end of the month. A duration rather than a
        // timestamp because of the budget: 114 of 127 characters are already spent in pt-BR on the two
        // billing variants, and a date does not fit where "3h 20m" does. Only shown when the crossing is on
        // file — see OverageSpell, which answers null rather than dating it from the log's own beginning.
        (string full, string compact)? spell = i.State == QuotaState.Billing && i.SpellSince > 0
                                              && i.Now > i.SpellSince
            ? (L.T("tip.spellFull", TrayContext.FmtDays(i.Now - i.SpellSince)),
               L.T("tip.spellCompact", TrayContext.FmtDays(i.Now - i.SpellSince)))
            : null;

        string statusLine = TrayContext.StatusLine(data, i.Metric, i.Updated);

        // The readings themselves can already be over budget before there is any projection to ration
        // (T215): French with an overage line reached 129 characters, and the profile label is text the
        // user typed, so no fixed set of strings can bound this. Measured, not supposed — nothing could
        // measure it until the composition left the tray.
        //
        // What goes first is the bounded window the icon is NOT about. The tooltip exists to report the
        // window the number on the icon reports, so losing the other one costs least, and it beats the
        // alternative: `Truncate` cuts the END, which is the status line carrying the time of the reading.
        if (Length(lines) + statusLine.Length > Cap)
            lines.Remove(i.Metric == "5h" ? week : session);

        // The refresh time sits on the last line, so a blind end-truncation would chop it mid-value.
        // Keep the status/time line intact and fit each optional sentence in: full form if it fits, else
        // compact, else not at all. Measured against what is on `lines` at the time, so an earlier rung
        // taken means less room for a later one — which is what makes the order below a priority.
        void Fit((string full, string compact)? candidate)
        {
            if (candidate is not { } c) return;
            int room = Cap - Length(lines) - statusLine.Length;
            if (c.full.Length + 1 <= room) lines.Add(c.full);
            else if (c.compact.Length + 1 <= room) lines.Add(c.compact);
        }

        Fit(projection);
        // After the projection, because it qualifies news the lines above it carry rather than carrying its
        // own: a duration under nothing that says money is being spent is a number about nothing, and the
        // sentence it modifies is the one that must survive if only one of them can.
        Fit(spell);
        lines.Add(statusLine);
        return string.Join("\n", lines);
    }

    /// <summary>What a set of lines costs against the cap, newline separators included.</summary>
    private static int Length(List<string> lines) => lines.Sum(l => l.Length + 1);

    /// <summary>How much of a profile's name the tooltip will show. A label is whatever the user typed,
    /// so it is the one input here with no bound of its own — and an unbounded line makes the cap a
    /// claim nothing can keep.</summary>
    private const int MaxLabel = 24;

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
