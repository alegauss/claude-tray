using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;
// System.IO is a global using here (see the csproj), so `Path` is ambiguous with System.IO.Path.
using Path = System.Windows.Shapes.Path;

namespace ClaudeTray;

/// <summary>
/// The live throughput chart: the last <see cref="Seconds"/> seconds of the rolling rate as one line per
/// series, entering at the right and leaving at the left. Two of these are drawn on the Throughput tab —
/// one per project, one per token type — because a chart that silently changes what its colours mean is
/// worse than either view alone (T116).
///
/// <para>The whole justification for animating anything in this app is that here <b>the moving axis
/// is time</b> — the motion carries the data rather than decorating it. Everything else follows from
/// that one rule, and each consequence is cheap:</para>
/// <list type="bullet">
/// <item>Nothing running ⇒ nothing moves. An empty chart is drawn once, flat, and then left alone —
/// no geometry rebuild, no animation. A spinner or a breathing pulse would imply activity during
/// idleness, which is a lie a monitoring tool cannot afford.</item>
/// <item>Hidden ⇒ stopped. The clock runs only while the window is actually on screen, so a minimized
/// Statistics window costs exactly what it cost before this existed.</item>
/// <item>The window average, the pace charts and the verdict chip are untouched.</item>
/// </list>
///
/// <para>Motion without per-frame work: the geometry is rebuilt once a second (when the underlying
/// per-second samples shift), and a single <see cref="TranslateTransform"/> is animated from one
/// sample-width to zero over that second. The rebuild's one-sample jump and the slide cancel exactly, so
/// the chart scrolls continuously while the app draws at 1 Hz.</para>
///
/// <para><b>Lines, not bars (T116), because the quantity changed.</b> This drew stacked per-second
/// <em>token counts</em> until T114: events, since a turn's whole usage lands in the second it finished
/// (§V.11) — so the picture was spikes separated by holes, and joining them with a line would have drawn
/// a rate that never existed. It now draws <see cref="LiveRate"/>'s rolling rate sampled once a second,
/// which is continuous by construction: it decays through a pause because the work really is ageing out
/// of the window, and rises when new work lands. The newest point is the number printed above the chart
/// — same kernel, same smoothing — so the two can never disagree. Lines rather than a stack: the
/// attack-only smoothing is applied per series, so the parts no longer sum to the total exactly during a
/// rise, and nothing in an unstacked chart asks them to.</para>
///
/// <para><b>The height means something (T110).</b> Full height is a round number, ruled across the plot
/// and labelled in tok/s in a right-hand gutter, the way Task Manager labels its graphs — and it is
/// taken from the <b>newest</b> <see cref="Seconds"/> samples, never from a burst that has already
/// scrolled off. The ceiling rises instantly and decays slowly, so the chart re-scales visibly rather
/// than flapping. When the peak still runs away from the rest of the window (a large cache write is
/// ~30× ordinary work even after the kernel) the ceiling falls back to the <b>95th percentile</b> of the
/// moving samples, and the runs that go past the top are drawn as a <b>dashed line along the ceiling</b>
/// — T112's rule in the form a line can carry. Clipping is something a monitor may only do out loud, so
/// the mark says "above here" and the hover says how many samples and what the real peak was.</para>
///
/// <para><b>A point can be read (T104).</b> Hovering the plot snaps a crosshair to the nearest second,
/// dots every series where its line is, and opens a readout naming that second, each series' rate
/// through it and the tokens that actually <em>landed</em> in it — the distinction the rate view exists
/// to smooth over, and the one a shape alone can never answer. The readout is drawn <b>inside the
/// plot</b> rather than as a tooltip: a tooltip is a top-level popup, so it flickers when its text is
/// reassigned under the pointer and cannot appear in a captured screenshot. The rate it reports is read
/// from what was <em>drawn</em> (see <see cref="Render"/>), never from a recomputation, so the number
/// under the pointer is the number on the line.</para>
/// </summary>
internal sealed class LiveChart
{
    /// <summary>Seconds of history visible — one sample each. The scale and the axis are read from
    /// exactly this many samples.</summary>
    public const int Seconds = 180;

    /// <summary>
    /// Extra samples fetched and drawn <b>past the left edge</b>, so the line's oldest vertex is always
    /// outside the plot and therefore clipped (T117).
    /// </summary>
    /// <remarks>Without them the figure ended at the oldest visible sample — one sample-width inside the
    /// plot — and the 1s slide moved that endpoint back and forth across a visible column every second,
    /// which read as the start of the chart pulsing to a beat. The newest end never showed the artifact
    /// because it starts off the right edge and slides in; the oldest end needs the same treatment on the
    /// other side. Two, not one: at the moment the geometry is rebuilt the layer is offset by a full
    /// sample-width, so one would only just reach the edge.</remarks>
    public const int Overscan = 2;

    /// <summary>Samples a caller should hand to <see cref="Render"/>: the visible span plus the
    /// overscan. Bounded by <see cref="LiveRate.MaxRateHistory"/>.</summary>
    public const int Samples = Seconds + Overscan;

    /// <summary>Width reserved at the right of the host for the axis labels. Inside the plate rather
    /// than beside it: the labels sit on the same surface the lines are drawn on, which costs no layout
    /// row and — unlike a label floated over the plot — never covers the data.</summary>
    private const double Gutter = 40;

    /// <summary>Below this host width the axis is dropped: a gutter that ate a fifth of the plot would
    /// cost more data than its labels explain.</summary>
    private const double AxisMinWidth = 220;

    /// <summary>Where the axis is ruled, as a fraction of the ceiling — the ceiling itself and its
    /// half. Two lines are enough to read a value off and few enough to stay background.</summary>
    private static readonly double[] Marks = { 1.0, 0.5 };

    /// <summary>Steps a ceiling is allowed to land on, per decade. Fine enough that rounding up costs
    /// at most a third of the plot height, coarse enough that every label is a number a person reads
    /// without decoding it.</summary>
    private static readonly double[] NiceSteps = { 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 };

    /// <summary>The quantile of the moving samples the ceiling falls back to when the peak runs away
    /// from it. Deliberately high: the axis should cut a handful of outliers, not a fifth of the
    /// data.</summary>
    private const double Quantile = 0.95;

    /// <summary>How far above that quantile the peak has to be before anything is clipped. Below it the
    /// chart stays exactly as faithful as it was — nothing is cut to buy contrast that isn't needed.</summary>
    private const double ClipRatio = 2.0;

    /// <summary>Moving samples needed before clipping is allowed at all. On a nearly-idle chart the few
    /// samples there are <em>are</em> the story, and cutting one of them costs more than it buys.</summary>
    private const int ClipMinSamples = 12;

    /// <summary>Line weight: heavy enough to follow where two projects cross, light enough that five of
    /// them do not become a solid block.</summary>
    private const double Thickness = 1.8;

    /// <summary>Categorical hues for the per-project lines (T100), in fixed order — slot 4 is the
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

    /// <summary>The three token types, in legend order. Same hues as the legend, so identity is never
    /// colour-alone — the legend direct-labels each with its own rate.</summary>
    public static Color[] Colors(bool dark) => dark
        ? new[] { Color.FromRgb(0x39, 0x87, 0xE5), Color.FromRgb(0x19, 0x9E, 0x70), Color.FromRgb(0x90, 0x85, 0xE9) }
        : new[] { Color.FromRgb(0x2A, 0x78, 0xD6), Color.FromRgb(0x1B, 0xAF, 0x7A), Color.FromRgb(0x4A, 0x3A, 0xA7) };

    private readonly Border _host;
    private readonly Grid _root = new();
    private readonly Grid _plot = new() { ClipToBounds = true };   // the lines' own clip: the slide
    private readonly Canvas _rules = new();                        // moves geometry past both edges
    private readonly Canvas _canvas = new();
    private readonly Canvas _overlay = new();                      // crosshair + readout, above the lines
    private readonly Canvas _gutter = new();
    private readonly TranslateTransform _slide = new();
    private readonly Func<double, string> _tick;
    private readonly Func<double, int, double, string> _tip;
    private readonly Line[] _lines;
    private readonly TextBlock[] _labels;
    private readonly Line _cross = new() { StrokeThickness = 1, SnapsToDevicePixels = true };
    private readonly Border _card = new() { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(8, 6, 8, 6) };
    private readonly StackPanel _cardLines = new();
    private Path[] _layers;      // the line per series
    private Path[] _over;        // and the dashed run marking where it left the top
    private Ellipse[] _dots = Array.Empty<Ellipse>();   // one per series, at the hovered sample
    private string? _tipText;
    private string? _cardText;   // what the readout currently says, so it is rebuilt only when it changes
    private int _clipped;        // samples drawn along the ceiling: what the card must own up to
    private double _hoverX = double.NaN, _hoverY = double.NaN;
    private int _pin = -1;       // fixture: hold the readout open at this sample (seconds back)

    // What has been drawn, and the second it ends at. The chart keeps its own copy of the series and only
    // ever *appends* to it — see Render for why recomputing every second was visibly wrong (T119).
    private double[][]? _hist;
    private long _histSecond = long.MinValue;

    // Vertical scale, in tokens/second at the top of the plot. Rises immediately to fit a new peak and
    // decays slowly, so the chart re-scales visibly rather than jumping every second — and so a quiet
    // minute after a burst doesn't blow up the remaining noise to full height.
    private double _scale;
    // The same scale rounded up to a readable step: what full height *says* it means. Kept separate
    // because the decay has to stay smooth while the label may only ever be a round number.
    private double _ceiling;
    private bool _flat = true;

    /// <summary>Formats the per-sample reading (T104): how many seconds back the hovered sample is
    /// (0 = the second still filling) and the values this chart <b>drew</b> for it, one per series in
    /// the order they were rendered. Returning null or an empty string means "nothing to read here",
    /// and the readout stays closed. The caller owns the copy, the localization and which raw counts
    /// belong beside the rate — this class owns only where the pointer is.</summary>
    public Func<int, double[], string?>? Readout { get; set; }

    /// <param name="tick">Formats an axis value (tokens per second) for the gutter — the caller owns the
    /// app's number formatting and its localized "/s".</param>
    /// <param name="tip">Formats the hover text explaining what full height means, from the ceiling, how
    /// many samples run past it, and the highest rate actually seen.</param>
    public LiveChart(Border host, bool dark, Func<double, string> tick, Func<double, int, double, string> tip)
    {
        _host = host;
        _tick = tick;
        _tip = tip;

        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gutter) });
        Grid.SetColumn(_plot, 0);
        Grid.SetColumn(_gutter, 1);
        _plot.Children.Add(_rules);      // behind the lines: a rule is a reference, not a series
        _plot.Children.Add(_canvas);
        _plot.Children.Add(_overlay);    // and the reading on top of them
        _root.Children.Add(_plot);
        _root.Children.Add(_gutter);
        _canvas.RenderTransform = _slide;
        // The host keeps the hover, so its tooltip covers the whole chart rather than only the lines.
        _root.IsHitTestVisible = false;
        _host.Child = _root;

        // The crosshair and the readout live outside the sliding layer: they mark where the *pointer*
        // is, and something that answers the mouse must not drift with the scroll between rebuilds.
        _cross.SetResourceReference(Shape.StrokeProperty, "TextFillColorTertiaryBrush");
        _card.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorSecondaryBrush");
        _card.SetResourceReference(Border.BorderBrushProperty, "DividerStrokeColorDefaultBrush");
        _card.Child = _cardLines;
        _cross.Visibility = _card.Visibility = Visibility.Collapsed;
        _overlay.Children.Add(_cross);
        _overlay.Children.Add(_card);

        _host.MouseMove += (_, e) =>
        {
            System.Windows.Point p = e.GetPosition(_plot);
            _hoverX = p.X;
            _hoverY = p.Y;
            ShowReading(SampleAt(p.X));
        };
        _host.MouseLeave += (_, _) =>
        {
            _hoverX = _hoverY = double.NaN;
            ShowReading(_pin);      // back to the fixture's pinned sample, or closed
        };

        _lines = new Line[Marks.Length];
        _labels = new TextBlock[Marks.Length];
        for (int i = 0; i < Marks.Length; i++)
        {
            // Resource *references*, not resolved brushes: the chart is built once and outlives a
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
        _over = Array.Empty<Path>();
        SetSeries(Colors(dark));
    }

    // Two Paths per series — the line, and the dashed run that marks where it left the top — rebuilt only
    // when the series set changes (a project starting or stopping, or a theme switch).
    private void SetSeries(Color[] colors)
    {
        if (_layers.Length == colors.Length &&
            _layers.Zip(colors).All(p => ((SolidColorBrush)p.First.Stroke).Color == p.Second))
            return;

        _canvas.Children.Clear();
        _layers = colors.Select(c => new Path
        {
            Stroke = Freeze(new SolidColorBrush(c)),
            StrokeThickness = Thickness,
            StrokeLineJoin = PenLineJoin.Round,
        }).ToArray();
        _over = colors.Select(c => new Path
        {
            Stroke = Freeze(new SolidColorBrush(c)),
            StrokeThickness = Thickness,
            StrokeDashArray = new DoubleCollection { 2, 2 },
        }).ToArray();
        foreach (Path p in _layers) _canvas.Children.Add(p);
        foreach (Path p in _over) _canvas.Children.Add(p);

        // One dot per series, parked in the overlay: the crosshair says *which second*, the dots say
        // where each line was in it, which is what makes a five-line chart readable at a glance.
        foreach (Ellipse d in _dots) _overlay.Children.Remove(d);
        _dots = colors.Select(c => new Ellipse
        {
            Width = 6, Height = 6,
            Fill = Freeze(new SolidColorBrush(c)),
            Visibility = Visibility.Collapsed,
        }).ToArray();
        foreach (Ellipse d in _dots) _overlay.Children.Insert(0, d);   // under the card, never over it
    }

    /// <summary>
    /// Draw one second of history as one line per series, oldest sample first. <paramref name="animate"/>
    /// slides the newest sample in; pass false for a static render (a resize, or the off-screen snapshot
    /// path). <paramref name="second"/> is the unix second the series ends at — what tells an append from
    /// a re-render or a gap.
    /// </summary>
    /// <remarks>
    /// <b>Drawn is drawn (T119).</b> The series handed in is recomputed from the buckets every second, and
    /// a turn is reported up to a few seconds after the second it is stamped with (the tail's watcher is
    /// fast, its floor sweep is 3s) — so a late arrival lands in a bucket that is already *behind* the
    /// head and the recomputation raises points that were on screen a moment ago. Watched live, the last
    /// couple of seconds of the line kept being rebuilt into a different shape. This keeps its own history
    /// and only appends the newest sample to it: a value takes whatever the rate was when its second was
    /// drawn, and never changes afterwards. The full series is adopted wholesale only when there is
    /// nothing to append to — first render, a changed series set, or a gap (a hidden window stops the
    /// clock) — which is also what fills the chart with the three minutes that preceded it being opened.
    /// <para>The cost is stated rather than hidden: tokens whose report arrives late are counted from the
    /// moment the app learned about them, not moved backwards into a second that has already been drawn.
    /// `--live` still measures the underlying instability (`worst redraw drift`), because
    /// <see cref="LiveRate"/> stays a faithful recomputation — the freezing belongs to the picture.</para>
    /// </remarks>
    public void Render(double[][] series, Color[] colors, bool animate, long second)
    {
        SetSeries(colors);

        double w = _host.ActualWidth, h = _host.ActualHeight;
        if (w <= 0 || h <= 0 || series.Length == 0 || series[0].Length == 0) return;

        int inLen = series[0].Length;
        bool sameShape = _hist is not null && _hist.Length == series.Length && _hist[0].Length == inLen;
        long step = second - _histSecond;
        if (!sameShape || step < 0 || step > 1)
        {
            _hist = series.Select(s => (double[])s.Clone()).ToArray();
            _histSecond = second;
        }
        else if (step == 1)
        {
            foreach (double[] s in _hist!) Array.Copy(s, 1, s, 0, inLen - 1);
            for (int L = 0; L < _hist.Length; L++) _hist[L][inLen - 1] = series[L][inLen - 1];
            _histSecond = second;
        }
        // step == 0 is a re-render of the same second (a resize, a tab switch): draw what is already
        // there rather than taking the recomputation, or the resize would import the rewritten past.
        series = _hist!;

        double gutter = w >= AxisMinWidth ? Gutter : 0;
        if (_root.ColumnDefinitions[1].Width.Value != gutter)
            _root.ColumnDefinitions[1].Width = new GridLength(gutter);
        double pw = w - gutter;                      // the plot: everything left of the labels

        // Only the newest Seconds samples are drawn, and the scale comes from exactly those: a peak that
        // has scrolled off must not still be flattening what is on screen.
        int len = series[0].Length;
        int n = Math.Min(Seconds, len);
        double peak = 0;
        var moving = new List<double>(n);
        for (int k = 0; k < n; k++)
        {
            double top = 0;
            foreach (double[] s in series) top = Math.Max(top, s[len - 1 - k]);
            if (top > peak) peak = top;
            if (top > 0) moving.Add(top);
        }

        if (peak <= 0)
        {
            // Nothing in the last three minutes. Clear once, then stop: repeated ticks over an empty
            // chart must not repaint, or "idle" would cost more than "busy".
            if (_flat) return;
            foreach (Path p in _layers) p.Data = null;
            foreach (Path p in _over) p.Data = null;
            _slide.BeginAnimation(TranslateTransform.XProperty, null);
            _slide.X = 0;
            _hist = null;                            // nothing drawn, so nothing to preserve
            _histSecond = long.MinValue;
            _scale = _ceiling = 0;
            _host.ToolTip = _tipText = null;
            HideReading();                           // nothing drawn is nothing to read
            LayoutAxis(pw, h, gutter);               // no data, no axis: a scale nothing is drawn against
            _flat = true;
            return;
        }
        _flat = false;

        double basis = Basis(moving, peak);
        _scale = basis > _scale ? basis : Math.Max(basis, _scale * 0.94);
        double ceiling = NiceCeiling(_scale);
        _ceiling = ceiling;

        double colW = pw / Seconds;
        // Everything the caller handed over, including the overscan past the left edge — see Overscan:
        // an endpoint inside the plot is an endpoint the slide makes visibly oscillate.
        int drawn = Math.Min(Samples, len);
        int clipped = 0;

        var geo = new StreamGeometry[_layers.Length];
        var ctx = new StreamGeometryContext[_layers.Length];
        var overGeo = new StreamGeometry[_layers.Length];
        var overCtx = new StreamGeometryContext[_layers.Length];
        for (int i = 0; i < geo.Length; i++)
        {
            geo[i] = new StreamGeometry(); ctx[i] = geo[i].Open();
            overGeo[i] = new StreamGeometry(); overCtx[i] = overGeo[i].Open();
        }

        for (int L = 0; L < series.Length && L < _layers.Length; L++)
        {
            double[] s = series[L];
            bool started = false, runOpen = false;
            for (int k = drawn - 1; k >= 0; k--)     // oldest sample first, so the figure runs left→right
            {
                double v = s[len - 1 - k];
                double x = pw - k * colW;
                bool off = v > ceiling;
                // A run above the ceiling is drawn dashed *along* it: the line has to go somewhere, and a
                // solid line at the top would claim a value it does not have.
                double y = h - Math.Min(v, ceiling) / ceiling * h;

                if (!started) { ctx[L].BeginFigure(new System.Windows.Point(x, y), false, false); started = true; }
                else ctx[L].LineTo(new System.Windows.Point(x, y), true, false);

                if (off)
                {
                    if (k < n) clipped++;
                    if (!runOpen) { overCtx[L].BeginFigure(new System.Windows.Point(x, y), false, false); runOpen = true; }
                    else overCtx[L].LineTo(new System.Windows.Point(x, y), true, false);
                }
                else runOpen = false;
            }
        }

        for (int i = 0; i < geo.Length; i++)
        {
            ctx[i].Close(); geo[i].Freeze(); _layers[i].Data = geo[i];
            overCtx[i].Close(); overGeo[i].Freeze(); _over[i].Data = overGeo[i];
        }

        // Rebuilt only when the *text* changes: the peak moves every second, but its compact form
        // ("8k/s") does not, and a tooltip reassigned under the pointer flickers.
        string tip = _tip(ceiling, clipped, peak);
        if (tip != _tipText || clipped != _clipped)
        {
            _tipText = tip;
            _clipped = clipped;
            _cardText = null;   // the card may carry this sentence; rebuild it with the new one
            SyncTip();
        }

        LayoutAxis(pw, h, gutter);

        if (animate)
        {
            // Linear, and a hair longer than the second it represents. The slide has to match the clock
            // or the motion stops being the data — but a slide that lands on exactly zero *waits* there
            // for a rebuild delivered by a background-priority 1s timer, which never arrives exactly on
            // time. Stalling and then jumping a whole sample-width is what makes the scroll read as a
            // rhythmic hitch rather than as time passing; 5% of overshoot (0.2px of lag) covers the drift
            // and the rebuild always interrupts a still-moving slide.
            _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(colW, 0,
                new Duration(TimeSpan.FromSeconds(1.05))) { EasingFunction = null });
        }
        else
        {
            _slide.BeginAnimation(TranslateTransform.XProperty, null);
            _slide.X = 0;
        }

        // The chart moved under a pointer that did not: the second beneath the crosshair is a different
        // one now, so the reading is re-evaluated rather than left saying what was true a second ago.
        ShowReading(double.IsNaN(_hoverX) ? _pin : SampleAt(_hoverX));
    }

    // ------------------------------------------------------------------ the reading (T104)

    /// <summary>Hold the readout open at a fixed sample — how the deterministic fixture behind the
    /// published screenshot shows the interaction, since a captured window has no pointer in it.
    /// −1 turns it off.</summary>
    public void Pin(int secondsBack)
    {
        _pin = secondsBack;
        if (double.IsNaN(_hoverX)) ShowReading(secondsBack);
    }

    // Which sample the pointer is over. The lines are drawn at pw − k·colW *inside the sliding layer*,
    // so the slide's current offset belongs in the mapping — without it the crosshair would name the
    // second beside the one it is standing on for most of every second.
    private int SampleAt(double x)
    {
        if (_hist is null || _flat) return -1;
        double w = _host.ActualWidth;
        double pw = w - (w >= AxisMinWidth ? Gutter : 0);
        if (pw <= 0 || x < 0 || x > pw) return -1;   // the gutter is axis, not data
        double colW = pw / Seconds;
        int n = Math.Min(Seconds, _hist[0].Length);
        return Math.Clamp((int)Math.Round((pw + _slide.X - x) / colW), 0, n - 1);
    }

    // Crosshair at that second, a dot per series where its line is, and the card saying what it all
    // means. Everything is read from `_hist` — what was *drawn* — so the reading and the line can never
    // disagree, which is the same rule T119 settled for the line itself.
    private void ShowReading(int k)
    {
        double w = _host.ActualWidth, h = _host.ActualHeight;
        double pw = w - (w >= AxisMinWidth ? Gutter : 0);
        if (k < 0 || _flat || _hist is null || _ceiling <= 0 || Readout is null || pw <= 0 || h <= 0)
        {
            HideReading();
            return;
        }

        int len = _hist[0].Length;
        if (k >= len) { HideReading(); return; }
        var vals = new double[_hist.Length];
        for (int i = 0; i < _hist.Length; i++) vals[i] = _hist[i][len - 1 - k];

        string? text = Readout(k, vals);
        if (string.IsNullOrEmpty(text)) { HideReading(); return; }

        double colW = pw / Seconds;
        double x = Math.Clamp(pw - k * colW + _slide.X, 0, pw);

        _cross.X1 = _cross.X2 = Math.Round(x) + 0.5;
        _cross.Y1 = 0;
        _cross.Y2 = h;
        _cross.Visibility = Visibility.Visible;

        for (int i = 0; i < _dots.Length; i++)
        {
            bool on = i < vals.Length && vals[i] > 0;
            _dots[i].Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) continue;
            // Min against the ceiling for the same reason the line is clamped there: a clipped sample is
            // drawn along the top, and its dot has to sit on the line, not above the plot.
            Canvas.SetLeft(_dots[i], x - 3);
            Canvas.SetTop(_dots[i], h - Math.Min(vals[i], _ceiling) / _ceiling * h - 3);
        }

        if (text != _cardText) { BuildCard(text); _cardText = text; }

        _card.MaxWidth = Math.Max(80, pw - 8);
        _card.Measure(new Size(Math.Max(1, pw), Math.Max(1, h)));
        Size sz = _card.DesiredSize;
        // A card taller than the plot is clipped by it, which loses whichever line falls off the
        // bottom. The scale footer is the one part that can go: it qualifies the axis, and the axis is
        // still labelled in the gutter — the readings themselves may never be the thing that is cut.
        if (sz.Height > h - 8 && DropFooter())
        {
            _card.Measure(new Size(Math.Max(1, pw), Math.Max(1, h)));
            sz = _card.DesiredSize;
        }
        // Beside the crosshair, flipped when it would run off the right edge, and on the side of the
        // plot the pointer is *not* on — a readout that covers the line it is explaining is worse than
        // no readout.
        double left = x + 12;
        if (left + sz.Width > pw - 4) left = x - 12 - sz.Width;
        double top = !double.IsNaN(_hoverY) && _hoverY < h / 2 ? Math.Max(4, h - sz.Height - 4) : 4;
        Canvas.SetLeft(_card, Math.Clamp(left, 4, Math.Max(4, pw - sz.Width - 4)));
        Canvas.SetTop(_card, top);
        _card.Visibility = Visibility.Visible;
        SyncTip();
    }

    // One explanation at a time. While the card is open it carries the scale sentence itself, so the
    // tooltip is withdrawn — a popup fading in over a readout that is already answering is noise.
    private void SyncTip()
        => _host.ToolTip = _card.Visibility == Visibility.Visible ? null : _tipText;

    // The card's text: the first line is the second being read, the rest one line per series. The
    // footer appears **only while something is actually clipped** — then it is T112's disclosure, which
    // has to stay reachable while the tooltip is withdrawn; the rest of the time it would only restate
    // the axis the gutter already labels, at the cost of covering the chart it explains.
    private void BuildCard(string text)
    {
        _cardLines.Children.Clear();
        string[] rows = text.Split('\n');
        for (int i = 0; i < rows.Length; i++)
        {
            var tb = new TextBlock
            {
                Text = rows[i],
                FontSize = 11,
                Margin = new Thickness(0, i == 0 ? 0 : 2, 0, 0),
                FontWeight = i == 0 ? FontWeights.SemiBold : FontWeights.Normal,
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty,
                i == 0 ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
            _cardLines.Children.Add(tb);
        }

        if (_clipped <= 0 || _tipText is not { Length: > 0 } scale) return;
        var foot = new TextBlock
        {
            Text = scale,
            FontSize = 10,
            MaxWidth = 260,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Tag = FooterTag,
        };
        foot.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        _cardLines.Children.Add(foot);
    }

    private const string FooterTag = "scale";

    // True if there was a footer to drop. Only the disclosure is ever removed to make room.
    private bool DropFooter()
    {
        if (_cardLines.Children.Count == 0 ||
            _cardLines.Children[^1] is not TextBlock { Tag: FooterTag }) return false;
        _cardLines.Children.RemoveAt(_cardLines.Children.Count - 1);
        return true;
    }

    private void HideReading()
    {
        _cross.Visibility = _card.Visibility = Visibility.Collapsed;
        foreach (Ellipse d in _dots) d.Visibility = Visibility.Collapsed;
        _cardText = null;
        SyncTip();
    }

    // The axis: one faint rule per mark across the plot, its value in the gutter. It lives outside the
    // sliding layer on purpose — a number attached to something that moves reads as data, not as scale.
    private void LayoutAxis(double pw, double h, double gutter)
    {
        for (int i = 0; i < Marks.Length; i++)
        {
            // A rule is only drawn where its label has room: crowding two 10px labels into 20px of plot
            // would make the axis the noisiest thing in the chart.
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
    /// What the ceiling is measured against: the peak, or the <see cref="Quantile"/> of the moving
    /// samples once the peak is more than <see cref="ClipRatio"/>× above it.
    /// </summary>
    /// <remarks>Three guards, each paying for the honesty this costs. Clipping needs
    /// <see cref="ClipMinSamples"/> non-zero samples, so a nearly-idle chart never loses one of the few
    /// it has. It needs the peak to be a genuine outlier, so ordinary traffic keeps a fully faithful axis
    /// rather than one cut to buy contrast it doesn't need. And the quantile is taken over the *non-zero*
    /// samples only — including the idle stretches would drag it to zero and clip the whole chart. The
    /// rolling rate already compresses a 240k-token second roughly 30-fold, so this fires far less often
    /// than it did over raw per-second counts; it still fires, which is why it stays.</remarks>
    private static double Basis(List<double> moving, double peak)
    {
        if (moving.Count < ClipMinSamples) return peak;
        moving.Sort();
        int idx = Math.Clamp((int)Math.Ceiling(Quantile * moving.Count) - 1, 0, moving.Count - 1);
        double q = moving[idx];
        return q > 0 && peak > q * ClipRatio ? q : peak;
    }

    /// <summary>The next readable step at or above <paramref name="peak"/> — what full height means.
    /// Rounding up costs plot height (at most a third), and buys a ceiling that can be *read*: a chart
    /// scaled to exactly its peak has a label like "23,481/s", which is a measurement of one sample
    /// rather than a scale for all of them.</summary>
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

    /// <summary>
    /// Throw away everything drawn so far, so the next <see cref="Render"/> adopts its series wholesale
    /// instead of appending to them. <b>Stopping is not the same as clearing</b>: this chart keeps its own
    /// history by design (T119) precisely so a recomputation cannot rewrite the past — which is right
    /// while the past belongs to the same source, and wrong the moment the source changes. Switching the
    /// Statistics window to another profile is exactly that: a different config dir's transcripts, whose
    /// first second would otherwise be spliced onto three minutes of the previous account's line (T164).
    /// </summary>
    public void Clear()
    {
        Stop();
        foreach (Path p in _layers) p.Data = null;
        foreach (Path p in _over) p.Data = null;
        _hist = null;
        _histSecond = long.MinValue;
        _scale = _ceiling = 0;
        _host.ToolTip = _tipText = null;
        _clipped = 0;
        HideReading();                  // nothing drawn is nothing to read
        LayoutAxis(0, 0, 0);            // and no scale to label, since nothing is drawn against one
        // An empty chart *is* the flat state, so a tick that finds nothing to draw must not repaint it.
        _flat = true;
    }

    private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}
