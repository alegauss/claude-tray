using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// WinForms and WPF each contribute a Brush / Color / Orientation / HorizontalAlignment; pin these
// names to the WPF ones the gauge is drawn with (same convention as StatisticsWindow).
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace ClaudeTray;

/// <summary>
/// The Context Load window: what opening a session in a project costs before the first prompt.
/// Master/detail — every <c>~/.claude/projects</c> directory on the left, heaviest first, and on the
/// right what the selected one loads, source by source, split into what is paid every request (eager)
/// and what is only paid when used (lazy).
///
/// It is the windowed half of <see cref="ContextScanner"/>, whose headless report (<c>--context</c>)
/// stays the developer view. The scan runs on a background thread — a cold walk of a developer's
/// whole <c>~/.claude</c> is hundreds of files — and the window is previewable standalone via
/// <c>--context --window [slug]</c>, so the `preview-ui` capture loop applies unchanged.
///
/// Privacy (the app's standing promise): paths, sizes, timestamps and token estimates only. No file
/// contents are shown here, and nothing leaves the machine.
/// </summary>
internal partial class ContextWindow : Window
{
    private readonly string? _initialSelection;
    private bool _scanning;
    private bool _selected;   // an initial selection has already been applied
    /// <summary>Base overhead for the gauge: fitted from this machine's transcripts when there are
    /// enough observed sessions, otherwise the measured fallback.</summary>
    private int _baseTokens = ContextScanner.FallbackBaseTokens;
    /// <summary>Evidence of use, mined after the first render because it reads hundreds of megabytes
    /// of transcripts. Null until that pass finishes — every consumer treats null as "unknown".</summary>
    private UsageEvidence? _evidence;

    /// <summary>
    /// Preview-only: once the panes are populated, scroll down to the source table so it can be
    /// screenshotted. The detail pane is taller than any screen at the default window size (the gauge
    /// is the hero and owns the top), and a screen-copy capture cannot scroll — the same reason the
    /// Statistics window has its own preview entry points.
    /// </summary>
    internal bool PreviewScrollToTable { get; set; }

    /// <param name="selectSlug">Project to open on (slug, directory name or path); null = the heaviest.</param>
    public ContextWindow(string? selectSlug = null)
    {
        InitializeComponent();
        _initialSelection = selectSlug;

        ProjectList.SelectionChanged += (_, _) => ShowSelected();
        // Re-sorting is just a rebuild of the two tables from the same scan — no IO, so it can be
        // wired straight to the selection.
        SortBox.SelectionChanged += (_, _) => { if (IsLoaded) ShowSelected(); };
        RescanButton.Click += (_, _) => StartScan();
        CloseButton.Click += (_, _) => Close();
        Loaded += (_, _) => StartScan();
    }

    // Scan off the UI thread, then render. Deliberately re-plans rather than forcing a cold walk:
    // the scanner's fingerprint is over every candidate file's path+size+mtime, so a cached result is
    // only reused when nothing that matters changed — which is exactly what "rescan" should mean.
    private async void StartScan()
    {
        if (_scanning) return;
        _scanning = true;
        RescanButton.IsEnabled = false;
        ShowStatus(L.T("context.scanning"));

        ContextScan scan;
        try
        {
            scan = await Task.Run(() => ContextScanner.Scan(DateTimeOffset.UtcNow.UtcDateTime));
        }
        catch (Exception e)
        {
            // Scan() already swallows its own IO errors into scan.Error; this is the belt-and-braces
            // path so a surprise from the task machinery can't take the window down with it.
            scan = new ContextScan { Error = e.Message };
        }
        finally
        {
            _scanning = false;
            RescanButton.IsEnabled = true;
        }

        Apply(scan);
    }

    private void Apply(ContextScan scan)
    {
        ScanInfo.Text = ScanInfoText(scan);

        if (scan.Error != null) { ShowStatus(L.T("context.scanFailed", scan.Error)); return; }
        if (scan.Projects.Count == 0) { ShowStatus(L.T("context.empty")); return; }

        // Prefer this machine's own fitted base overhead — on a dev box with plenty of transcripts it
        // is a measurement, not a constant. Needs 3+ projects with an observed session, so a fresh
        // install keeps the fallback.
        _baseTokens = ContextScanner.Calibrate(scan) is { Base: > 0 } c
            ? (int)Math.Round(c.Base)
            : ContextScanner.FallbackBaseTokens;

        StatusText.Visibility = Visibility.Collapsed;
        MasterPane.Visibility = Visibility.Visible;
        DetailPane.Visibility = Visibility.Visible;

        // Keep the user on the project they were looking at across a rescan; otherwise honor the
        // slug the window was opened with, and fall back to the heaviest (the list is already
        // sorted by eager tokens).
        string? keep = (ProjectList.SelectedItem as ProjectRow)?.Slug
                       ?? (_selected ? null : _initialSelection);

        // "All projects" leads the list: the duplication and dead-directory problems only exist
        // between projects, so there has to be somewhere they are visible. The default selection is
        // still the heaviest project — the gauge is what the window is for.
        var rows = new List<ProjectRow> { new(scan, null) };
        rows.AddRange(scan.Projects.Select(p => new ProjectRow(scan, p)));
        ProjectList.ItemsSource = rows;
        // "all" selects the overview — it mirrors `--context --all` on the CLI and gives the preview
        // loop a way to open this view, which is otherwise never the default.
        ProjectList.SelectedItem =
            "all".Equals(keep, StringComparison.OrdinalIgnoreCase)
                ? rows[0]
                : rows.FirstOrDefault(r => r.Matches(keep))
                  ?? rows.FirstOrDefault(r => !r.IsAll)
                  ?? rows[0];
        // The list is sorted by weight, so a project named on the command line — or simply kept
        // across a rescan — is usually below the fold; without this the selection is invisible.
        ProjectList.ScrollIntoView(ProjectList.SelectedItem);
        _selected = true;

        StartUsagePass();
    }

    // The evidence pass reads every recent transcript — hundreds of megabytes on a real machine — so
    // it runs after the window is already useful, and the table re-renders with the counts when it
    // lands. Once per window: the per-file cache makes a later rescan cheap, but the first pass is
    // seconds, and nothing here changes while the window is open.
    private async void StartUsagePass()
    {
        if (_evidence != null || _usageRunning) return;
        _usageRunning = true;
        try
        {
            _evidence = await Task.Run(() => ContextUsage.Compute(DateTimeOffset.UtcNow.UtcDateTime));
            ShowSelected();
        }
        catch { /* no evidence is a supported state — every consumer renders "—" for unknown */ }
        finally { _usageRunning = false; }
    }

    private bool _usageRunning;

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
        MasterPane.Visibility = Visibility.Collapsed;
        DetailPane.Visibility = Visibility.Collapsed;
    }

    // "33 projects · 812 files · 76 ms (cached)", plus the capped-walk warning when the file cap cut
    // the scan short — a silent cap reads as "all clear", which it isn't.
    private static string ScanInfoText(ContextScan scan)
    {
        string info = L.T("context.footer",
            scan.Projects.Count, scan.FilesWalked,
            scan.ElapsedMs.ToString("0", L.Culture),
            L.T(scan.FromCache ? "context.footer.cached" : "context.footer.fresh"));
        return scan.Truncated ? info + "  " + L.T("context.truncated") : info;
    }

    private void ShowSelected()
    {
        if (ProjectList.SelectedItem is not ProjectRow row) return;

        AllDetail.Visibility = row.IsAll ? Visibility.Visible : Visibility.Collapsed;
        ProjectDetail.Visibility = row.IsAll ? Visibility.Collapsed : Visibility.Visible;
        if (row.IsAll) { BuildAllProjects(row.Scan); ScrollToCrossProjectIssues(); return; }

        ProjectTitle.Text = row.Project!.ShortPath;
        ProjectPath.Text = row.PathLine;

        EstimatedValue.Text = TokenEstimate.Format(row.Estimated);
        EstimatedParts.Text = L.T("context.sessionZero.parts",
            TokenEstimate.Format(row.Scan.SharedEagerTokens),
            TokenEstimate.Format(row.Project!.EagerTokens));

        if (row.Project!.Observed is { } o)
        {
            ObservedValue.Text = TokenEstimate.Format(o.Median);
            ObservedParts.Text = L.T("context.observed.parts",
                o.Samples, TokenEstimate.Format(o.Min), TokenEstimate.Format(o.Max),
                o.Cost.ToString("0.000", L.Culture));
        }
        else
        {
            ObservedValue.Text = "—";
            ObservedParts.Text = L.T("context.observed.none", row.Project!.TranscriptsRead);
        }

        BuildGauge(row);

        var style = RowStyle.For(this, IsDarkTheme());
        SourceSort sort = (SourceSort)Math.Clamp(SortBox.SelectedIndex, 0, 2);
        DateTime now = DateTimeOffset.UtcNow.UtcDateTime;
        ProjectGroups.ItemsSource = SourceGroup.Build(row.Project!.Sources, style, sort, _evidence, now);
        SharedGroups.ItemsSource = SourceGroup.Build(row.Scan.Shared, style, sort, _evidence, now);

        // After layout, so the new rows have a height to scroll past. BringIntoView stops at the
        // top of the project's own table rather than running on to the shared section at the end.
        if (PreviewScrollToTable) Dispatcher.BeginInvoke(() => ProjectGroups.BringIntoView());
    }

    /// <summary>Preview-only, as above: bring the cross-project findings into view.</summary>
    private void ScrollToCrossProjectIssues()
    {
        if (PreviewScrollToTable) Dispatcher.BeginInvoke(() => AllIssuesList.BringIntoView());
    }

    // ---------------------------------------------------------------- all projects

    /// <summary>
    /// The cross-project view. Everything here answers a question no single project can: which
    /// projects actually carry the weight, and what is duplicated or dead between them. Worktree
    /// siblings sharing one memory directory byte for byte is the expensive case, and it is
    /// completely invisible from inside either of them.
    /// </summary>
    private void BuildAllProjects(ContextScan scan)
    {
        int files = scan.Shared.Count + scan.Projects.Sum(p => p.Sources.Count);
        long bytes = scan.Shared.Sum(s => s.Bytes) + scan.Projects.Sum(p => p.Bytes);

        AllSubtitle.Text = L.T("context.all.subtitle",
            scan.Projects.Count, files, ContextText.Size(bytes));
        AllFootprint.Text = ContextText.Size(bytes);
        AllShared.Text = TokenEstimate.Format(scan.SharedEagerTokens);

        var ranked = scan.Projects
            .Select(p => (project: p, eager: scan.EstimatedSessionZero(p)))
            .OrderByDescending(x => x.eager)
            .ToList();

        AllHeaviestValue.Text = ranked.Count > 0 ? TokenEstimate.Format(ranked[0].eager) : "—";
        AllHeaviestName.Text = ranked.Count > 0 ? ranked[0].project.ShortPath : "";
        // Per §II.0.1: the base overhead is never folded into a per-project number, and it is paid
        // per session rather than per project — so it is named here instead of added in.
        AllNote.Text = L.T("context.all.note", TokenEstimate.Format(_baseTokens));

        BuildHeaviestList(ranked.Take(10).ToList());
        BuildCrossProjectIssues(scan);
    }

    // One row per heavy project: name, a bar relative to the heaviest, and the number. The bar is two
    // star-weighted columns, so it stays correct when the window is resized.
    private void BuildHeaviestList(List<(ContextProject project, int eager)> ranked)
    {
        AllHeaviestList.Children.Clear();
        if (ranked.Count == 0) return;

        int max = Math.Max(1, ranked[0].eager);
        var track = (Brush)FindResource("SubtleFillColorSecondaryBrush");
        var fill = Freeze(new SolidColorBrush(IsDarkTheme()
            ? Color.FromRgb(0x39, 0x87, 0xE5)
            : Color.FromRgb(0x2A, 0x78, 0xD6)));

        foreach (var (project, eager) in ranked)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });

            var name = new TextBlock
            {
                Text = project.ShortPath,
                FontSize = 12,
                Margin = new Thickness(0, 0, 10, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextFillColorPrimaryBrush"),
            };
            Grid.SetColumn(name, 0);
            grid.Children.Add(name);

            double ratio = Math.Clamp((double)eager / max, 0, 1);
            var bar = new Grid { Height = 10, Margin = new Thickness(0, 0, 10, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - ratio, GridUnitType.Star) });
            var filled = new Border { Background = fill, CornerRadius = new CornerRadius(5) };
            Grid.SetColumn(filled, 0);
            bar.Children.Add(filled);
            var barTrack = new Border
            {
                Background = track,
                CornerRadius = new CornerRadius(5),
                Height = 10,
                Child = bar,
            };
            Grid.SetColumn(barTrack, 1);
            grid.Children.Add(barTrack);

            var value = new TextBlock
            {
                Text = TokenEstimate.Format(eager),
                FontSize = 12,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
            };
            Grid.SetColumn(value, 2);
            grid.Children.Add(value);

            // Clicking a row opens that project — the overview is a way in, not a dead end.
            var hit = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 3, 5, 3),
                Margin = new Thickness(-5, 0, -5, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = grid,
                Tag = project.Slug,
                ToolTip = project.Path.Length > 0 ? project.Path : project.Slug,
            };
            hit.MouseLeftButtonUp += (s, _) =>
            {
                if ((s as Border)?.Tag is string slug) SelectProject(slug);
            };
            AllHeaviestList.Children.Add(hit);
        }
    }

    // The two findings that are inherently cross-project. Detection is the rule engine's
    // (ContextRules owns what counts as a duplicate); the sentences are localized here, because the
    // CLI's English and the window's five languages are different surfaces over the same fact.
    private void BuildCrossProjectIssues(ContextScan scan)
    {
        AllIssuesList.Children.Clear();

        var style = RowStyle.For(this, IsDarkTheme());
        int shown = 0;

        foreach (ContextRules.DuplicateCluster c in ContextRules.DuplicateMemoryDirs(scan))
        {
            AddIssue(style.ProblemDot, L.T("context.severity.high"),
                L.T("context.all.dup", c.Projects.Count, c.Files, ContextText.Size(c.Bytes),
                    string.Join(", ", c.Projects.Select(p => p.ShortPath))),
                L.T("context.all.dupFix"));
            shown++;
        }

        foreach (ContextProject p in scan.Projects.Where(p => p.State == PathState.Missing))
        {
            long bytes = p.Bytes;
            AddIssue(bytes > 0 ? style.StaleDot : style.MutedText,
                L.T(bytes > 0 ? "context.severity.medium" : "context.severity.low"),
                bytes > 0
                    ? L.T("context.all.dead", p.ShortPath, ContextText.Size(bytes))
                    : L.T("context.all.deadEmpty", p.ShortPath),
                L.T("context.all.deadFix"));
            shown++;
        }

        if (shown == 0)
            AllIssuesList.Children.Add(new TextBlock
            {
                Text = L.T("context.all.none"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
            });
    }

    // Severity dot + label, the plain sentence, and the fix under it — the same shape the CLI prints,
    // because a finding without a fix is nagging.
    private void AddIssue(Brush dot, string severity, string message, string fix)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var mark = new System.Windows.Shapes.Ellipse
        {
            Width = 7, Height = 7, Fill = dot,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 5, 0, 0),
            ToolTip = severity,
        };
        Grid.SetColumn(mark, 0);
        grid.Children.Add(mark);

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextFillColorPrimaryBrush"),
        });
        text.Children.Add(new TextBlock
        {
            Text = fix,
            FontSize = 11.5,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        AllIssuesList.Children.Add(grid);
    }

    /// <summary>Move the selection to a project by slug (the overview's rows link into it).</summary>
    private void SelectProject(string slug)
    {
        if (ProjectList.ItemsSource is not IEnumerable<ProjectRow> rows) return;
        if (rows.FirstOrDefault(r => !r.IsAll && r.Slug == slug) is not { } target) return;
        ProjectList.SelectedItem = target;
        ProjectList.ScrollIntoView(target);
        DetailScroll.ScrollToTop();
    }

    // ---------------------------------------------------------------- row actions

    // Reveal and open are the only actions here, and deliberately so: this window measures
    // ~/.claude, it never edits it (see IMPROVEMENTS §I.4). The edit is handed to Explorer, to the
    // user's editor, or later to Claude itself.
    private void RevealRow_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        // /select needs the path quoted as a single argument, and Explorer wants backslashes.
        Launch(row, "explorer.exe", $"/select,\"{row.FullPath}\"");
    }

    private void OpenRow_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) Launch(row, row.FullPath, null);
    }

    private void SourceRow_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left && e.ClickCount == 2 &&
            RowOf(sender) is { } row)
            Launch(row, row.FullPath, null);
    }

    /// <summary>The row a context-menu item or a click belongs to. A <c>ContextMenu</c> declared
    /// inside the row template inherits the row's DataContext, so both paths land here.</summary>
    private static SourceRow? RowOf(object sender)
        => (sender as FrameworkElement)?.DataContext as SourceRow;

    private void Launch(SourceRow row, string file, string? arguments)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true };
            if (arguments != null) psi.Arguments = arguments;
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, L.T("context.actionFailed", row.Label, ex.Message),
                L.T("dialog.appName"), System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    // ---------------------------------------------------------------- the session-zero gauge

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
        int window = ContextScanner.ContextWindowFor(observed?.Model);
        int total = _baseTokens + row.Estimated;

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
    private static int Eager(ProjectRow row, Bucket bucket)
        => row.Project!.Sources.Concat(row.Scan.Shared)
            .Where(s => BucketOf(s.Kind) == bucket)
            .Sum(s => s.EagerTokens);

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

/// <summary>
/// One row of the project list. Public (like the two view models below) because WPF resolves a
/// <c>{Binding}</c> path by reflection over a public type — an internal one binds to nothing, and
/// silently.
/// </summary>
public sealed class ProjectRow
{
    /// <param name="project">The project, or <c>null</c> for the "All projects" row that leads the list.</param>
    internal ProjectRow(ContextScan scan, ContextProject? project)
    {
        Scan = scan;
        Project = project;
        Estimated = project is null ? 0 : scan.EstimatedSessionZero(project);
    }

    internal ContextScan Scan { get; }
    internal ContextProject? Project { get; }
    /// <summary>True for the cross-project overview row.</summary>
    internal bool IsAll => Project is null;
    /// <summary>Session zero as the filesystem sees it: shared eager + this project's eager.</summary>
    internal int Estimated { get; }
    internal string Slug => Project?.Slug ?? "";

    public string Title => IsAll ? L.T("context.all.title") : Project!.ShortPath;

    /// <summary>
    /// What a session in this project costs. Blank for the overview row on purpose: this column means
    /// "per session", and there is no such thing for all projects at once — the shared part would be
    /// counted 33 times. The overview's own pane carries the three honest machine-wide numbers.
    /// </summary>
    public string Eager => IsAll ? "" : TokenEstimate.Format(Estimated);

    /// <summary>"18 sources · 6 memories", with the path state appended when it isn't a live directory.</summary>
    public string Detail
    {
        get
        {
            // Size rather than a file count: the footer already shows how many files were *walked*
            // (transcripts included), and two different file counts side by side read as a bug.
            if (IsAll)
                return L.T("context.all.rowDetail", Scan.Projects.Count,
                    ContextText.Size(Scan.Shared.Sum(s => s.Bytes) + Scan.Projects.Sum(p => p.Bytes)));

            int memories = Project!.Sources.Count(s =>
                s.Kind is ContextKind.MemoryFile or ContextKind.MemoryIndex);
            string detail = L.T("context.project.detail", Project.Sources.Count, memories);
            return Project.State switch
            {
                PathState.Missing => detail + " · " + L.T("context.state.missing"),
                PathState.NotAPath => detail + " · " + L.T("context.state.notAPath"),
                _ => detail,
            };
        }
    }

    /// <summary>The project's real directory, or why there isn't one.</summary>
    internal string PathLine
    {
        get
        {
            if (Project!.Path.Length == 0) return L.T("context.state.notAPath");
            return Project.State == PathState.Missing
                ? Project.Path + "  ·  " + L.T("context.state.missing")
                : Project.Path;
        }
    }

    /// <summary>Whether this row is the one named by a slug, directory name or full path. Never the
    /// overview row — a name on the command line always means a project.</summary>
    internal bool Matches(string? name) =>
        !IsAll && name is { Length: > 0 } &&
        (Project!.Slug.Equals(name, StringComparison.OrdinalIgnoreCase) ||
         Project.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
         Project.ShortPath.Equals(name, StringComparison.OrdinalIgnoreCase) ||
         Project.Path.Equals(name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>How the rows inside each group are ordered. Matches the order of the sort picker.</summary>
internal enum SourceSort
{
    /// <summary>What it costs every request, biggest first — the default, and the actionable one.</summary>
    Eager,
    /// <summary>Biggest file first, eager or not.</summary>
    Size,
    /// <summary>Oldest first — the review queue.</summary>
    Age,
}

/// <summary>
/// The theme-resolved brushes the rows are drawn with, looked up once per render instead of per row.
/// Tints are translucent so one pair of values works on both the light and the dark card surface.
/// </summary>
internal sealed record RowStyle(
    Brush EagerChip, Brush IndexChip, Brush LazyChip, Brush ChipText, Brush LazyChipText,
    Brush MutedText, Brush ProblemDot, Brush StaleDot)
{
    public static RowStyle For(FrameworkElement host, bool dark)
    {
        Brush Tint(byte r, byte g, byte b) => Frozen(new SolidColorBrush(
            Color.FromArgb(dark ? (byte)0x40 : (byte)0x30, r, g, b)));

        return new RowStyle(
            // Blue for what is paid every request, green for the index-only cost of a skill/agent —
            // the same two hues the gauge uses for those sources, so the chip and the bar agree.
            EagerChip: Tint(0x39, 0x87, 0xE5),
            IndexChip: Tint(0x19, 0x9E, 0x70),
            LazyChip: (Brush)host.FindResource("SubtleFillColorSecondaryBrush"),
            ChipText: (Brush)host.FindResource("TextFillColorPrimaryBrush"),
            LazyChipText: (Brush)host.FindResource("TextFillColorSecondaryBrush"),
            MutedText: (Brush)host.FindResource("TextFillColorTertiaryBrush"),
            ProblemDot: Frozen(new SolidColorBrush(dark
                ? Color.FromRgb(0xE8, 0x6A, 0x5E)
                : Color.FromRgb(0xC4, 0x3E, 0x3E))),
            StaleDot: Frozen(new SolidColorBrush(dark
                ? Color.FromRgb(0xE0, 0xA0, 0x30)
                : Color.FromRgb(0xC7, 0x77, 0x00))));
    }

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }
}

/// <summary>One kind of context source — Instructions, Memory, Skills, Agents — and its files.</summary>
public sealed class SourceGroup
{
    private SourceGroup(ContextKind kind, List<ContextSource> sources, RowStyle style, SourceSort sort,
        UsageEvidence? evidence, DateTime nowUtc)
    {
        // The count rides in the header rather than in a "{0} files" string: it needs no plural rule
        // in any of the five languages, and it leaves the Load column to the rows, where the
        // eager/lazy word actually means something.
        Header = $"{ContextText.Kind(kind)}  ({sources.Count})";
        Size = ContextText.Size(sources.Sum(s => s.Bytes));
        Tokens = TokenEstimate.Format(sources.Sum(s => s.Tokens));
        int eager = sources.Sum(s => s.EagerTokens);
        // A whole group that costs nothing every session (memory bodies, settings files) reads as an
        // em dash like its rows do — "≈0" invites the question of whether it is a rounding artifact.
        Eager = eager > 0 ? TokenEstimate.Format(eager) : "—";
        Items = Sorted(sources, sort).Select(s => new SourceRow(s, style, evidence, nowUtc)).ToList();
    }

    private static IEnumerable<ContextSource> Sorted(List<ContextSource> sources, SourceSort sort)
        => sort switch
        {
            SourceSort.Size => sources.OrderByDescending(s => s.Bytes).ThenBy(s => s.Label),
            SourceSort.Age => sources.OrderBy(s => s.ModifiedUtc).ThenBy(s => s.Label),
            // Eager first, then size — so a group of all-lazy bodies still reads biggest-first
            // rather than in whatever order the directory happened to enumerate.
            _ => sources.OrderByDescending(s => s.EagerTokens).ThenByDescending(s => s.Bytes),
        };

    public string Header { get; }
    public string Size { get; }
    public string Tokens { get; }
    public string Eager { get; }
    public List<SourceRow> Items { get; }

    /// <summary>Group by kind, in the enum's own order — which puts the eager instruction chain
    /// first and the never-loaded settings files last.</summary>
    internal static List<SourceGroup> Build(List<ContextSource> sources, RowStyle style, SourceSort sort,
        UsageEvidence? evidence, DateTime nowUtc)
        => sources
            .GroupBy(s => s.Kind)
            .OrderBy(g => (int)g.Key)
            .Select(g => new SourceGroup(g.Key, g.ToList(), style, sort, evidence, nowUtc))
            .ToList();
}

/// <summary>One measured file in the detail table.</summary>
public sealed class SourceRow
{
    /// <summary>A file nobody has touched in this long is worth a look — the info-level end of the
    /// review queue, and the one health signal available before the rule engine lands.</summary>
    private const int StaleDays = 90;

    internal SourceRow(ContextSource s, RowStyle style, UsageEvidence? evidence, DateTime nowUtc)
    {
        Label = s.Label;
        FullPath = s.Path;
        Mode = ContextText.Mode(s);
        Size = ContextText.Size(s.Bytes);
        Tokens = TokenEstimate.Format(s.Tokens);
        Eager = s.EagerTokens > 0 ? TokenEstimate.Format(s.EagerTokens) : "—";
        EagerWeight = s.EagerTokens > 0 ? "SemiBold" : "Normal";
        Modified = s.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd");

        bool indexed = s.Mode == LoadMode.Lazy && s.EagerTokens > 0;
        (ChipBackground, ChipForeground) = s.Mode switch
        {
            LoadMode.Eager => (style.EagerChip, style.ChipText),
            LoadMode.Lazy when indexed => (style.IndexChip, style.ChipText),
            LoadMode.Lazy => (style.LazyChip, style.LazyChipText),
            _ => (Brushes.Transparent, style.MutedText),
        };
        ChipTip = L.T(s.Mode switch
        {
            LoadMode.Eager => "context.mode.eagerTip",
            LoadMode.Lazy when indexed => "context.mode.indexTip",
            LoadMode.Lazy => "context.mode.lazyTip",
            _ => "context.mode.notLoadedTip",
        });

        // The health dot, from the two signals the scan already carries. The rule engine will have a
        // great deal more to say (severity per finding); this is the honest subset available now, and
        // an absent dot means "nothing to report", never "checked and healthy".
        int ageDays = (int)(DateTime.UtcNow - s.ModifiedUtc).TotalDays;
        Brush? dot = null;
        if (s.Note is { Length: > 0 } note) { dot = style.ProblemDot; DotTip = note; }
        else if (ageDays >= StaleDays) { dot = style.StaleDot; DotTip = L.T("context.health.stale", ageDays); }

        DotBrush = dot ?? Brushes.Transparent;
        DotVisibility = dot is null ? Visibility.Hidden : Visibility.Visible;

        // Evidence of use. Only skills and agents can carry it: a memory recall leaves no structured
        // trace, so a memory row stays blank rather than implying it is unused (see ContextUsage).
        bool eligible = s.Kind is ContextKind.Skill or ContextKind.Agent;
        UsageStat? stat = eligible ? evidence?.For(s) : null;
        bool canReportZero = eligible && evidence?.CanReportZero(s) == true;

        Used = eligible ? ContextUsage.Format(stat, canReportZero) : "";
        UsedWeight = stat is { Total: > 0 } ? "SemiBold" : "Normal";
        UsedBrush = stat is { Total: > 0 } ? style.LazyChipText
            : canReportZero ? style.StaleDot
            : style.MutedText;
        UsedTip = !eligible ? null
            : stat is { Total: > 0 }
                ? L.T("context.usage.tip", stat.Total, evidence!.WindowDays, stat.Recent,
                    evidence.RecentDays, stat.LastUsedUtc.ToLocalTime().ToString("yyyy-MM-dd"))
                : canReportZero
                    ? L.T("context.usage.neverTip", evidence!.WindowDays)
                    : L.T("context.usage.unknownTip");
    }

    public string Label { get; }
    public string FullPath { get; }
    public string Mode { get; }
    public string Size { get; }
    public string Tokens { get; }
    public string Eager { get; }
    /// <summary>Bold the eager column only where there is a cost — the row's whole point.</summary>
    public string EagerWeight { get; }
    public string Modified { get; }

    public Brush ChipBackground { get; }
    public Brush ChipForeground { get; }
    public string ChipTip { get; }

    /// <summary>"12×", the localized "never", or "" / "—" when there is nothing honest to say.</summary>
    public string Used { get; }
    public string UsedWeight { get; }
    public Brush UsedBrush { get; }
    public string? UsedTip { get; }

    public Brush DotBrush { get; }
    /// <summary><see cref="Visibility.Hidden"/>, not Collapsed — the gutter keeps its width so every
    /// label in the table starts at the same x.</summary>
    public Visibility DotVisibility { get; }
    public string? DotTip { get; }
}

/// <summary>The display words shared by the window's view models.</summary>
internal static class ContextText
{
    public static string Kind(ContextKind kind) => L.T(kind switch
    {
        ContextKind.UserInstructions => "context.kind.userInstructions",
        ContextKind.ProjectInstructions => "context.kind.projectInstructions",
        ContextKind.NestedInstructions => "context.kind.nestedInstructions",
        ContextKind.Import => "context.kind.import",
        ContextKind.MemoryIndex => "context.kind.memoryIndex",
        ContextKind.MemoryFile => "context.kind.memoryFile",
        ContextKind.Skill => "context.kind.skill",
        ContextKind.Agent => "context.kind.agent",
        _ => "context.kind.settings",
    });

    /// <summary>
    /// Eager / lazy / index / not loaded. "index" is the honest word for a skill or agent: the body
    /// is only read when it is invoked, but its name and description sit in the always-loaded index,
    /// so having it available is never quite free.
    /// </summary>
    public static string Mode(ContextSource s) => L.T(s.Mode switch
    {
        LoadMode.Eager => "context.mode.eager",
        LoadMode.Lazy => s.EagerTokens > 0 ? "context.mode.index" : "context.mode.lazy",
        _ => "context.mode.notLoaded",
    });

    /// <summary>Bytes / KB / MB — MB because a whole machine's footprint is megabytes, and "1434 KB"
    /// is a number nobody reads.</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => L.T("context.size.bytes", bytes),
        < 1024 * 1024 => L.T("context.size.kb", (bytes / 1024.0).ToString("0.#", L.Culture)),
        _ => L.T("context.size.mb", (bytes / (1024.0 * 1024)).ToString("0.#", L.Culture)),
    };
}
