using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// WinForms and WPF each contribute a Brush / Color / Orientation / HorizontalAlignment; pin these
// names to the WPF ones the gauge is drawn with (same convention as StatisticsPage).
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace ClaudeTray;

/// <summary>Part of <see cref="ContextPage"/> — split out by T133, moved verbatim.</summary>
internal partial class ContextPage
{
    /// <summary>
    /// The one visual the window exists for: what a session in this project loads, against the whole
    /// context window, split by where it comes from.
    ///
    /// Two things it must not fudge. Claude Code's own system prompt and tool definitions are ≈32k
    /// tokens that no file scan can see — they get their own neutral segment, because without them a
    /// bloated project looks like the entire problem when it is a third of it. And the number the
    /// transcripts actually measured is drawn as a tick over the bar rather than replacing it: the
    /// bar is an estimate, the tick is a measurement, and the gap between them is information.
    /// </summary>
    private void BuildGauge(ProjectRow row)
    {
        SessionZero? observed = row.Project!.Observed;
        int window = ContextScanner.ContextPageFor(observed?.Model);
        // The simulated saving comes off the total as well as off the segments, so the bar, the
        // caption and the banner all describe the same hypothetical session.
        int total = _baseTokens + row.Estimated - SimulatedSaving(row);

        bool dark = IsDarkTheme();
        var segments = new List<(string label, int tokens, Color color)>
        {
            // Neutral on purpose: this segment is not the developer's to trim.
            (L.T("context.gauge.base"), _baseTokens,
                dark ? Color.FromRgb(0x7A, 0x7A, 0x84) : Color.FromRgb(0x8C, 0x8C, 0x96)),
            (L.T("context.gauge.instructions"), Eager(row, Bucket.Instructions),
                dark ? Color.FromRgb(0x39, 0x87, 0xE5) : Color.FromRgb(0x2A, 0x78, 0xD6)),
            (L.T("context.gauge.memory"), Eager(row, Bucket.Memory),
                dark ? Color.FromRgb(0x90, 0x85, 0xE9) : Color.FromRgb(0x4A, 0x3A, 0xA7)),
            (L.T("context.gauge.skills"), Eager(row, Bucket.Skills),
                dark ? Color.FromRgb(0x19, 0x9E, 0x70) : Color.FromRgb(0x1B, 0xAF, 0x7A)),
        };

        int free = Math.Max(0, window - total);
        var track = (Brush)FindResource("SubtleFillColorSecondaryBrush");
        var gap = (Brush)FindResource("CardBackgroundFillColorDefaultBrush");

        // Stacked bar: one star-weighted column per non-empty segment, a 2px surface-colored gap
        // between them, and the free remainder left empty so the track shows through.
        var bar = new Grid();
        var present = segments.Where(s => s.tokens > 0).ToList();
        int col = 0;
        for (int i = 0; i < present.Count; i++)
        {
            if (i > 0)
            {
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
                var spacer = new Border { Background = gap };
                Grid.SetColumn(spacer, col++);
                bar.Children.Add(spacer);
            }
            bar.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(present[i].tokens, GridUnitType.Star),
            });
            var seg = new Border
            {
                Background = Freeze(new SolidColorBrush(present[i].color)),
                // ClipToBounds on the host Border clips to a rectangle, not to its rounded corners,
                // so the ends are rounded here instead — left on the first segment, right on the last
                // one only when nothing is free and it really is the end of the bar.
                CornerRadius = new CornerRadius(
                    i == 0 ? 6 : 0, i == present.Count - 1 && free == 0 ? 6 : 0,
                    i == present.Count - 1 && free == 0 ? 6 : 0, i == 0 ? 6 : 0),
                ToolTip = SegmentTip(present[i].label, present[i].tokens, window),
            };
            Grid.SetColumn(seg, col++);
            bar.Children.Add(seg);
        }
        if (free > 0)
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(free, GridUnitType.Star) });
        GaugeBar.Child = bar;

        // The measured tick. Two star-weighted columns put it exactly at the observed share of the
        // window; the label flips to the other side of it when it would be squeezed against the left
        // edge of the pane.
        GaugeTick.Children.Clear();
        GaugeTick.ColumnDefinitions.Clear();
        if (observed is { Median: > 0 } o)
        {
            double ratio = Math.Clamp((double)o.Median / window, 0, 1);
            GaugeTick.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
            GaugeTick.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - ratio, GridUnitType.Star) });

            var stem = new Border
            {
                Width = 2, Height = 9,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = (Brush)FindResource("TextFillColorPrimaryBrush"),
            };
            Grid.SetColumn(stem, 0);
            GaugeTick.Children.Add(stem);

            bool labelLeftOfTick = ratio >= 0.35;
            var label = new TextBlock
            {
                Text = L.T("context.gauge.measured", TokenEstimate.Format(o.Median)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = labelLeftOfTick ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = labelLeftOfTick ? new Thickness(0, 0, 3, 0) : new Thickness(4, 0, 0, 0),
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
                ToolTip = L.T("context.gauge.measuredTip", o.Samples),
            };
            Grid.SetColumn(label, labelLeftOfTick ? 0 : 1);
            GaugeTick.Children.Add(label);
        }

        BuildSimBanner(row, observed?.Model, window);

        BuildGrade(row);
        BuildDrift(row);

        int fixable = ProjectFindings(row).Count;
        FixCount.Text = fixable > 0 ? L.T("context.fix.count", fixable) : L.T("context.fix.none");
        FixHint.Text = L.T("context.fix.hint");

        // Caption: the total, its share of the window, and what loading it costs on a cold cache —
        // priced with the observed session's model when there is one, else the Opus-tier default.
        double cost = total * UsageInsights.Price(observed?.Model ?? "").cw / 1_000_000.0;
        GaugeCaption.Text = L.T("context.gauge.caption",
            TokenEstimate.Format(total), WindowLabel(window),
            ((double)total / window).ToString("P0", L.Culture),
            cost.ToString("0.000", L.Culture));

        // Legend, including the free remainder — the empty part of the bar is a number too.
        GaugeLegend.Children.Clear();
        foreach (var (label, tokens, color) in segments)
            AddLegendItem(label, tokens, Freeze(new SolidColorBrush(color)), window);
        AddLegendItem(L.T("context.gauge.free"), free, track, window);
    }

    // The grade chip beside the project title, with its inputs in the tooltip.
    private void BuildGrade(ProjectRow row)
    {
        if (row.Debt is not { } debt)
        {
            GradeChip.Visibility = Visibility.Collapsed;
            return;
        }

        GradeChip.Visibility = Visibility.Visible;
        GradeChip.Background = RowStyle.For(this, IsDarkTheme()).GradeChip(debt.Grade);
        GradeText.Text = debt.Grade.ToString();
        GradeChip.ToolTip = row.GradeTip;
    }

    /// <summary>
    /// The drift line: what this project&apos;s eager context did over the last weeks. Without history
    /// there is nothing honest to draw, so the row stays hidden rather than showing a flat line that
    /// would read as "stable".
    /// </summary>
    private void BuildDrift(ProjectRow row)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ContextTrend? trend = PreviewDemoHistory
            ? ContextHistory.Demo(row.Estimated, row.Project!.Bytes, now)
            : ContextHistory.Trend(ProfileStore.Monitored, row.Slug, DateTimeOffset.UtcNow.UtcDateTime);

        DriftSpark.Children.Clear();
        if (trend is null || !trend.CanDraw)
        {
            DriftRow.Visibility = Visibility.Collapsed;
            return;
        }

        DriftRow.Visibility = Visibility.Visible;
        DriftText.Text = trend.HasBaseline
            ? L.T(trend.DeltaTokens >= 0 ? "context.drift.up" : "context.drift.down",
                TokenEstimate.Format(Math.Abs(trend.DeltaTokens)),
                ContextText.Size(Math.Abs(trend.DeltaBytes)), ContextHistory.WeekDays)
            : L.T("context.drift.building", trend.Points.Count, trend.SpanDays);

        DrawSparkline(trend);
    }

    // A sparkline of the eager total, min-max scaled over its samples so the shape of the change is
    // visible even when the absolute movement is small.
    private void DrawSparkline(ContextTrend trend)
    {
        double w = DriftSpark.Width, h = DriftSpark.Height;
        int min = trend.Points.Min(s => s.Eager);
        int max = trend.Points.Max(s => s.Eager);
        double span = Math.Max(1, max - min);
        long t0 = trend.Points[0].T, t1 = trend.Points[^1].T;
        double dt = Math.Max(1, t1 - t0);

        var line = new System.Windows.Shapes.Polyline
        {
            Stroke = Freeze(new SolidColorBrush(IsDarkTheme()
                ? Color.FromRgb(0x39, 0x87, 0xE5)
                : Color.FromRgb(0x2A, 0x78, 0xD6))),
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round,
        };
        foreach (ContextSample s in trend.Points)
            line.Points.Add(new System.Windows.Point(
                (s.T - t0) / dt * (w - 2) + 1,
                h - 3 - (s.Eager - min) / span * (h - 6)));
        DriftSpark.Children.Add(line);

        // The latest sample gets a dot: it is the number every other panel is talking about.
        var dot = new System.Windows.Shapes.Ellipse { Width = 4, Height = 4, Fill = line.Stroke };
        System.Windows.Controls.Canvas.SetLeft(dot, line.Points[^1].X - 2);
        System.Windows.Controls.Canvas.SetTop(dot, line.Points[^1].Y - 2);
        DriftSpark.Children.Add(dot);
    }

    /// <summary>
    /// What the ticked rows would give back — the whole point of the exercise: see the payoff before
    /// taking any risk. This window never deletes anything, so the banner says so plainly rather than
    /// offering an Apply button it would not honour.
    /// </summary>
    private void BuildSimBanner(ProjectRow row, string? model, int window)
    {
        int saving = SimulatedSaving(row);
        if (saving <= 0 && _simulated.Count == 0)
        {
            SimBanner.Visibility = Visibility.Collapsed;
            return;
        }

        SimBanner.Visibility = Visibility.Visible;
        int before = _baseTokens + row.Estimated;
        int after = before - saving;
        double saved = saving * UsageInsights.Price(model ?? "").cw / 1_000_000.0;
        int ticked = Relevant(row).Count(s => _simulated.Contains(s.Path));

        SimHeadline.Text = L.T("context.sim.headline",
            TokenEstimate.Format(saving), saved.ToString("0.000", L.Culture));
        SimDetail.Text = L.T("context.sim.detail",
            ticked, TokenEstimate.Format(before), TokenEstimate.Format(after),
            ((double)after / window).ToString("P0", L.Culture));
    }

    // A checkbox toggled: update the selection and redraw the gauge only. The table is deliberately
    // not rebuilt — it would reset the scroll position under the pointer that just clicked.
    private void SimulateToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.ToggleButton box) return;
        if (RowOf(sender) is not { } row) return;

        if (box.IsChecked == true) _simulated.Add(row.FullPath);
        else _simulated.Remove(row.FullPath);

        if (ProjectList.SelectedItem is ProjectRow selected && !selected.IsAll)
            BuildGauge(selected);
    }

    private void AddLegendItem(string label, int tokens, Brush swatch, int window)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 18, 4),
            ToolTip = SegmentTip(label, tokens, window),
        };
        row.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width = 10, Height = 10, RadiusX = 3, RadiusY = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = swatch,
        });
        row.Children.Add(new TextBlock
        {
            Text = $"{label}  {TokenEstimate.Format(tokens)}",
            Margin = new Thickness(6, 0, 0, 0), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource(tokens > 0
                ? "TextFillColorSecondaryBrush"
                : "TextFillColorTertiaryBrush"),
        });
        GaugeLegend.Children.Add(row);
    }

    private static string SegmentTip(string label, int tokens, int window)
        => L.T("context.gauge.tip", label, TokenEstimate.Format(tokens),
            ((double)tokens / window).ToString("P0", L.Culture));

    /// <summary>"200k" / "1M" — the window size, which is exact, so no "≈".</summary>
    private static string WindowLabel(int window) => window >= 1_000_000
        ? (window / 1_000_000) + "M"
        : (window / 1000) + "k";

    private enum Bucket { Instructions, Memory, Skills }

    /// <summary>Eager tokens in one bucket, over the project's own sources and the shared ones —
    /// which is exactly what the project's session zero is made of.</summary>
    private int Eager(ProjectRow row, Bucket bucket)
        => Relevant(row)
            .Where(s => BucketOf(s.Kind) == bucket)
            .Where(s => !_simulated.Contains(s.Path))
            .Sum(s => s.EagerTokens);

    /// <summary>Everything a session in this project loads: its own sources plus the shared ones.</summary>
    private static IEnumerable<ContextSource> Relevant(ProjectRow row)
        => row.Project!.Sources.Concat(row.Scan.Shared);

    /// <summary>What the ticked rows would give back in this project, in eager tokens.</summary>
    private int SimulatedSaving(ProjectRow row)
        => _simulated.Count == 0
            ? 0
            : Relevant(row).Where(s => _simulated.Contains(s.Path)).Sum(s => s.EagerTokens);

    private static Bucket BucketOf(ContextKind kind) => kind switch
    {
        ContextKind.MemoryIndex or ContextKind.MemoryFile => Bucket.Memory,
        ContextKind.Skill or ContextKind.Agent => Bucket.Skills,
        // Instructions and their @imports; settings never reach here, since they cost 0 eager.
        _ => Bucket.Instructions,
    };

    // Same test the Statistics window uses: read the theme's own text brush rather than guessing from
    // a setting, so it follows ThemeMode="System" even when Windows flips mid-session.
    private bool IsDarkTheme()
        => FindResource("TextFillColorPrimaryBrush") is not SolidColorBrush b ||
           0.299 * b.Color.R + 0.587 * b.Color.G + 0.114 * b.Color.B > 128;

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
}
