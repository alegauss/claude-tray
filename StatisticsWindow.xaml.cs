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
    // Invariant formatting, to match the English UI used throughout the app.
    private static readonly CultureInfo Fmt = CultureInfo.InvariantCulture;

    // Projection line color — a warm amber that reads on both light and dark backgrounds.
    private static readonly Brush ProjectionBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x30)));

    private readonly PaceSnapshot? _snapshot;
    private int _generation;
    private WindowPace? _session;  // last-rendered data, kept so the charts can redraw on resize
    private WindowPace? _weekly;

    public StatisticsWindow(PaceSnapshot? snapshot)
    {
        _snapshot = snapshot;
        InitializeComponent();

        try
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri(Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath));
        }
        catch { /* fall back to the default window icon */ }

        if (_snapshot is null)
            ShowStatus("Connect Claude Code to see your consumption pace. As soon as a usage reading comes in, the report appears here.");
        else
            Reload();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is not null) Reload();
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
        var target = (FrameworkElement)Content;

        const double scale = 1.5;
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(target.ActualWidth * scale), (int)(target.ActualHeight * scale),
            96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(target);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(path);
        encoder.Save(fs);
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
        ShowStatus("Computing your consumption pace…");

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
        ComputedText.Text = "Updated " + r.ComputedLocal.ToString("MMM d, HH:mm", Fmt);

        if (r.Error != null)
        {
            ShowStatus($"Couldn't build the report: {r.Error}");
            return;
        }

        StatusText.Visibility = Visibility.Collapsed;
        PanesBody.Visibility = Visibility.Visible;
        MethodNote.Visibility = Visibility.Visible;

        _session = r.Session;
        _weekly = r.Weekly;

        Populate(r.Session, ChipS, ChipTextS, UsedS, IdealS, ResetS, ProjectionS, ChartS);
        Populate(r.Weekly, ChipW, ChipTextW, UsedW, IdealW, ResetW, ProjectionW, ChartW);
    }

    private void Populate(WindowPace w, Border chip, TextBlock chipText,
        TextBlock used, TextBlock ideal, TextBlock reset, TextBlock projection, Canvas chart)
    {
        used.Text = w.HasWindow ? Pct(w.Util) : "—";
        ideal.Text = w.HasWindow ? Pct(w.IdealNow) : "—";
        reset.Text = w.HasWindow ? Dur(w.SecondsToReset) : "—";

        var (chipBg, chipLabel) = w.Verdict switch
        {
            PaceVerdict.Adequate => (Color.FromRgb(0x2E, 0x7D, 0x46), "On track"),
            PaceVerdict.Ahead => (Color.FromRgb(0xC7, 0x77, 0x00), "Too fast"),
            PaceVerdict.AtLimit => (Color.FromRgb(0xC4, 0x3E, 0x3E), "At limit"),
            _ => (Color.FromRgb(0x80, 0x80, 0x80), "No data"),
        };
        chip.Background = Freeze(new SolidColorBrush(chipBg));
        chipText.Text = chipLabel;

        projection.Text = ProjectionText(w);
        DrawChart(chart, w);
    }

    // Plain-language read of the pace + projection for one window.
    private string ProjectionText(WindowPace w)
    {
        if (!w.HasWindow)
            return "No limit reading for this window yet.";

        string toReset = Dur(w.SecondsToReset);
        return w.Verdict switch
        {
            PaceVerdict.AtLimit =>
                $"You've hit this window's limit. It resets in {toReset} — until then, usage is blocked.",
            PaceVerdict.Ahead when w.ExhaustFraction <= 1 && !double.IsInfinity(w.ExhaustSeconds) =>
                $"At the current pace, your quota reaches 100% in {Dur(w.ExhaustSeconds)} — about "
                + $"{Dur(w.SecondsToReset - w.ExhaustSeconds)} before the reset (in {toReset}). "
                + "Worth easing off so you don't get blocked.",
            PaceVerdict.Ahead =>
                $"You're consuming above the even pace ({Pct(w.IdealNow)} would be expected "
                + $"by now, but you've already used {Pct(w.Util)}). It resets in {toReset}.",
            _ =>
                $"You're pacing evenly — below the even-pace target of {Pct(w.IdealNow)}. "
                + $"At the current rate, the quota comfortably lasts until the reset, in {toReset}.",
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
        double Y(double cum) => top + (1 - Math.Clamp(cum, 0, 1)) * ph;

        if (!w.HasWindow)
        {
            var msg = new TextBlock
            {
                Text = "Sem dados de limite para esta janela.",
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

        // Even-pace reference: straight line from (0,0) to (reset,100%).
        c.Children.Add(new Line
        {
            X1 = X(0), Y1 = Y(0), X2 = X(1), Y2 = Y(1),
            Stroke = muted, StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 4 },
        });

        double ef = w.ElapsedFraction, util = w.Util;

        // Real consumption curve + soft fill under it.
        if (w.Curve.Count >= 2)
        {
            var pts = new PointCollection(w.Curve.Select(p => new Point(X(p.frac), Y(p.cum))));

            var fillPts = new PointCollection(pts) { new Point(X(ef), Y(0)), new Point(X(0), Y(0)) };
            c.Children.Add(new Polygon { Points = fillPts, Fill = accent, Opacity = 0.12 });

            c.Children.Add(new Polyline { Points = pts, Stroke = accent, StrokeThickness = 2.5 });
        }

        // Projection: extend the average pace from the current point to 100% (or to the reset).
        if (util > 0 && ef > 0)
        {
            var proj = new PointCollection { new Point(X(ef), Y(util)) };
            if (w.ExhaustFraction <= 1)
            {
                proj.Add(new Point(X(w.ExhaustFraction), Y(1)));
                proj.Add(new Point(X(1), Y(1)));
            }
            else
            {
                proj.Add(new Point(X(1), Y(util / ef)));
            }
            c.Children.Add(new Polyline
            {
                Points = proj, Stroke = ProjectionBrush, StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 4 },
            });
        }

        // "Now" marker: vertical line + dot at the current point.
        c.Children.Add(new Line
        {
            X1 = X(ef), Y1 = top, X2 = X(ef), Y2 = top + ph,
            Stroke = muted, StrokeThickness = 1, Opacity = 0.5,
        });
        var dot = new Ellipse { Width = 9, Height = 9, Fill = accent };
        Canvas.SetLeft(dot, X(ef) - 4.5);
        Canvas.SetTop(dot, Y(util) - 4.5);
        c.Children.Add(dot);

        // Axis labels: window start (left) and reset time (right).
        double startUnix = w.ResetUnix - w.WindowSeconds;
        AddAxisLabel(c, "start " + LocalTime(startUnix), left, top + ph + 4, axisFg, TextAlignment.Left);
        AddAxisLabel(c, "reset " + LocalTime(w.ResetUnix), X(1), top + ph + 4, axisFg, TextAlignment.Right);
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

    private static string LocalTime(double unix)
    {
        if (unix <= 0) return "—";
        DateTime local = DateTimeOffset.FromUnixTimeSeconds((long)unix).LocalDateTime;
        return local.ToString("MMM d, HH:mm", Fmt);
    }

    // Compact duration, matching the tray tooltip's style: "2d 4h", "3h 20m", "45m", "now".
    private static string Dur(double seconds)
    {
        if (double.IsInfinity(seconds) || seconds <= 0) return seconds <= 0 ? "now" : "—";
        int s = (int)Math.Round(seconds);
        int d = s / 86400, h = s % 86400 / 3600, m = s % 3600 / 60;
        if (d > 0) return $"{d}d {h}h";
        if (h > 0) return $"{h}h {m:00}m";
        return $"{Math.Max(1, m)}m";
    }

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
}
