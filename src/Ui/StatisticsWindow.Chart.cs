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
using Size = System.Windows.Size;

namespace ClaudeTray;

/// <summary>Part of <see cref="StatisticsWindow"/> — split out by T133, moved verbatim.</summary>
internal partial class StatisticsWindow : Window
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

        // Right margin leaves room for the 0/50/100% gridline labels past the plot's right edge.
        const double left = 6, right = 36, top = 10, bottom = 22;
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
                double dw = MeasureText(day.ToString("d/M", DateFmt), 9);
                if (x - dw / 2 < startRight + 4 || x + dw / 2 > resetLeft - 4) continue;
                var dl = new TextBlock { Text = day.ToString("d/M", DateFmt), FontSize = 9, Foreground = axisFg };
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
            AddHit(c, X(1), Yc(ghost.Total),
                L.T("stats.chart.lastWeek", Pct(Disp(ghost.Total)), Pct(Disp(ghost.AtSameFraction))));
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
