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

    private readonly Border _host;
    private readonly Canvas _canvas = new();
    private readonly TranslateTransform _slide = new();
    private readonly Path[] _layers;

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

        _layers = Colors(dark).Select(c => new Path { Fill = Freeze(new SolidColorBrush(c)) }).ToArray();
        foreach (Path p in _layers) _canvas.Children.Add(p);
    }

    /// <summary>The three token types, in stacking order (bottom first). Same hues as the legend, so
    /// identity is never colour-alone — the legend direct-labels each with its own rate.</summary>
    public static Color[] Colors(bool dark) => dark
        ? new[] { Color.FromRgb(0x39, 0x87, 0xE5), Color.FromRgb(0x19, 0x9E, 0x70), Color.FromRgb(0x90, 0x85, 0xE9) }
        : new[] { Color.FromRgb(0x2A, 0x78, 0xD6), Color.FromRgb(0x1B, 0xAF, 0x7A), Color.FromRgb(0x4A, 0x3A, 0xA7) };

    /// <summary>Draw one second of history. <paramref name="animate"/> slides the new column in;
    /// pass false for a static render (the off-screen snapshot path, or a first paint).</summary>
    public void Render(TokenBits[] strip, bool animate)
    {
        double w = _host.ActualWidth, h = _host.ActualHeight;
        if (w <= 0 || h <= 0) return;

        long peak = 0;
        foreach (TokenBits b in strip)
        {
            long v = b.Input + b.Output + b.CacheCreate;
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

        for (int i = 0; i < strip.Length; i++)
        {
            TokenBits b = strip[i];
            double x = i * colW;
            double y = h;                            // stack upward from the baseline
            long[] parts = { b.Input, b.Output, b.CacheCreate };
            for (int L = 0; L < parts.Length; L++)
            {
                if (parts[L] <= 0) continue;
                double seg = parts[L] / _scale * h;
                // A non-zero second must be visible: a single small turn is information, and rounding
                // it to nothing would say "idle" when something did happen.
                if (seg < 1) seg = 1;
                Rect(ctx[L], x, y - seg, barW, seg);
                y -= seg;
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
