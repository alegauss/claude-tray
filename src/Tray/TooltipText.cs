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
    internal sealed record Input(
        UsageData? Data,
        string Metric,
        bool ShowRemaining,
        string? ProfileLabel,
        Projection Verdict,
        double Eta,
        QuotaState State,
        string Updated,
        long Now);

    /// <summary>
    /// The tooltip for one reading, newline-separated and already within <see cref="Cap"/>.
    ///
    /// <para>The budget is the interesting part and the reason this is worth being able to read: the
    /// status line carries the refresh time and must survive whole, so a blind end-truncation would chop
    /// it mid-value. Instead the projection sentence is fitted — full form if it fits, else its compact
    /// fallback, else dropped entirely — and everything above it is kept.</para>
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
            lines.Add(L.T("tip.profile", label));
        lines.Add($"{L.T("tip.session")}{leftSuffix}: {TrayContext.PctShown(data.Session5h, i.ShowRemaining)}  ⟳ {r5}");
        lines.Add($"{L.T("tip.week")}{leftSuffix}: {TrayContext.PctShown(data.Week7d, i.ShowRemaining)}  ⟳ {r7}");
        if (data.Extra > 0.001)
        {
            string re = data.ResetExtra > 0 ? TrayContext.FmtDays(data.ResetExtra - i.Now) : "--";
            lines.Add($"{L.T("tip.extra")}: {TrayContext.Pct(data.Extra)}  ⟳ {re}");
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
        // Each projection verdict has a full form and a compact fallback for when the tooltip is
        // tight (see the budget below). null => no projection line at all.
        (string full, string compact)? projection = pct >= QuotaStates.AtLimitThreshold
            // Already maxed: state it plainly rather than "projecting" a limit you've reached — and say
            // *which* kind of maxed, because "you have stopped" and "you are paying to carry on" are
            // opposite pieces of news and this line used to give the first for both (T182).
            ? i.State == QuotaState.Billing
                ? (L.T("tip.billingFull", scope), L.T("tip.billingCompact"))
                : (L.T("tip.atLimitFull", scope, limit), L.T("tip.atLimitCompact", limit))
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

        string statusLine = TrayContext.StatusLine(data, i.Metric, i.Updated);

        // The refresh time sits on the last line, so a blind end-truncation would chop it mid-value.
        // Keep the status/time line intact and fit the projection in: full form if it fits, else compact.
        int used = lines.Sum(l => l.Length + 1) + statusLine.Length;
        if (projection is { } p)
        {
            if (used + p.full.Length + 1 <= Cap) lines.Add(p.full);
            else if (used + p.compact.Length + 1 <= Cap) lines.Add(p.compact);
        }
        lines.Add(statusLine);
        return string.Join("\n", lines);
    }
}
