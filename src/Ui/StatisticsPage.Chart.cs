using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

// This is a WinForms + WPF hybrid, so System.Drawing and System.Windows.Media both contribute a
// Brush / Color / Point / Size. Pin these names to the WPF (Media) types the charts are drawn with.
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;

namespace ClaudeTray;

/// <summary>Part of <see cref="StatisticsPage"/> — split out by T133, moved verbatim.</summary>
internal partial class StatisticsPage
{
    // Plain-language read of the pace + projection for one window.
    private string ProjectionText(WindowPace w)
    {
        if (!w.HasWindow)
            return L.T("stats.proj.noWindow");

        string toReset = Dur(w.SecondsToReset);

        // With a shape to follow, the sentence follows it too — and hedges. The model is a habit, not
        // a schedule, so it says "around", never a to-the-minute landing.
        if (w.Shape is { } shape)
        {
            double now = w.ResetUnix - w.SecondsToReset;
            if (!shape.RunsOut)
                return L.T(_remaining ? "stats.proj.shapeOk.left" : "stats.proj.shapeOk",
                           Pct(Disp(shape.EndCum)), toReset);

            string warning = L.T(_remaining ? "stats.proj.shapeEta.left" : "stats.proj.shapeEta",
                LocalTime(shape.ExhaustUnix), Dur(w.ResetUnix - shape.ExhaustUnix), Dur(shape.ExhaustUnix - now));

            // The window has always reported the problem; this is the part that says what to do about
            // it. Only when there is a real answer — no advice beats invented advice.
            return shape.HasAdvice
                ? warning + " " + L.T(_remaining ? "stats.proj.shapeResume.left" : "stats.proj.shapeResume",
                                      LocalTime(shape.ResumeUnix), Pct(Disp(shape.ResumeEndCum)))
                : warning;
        }

        // At the limit *and* still spending: the window said "until then, usage is blocked" while the
        // account was working and being charged for it — the tooltip's defect (T182) in the one surface
        // that also draws the evidence. Said before either mode's switch, because both were wrong.
        //
        // The state is `BillingNow` and only the *wording* is chosen here (T323): with a figure, the
        // percentage of the allowance spent; without one, the state and no amount, because no header states
        // one — the spell that prompted T275 recorded `ux:0` on every reading through the crossing. Both
        // still need `AtLimit`, and that is not the state's condition but the string's: each says "the quota
        // included in **this window**", which is false of a session with room behind a week that crossed.
        if (w.Verdict == PaceVerdict.AtLimit && BillingNow(w))
            return HasBillingFigure(w)
                ? L.T("stats.proj.billing", Pct(w.ExtraCurve[^1].val), toReset)
                : L.T("stats.proj.billingNoAmount", toReset);

        if (_remaining)
        {
            return w.Verdict switch
            {
                PaceVerdict.AtLimit =>
                    L.T("stats.proj.atLimit.left", toReset),
                PaceVerdict.Ahead when w.ExhaustFraction <= 1 && !double.IsInfinity(w.ExhaustSeconds) =>
                    L.T("stats.proj.aheadEta.left", Dur(w.ExhaustSeconds), Dur(w.SecondsToReset - w.ExhaustSeconds), toReset),
                PaceVerdict.Ahead =>
                    L.T("stats.proj.ahead.left", Pct(1 - w.IdealNow), Pct(1 - w.Util), toReset),
                _ =>
                    L.T("stats.proj.ok.left", Pct(1 - w.IdealNow), toReset),
            };
        }
        return w.Verdict switch
        {
            PaceVerdict.AtLimit =>
                L.T("stats.proj.atLimit", toReset),
            PaceVerdict.Ahead when w.ExhaustFraction <= 1 && !double.IsInfinity(w.ExhaustSeconds) =>
                L.T("stats.proj.aheadEta", Dur(w.ExhaustSeconds), Dur(w.SecondsToReset - w.ExhaustSeconds), toReset),
            PaceVerdict.Ahead =>
                L.T("stats.proj.ahead", Pct(w.IdealNow), Pct(w.Util), toReset),
            _ =>
                L.T("stats.proj.ok", Pct(w.IdealNow), toReset),
        };
    }

    /// <summary>Whether this window carries an overage <em>figure</em> to state a percentage from — the
    /// reading only some accounts send (T183), kept apart from the state itself because the spell that
    /// prompted T275 had the state and no figure at all.</summary>
    internal static bool HasBillingFigure(WindowPace w) => w.ExtraCurve.Count > 0 && w.ExtraMax > 0;

    /// <summary>How recent the last over-quota reading has to be for this window to be called <em>still</em>
    /// paying: a spell that ended earlier in the window has a band to look at and no claim to make about
    /// right now. Fifteen minutes, which is <see cref="UsageReport.BridgeSeconds"/>' own floor — the same
    /// silence that ends a span is the silence that stops this being present tense.</summary>
    private const double SpellStillLiveSeconds = 900;

    /// <summary>Whether a reading at this fraction of the window is recent enough to speak in the present
    /// tense — asked of the span's end and of the figure's last point, which is what keeps the two routes
    /// below on one rule.</summary>
    private static bool StillCurrent(WindowPace w, double frac)
        => w.WindowSeconds > 0 && w.ElapsedFraction - frac <= SpellStillLiveSeconds / w.WindowSeconds;

    /// <summary>
    /// Whether this window's own readings say the account is past its included quota <em>right now</em>
    /// (T323) — read by the projection sentence and by the chip above it, which is the whole point of its
    /// being here.
    ///
    /// <para>The chip used to answer a different question. <see cref="Populate"/> maps
    /// <see cref="PaceVerdict"/> to a colour and a two-word label, and that enum has four values, none of
    /// which is billing — so a paying account wore <c>#C43E3E</c>, this page's <em>at-limit</em> red, which on
    /// a quota surface means <b>stopped</b>, directly above a sentence saying extra usage was paying and a
    /// chart shading the stretch. T182's defect, in the last surface nothing had moved out of it.</para>
    ///
    /// <para><b>Either route, one recency rule.</b> The header's span and the overage figure are two ways to
    /// have measured the same state (T273, T275), and both are asked whether their newest reading is still
    /// current — which also tightens the figure route the sentence took before: a percentage recorded early in
    /// a week whose spell had ended was being read out in the present tense.</para>
    ///
    /// <para><c>internal static</c> so <c>--selftest</c> can drive it, and one reader rather than two because
    /// a second copy three hundred lines away is two things free to disagree about one pane. It settles the
    /// <em>scope</em> for both at once: a pane speaks from <b>its own</b> readings, so the session pane says
    /// this too, over the band its own chart is already drawing.</para></summary>
    internal static bool BillingNow(WindowPace w)
        => (w.ExtraSpans.Count > 0 && StillCurrent(w, w.ExtraSpans[^1].f1))
           // The *newest* figure above zero, not merely one somewhere in the window: `ExtraCurve` carries a
           // point for every reading that had the header at all, zeros included (T179), so a spell that ended
           // leaves a recent 0 behind — and reading that as "paying now" is the opposite mistake.
           || (w.ExtraCurve.Count > 0 && w.ExtraCurve[^1].val > 0
               && StillCurrent(w, w.ExtraCurve[^1].frac));

    /// <summary>How far into the plot the ghost's over-quota stretch sits from the ghost's own line, in
    /// device-independent pixels (T295) — enough to clear the 100% gridline and the projection that share
    /// that edge, small enough to still read as part of the curve it hugs.</summary>
    private const double GhostOverOffset = 4;

    /// <summary>Whether the ceiling — 100% consumed — is the top edge of the plot. It is, except in
    /// remaining mode, where <see cref="Disp"/> flips the axis so <c>Yc(1)</c> lands on the bottom.
    ///
    /// <para>One reader for one fact, because two things follow from it in two different types and they were
    /// free to disagree (T316): the offset that keeps the ghost's over-quota mark inside the plot, and the
    /// edge its legend swatch puts its bar on. T295 flipped the offset and the swatch T300 added never
    /// flipped at all, so in remaining mode the mark sat above the 0% line and the legend showed a bar at the
    /// top of its box — a swatch pointing the wrong way in one of the app's two modes.</para></summary>
    internal static bool CeilingAtTop(bool remaining) => !remaining;

    /// <summary>How far, and which way, the ghost's mark sits off the ceiling: always <em>into</em> the plot,
    /// which is downward when the ceiling is the top edge and upward when it is the bottom one.</summary>
    internal static double CeilingLift(bool remaining)
        => CeilingAtTop(remaining) ? GhostOverOffset : -GhostOverOffset;

    /// <summary>Which edge of its box the legend swatch's ceiling bar sits against — the same fact as
    /// <see cref="CeilingLift"/>, in the type the markup needs.</summary>
    internal static VerticalAlignment CeilingSwatchEdge(bool remaining)
        => CeilingAtTop(remaining) ? VerticalAlignment.Top : VerticalAlignment.Bottom;

    /// <summary>The overage axis' own caption, and the legend entry naming the line on it (T318). Both have
    /// to change in remaining mode and for one reason, so they are chosen here rather than at their two
    /// draw sites.
    ///
    /// <para><b>What this says and why.</b> The used/remaining toggle inverts the consumption axis through
    /// <see cref="Disp"/>, and this second axis does not — the app receives a percentage of the extra-usage
    /// allowance <em>spent</em> and never that allowance's size, so there is no remaining figure to flip to.
    /// The consequence on screen is two lines moving oppositely for one meaning: the accent falls as the
    /// included quota runs out while the clay climbs as the allowance is spent, and they cross mid-plot where
    /// a crossing normally marks an event. Left as it is and said out loud, which is the honest half of that
    /// pair: in remaining mode both strings name what the line counts.</para></summary>
    internal static string ExtraAxisKey(bool remaining)
        => remaining ? "stats.chart.extraAxisSpent" : "stats.chart.extraAxis";

    /// <summary>The legend's wording for the same line, flipping with <see cref="ExtraAxisKey"/> — one
    /// surface saying "spent" while the other did not is the disagreement T311, T313 and T316 each
    /// were.</summary>
    internal static string ExtraLegendKey(bool remaining)
        => remaining ? "stats.legend.extraSpent" : "stats.legend.extra";

    /// <summary>Whether this window gets the overage series and the second right-hand axis that rules it
    /// (T183) — the gutter, the clay curve, its own 0–max labels, and the legend entry that says the
    /// percentage is of a different denominator (T308). Written here, beside the chart it decides the width
    /// of, so the legend cannot claim a scale nobody drew.</summary>
    internal static bool HasExtraAxis(WindowPace w) => w.ExtraCurve.Count >= 2 && w.ExtraMax > 0;

    /// <summary>Which of the two clay shapes a mark is: a stretch shaded top to bottom for the window in
    /// front of you (T275), or a short bar at the ceiling for the ghost week behind it (T295). Same fact,
    /// same colour, told apart by where it is drawn — which is why the legend names them together and the
    /// z-order has to keep them apart.</summary>
    internal enum OverMark { Band, Ceiling }

    /// <summary>Every clay over-quota mark this window carries, in draw order (T309). <b>The one reader</b>
    /// of that question: the loops below draw what this yields and the legend counts it, where before each
    /// loop tested `f1 &gt; f0` and the ghost's curve length for itself and a predicate beside them spelled
    /// the same two things a third time.
    ///
    /// <para>An enumerator rather than a draw call, because the two shapes cannot share one loop: the band
    /// goes down before the usage line and the ceiling bar inside the ghost's own block, so the caller
    /// filters by <see cref="OverMark"/> and the z-order stays a decision made where the drawing is. What it
    /// does not leave to the caller is <em>which spans count</em>, which is the thing that could disagree.
    /// </para>
    ///
    /// <para><b>Fractions, not pixels.</b> A span narrower than a device pixel still counts here, and the
    /// paint sites give it a visible minimum instead of dropping it: <see cref="UsageReport.MergeSpans"/>
    /// widens a lone reading to a sliver precisely so a measurement is not lost to the plot's width, and a
    /// mark the legend names has to be findable on the chart.</para></summary>
    internal static IEnumerable<(OverMark kind, double f0, double f1)> OverQuotaMarks(WindowPace w)
    {
        foreach (var (f0, f1) in w.ExtraSpans)
            if (f1 > f0) yield return (OverMark.Band, f0, f1);
        if (w.Ghost is { } ghost && ghost.Curve.Count >= 2)
            foreach (var (f0, f1) in ghost.OverSpans)
                if (f1 > f0) yield return (OverMark.Ceiling, f0, f1);
    }

    /// <summary>What the legend's one clay entry should be, for the marks this window actually carries
    /// (T311): whether to show it at all, which halves of its swatch to draw, and which sentence explains
    /// it.</summary>
    /// <param name="Show">False when the chart carries no clay mark, in which case the rest means nothing
    /// and <paramref name="TipKey"/> is empty.</param>
    /// <param name="Band">Draw the shaded-stretch half of the swatch — this window went over.</param>
    /// <param name="Ceiling">Draw the bar-at-the-ceiling half — the ghost week behind it went over.</param>
    /// <param name="TipKey">The one sentence true of what was drawn — three states, three strings, each
    /// written for a legend entry (T313). The chart elements' own tips are not reusable here despite making
    /// the same claim: they say <em>over this stretch</em>, which has a referent under a cursor on the
    /// stretch and none under a cursor on a swatch, so a legend tip has to name the shape and say where on
    /// the chart to find it instead of pointing at where it already is.</param>
    internal readonly record struct OverLegend(bool Show, bool Band, bool Ceiling, string TipKey);

    /// <summary>The legend's clay entry, decided from the marks the chart drew (T311) — and with it whether
    /// there is an entry at all, which is the question T300 asked and T309 gave one reader.
    ///
    /// <para>T300 made the entry appear when <em>either</em> mark is drawn and left its content fixed,
    /// describing both. On the state that is most weeks — a previous week that went over, a current one that
    /// has not — the swatch drew a band and the tip promised a shaded stretch, neither of which was on the
    /// chart: an entry for a mark nobody drew, which is the defect T300 exists against. So the content is
    /// per render, from the same enumerator, and the three states are the three the chart can be in.</para>
    ///
    /// <para>Both tabs come through here rather than the 5-hour one carrying a hand-written band-only case
    /// (T308): nothing sets <c>Session.Ghost</c> today, so it resolves to band-only on its own — and if
    /// something ever does, the legend is already right instead of quietly wrong.</para></summary>
    internal static OverLegend OverLegendFor(WindowPace w)
    {
        bool band = false, ceiling = false;
        foreach (var (kind, _, _) in OverQuotaMarks(w))
            if (kind == OverMark.Band) band = true; else ceiling = true;

        if (!band && !ceiling) return new OverLegend(false, false, false, "");
        return new OverLegend(true, band, ceiling,
            band && ceiling ? "stats.legend.overQuota.tip"
            : band ? "stats.legend.overQuota.tipBand"
            : "stats.legend.overQuota.tipCeiling");
    }

    /// <summary>Whether this window puts a clay over-quota mark on the chart at all — the legend's one entry
    /// for the pair (T300), asking the same enumerator the marks are drawn from (T309).</summary>
    internal static bool HasOverQuotaMark(WindowPace w) => OverLegendFor(w).Show;

    private void Chart_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var canvas = (Canvas)sender;
        WindowPace? w = ReferenceEquals(canvas, ChartS) ? _session
            : ReferenceEquals(canvas, ChartW) ? _weekly : null;
        if (w != null) DrawChart(canvas, w);
    }

    // Draw the burn-up chart: even-pace reference line, real consumption curve (with a soft fill),
    // the "now" marker, and the dashed projection to 100% / reset.
    private void DrawChart(Canvas c, WindowPace w)
    {
        c.Children.Clear();
        double W = c.ActualWidth, H = c.ActualHeight;
        if (W <= 1 || H <= 1) return;

        var accent = (Brush)FindResource("AccentFillColorDefaultBrush");
        var muted = (Brush)FindResource("TextFillColorTertiaryBrush");
        var grid = (Brush)FindResource("DividerStrokeColorDefaultBrush");
        var axisFg = (Brush)FindResource("TextFillColorTertiaryBrush");

        // Right margin leaves room for the 0/50/100% gridline labels past the plot's right edge — and a
        // second gutter beyond them when there is an overage series to scale (T183).
        bool hasExtra = HasExtraAxis(w);
        const double left = 6, top = 10, bottom = 22;
        double right = hasExtra ? 78 : 36;
        double pw = W - left - right, ph = H - top - bottom;
        double X(double frac) => left + Math.Clamp(frac, 0, 1) * pw;
        // Y for a display value (0 at the bottom axis, 1 at the top). The 0/50/100% gridlines and their
        // labels sit on this axis and read the same in both modes — only the label's *meaning* flips.
        double Y(double v) => top + (1 - Math.Clamp(v, 0, 1)) * ph;
        // Y for a consumption fraction: in "used" mode it's Y(cum); in "remaining" mode it's flipped via
        // Disp, so the curve starts full at the top (100% left) and burns down toward 0%.
        double Yc(double cum) => Y(Disp(cum));

        if (!w.HasWindow)
        {
            var msg = new TextBlock
            {
                Text = L.T("stats.chart.noWindow"),
                Foreground = muted,
                FontSize = 13,
            };
            Canvas.SetLeft(msg, left + 4);
            Canvas.SetTop(msg, top + ph / 2 - 10);
            c.Children.Add(msg);
            return;
        }

        // Horizontal gridlines at 0 / 50 / 100% with tiny right-edge labels.
        foreach (double g in new[] { 0.0, 0.5, 1.0 })
        {
            c.Children.Add(HLine(X(0), X(1), Y(g), grid, g == 1.0 ? null : new DoubleCollection { 3, 3 }));
            var gl = new TextBlock { Text = Pct(g), FontSize = 10, Foreground = axisFg };
            Canvas.SetLeft(gl, X(1) + 2);
            Canvas.SetTop(gl, Y(g) - 8);
            c.Children.Add(gl);
        }

        // When the account was past its included quota, as a shaded stretch behind everything (T275).
        //
        // A band and not a series, because on the spell this was built from there is no series: `ux` read
        // 0 on every reading through the crossing, so the clay curve below draws nothing and T247's guard
        // is right to keep it that way. What the band claims is only what the header stated — *this* is
        // when it was happening — and it is drawn first so the usage line, the ghost and the projection all
        // stay on top of it. The same clay as the second axis and the tray's paying icon, at the strength
        // a background can carry without competing with the line it sits behind.
        bool anyBand = false;
        foreach (var (_, f0, f1) in OverQuotaMarks(w).Where(m => m.kind == OverMark.Band))
        {
            double x0 = X(f0), x1 = X(f1);
            var band = new Rectangle
            {
                Width = Math.Max(1.5, x1 - x0), Height = ph,
                Fill = BillingBrush, Opacity = 0.14,
            };
            Canvas.SetLeft(band, x0);
            Canvas.SetTop(band, top);
            c.Children.Add(band);
            // Said once, on the first stretch, rather than per band: several spans are one piece of news.
            if (!anyBand)
            {
                anyBand = true;
                AddHit(c, (x0 + x1) / 2, top + ph / 2, L.T("stats.chart.overSpan"));
            }
        }

        // Day boundaries: a faint dashed vertical at each local midnight, so a multi-day span (the 7-day
        // weekly chart) reads as day-sized columns instead of one long ramp. Skipped for short windows
        // like the 5-hour session, where day marks would be meaningless.
        if (w.WindowSeconds >= 2 * 86400)
        {
            double dayStart = w.ResetUnix - w.WindowSeconds;
            DateTime startLocal = DateTimeOffset.FromUnixTimeSeconds((long)dayStart).LocalDateTime;
            DateTime resetLocal = DateTimeOffset.FromUnixTimeSeconds((long)w.ResetUnix).LocalDateTime;

            // The start/reset axis labels own the two ends of this same bottom strip; a day label that
            // would run into either is dropped (its divider line still shows) so the dates never collide.
            double startRight = left + MeasureText(L.T("stats.chart.start", LocalTime(dayStart)), 10);
            double resetLeft = X(1) - MeasureText(L.T("stats.chart.reset", LocalTime(w.ResetUnix)), 10);

            // First local midnight after the window opens, then step a day at a time until the reset.
            for (DateTime day = startLocal.Date.AddDays(1); day < resetLocal; day = day.AddDays(1))
            {
                double frac = (new DateTimeOffset(day).ToUnixTimeSeconds() - dayStart) / w.WindowSeconds;
                if (frac <= 0.001 || frac >= 0.999) continue; // don't double the start/reset edges
                double x = X(frac);
                c.Children.Add(new Line
                {
                    X1 = x, Y1 = top, X2 = x, Y2 = top + ph,
                    Stroke = grid, StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 4 }, Opacity = 0.7,
                });
                // Date of the division, centered under its line on the bottom axis — unless it would
                // overlap the start/reset labels sharing that strip.
                // Measured and drawn from one string, and that string in the culture's own field order:
                // "d/M" everywhere read 3/8 for 3 August to an American, whose short date is M/d (T263).
                string dayLabel = Dates.DayMonthDigits(day);
                double dw = MeasureText(dayLabel, 9);
                if (x - dw / 2 < startRight + 4 || x + dw / 2 > resetLeft - 4) continue;
                var dl = new TextBlock { Text = dayLabel, FontSize = 9, Foreground = axisFg };
                Canvas.SetLeft(dl, x - dw / 2);
                Canvas.SetTop(dl, top + ph + 4);
                c.Children.Add(dl);
            }
        }

        // Last week, faintly, behind everything else: same metric, same axes, same color as this
        // week's curve at a third of the strength — so it reads as "this, previously" rather than as
        // a new quantity. Drawn first so the live curve always wins where they overlap.
        if (w.Ghost is { } ghost && ghost.Curve.Count >= 2)
        {
            var gpts = new PointCollection(ghost.Curve.Select(p => new Point(X(p.frac), Yc(p.cum))));
            c.Children.Add(new Polyline
            {
                Points = gpts, Stroke = accent, StrokeThickness = 1.5, Opacity = 0.32,
            });

            // Where that week was past its included quota, as a clay stretch hugging the ghost's own line
            // (T295).
            //
            // On the line and not as a band, which is the one thing this cannot copy from the band above.
            // Both weeks share this x-axis — a fraction of a window, not a date — so a second full-height
            // band would sit exactly where the first one does and name no week: the reader would be left to
            // guess which of the two curves it belongs to.
            //
            // Beside the line rather than on it, because of where the stretch always falls. Being past the
            // included quota means the week is *at* its ceiling, so the stretch is flat against the 100%
            // gridline — which is also where the projection lands, in a clay that is nearly this one. The
            // first capture of this drew it there and it could not be seen at all. Offset a few pixels into
            // the plot it belongs to the same curve and collides with neither, and the offset flips with the
            // axis so "into the plot" survives the remaining-mode flip.
            //
            // Pinned to the ceiling rather than tracked along the curve (T299). The header's claim is *that
            // the account was over*, which is a statement about 100% and about nothing else; the curve is a
            // sum of deltas the fold drops one of at every window reset, so a week seen in pieces can carry
            // these hours while its line peaks at 80%. Drawn on that line the stretch would sit in the
            // middle of the plot contradicting the very axis it was placed on — so the stretch goes where
            // the claim lives, and the disagreement is said in words below instead of drawn.
            double lift = CeilingLift(_remaining);
            foreach (var (_, f0, f1) in OverQuotaMarks(w).Where(m => m.kind == OverMark.Ceiling))
            {
                // Widened to the band's own minimum rather than dropped when the plot rounds it to nothing
                // (T309): the fraction test above is the claim, and a mark the legend names has to be there.
                double x0 = X(f0), x1 = Math.Max(X(f1), X(f0) + 1.5);
                var line = new Polyline
                {
                    Points = new PointCollection { new(x0, Yc(1) + lift), new(x1, Yc(1) + lift) },
                    Stroke = BillingBrush, StrokeThickness = 3, Opacity = 0.8,
                };
                line.ToolTip = L.T("stats.chart.lastWeekOverSpan");
                c.Children.Add(line);
            }

            // The ghost's own figure, plus the sentences the picture alone would get wrong: that an unshaded
            // ghost may simply not know, and that a line under a shaded stretch is a floor, not a week that
            // stayed inside its quota after all.
            string tip = L.T("stats.chart.lastWeek", Pct(Disp(ghost.Total)), Pct(Disp(ghost.AtSameFraction)));
            if (!ghost.OverKnown) tip += " " + L.T("stats.chart.lastWeekOverUnknown");
            if (ghost.ShadedAboveCurve) tip += " " + L.T("stats.chart.lastWeekOverFloor");
            AddHit(c, X(1), Yc(ghost.Total), tip);
        }

        // Even-pace reference: straight line between empty (consumption 0) at the start and the limit
        // (consumption 1) at the reset — rising in "used" mode, falling in "remaining" mode.
        c.Children.Add(new Line
        {
            X1 = X(0), Y1 = Yc(0), X2 = X(1), Y2 = Yc(1),
            Stroke = muted, StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 4 },
        });

        double ef = w.ElapsedFraction, util = w.Util;

        // Real consumption curve + soft fill down to the bottom (0%) axis.
        if (w.Curve.Count >= 2)
        {
            var pts = new PointCollection(w.Curve.Select(p => new Point(X(p.frac), Yc(p.cum))));

            var fillPts = new PointCollection(pts) { new Point(X(ef), Y(0)), new Point(X(0), Y(0)) };
            c.Children.Add(new Polygon { Points = fillPts, Fill = accent, Opacity = 0.12 });

            c.Children.Add(new Polyline { Points = pts, Stroke = accent, StrokeThickness = 2.5 });
        }

        // Projected-idle bands: the stretches ahead this machine is usually not working. Drawn under
        // the projection so the flat steps have a visible reason, and only when the staircase is what
        // is being drawn — a band without a staircase would explain nothing.
        if (w.Shape is { } bands)
            foreach (var (f0, f1) in bands.IdleBands)
            {
                double xa = X(f0), xb = X(f1);
                var band = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(0, xb - xa), Height = ph, Fill = IdleBandBrush,
                };
                Canvas.SetLeft(band, xa);
                Canvas.SetTop(band, top);
                c.Children.Add(band);
                band.ToolTip = L.T("stats.chart.idleBand",
                    LocalTime(w.ResetUnix - w.WindowSeconds + f0 * w.WindowSeconds),
                    LocalTime(w.ResetUnix - w.WindowSeconds + f1 * w.WindowSeconds));
            }

        // Projection, activity-aware when there is a trustworthy shape to follow: quota spent along
        // the usual-hours curve (flat overnight, sloped through working hours) rather than uniformly.
        if (w.Shape is { } shape && shape.Curve.Count >= 2)
        {
            var stair = new PointCollection(shape.Curve.Select(p => new Point(X(p.frac), Yc(p.cum))));
            c.Children.Add(new Polyline
            {
                Points = stair, Stroke = ProjectionBrush, StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 4 },
            });

            double endX = X(shape.RunsOut ? shape.ExhaustFraction : 1);
            double endY = Yc(shape.RunsOut ? 1 : shape.EndCum);
            string tip = shape.RunsOut
                ? L.T(_remaining ? "stats.chart.projShapeHitZero" : "stats.chart.projShapeHit",
                      LocalTime(shape.ExhaustUnix), Dur(shape.ExhaustUnix - (w.ResetUnix - w.WindowSeconds + ef * w.WindowSeconds)))
                : L.T("stats.chart.projShapeReset", Pct(Disp(shape.EndCum)));

            var landing = new Ellipse { Width = 7, Height = 7, Fill = ProjectionBrush };
            Canvas.SetLeft(landing, endX - 3.5);
            Canvas.SetTop(landing, endY - 3.5);
            c.Children.Add(landing);

            // The landing clock time, as with the straight projection — hedged elsewhere in words, but
            // the marker itself still has to point somewhere.
            if (shape.RunsOut)
            {
                string clock = ShortTime(shape.ExhaustUnix, w.WindowSeconds);
                var cl = new TextBlock
                {
                    Text = clock, FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = ProjectionBrush,
                };
                double cw = MeasureText(clock, 10);
                Canvas.SetLeft(cl, Math.Clamp(endX - cw / 2, left, X(1) - cw));
                Canvas.SetTop(cl, _remaining ? endY - 16 : endY + 4);
                c.Children.Add(cl);
            }

            AddHit(c, endX, endY, tip);
        }
        // Projection: extend the average pace from the current point to 100% (or to the reset).
        else if (util > 0 && ef > 0)
        {
            var proj = new PointCollection { new Point(X(ef), Yc(util)) };
            double endX, endY;
            string projTip;
            // Clock time of the landing point, drawn in-plot when the quota runs out early — "in 1h 20m"
            // alone makes you do the arithmetic; the wall-clock time is what you plan around. Only for
            // the early-exhaust branch: landing at the reset, the time is already the reset axis label.
            string? projClock = null;
            if (w.ExhaustFraction <= 1)
            {
                proj.Add(new Point(X(w.ExhaustFraction), Yc(1)));
                proj.Add(new Point(X(1), Yc(1)));
                endX = X(w.ExhaustFraction); endY = Yc(1);
                double exhaustUnix = w.ResetUnix - w.WindowSeconds + w.ExhaustFraction * w.WindowSeconds;
                projClock = ShortTime(exhaustUnix, w.WindowSeconds);
                projTip = L.T(_remaining ? "stats.chart.projHitZero" : "stats.chart.projHit",
                    Dur(w.ExhaustSeconds), LocalTime(exhaustUnix));
            }
            else
            {
                double end = util / ef;
                proj.Add(new Point(X(1), Yc(end)));
                endX = X(1); endY = Yc(end);
                projTip = L.T("stats.chart.projReset", Pct(Disp(end)));
            }
            c.Children.Add(new Polyline
            {
                Points = proj, Stroke = ProjectionBrush, StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 4 },
            });

            // Amber dot at the projection's landing point, with a hover tooltip showing the outcome.
            var projDot = new Ellipse { Width = 7, Height = 7, Fill = ProjectionBrush };
            Canvas.SetLeft(projDot, endX - 3.5);
            Canvas.SetTop(projDot, endY - 3.5);
            c.Children.Add(projDot);

            // The landing time, centered on the dot and kept inside the plot: below it in "used" mode
            // (the dot sits on the 100% line at the top), above it in "remaining" mode (it sits on the
            // 0%-left line at the bottom).
            if (projClock != null)
            {
                var cl = new TextBlock
                {
                    Text = projClock, FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = ProjectionBrush,
                };
                double cw = MeasureText(projClock, 10);
                Canvas.SetLeft(cl, Math.Clamp(endX - cw / 2, left, X(1) - cw));
                Canvas.SetTop(cl, _remaining ? endY - 16 : endY + 4);
                c.Children.Add(cl);
            }

            AddHit(c, endX, endY, projTip);
        }

        // "Now" marker: vertical line + dot at the current point.
        c.Children.Add(new Line
        {
            X1 = X(ef), Y1 = top, X2 = X(ef), Y2 = top + ph,
            Stroke = muted, StrokeThickness = 1, Opacity = 0.5,
        });
        // Even-pace target for "now": where the vertical "now" line crosses the even-pace diagonal.
        var idealDot = new Ellipse
        {
            Width = 8, Height = 8, Stroke = muted, StrokeThickness = 1.5,
            Fill = System.Windows.Media.Brushes.Transparent,
        };
        Canvas.SetLeft(idealDot, X(ef) - 4);
        Canvas.SetTop(idealDot, Yc(w.IdealNow) - 4);
        c.Children.Add(idealDot);
        AddHit(c, X(ef), Yc(w.IdealNow), L.T("stats.chart.idealNow", Pct(Disp(w.IdealNow))));

        var dot = new Ellipse { Width = 9, Height = 9, Fill = accent };
        Canvas.SetLeft(dot, X(ef) - 4.5);
        Canvas.SetTop(dot, Yc(util) - 4.5);
        c.Children.Add(dot);
        AddHit(c, X(ef), Yc(util), L.T(_remaining ? "stats.chart.currentLeft" : "stats.chart.currentUsage", Pct(Disp(util))));

        // Outage spans: stretches where no live reading was logged (an API error like a 403, or the app
        // not running). Redraw that part of the usage line in red dashed — it *is* the usage line, just
        // interpolated across a gap we couldn't measure — with a faint band and an "unavailable since …"
        // hover, so it doesn't read as smooth, real consumption.
        double windowStart = w.ResetUnix - w.WindowSeconds;
        foreach (var (f0, c0, f1, c1) in w.Gaps)
        {
            double xa = X(f0), xb = X(f1);
            var band = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(0, xb - xa), Height = ph, Fill = OutageBandBrush,
            };
            Canvas.SetLeft(band, xa);
            Canvas.SetTop(band, top);
            c.Children.Add(band);
            c.Children.Add(new Line
            {
                X1 = xa, Y1 = Yc(c0), X2 = xb, Y2 = Yc(c1),
                Stroke = OutageBrush, StrokeThickness = 2.5,
                StrokeDashArray = new DoubleCollection { 4, 3 },
            });
            AddHit(c, (xa + xb) / 2, Yc((c0 + c1) / 2),
                L.T("stats.chart.unavailable", LocalTime(windowStart + f0 * w.WindowSeconds)));
        }

        // The overage series, on an axis of its own (T183).
        //
        // The quantity that costs money is the one thing this chart could not draw: the usage curve
        // flattens against the ceiling and the interesting number keeps climbing off-canvas. It is drawn
        // here in the same clay the tray icon wears for the paying state, on a **separate right-hand
        // scale** with its own labelled maximum — never against the 0–100% axis, because that axis means
        // "of the quota included in your plan" and nothing has established that this figure is a fraction
        // of anything comparable. Two axes stated plainly is honest; one axis silently shared is not.
        //
        // Explicitly not a projection: the weekly projection answers "when do you run out", which for an
        // account already in overage has been answered. Projecting spend forward would be the app making
        // a claim about money, and it is deliberately not this task.
        if (hasExtra)
        {
            // Headroom above the peak, so this series' maximum never lands on the 100% gridline. Scaled
            // flush to its own max, the two axes would agree exactly at the top of the plot — drawing the
            // precise coincidence ("your overage maximum *is* your quota ceiling") that separating them
            // exists to deny. The peak sits at ~83% of the height instead, where it reads as its own.
            double eTop = w.ExtraMax * 1.2;
            double Ye(double v) => top + (1 - Math.Clamp(v / eTop, 0, 1)) * ph;

            var poly = new Polyline { Stroke = BillingBrush, StrokeThickness = 2 };
            foreach (var (frac, val) in w.ExtraCurve)
                poly.Points.Add(new System.Windows.Point(X(frac), Ye(val)));
            c.Children.Add(poly);

            // A short tick at the peak rather than a rule across the plot: a full-width dashed line would
            // compete with the projection's, and this axis has no target worth spanning the chart for.
            c.Children.Add(HLine(X(1) - 8, X(1), Ye(w.ExtraMax), BillingBrush, null));
            var top1 = new TextBlock { Text = Pct(w.ExtraMax), FontSize = 10, Foreground = BillingBrush };
            Canvas.SetLeft(top1, X(1) + 38);
            Canvas.SetTop(top1, Ye(w.ExtraMax) - 8);
            c.Children.Add(top1);

            var zero = new TextBlock { Text = Pct(0), FontSize = 10, Foreground = BillingBrush };
            Canvas.SetLeft(zero, X(1) + 38);
            Canvas.SetTop(zero, Ye(0) - 8);
            c.Children.Add(zero);

            // Which gutter is which, said once rather than inferred from two columns of percentages.
            var cap = new TextBlock
            {
                Text = L.T(ExtraAxisKey(_remaining)), FontSize = 9, Foreground = BillingBrush,
                LayoutTransform = new RotateTransform(90),
            };
            Canvas.SetLeft(cap, X(1) + 66);
            Canvas.SetTop(cap, top);
            c.Children.Add(cap);

            var (lastF, lastV) = w.ExtraCurve[^1];
            var edot = new Ellipse { Width = 7, Height = 7, Fill = BillingBrush };
            Canvas.SetLeft(edot, X(lastF) - 3.5);
            Canvas.SetTop(edot, Ye(lastV) - 3.5);
            c.Children.Add(edot);
            AddHit(c, X(lastF), Ye(lastV), L.T("stats.chart.extraNow", Pct(lastV)));
        }

        // Axis labels: window start (left) and reset time (right).
        double startUnix = w.ResetUnix - w.WindowSeconds;
        AddAxisLabel(c, L.T("stats.chart.start", LocalTime(startUnix)), left, top + ph + 4, axisFg, TextAlignment.Left);
        AddAxisLabel(c, L.T("stats.chart.reset", LocalTime(w.ResetUnix)), X(1), top + ph + 4, axisFg, TextAlignment.Right);
    }

    // A transparent circular hit-target with a hover tooltip, laid over a key chart point so the thin
    // lines and small dots underneath are easy to hover. Added last, so it sits on top for hit-testing.
    private static void AddHit(Canvas c, double x, double y, string tip)
    {
        var hit = new Ellipse
        {
            Width = 18, Height = 18,
            Fill = System.Windows.Media.Brushes.Transparent,
            ToolTip = tip,
        };
        ToolTipService.SetInitialShowDelay(hit, 150);
        ToolTipService.SetShowDuration(hit, 20000);
        Canvas.SetLeft(hit, x - 9);
        Canvas.SetTop(hit, y - 9);
        c.Children.Add(hit);
    }

    // Rendered width of a chart label, for laying out / collision-testing the bottom axis strip.
    private static double MeasureText(string text, double fontSize)
    {
        var t = new TextBlock { Text = text, FontSize = fontSize };
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return t.DesiredSize.Width;
    }

    private static Line HLine(double x1, double x2, double y, Brush stroke, DoubleCollection? dash) => new()
    {
        X1 = x1, Y1 = y, X2 = x2, Y2 = y, Stroke = stroke, StrokeThickness = 1, StrokeDashArray = dash,
    };

    private void AddAxisLabel(Canvas c, string text, double x, double y, Brush fg, TextAlignment align)
    {
        var t = new TextBlock { Text = text, FontSize = 10, Foreground = fg };
        if (align == TextAlignment.Right)
        {
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(t, x - t.DesiredSize.Width);
        }
        else
        {
            Canvas.SetLeft(t, x);
        }
        Canvas.SetTop(t, y);
        c.Children.Add(t);
    }
}
