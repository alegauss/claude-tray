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

/// <summary>
/// The Statistics window ("Statistics" in the tray menu): a pacing report for the 5-hour session and
/// 7-day weekly rate-limit windows, built from <see cref="UsageReport.ComputePace"/>. Each window gets
/// a burn-up chart — real consumption vs. the even-pace line, plus a dashed projection of where the
/// current pace lands — so it's visually obvious whether the quota is being spent evenly or will run
/// out before the reset.
///
/// Same WPF + built-in Fluent theme (<c>ThemeMode="System"</c>) as <see cref="SettingsWindow"/>. The
/// live utilization/reset numbers are passed in from the tray as a <see cref="PaceSnapshot"/>; the
/// transcript scan that shapes the curves runs off the UI thread, and a generation counter drops
/// stale results if the report is refreshed while a scan is in flight.
/// </summary>
internal partial class StatisticsWindow : Window
{
    // Invariant formatting for numbers/percentages, kept consistent regardless of the OS locale.
    private static readonly CultureInfo Fmt = CultureInfo.InvariantCulture;

    // Dates read more naturally in the display language (localized month names), so format them with
    // the active language's culture rather than the invariant one used for the numeric values.
    private static readonly CultureInfo DateFmt =
        L.Current == L.Lang.PtBr ? new CultureInfo("pt-BR") : CultureInfo.InvariantCulture;

    // Projection line color — a warm amber that reads on both light and dark backgrounds.
    private static readonly Brush ProjectionBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x30)));

    // The live rate-limit reading, refreshed in place by the tray on each poll (see UpdateSnapshot),
    // so the report tracks the same cadence configured in Settings without polling the API itself.
    private PaceSnapshot? _snapshot;
    private int _generation;
    private WindowPace? _session;  // last-rendered data, kept so the charts can redraw on resize
    private WindowPace? _weekly;

    // Mirrors Settings.ShowRemaining: when true the report reads as quota *left* — the burn-up chart
    // flips into a burn-down (starts full at 100%, descends to 0%), and every percentage/caption shows
    // the remaining side. The underlying pace, verdict and projection are unchanged; only the framing.
    private bool _remaining;

    public StatisticsWindow(PaceSnapshot? snapshot, bool showRemaining = false)
    {
        _snapshot = snapshot;
        _remaining = showRemaining;
        InitializeComponent();

        try
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri(Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath));
        }
        catch { /* fall back to the default window icon */ }

        if (_snapshot is null)
            ShowStatus(L.T("stats.connect"));
        else
            Reload();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is not null) Reload();
    }

    /// <summary>
    /// Feed a fresh live reading in from the tray's poll loop and re-render, so the open report
    /// auto-refreshes on the same cadence as the tray icon (the <c>RefreshSeconds</c> from Settings).
    /// A null snapshot (signed out / no reading) drops back to the "connect" hint. Called on the UI
    /// thread from <see cref="TrayContext"/>.
    /// </summary>
    internal void UpdateSnapshot(PaceSnapshot? snapshot)
    {
        _snapshot = snapshot;
        if (_snapshot is null)
            ShowStatus(L.T("stats.connect"));
        else
            Reload();
    }

    /// <summary>
    /// Flip the report between "used" and "remaining" framing when the Settings toggle changes while
    /// the window is open. Re-renders from the last computed pace (no re-scan) so it's instant. Called
    /// on the UI thread from <see cref="TrayContext"/>.
    /// </summary>
    internal void SetShowRemaining(bool showRemaining)
    {
        if (_remaining == showRemaining) return;
        _remaining = showRemaining;
        if (_session is { } s && _weekly is { } w)
        {
            ApplyModeLabels();
            Populate(s, ChipS, ChipTextS, UsedS, IdealS, ResetS, ProjectionS, ChartS, TpsHeadS, TpsBarS, TpsLegendS);
            Populate(w, ChipW, ChipTextW, UsedW, IdealW, ResetW, ProjectionW, ChartW, TpsHeadW, TpsBarW, TpsLegendW);
        }
    }

    /// <summary>
    /// Render the window's content — panes, verdict chips and both burn-up charts — to a PNG at 1.5×,
    /// off-screen, without depending on the window being visible or foreground. This is the
    /// deterministic capture path behind <c>--capture-stats</c> (the screen-copy path can't see a
    /// window that another app covers). Call after the async pace computation has rendered the charts.
    /// </summary>
    internal void SaveSnapshot(string path)
    {
        UpdateLayout();
        var target = (System.Windows.Controls.Panel)Content;

        // The window's own backdrop (Mica) isn't part of the visual tree, so paint an opaque themed
        // surface behind the content for the snapshot, then restore it.
        Brush? prevBg = target.Background;
        target.Background = CaptureBackdrop();
        target.UpdateLayout();

        const double scale = 1.5;
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(target.ActualWidth * scale), (int)(target.ActualHeight * scale),
            96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(target);

        target.Background = prevBg;

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(path);
        encoder.Save(fs);
    }

    // An opaque background for snapshots: the theme's base surface if present, else a Fluent-dark gray.
    private Brush CaptureBackdrop()
    {
        foreach (string key in new[] { "SolidBackgroundFillColorBaseBrush", "ApplicationBackgroundBrush" })
            if (TryFindResource(key) is Brush b) return b;
        return Freeze(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)));
    }

    /// <summary>Snapshot each tab in turn to <c>{basePath}-5h.png</c> / <c>{basePath}-7d.png</c>.
    /// Selecting a tab realizes its chart (its <c>SizeChanged</c> draws it), so both render fully.</summary>
    internal void SaveAllTabs(string basePath)
    {
        string[] suffixes = { "-5h.png", "-7d.png" };
        for (int i = 0; i < PanesBody.Items.Count && i < suffixes.Length; i++)
        {
            PanesBody.SelectedIndex = i;
            UpdateLayout();
            // Flush the render queue so the chart drawn on this tab's SizeChanged is present.
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            SaveSnapshot(basePath + suffixes[i]);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Reload()
    {
        if (_snapshot is not { } snap) return;
        int gen = ++_generation;
        ShowStatus(L.T("stats.computing"));

        DateTime nowUtc = DateTime.UtcNow;
        // Marshal the result back through the window's Dispatcher rather than a sync-context scheduler:
        // Reload() runs from the constructor, before a WPF SynchronizationContext exists on this thread.
        Task.Run(() => UsageReport.ComputePace(nowUtc, snap)).ContinueWith(t =>
        {
            PaceReport result = t.Result;
            Dispatcher.Invoke(() =>
            {
                if (gen != _generation) return;
                Render(result);
            });
        });
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
        PanesBody.Visibility = Visibility.Collapsed;
        MethodNote.Visibility = Visibility.Collapsed;
    }

    private void Render(PaceReport r)
    {
        ComputedText.Text = L.T("stats.updated", r.ComputedLocal.ToString("MMM d, HH:mm", DateFmt));

        if (r.Error != null)
        {
            ShowStatus(L.T("stats.buildFailed", r.Error));
            return;
        }

        StatusText.Visibility = Visibility.Collapsed;
        PanesBody.Visibility = Visibility.Visible;
        MethodNote.Visibility = Visibility.Visible;

        _session = r.Session;
        _weekly = r.Weekly;

        ApplyModeLabels();
        Populate(r.Session, ChipS, ChipTextS, UsedS, IdealS, ResetS, ProjectionS, ChartS, TpsHeadS, TpsBarS, TpsLegendS);
        Populate(r.Weekly, ChipW, ChipTextW, UsedW, IdealW, ResetW, ProjectionW, ChartW, TpsHeadW, TpsBarW, TpsLegendW);
    }

    // The static captions/legend labels that change wording between "used" and "remaining" framing.
    private void ApplyModeLabels()
    {
        string usedCaption = L.T(_remaining ? "stats.stat.left" : "stats.stat.used");
        UsedCaptionS.Text = usedCaption;
        UsedCaptionW.Text = usedCaption;

        string actual = L.T(_remaining ? "stats.legend.actualLeft" : "stats.legend.actual");
        LegendActualS.Text = actual;
        LegendActualW.Text = actual;
    }

    // A consumption fraction (0=empty, 1=at limit) as its displayed value: the same number when
    // showing "used", or its complement (quota left) when showing "remaining".
    private double Disp(double consumption) => _remaining ? 1 - consumption : consumption;

    private void Populate(WindowPace w, Border chip, TextBlock chipText,
        TextBlock used, TextBlock ideal, TextBlock reset, TextBlock projection, Canvas chart,
        TextBlock tpsHead, Border tpsBar, StackPanel tpsLegend)
    {
        used.Text = w.HasWindow ? Pct(Disp(w.Util)) : "—";
        ideal.Text = w.HasWindow ? Pct(Disp(w.IdealNow)) : "—";
        reset.Text = w.HasWindow ? Dur(w.SecondsToReset) : "—";

        var (chipBg, chipLabel) = w.Verdict switch
        {
            PaceVerdict.Adequate => (Color.FromRgb(0x2E, 0x7D, 0x46), L.T("stats.verdict.onTrack")),
            PaceVerdict.Ahead => (Color.FromRgb(0xC7, 0x77, 0x00), L.T("stats.verdict.tooFast")),
            PaceVerdict.AtLimit => (Color.FromRgb(0xC4, 0x3E, 0x3E), L.T("stats.verdict.atLimit")),
            _ => (Color.FromRgb(0x80, 0x80, 0x80), L.T("stats.verdict.noData")),
        };
        chip.Background = Freeze(new SolidColorBrush(chipBg));
        chipText.Text = chipLabel;

        projection.Text = ProjectionText(w);
        DrawChart(chart, w);
        PopulateThroughput(w, tpsHead, tpsBar, tpsLegend);
    }

    // Throughput breakdown: average tokens/second over the window, split into input / output /
    // cache-creation (cache reads are excluded — see WindowPace.TokensPerSecond). A rounded stacked
    // bar shows the mix; the legend direct-labels each type with its own tokens/sec (so identity is
    // never color-alone, per the palette's relief rule).
    private void PopulateThroughput(WindowPace w, TextBlock head, Border bar, StackPanel legend)
    {
        bar.Child = null;
        legend.Children.Clear();

        long input = w.InputTokens, output = w.OutputTokens, cache = w.CacheCreationTokens;
        long sum = input + output + cache;

        if (!w.HasWindow || w.ElapsedSeconds <= 0 || sum <= 0)
        {
            head.Text = L.T("stats.tps.none");
            bar.Visibility = Visibility.Collapsed;
            return;
        }
        bar.Visibility = Visibility.Visible;

        double elapsed = w.ElapsedSeconds;
        head.Text = L.T("stats.tps.head", Rate(w.TokensPerSecond), Big(sum));

        bool dark = IsDarkTheme();
        var types = new (string label, long tokens, Color color)[]
        {
            (L.T("stats.tps.input"),  input,  dark ? Color.FromRgb(0x39, 0x87, 0xE5) : Color.FromRgb(0x2A, 0x78, 0xD6)),
            (L.T("stats.tps.output"), output, dark ? Color.FromRgb(0x19, 0x9E, 0x70) : Color.FromRgb(0x1B, 0xAF, 0x7A)),
            (L.T("stats.tps.cacheCreate"), cache, dark ? Color.FromRgb(0x90, 0x85, 0xE9) : Color.FromRgb(0x4A, 0x3A, 0xA7)),
        };

        // Stacked bar: one star-weighted column per non-empty type, a 2px surface gap between them.
        var grid = new Grid();
        var gap = (Brush)FindResource("SolidBackgroundFillColorBaseBrush");
        int col = 0;
        var present = types.Where(t => t.tokens > 0).ToArray();
        for (int i = 0; i < present.Length; i++)
        {
            if (i > 0)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
                var spacer = new System.Windows.Controls.Border { Background = gap };
                Grid.SetColumn(spacer, col++);
                grid.Children.Add(spacer);
            }
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(present[i].tokens, GridUnitType.Star) });
            var seg = new System.Windows.Controls.Border { Background = Freeze(new SolidColorBrush(present[i].color)) };
            Grid.SetColumn(seg, col++);
            grid.Children.Add(seg);
        }
        bar.Child = grid;

        // Legend: colored swatch + type + its own rate, with the absolute total on hover.
        foreach (var t in types)
        {
            var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0) };
            row.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = 10, Height = 10, RadiusX = 3, RadiusY = 3,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = Freeze(new SolidColorBrush(t.color)),
            });
            double share = sum > 0 ? (double)t.tokens / sum : 0;
            row.Children.Add(new TextBlock
            {
                Text = L.T("stats.tps.legend", t.label, Rate(t.tokens / elapsed)),
                Margin = new Thickness(6, 0, 0, 0), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
                ToolTip = L.T("stats.tps.tip", t.label, Big(t.tokens), Pct(share)),
            });
            legend.Children.Add(row);
        }
    }

    // Plain-language read of the pace + projection for one window.
    private string ProjectionText(WindowPace w)
    {
        if (!w.HasWindow)
            return L.T("stats.proj.noWindow");

        string toReset = Dur(w.SecondsToReset);
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

        // Projection: extend the average pace from the current point to 100% (or to the reset).
        if (util > 0 && ef > 0)
        {
            var proj = new PointCollection { new Point(X(ef), Yc(util)) };
            double endX, endY;
            string projTip;
            if (w.ExhaustFraction <= 1)
            {
                proj.Add(new Point(X(w.ExhaustFraction), Yc(1)));
                proj.Add(new Point(X(1), Yc(1)));
                endX = X(w.ExhaustFraction); endY = Yc(1);
                projTip = L.T(_remaining ? "stats.chart.projHitZero" : "stats.chart.projHit", Dur(w.ExhaustSeconds));
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

    // "72%" — no space, matching the tray's percentage style.
    private static string Pct(double frac) => Math.Round(Math.Clamp(frac, 0, 1) * 100).ToString("0", Fmt) + "%";

    // A token rate: whole numbers once it's fast, a decimal or two when it's a trickle.
    private static string Rate(double tps) =>
        tps >= 100 ? tps.ToString("#,##0", Fmt)
        : tps >= 10 ? tps.ToString("0.0", Fmt)
        : tps.ToString("0.00", Fmt);

    // Compact token count: 3.1M / 42k / 517.
    private static string Big(long n) =>
        n >= 1_000_000 ? (n / 1e6).ToString("0.0", Fmt) + "M"
        : n >= 1_000 ? (n / 1e3).ToString("0.0", Fmt) + "k"
        : n.ToString(Fmt);

    // Dark theme when the primary text ink is light — used to pick the categorical bar hues (the
    // palette has a light and a dark step per series; the theme brushes only cover text/surfaces).
    private bool IsDarkTheme()
    {
        if (FindResource("TextFillColorPrimaryBrush") is SolidColorBrush b)
            return 0.299 * b.Color.R + 0.587 * b.Color.G + 0.114 * b.Color.B > 128;
        return true;
    }

    private static string LocalTime(double unix)
    {
        if (unix <= 0) return "—";
        DateTime local = DateTimeOffset.FromUnixTimeSeconds((long)unix).LocalDateTime;
        return local.ToString("MMM d, HH:mm", DateFmt);
    }

    // Compact duration, matching the tray tooltip's style: "2d 4h", "3h 20m", "45m", "now".
    private static string Dur(double seconds)
    {
        if (double.IsInfinity(seconds) || seconds <= 0) return seconds <= 0 ? L.T("dur.now") : "—";
        int s = (int)Math.Round(seconds);
        int d = s / 86400, h = s % 86400 / 3600, m = s % 3600 / 60;
        if (d > 0) return $"{d}d {h}h";
        if (h > 0) return $"{h}h {m:00}m";
        return $"{Math.Max(1, m)}m";
    }

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
}
