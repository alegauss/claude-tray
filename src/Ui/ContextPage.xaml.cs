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

/// <summary>
/// The Context Load page (a destination of <see cref="MainWindow"/>): what opening a session in a
/// project costs before the first prompt.
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
internal partial class ContextPage : System.Windows.Controls.UserControl
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
    /// The what-if selection: full paths of sources the user has ticked as "suppose this were gone".
    /// It is the single source of truth for the checkboxes, so it survives switching projects and
    /// rescanning, and it is <em>only</em> ever read — nothing in this window writes to ~/.claude
    /// (IMPROVEMENTS §I.4).
    /// </summary>
    private readonly HashSet<string> _simulated = new(StringComparer.OrdinalIgnoreCase);

    // Live refresh: a watcher over ~/.claude, debounced, so the numbers keep up while Claude Code is
    // writing memories in another window. Both are disposed with the window.
    private FileSystemWatcher? _watcher;
    private System.Windows.Threading.DispatcherTimer? _debounce;
    /// <summary>Set when the pending scan was triggered by a file change rather than by the user, so
    /// the footer can say why the numbers just moved.</summary>
    private bool _liveRefresh;

    /// <summary>Findings for the whole scan, recomputed when the scan or the evidence changes. A pass
    /// over data already in memory, so it is cheap enough to redo on every render.</summary>
    private List<Finding> _findings = new();

    /// <summary>
    /// Preview-only: once the panes are populated, scroll down to the source table so it can be
    /// screenshotted. The detail pane is taller than any screen at the default window size (the gauge
    /// is the hero and owns the top), and a screen-copy capture cannot scroll — the same reason the
    /// Statistics window has its own preview entry points.
    /// </summary>
    internal bool PreviewScrollToTable { get; set; }

    /// <summary>
    /// Preview-only: pre-tick the three heaviest removable sources so the what-if banner and the
    /// shrunken gauge can be screenshotted. Ticking is a mouse gesture, and a screen-copy capture
    /// cannot click — same reason <see cref="PreviewScrollToTable"/> exists.
    /// </summary>
    internal bool PreviewSimulateTop { get; set; }

    /// <summary>
    /// Preview-only: draw the drift row from a synthetic series. Real history needs weeks to
    /// accumulate, and a feature nobody can look at cannot be verified.
    /// </summary>
    internal bool PreviewDemoHistory { get; set; }

    /// <summary>
    /// Scan somewhere other than <c>~/.claude</c> — a fixture tree (<c>--sample</c>) or any stand-in
    /// (<c>--root</c>). Null means the real one. The live watcher follows it, so a fixture behaves
    /// exactly like the real thing.
    /// </summary>
    internal string? ScanRoot { get; set; }

    /// <param name="selectSlug">Project to open on (slug, directory name or path); null = the heaviest.</param>
    public ContextPage(string? selectSlug = null)
    {
        InitializeComponent();
        _initialSelection = selectSlug;

        ProjectList.SelectionChanged += (_, _) => ShowSelected();
        // Re-sorting is just a rebuild of the two tables from the same scan — no IO, so it can be
        // wired straight to the selection.
        SortBox.SelectionChanged += (_, _) => { if (IsLoaded) ShowSelected(); };
        RescanButton.Click += (_, _) => StartScan();
        // Close means the shell: a destination has nothing of its own to close.
        ContextCloseButton.Click += (_, _) => Window.GetWindow(this)?.Close();
        SimClearButton.Click += (_, _) => { _simulated.Clear(); ShowSelected(); };
        CopyPromptButton.Click += (_, _) => CopyCleanupPrompt(projectScope: true);
        AllCopyPromptButton.Click += (_, _) => CopyCleanupPrompt(projectScope: false);
        // The scan and the watcher start when the page is first put on screen, which is the first
        // navigation to this destination rather than the moment the shell opens (the shell builds a
        // destination lazily, so opening the tray icon on Statistics costs no ~/.claude walk at all).
        Loaded += (_, _) =>
        {
            StartScan();
            StartWatching();
        };
        Unloaded += (_, _) => StopWatching();
        // Navigating to another destination collapses this page but keeps it alive, and a watcher over
        // ~/.claude that re-scans on every memory write is not something to leave running behind a page
        // nobody is looking at. Coming back re-scans, so the numbers are current rather than as old as
        // the last visit — the same claim the watcher makes while the page *is* on screen.
        IsVisibleChanged += (_, _) =>
        {
            if (!IsLoaded) return;   // the first show is the Loaded handler's, not this one's
            if (IsVisible) { StartScan(); StartWatching(); }
            else StopWatching();
        };
    }

    /// <summary>
    /// Watch <c>~/.claude</c> and re-scan shortly after it settles. A warm scan is ~100ms, so this can
    /// be a plain re-scan rather than an incremental update — far less to get wrong.
    ///
    /// Two deliberate limits. The filter is <c>*.md</c>, which keeps the transcripts out: they are
    /// appended to continuously by every running session, and watching them would mean re-scanning
    /// forever. And project instruction files (<c>AGENTS.md</c> next to the code) live outside
    /// <c>~/.claude</c>, so editing one still needs the Rescan button — watching dozens of whole repos
    /// would cost more than it is worth, and build output would trigger it constantly.
    /// </summary>
    private void StartWatching()
    {
        if (_watcher is not null) return;   // called again on every return to this destination
        try
        {
            string root = ScanRoot ?? ContextScanner.DefaultClaudeRoot;
            if (!Directory.Exists(root)) return;

            _debounce = new System.Windows.Threading.DispatcherTimer
            {
                // Long enough to coalesce a burst of memory writes into one scan, short enough that
                // the window feels live rather than stale.
                Interval = TimeSpan.FromMilliseconds(900),
            };
            _debounce.Tick += (_, _) =>
            {
                _debounce!.Stop();
                _liveRefresh = true;
                StartScan();
            };

            _watcher = new FileSystemWatcher(root, "*.md")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,   // a memory-writing burst shouldn't overflow the queue
            };

            void Bump() => Dispatcher.BeginInvoke(() => { _debounce?.Stop(); _debounce?.Start(); });
            _watcher.Changed += (_, _) => Bump();
            _watcher.Created += (_, _) => Bump();
            _watcher.Deleted += (_, _) => Bump();
            _watcher.Renamed += (_, _) => Bump();
            // A watcher that has errored (buffer overflow, the directory going away) is finished; drop
            // it and leave the user with the Rescan button rather than a silently dead feature.
            _watcher.Error += (_, _) => Dispatcher.BeginInvoke(StopWatching);
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            // No watcher is a supported state: the window simply stops updating by itself.
            StopWatching();
        }
    }

    private void StopWatching()
    {
        try
        {
            _debounce?.Stop();
            _debounce = null;
            if (_watcher is { } w)
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
        }
        catch { /* disposal is best-effort */ }
        finally { _watcher = null; }
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
            scan = await Task.Run(() => ContextScanner.Scan(DateTimeOffset.UtcNow.UtcDateTime,
                new ContextScanner.Options
                {
                    ClaudeRoot = ScanRoot ?? ContextScanner.DefaultClaudeRoot,
                    // A fixture scan must never write to (or read from) the real cache.
                    UseCache = ScanRoot is null,
                }));
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

        _findings = ContextRules.Evaluate(scan, DateTimeOffset.UtcNow.UtcDateTime, _evidence);
        // One line per project per day, and only when the number moved - see ContextHistory. Skipped
        // for a fixture scan: sample projects have no business in the real drift history.
        if (ScanRoot is null) ContextHistory.Record(ProfileStore.Monitored, scan, DateTimeOffset.UtcNow.UtcDateTime);

        ContextStatusText.Visibility = Visibility.Collapsed;
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
        var listStyle = RowStyle.For(this, IsDarkTheme());
        var rows = new List<ProjectRow> { new(scan, null) };
        rows.AddRange(scan.Projects.Select(p =>
            new ProjectRow(scan, p, ContextRules.Debt(scan, p, _findings), listStyle)));
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
            _evidence = await Task.Run(() => ContextUsage.Compute(DateTimeOffset.UtcNow.UtcDateTime,
                new ContextUsage.Options
                {
                    ClaudeRoot = ScanRoot ?? ContextScanner.DefaultClaudeRoot,
                    UseCache = ScanRoot is null,
                }));
            // The evidence unlocks one more rule (never-invoked), so the findings are recomputed with
            // it rather than left as they were at first render.
            if ((ProjectList.SelectedItem as ProjectRow)?.Scan is { } scan)
                _findings = ContextRules.Evaluate(scan, DateTimeOffset.UtcNow.UtcDateTime, _evidence);
            ShowSelected();
        }
        catch { /* no evidence is a supported state — every consumer renders "—" for unknown */ }
        finally { _usageRunning = false; }
    }

    private bool _usageRunning;

    private void ShowStatus(string text)
    {
        ContextStatusText.Text = text;
        ContextStatusText.Visibility = Visibility.Visible;
        MasterPane.Visibility = Visibility.Collapsed;
        DetailPane.Visibility = Visibility.Collapsed;
    }

    // "33 projects · 812 files · 76 ms (cached)", plus the capped-walk warning when the file cap cut
    // the scan short — a silent cap reads as "all clear", which it isn't.
    private string ScanInfoText(ContextScan scan)
    {
        string info = FooterText(scan.Projects.Count, scan.FilesWalked, scan.ElapsedMs, scan.FromCache);

        // Numbers that change on their own are unsettling unless the window says why.
        if (_liveRefresh)
        {
            _liveRefresh = false;
            info += "  " + L.T("context.footer.live");
        }
        return scan.Truncated ? info + "  " + L.T("context.truncated") : info;
    }

    // The page's two number-bearing sentences, out of the render so a check can call them (T216).
    private static string FooterText(int projects, int files, double elapsedMs, bool cached) =>
        L.T("context.footer", projects, files, Nums.Of(elapsedMs, "0"),
            L.T(cached ? "context.footer.cached" : "context.footer.fresh"));

    private static string ObservedPartsText(int samples, int min, int max, double cost) =>
        L.T("context.observed.parts", samples, TokenEstimate.Format(min), TokenEstimate.Format(max),
            Nums.Of(cost, "0.000"));

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
            ObservedParts.Text = ObservedPartsText(o.Samples, o.Min, o.Max, o.Cost);
        }
        else
        {
            ObservedValue.Text = "—";
            ObservedParts.Text = L.T("context.observed.none", row.Project!.TranscriptsRead);
        }

        // Preview hook: seed the selection once, before the gauge is drawn, so the first render
        // already shows the simulated state.
        if (PreviewSimulateTop && _simulated.Count == 0)
            foreach (ContextSource s in Relevant(row)
                         .Where(s => s.EagerTokens > 0)
                         .OrderByDescending(s => s.EagerTokens)
                         .Take(3))
                _simulated.Add(s.Path);

        BuildGauge(row);

        var style = RowStyle.For(this, IsDarkTheme());
        SourceSort sort = (SourceSort)Math.Clamp(SortBox.SelectedIndex, 0, 2);
        DateTime now = DateTimeOffset.UtcNow.UtcDateTime;
        ProjectGroups.ItemsSource = SourceGroup.Build(row.Project!.Sources, style, sort, _evidence, now, _simulated);
        SharedGroups.ItemsSource = SourceGroup.Build(row.Scan.Shared, style, sort, _evidence, now, _simulated);

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

        // The machine-wide findings live here, with the cross-project ones — a per-project view has
        // no business advising about ~/.claude/settings.json.
        int machineWide = MachineFindings().Count;
        AllFixCount.Text = machineWide > 0
            ? L.T("context.fix.count", machineWide)
            : L.T("context.fix.none");
        AllFixHint.Text = L.T("context.fix.hint");
    }

    /// <summary>Findings that belong to the machine rather than to one project: the shared instruction
    /// file, the user/plugin skill index, user settings, and the cross-project clusters.</summary>
    private List<Finding> MachineFindings()
        => _findings
            .Where(f => f.Scope == "~/.claude" || f.RuleId is "memory-duplicated" or "project-dir-dead"
                        or "project-dir-dead-empty")
            .ToList();

    private List<Finding> ProjectFindings(ProjectRow row)
        => _findings.Where(f => f.Scope == row.Project!.ShortPath).ToList();

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

    /// <summary>
    /// Put a cleanup prompt on the clipboard: the findings for this view, each with its fix, plus
    /// whatever the user ticked in the what-if simulator. Copying is the whole action — the app does
    /// not edit ~/.claude, it hands the job to Claude, which can read the files and decide.
    /// </summary>
    private void CopyCleanupPrompt(bool projectScope)
    {
        if (ProjectList.SelectedItem is not ProjectRow row) return;

        List<Finding> findings;
        string title;
        List<ContextSource> candidates = new();
        int candidateTokens = 0;

        if (projectScope && !row.IsAll)
        {
            findings = ProjectFindings(row);
            title = row.Project!.Path.Length > 0 ? row.Project.Path : row.Project.ShortPath;
            candidates = Relevant(row).Where(s => _simulated.Contains(s.Path)).ToList();
            candidateTokens = candidates.Sum(s => s.EagerTokens);
        }
        else
        {
            findings = MachineFindings();
            title = ContextScanner.DefaultClaudeRoot;
        }

        string prompt = ContextPrompt.Build(title, findings, candidates, candidateTokens);
        try
        {
            System.Windows.Clipboard.SetText(prompt);
            ShowCopied(projectScope, L.T("context.fix.copied"));
        }
        catch (Exception ex)
        {
            // The clipboard can genuinely be locked by another process; say so rather than silently
            // doing nothing.
            Warn(L.T("context.fix.copyFailed", ex.Message));
        }
    }

    // Confirm in place, then put the hint back — a message box for "it worked" would be noise.
    private async void ShowCopied(bool projectScope, string message)
    {
        TextBlock target = projectScope ? FixHint : AllFixHint;
        target.Text = message;
        await Task.Delay(4000);
        if (target.Text == message) target.Text = L.T("context.fix.hint");
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
        // A double-click on the simulate checkbox is a double toggle, not a request to open the file.
        if (e.OriginalSource is CheckBox) return;
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
            Warn(L.T("context.actionFailed", row.Label, ex.Message));
        }
    }

    /// <summary>A warning owned by whichever window this page is in — a page cannot own a dialog, and an
    /// ownerless message box can end up behind the window that raised it.</summary>
    private void Warn(string message)
    {
        if (Window.GetWindow(this) is { } owner)
            System.Windows.MessageBox.Show(owner, message, L.T("dialog.appName"),
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        else
            System.Windows.MessageBox.Show(message, L.T("dialog.appName"),
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
    }

    // ---------------------------------------------------------------- the session-zero gauge

}
