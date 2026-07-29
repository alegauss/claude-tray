using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
// System.IO is a global using here (see the csproj), so `Path` is ambiguous with System.IO.Path.
using Path = System.Windows.Shapes.Path;

namespace ClaudeTray;

/// <summary>
/// The live throughput strip: the last <see cref="Seconds"/> of real work, one column per second,
/// entering at the right and leaving at the left, split input / output / cache-create.
///
/// <para>The whole justification for animating anything in this app is that here <b>the moving axis
/// is time</b> — the motion carries the data rather than decorating it. Everything else follows from
/// that one rule, and each consequence is cheap:</para>
/// <list type="bullet">
/// <item>Nothing running ⇒ nothing moves. An empty strip is drawn once, flat, and then left alone —
/// no geometry rebuild, no animation. A spinner or a breathing pulse would imply activity during
/// idleness, which is a lie a monitoring tool cannot afford.</item>
/// <item>Hidden ⇒ stopped. The clock runs only while the window is actually on screen, so a minimized
/// Statistics window costs exactly what it cost before this existed.</item>
/// <item>The window average, the charts and the verdict chip are untouched. This is a row, not a
/// re-timing of the pace report.</item>
/// </list>
///
/// <para>Motion without per-frame work: the geometry is rebuilt once a second (when the underlying
/// per-second buckets shift), and a single <see cref="TranslateTransform"/> is animated from one
/// column-width to zero over that second. The rebuild's one-column jump and the slide cancel exactly,
/// so the strip scrolls continuously while the app draws at 1 Hz.</para>
///
/// <para><b>The height means something (T110).</b> A column used to be tall only <em>relative to
/// whatever the tallest bucket in the feed happened to be</em> — and the feed is longer than the strip
/// (<see cref="LiveRate.HistorySeconds"/> vs <see cref="Seconds"/>), so the scale could be set by a
/// burst scrolled off the plot, flattening everything on screen to a few pixels. Two things fix that:
/// the strip draws (and scales to) the <b>newest</b> <see cref="Seconds"/> buckets, so the tallest
/// thing visible is what full height means; and full height is a round number, ruled across the plot
/// and labelled in tok/s in a right-hand gutter, the way Task Manager labels its graphs. The ceiling
/// still rises instantly and decays slowly, so it re-scales visibly rather than flapping.</para>
///
/// <para><b>One second must not flatten the minute (T112).</b> Real traffic has a dynamic range a
/// linear axis cannot hold: a single turn that writes a large cache block lands ~240,000 tokens in one
/// second next to ordinary seconds of ~2,000, so scaling to the peak puts the actual work on the 1px
/// floor. The ceiling is therefore the <b>95th percentile of the busy seconds on screen</b> once the
/// peak runs away from it (see <see cref="Ceiling"/>), and the columns that run past the top are drawn
/// <b>broken</b> — filled to a gap, then a cap — rather than silently truncated. Clipping data is a
/// thing a monitor may only do out loud: the mark says the column continues, the hover says how many
/// did it and what the busiest second actually was.</para>
/// </summary>
internal sealed class LiveStrip
{
    /// <summary>Seconds of history drawn — one column each.</summary>
    public const int Seconds = 180;

    /// <summary>Gap between columns, in device-independent pixels. Zero below a column width where
    /// the gap would eat the bar.</summary>
    private const double Gap = 1;

    /// <summary>Width reserved at the right of the host for the axis labels. Inside the strip rather
    /// than beside it: the labels sit on the same plate the bars stand on, which costs no layout row
    /// (§V.9's one constraint) and — unlike a label floated over the plot — never covers a column.</summary>
    private const double Gutter = 40;

    /// <summary>Below this host width the axis is dropped: a gutter that ate a fifth of the plot would
    /// cost more data than its labels explain.</summary>
    private const double AxisMinWidth = 220;

    /// <summary>Where the axis is ruled, as a fraction of the ceiling — the ceiling itself and its
    /// half. Two lines are enough to read a bar off and few enough to stay background.</summary>
    private static readonly double[] Marks = { 1.0, 0.5 };

    /// <summary>Steps a ceiling is allowed to land on, per decade. Fine enough that rounding up costs
    /// at most a third of the bar height, coarse enough that every label is a number a person reads
    /// without decoding it.</summary>
    private static readonly double[] NiceSteps = { 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 };

    /// <summary>The quantile of the busy seconds the ceiling falls back to when the peak runs away from
    /// it. Deliberately high: the axis should cut a handful of outliers, not a fifth of the data.</summary>
    private const double Quantile = 0.95;

    /// <summary>How far above that quantile the peak has to be before anything is clipped. Below it the
    /// strip stays exactly as faithful as it was — nothing is cut to buy contrast that isn't needed.</summary>
    private const double ClipRatio = 2.0;

    /// <summary>Busy seconds needed before clipping is allowed at all. On a sparse strip the few bars
    /// there are <em>are</em> the story, and cutting one of them costs more than it buys.</summary>
    private const int ClipMinSamples = 12;

    // The broken-bar mark: fill stops CapGap+CapHeight below the top, then a cap sits at the very top.
    // The gap is what reads as "cut here"; the cap is what reads as "and it continues".
    private const double CapGap = 2, CapHeight = 2;

    /// <summary>Categorical hues for the per-project stack (T100), in fixed order — slot 4 is the
    /// ceiling and a fifth series folds into <see cref="Others"/> rather than inventing a hue.
    /// Validated as a stack (adjacent pairs) against both themes' surfaces: worst CVD ΔE 9.1 light /
    /// 8.4 dark, worst normal-vision ΔE 20.8 light / 16.9 dark. The light steps for orange, aqua and
    /// yellow sit under 3:1 on the light surface, so the legend's direct labels are load-bearing
    /// relief, not decoration.</summary>
    public static Color[] ProjectColors(bool dark) => dark
        ? new[] { Color.FromRgb(0x39, 0x87, 0xE5), Color.FromRgb(0xD9, 0x59, 0x26),
                  Color.FromRgb(0x19, 0x9E, 0x70), Color.FromRgb(0xC9, 0x85, 0x00) }
        : new[] { Color.FromRgb(0x2A, 0x78, 0xD6), Color.FromRgb(0xEB, 0x68, 0x34),
                  Color.FromRgb(0x1B, 0xAF, 0x7A), Color.FromRgb(0xED, 0xA1, 0x00) };

    /// <summary>The residual bucket. Deliberately achromatic: "everything else" is not an identity,
    /// and a grey says so without competing with the four that are.</summary>
    public static Color Others(bool dark)
        => dark ? Color.FromRgb(0x7A, 0x7A, 0x7A) : Color.FromRgb(0x8A, 0x8A, 0x8A);

    private readonly Border _host;
    private readonly Grid _root = new();
    private readonly Grid _plot = new() { ClipToBounds = true };   // the columns' own clip: the slide
    private readonly Canvas _rules = new();                        // moves geometry past both edges
    private readonly Canvas _canvas = new();
    private readonly Canvas _gutter = new();
    private readonly TranslateTransform _slide = new();
    private readonly Func<double, string> _tick;
    private readonly Func<double, int, double, string> _tip;
    private readonly Line[] _lines;
    private readonly TextBlock[] _labels;
    private readonly Path _overflow = new();       // the caps on the columns that run past the top
    private Path[] _layers;
    private string? _tipText;

    // Vertical scale, in tokens for a full-height column. Rises immediately to fit a new peak and
    // decays slowly, so the strip re-scales visibly rather than jumping every second — and so a
    // quiet minute after a burst doesn't blow up the remaining noise to full height.
    private double _scale;
    // The same scale rounded up to a readable step: what full height *says* it means. Kept separate
    // because the decay has to stay smooth while the label may only ever be a round number.
    private double _ceiling;
    private bool _flat = true;

    /// <param name="tick">Formats an axis value (tokens in one second) for the gutter — the caller owns
    /// the app's number formatting and its localized "/s".</param>
    /// <param name="tip">Formats the hover text explaining what full height means, from the ceiling, how
    /// many columns run past it, and the busiest second actually seen.</param>
    public LiveStrip(Border host, bool dark, Func<double, string> tick, Func<double, int, double, string> tip)
    {
        _host = host;
        _tick = tick;
        _tip = tip;

        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gutter) });
        Grid.SetColumn(_plot, 0);
        Grid.SetColumn(_gutter, 1);
        _plot.Children.Add(_rules);      // behind the bars: a rule is a reference, not a series
        _plot.Children.Add(_canvas);
        _root.Children.Add(_plot);
        _root.Children.Add(_gutter);
        _canvas.RenderTransform = _slide;
        // The host keeps the hover, so its tooltip covers the whole strip rather than only the bars.
        _root.IsHitTestVisible = false;
        _host.Child = _root;

        _lines = new Line[Marks.Length];
        _labels = new TextBlock[Marks.Length];
        for (int i = 0; i < Marks.Length; i++)
        {
            // Resource *references*, not resolved brushes: the strip is built once and outlives a
            // light/dark switch, which a brush read at construction would not follow.
            var line = new Line { StrokeThickness = 1, SnapsToDevicePixels = true };
            if (Marks[i] < 1) line.StrokeDashArray = new DoubleCollection { 3, 3 };
            line.SetResourceReference(Shape.StrokeProperty, "DividerStrokeColorDefaultBrush");
            var label = new TextBlock { FontSize = 10 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
            _rules.Children.Add(_lines[i] = line);
            _gutter.Children.Add(_labels[i] = label);
        }

        _layers = Array.Empty<Path>();
        SetSeries(Colors(dark));
    }

    // One Path per series, rebuilt only when the series set changes (a project starting or stopping,
    // or the strip switching between the by-type and by-project views).
    private void SetSeries(Color[] colors)
    {
        if (_layers.Length == colors.Length &&
            _layers.Zip(colors).All(p => ((SolidColorBrush)p.First.Fill).Color == p.Second))
            return;

        _canvas.Children.Clear();
        _layers = colors.Select(c => new Path { Fill = Freeze(new SolidColorBrush(c)) }).ToArray();
        foreach (Path p in _layers) _canvas.Children.Add(p);
        // Last, so a cap is never buried under a series. Achromatic on purpose: the whole *stack*
        // overflowed, and painting the mark in one series' hue would blame that series for it. The
        // *primary* ink rather than a softer grey, so it can never be mistaken for the grey "others"
        // series it may sit directly above.
        _overflow.SetResourceReference(Shape.FillProperty, "TextFillColorPrimaryBrush");
        _canvas.Children.Add(_overflow);
    }

    /// <summary>The three token types, in stacking order (bottom first). Same hues as the legend, so
    /// identity is never colour-alone — the legend direct-labels each with its own rate.</summary>
    public static Color[] Colors(bool dark) => dark
        ? new[] { Color.FromRgb(0x39, 0x87, 0xE5), Color.FromRgb(0x19, 0x9E, 0x70), Color.FromRgb(0x90, 0x85, 0xE9) }
        : new[] { Color.FromRgb(0x2A, 0x78, 0xD6), Color.FromRgb(0x1B, 0xAF, 0x7A), Color.FromRgb(0x4A, 0x3A, 0xA7) };

    /// <summary>Draw the strip split by token type — the view when only one project is generating,
    /// where there is nothing to attribute and the mix is the informative cut.</summary>
    public void Render(TokenBits[] strip, bool dark, bool animate)
    {
        var series = new long[3][];
        for (int s = 0; s < 3; s++) series[s] = new long[strip.Length];
        for (int i = 0; i < strip.Length; i++)
        {
            series[0][i] = strip[i].Input;
            series[1][i] = strip[i].Output;
            series[2][i] = strip[i].CacheCreate;
        }
        Render(series, Colors(dark), animate);
    }

    /// <summary>Draw one second of history as stacked series. <paramref name="animate"/> slides the
    /// new column in; pass false for a static render (a resize, or the off-screen snapshot path).</summary>
    public void Render(long[][] series, Color[] colors, bool animate)
    {
        SetSeries(colors);

        double w = _host.ActualWidth, h = _host.ActualHeight;
        if (w <= 0 || h <= 0 || series.Length == 0 || series[0].Length == 0) return;

        double gutter = w >= AxisMinWidth ? Gutter : 0;
        if (_root.ColumnDefinitions[1].Width.Value != gutter)
            _root.ColumnDefinitions[1].Width = new GridLength(gutter);
        double pw = w - gutter;                      // the plot: everything left of the labels

        // The feed carries more history than the strip shows, and the *newest* end is the one this row
        // is about — so both the geometry and the scale below read backwards from the last bucket. (An
        // index-0 walk drew the oldest three minutes and clipped the rest away, then scaled what was
        // left to a peak that had already scrolled off.)
        int len = series[0].Length;
        int visible = Math.Min(Seconds, len);
        long peak = 0;
        var busy = new List<long>(visible);            // the non-zero seconds, for the quantile below
        for (int k = 0; k < visible; k++)
        {
            long v = 0;
            foreach (long[] s in series) v += s[len - 1 - k];
            if (v > peak) peak = v;
            if (v > 0) busy.Add(v);
        }

        if (peak <= 0)
        {
            // Nothing in the last three minutes. Clear once, then stop: repeated ticks over an empty
            // strip must not repaint, or "idle" would cost more than "busy".
            if (_flat) return;
            foreach (Path p in _layers) p.Data = null;
            _slide.BeginAnimation(TranslateTransform.XProperty, null);
            _slide.X = 0;
            _scale = _ceiling = 0;
            _overflow.Data = null;
            _host.ToolTip = _tipText = null;
            LayoutAxis(pw, h, gutter);               // no data, no axis: a scale nothing is drawn against
            _flat = true;
            return;
        }
        _flat = false;

        // What full height is measured against. The peak while the range is one a linear axis can hold;
        // the 95th percentile of the busy seconds once it isn't, because one 240k-token cache write
        // should not push a minute of real work onto the 1px floor.
        double basis = Basis(busy, peak);
        _scale = basis > _scale ? basis : Math.Max(basis, _scale * 0.94);
        double ceiling = NiceCeiling(_scale);
        _ceiling = ceiling;

        double colW = pw / Seconds;
        double barW = Math.Max(0.5, colW - (colW > 2.5 ? Gap : 0));
        // One extra column, laid out past the left edge, so the slide always has something to bring in.
        int drawn = Math.Min(Seconds + 1, len);

        var geo = new StreamGeometry[_layers.Length];
        var ctx = new StreamGeometryContext[_layers.Length];
        for (int i = 0; i < geo.Length; i++) { geo[i] = new StreamGeometry(); ctx[i] = geo[i].Open(); }
        var capGeo = new StreamGeometry();
        StreamGeometryContext capCtx = capGeo.Open();
        int clipped = 0;

        for (int k = 0; k < drawn; k++)
        {
            int i = len - 1 - k;                     // newest column at the right edge
            double x = pw - (k + 1) * colW;
            double y = h;                            // stack upward from the baseline
            bool first = true;

            // A column taller than the ceiling is drawn broken: filled to a gap below the top, then a
            // cap at the top. Truncating from the top of the stack keeps every segment at true scale —
            // the alternative (squashing the whole column to fit) would misreport all of them.
            long total = 0;
            foreach (long[] s in series) total += s[i];
            bool over = total > ceiling;
            if (over && k < visible) clipped++;
            double limit = over ? h - (CapGap + CapHeight) : h;

            for (int L = 0; L < series.Length; L++)
            {
                long v = series[L][i];
                if (v <= 0) continue;
                double seg = v / ceiling * h;
                // A non-zero second must be visible: a single small turn is information, and rounding
                // it to nothing would say "idle" when something did happen.
                if (seg < 1) seg = 1;
                // A hairline of surface between stacked fills, so two adjacent hues never touch — but
                // only where there is room for it, since eating a 2px segment to draw its gap would
                // delete the data it separates.
                if (!first && seg >= 3) { y -= 1; seg -= 1; }
                double room = limit - (h - y);
                if (room <= 0) break;
                if (seg > room) seg = room;
                Rect(ctx[L], x, y - seg, barW, seg);
                y -= seg;
                first = false;
            }

            if (over) Rect(capCtx, x, 0, barW, CapHeight);
        }

        for (int i = 0; i < geo.Length; i++)
        {
            ctx[i].Close();
            geo[i].Freeze();
            _layers[i].Data = geo[i];
        }
        capCtx.Close();
        capGeo.Freeze();
        _overflow.Data = clipped > 0 ? capGeo : null;

        // Rebuilt only when the *text* changes: the peak moves every second, but its compact form
        // ("240k/s") does not, and a tooltip reassigned under the pointer flickers.
        string tip = _tip(ceiling, clipped, peak);
        if (tip != _tipText) _host.ToolTip = _tipText = tip;

        LayoutAxis(pw, h, gutter);

        if (animate)
        {
            // Linear, exactly one second: the slide has to match the clock it represents, or the
            // motion stops being the data.
            _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(colW, 0,
                new Duration(TimeSpan.FromSeconds(1))) { EasingFunction = null });
        }
        else
        {
            _slide.BeginAnimation(TranslateTransform.XProperty, null);
            _slide.X = 0;
        }
    }

    // The axis: one faint rule per mark across the plot, its value in the gutter. It lives outside the
    // sliding layer on purpose — a number attached to something that moves reads as data, not as scale.
    private void LayoutAxis(double pw, double h, double gutter)
    {
        for (int i = 0; i < Marks.Length; i++)
        {
            // A rule is only drawn where its label has room: crowding two 10px labels into 20px of
            // strip would make the axis the noisiest thing in a row that is mostly 3px bars.
            bool show = gutter > 0 && _ceiling > 0 && pw > 0 &&
                        (i == 0 || h * (Marks[i - 1] - Marks[i]) >= 14);
            _lines[i].Visibility = _labels[i].Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) continue;

            double y = Math.Round(h - Marks[i] * h) + 0.5;
            _lines[i].X1 = 0;
            _lines[i].X2 = pw;
            _lines[i].Y1 = _lines[i].Y2 = y;

            _labels[i].Text = _tick(_ceiling * Marks[i]);
            Canvas.SetLeft(_labels[i], 5);
            Canvas.SetTop(_labels[i], Math.Clamp(y - 7, 0, Math.Max(0, h - 14)));
        }
    }

    /// <summary>
    /// What the ceiling is measured against: the peak, or the <see cref="Quantile"/> of the busy seconds
    /// once the peak is more than <see cref="ClipRatio"/>× above it.
    /// </summary>
    /// <remarks>Three guards, each paying for the honesty this costs. Clipping needs
    /// <see cref="ClipMinSamples"/> busy seconds, so a sparse strip never loses one of the few bars it
    /// has. It needs the peak to be a genuine outlier, so ordinary traffic keeps a fully faithful axis
    /// rather than one cut to buy contrast it doesn't need. And the quantile is taken over the *busy*
    /// seconds only — including the idle ones would drag it to zero and clip the whole strip.</remarks>
    private static double Basis(List<long> busy, long peak)
    {
        if (busy.Count < ClipMinSamples) return peak;
        busy.Sort();
        int idx = Math.Clamp((int)Math.Ceiling(Quantile * busy.Count) - 1, 0, busy.Count - 1);
        double q = busy[idx];
        return q > 0 && peak > q * ClipRatio ? q : peak;
    }

    /// <summary>The next readable step at or above <paramref name="peak"/> — what full height means.
    /// Rounding up costs bar height (at most a third), and buys a ceiling that can be *read*: a strip
    /// scaled to exactly its peak has a label like "23,481/s", which is a measurement of one bar rather
    /// than a scale for all of them.</summary>
    private static double NiceCeiling(double peak)
    {
        if (peak <= 0) return 0;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(peak)));
        foreach (double s in NiceSteps)
            if (peak <= s * mag * 1.000001) return s * mag;
        return 10 * mag;
    }

    /// <summary>Stop any running animation and forget the scale — used when the window is hidden, so
    /// nothing is left ticking behind a minimized window.</summary>
    public void Stop()
    {
        _slide.BeginAnimation(TranslateTransform.XProperty, null);
        _slide.X = 0;
    }

    private static void Rect(StreamGeometryContext ctx, double x, double y, double w, double h)
    {
        ctx.BeginFigure(new System.Windows.Point(x, y), isFilled: true, isClosed: true);
        ctx.LineTo(new System.Windows.Point(x + w, y), false, false);
        ctx.LineTo(new System.Windows.Point(x + w, y + h), false, false);
        ctx.LineTo(new System.Windows.Point(x, y + h), false, false);
    }

    private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}
