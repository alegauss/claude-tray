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
/// </summary>
internal sealed class LiveStrip
{
    /// <summary>Seconds of history drawn — one column each.</summary>
    public const int Seconds = 180;

    /// <summary>Gap between columns, in device-independent pixels. Zero below a column width where
    /// the gap would eat the bar.</summary>
    private const double Gap = 1;

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
    private readonly Canvas _canvas = new();
    private readonly TranslateTransform _slide = new();
    private Path[] _layers;

    // Vertical scale, in tokens for a full-height column. Rises immediately to fit a new peak and
    // decays slowly, so the strip re-scales visibly rather than jumping every second — and so a
    // quiet minute after a burst doesn't blow up the remaining noise to full height.
    private double _scale;
    private bool _flat = true;

    public LiveStrip(Border host, bool dark)
    {
        _host = host;
        _canvas.RenderTransform = _slide;
        _canvas.IsHitTestVisible = false;
        _host.Child = _canvas;
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
        if (w <= 0 || h <= 0 || series.Length == 0) return;

        int n = series[0].Length;
        long peak = 0;
        for (int i = 0; i < n; i++)
        {
            long v = 0;
            foreach (long[] s in series) v += s[i];
            if (v > peak) peak = v;
        }

        if (peak <= 0)
        {
            // Nothing in the last three minutes. Clear once, then stop: repeated ticks over an empty
            // strip must not repaint, or "idle" would cost more than "busy".
            if (_flat) return;
            foreach (Path p in _layers) p.Data = null;
            _slide.BeginAnimation(TranslateTransform.XProperty, null);
            _slide.X = 0;
            _scale = 0;
            _flat = true;
            return;
        }
        _flat = false;

        _scale = peak > _scale ? peak : Math.Max(peak, _scale * 0.94);

        // One extra column so the incoming one starts fully off the right edge.
        double colW = w / Seconds;
        double barW = Math.Max(0.5, colW - (colW > 2.5 ? Gap : 0));

        var geo = new StreamGeometry[_layers.Length];
        var ctx = new StreamGeometryContext[_layers.Length];
        for (int i = 0; i < geo.Length; i++) { geo[i] = new StreamGeometry(); ctx[i] = geo[i].Open(); }

        for (int i = 0; i < n; i++)
        {
            double x = i * colW;
            double y = h;                            // stack upward from the baseline
            bool first = true;
            for (int L = 0; L < series.Length; L++)
            {
                long v = series[L][i];
                if (v <= 0) continue;
                double seg = v / _scale * h;
                // A non-zero second must be visible: a single small turn is information, and rounding
                // it to nothing would say "idle" when something did happen.
                if (seg < 1) seg = 1;
                // A hairline of surface between stacked fills, so two adjacent hues never touch — but
                // only where there is room for it, since eating a 2px segment to draw its gap would
                // delete the data it separates.
                if (!first && seg >= 3) { y -= 1; seg -= 1; }
                Rect(ctx[L], x, y - seg, barW, seg);
                y -= seg;
                first = false;
            }
        }

        for (int i = 0; i < geo.Length; i++)
        {
            ctx[i].Close();
            geo[i].Freeze();
            _layers[i].Data = geo[i];
        }

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
