using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// WinForms and WPF each contribute a Brush / Color / Orientation / HorizontalAlignment; pin these
// names to the WPF ones the gauge is drawn with (same convention as StatisticsWindow).
using Brush = System.Windows.Media.Brush;
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

    /// <param name="selectSlug">Project to open on (slug, directory name or path); null = the heaviest.</param>
    public ContextWindow(string? selectSlug = null)
    {
        InitializeComponent();
        _initialSelection = selectSlug;

        ProjectList.SelectionChanged += (_, _) => ShowSelected();
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

        var rows = scan.Projects.Select(p => new ProjectRow(scan, p)).ToList();
        ProjectList.ItemsSource = rows;
        ProjectList.SelectedItem = rows.FirstOrDefault(r => r.Matches(keep)) ?? rows[0];
        // The list is sorted by weight, so a project named on the command line — or simply kept
        // across a rescan — is usually below the fold; without this the selection is invisible.
        ProjectList.ScrollIntoView(ProjectList.SelectedItem);
        _selected = true;
    }

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

        ProjectTitle.Text = row.Project.ShortPath;
        ProjectPath.Text = row.PathLine;

        EstimatedValue.Text = TokenEstimate.Format(row.Estimated);
        EstimatedParts.Text = L.T("context.sessionZero.parts",
            TokenEstimate.Format(row.Scan.SharedEagerTokens),
            TokenEstimate.Format(row.Project.EagerTokens));

        if (row.Project.Observed is { } o)
        {
            ObservedValue.Text = TokenEstimate.Format(o.Median);
            ObservedParts.Text = L.T("context.observed.parts",
                o.Samples, TokenEstimate.Format(o.Min), TokenEstimate.Format(o.Max),
                o.Cost.ToString("0.000", L.Culture));
        }
        else
        {
            ObservedValue.Text = "—";
            ObservedParts.Text = L.T("context.observed.none", row.Project.TranscriptsRead);
        }

        BuildGauge(row);

        ProjectGroups.ItemsSource = SourceGroup.Build(row.Project.Sources);
        SharedGroups.ItemsSource = SourceGroup.Build(row.Scan.Shared);
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
        SessionZero? observed = row.Project.Observed;
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
        => row.Project.Sources.Concat(row.Scan.Shared)
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
    internal ProjectRow(ContextScan scan, ContextProject project)
    {
        Scan = scan;
        Project = project;
        Estimated = scan.EstimatedSessionZero(project);
    }

    internal ContextScan Scan { get; }
    internal ContextProject Project { get; }
    /// <summary>Session zero as the filesystem sees it: shared eager + this project's eager.</summary>
    internal int Estimated { get; }
    internal string Slug => Project.Slug;

    public string Title => Project.ShortPath;
    public string Eager => TokenEstimate.Format(Estimated);

    /// <summary>"18 sources · 6 memories", with the path state appended when it isn't a live directory.</summary>
    public string Detail
    {
        get
        {
            int memories = Project.Sources.Count(s =>
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
            if (Project.Path.Length == 0) return L.T("context.state.notAPath");
            return Project.State == PathState.Missing
                ? Project.Path + "  ·  " + L.T("context.state.missing")
                : Project.Path;
        }
    }

    /// <summary>Whether this row is the one named by a slug, directory name or full path.</summary>
    internal bool Matches(string? name) =>
        name is { Length: > 0 } &&
        (Project.Slug.Equals(name, StringComparison.OrdinalIgnoreCase) ||
         Project.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
         Project.ShortPath.Equals(name, StringComparison.OrdinalIgnoreCase) ||
         Project.Path.Equals(name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One kind of context source — Instructions, Memory, Skills, Agents — and its files.</summary>
public sealed class SourceGroup
{
    private SourceGroup(ContextKind kind, List<ContextSource> sources)
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
        Items = sources
            .OrderByDescending(s => s.EagerTokens)
            .ThenByDescending(s => s.Bytes)
            .Select(s => new SourceRow(s))
            .ToList();
    }

    public string Header { get; }
    public string Size { get; }
    public string Tokens { get; }
    public string Eager { get; }
    public List<SourceRow> Items { get; }

    /// <summary>Group by kind, in the enum's own order — which puts the eager instruction chain
    /// first and the never-loaded settings files last.</summary>
    internal static List<SourceGroup> Build(List<ContextSource> sources) => sources
        .GroupBy(s => s.Kind)
        .OrderBy(g => (int)g.Key)
        .Select(g => new SourceGroup(g.Key, g.ToList()))
        .ToList();
}

/// <summary>One measured file in the detail table.</summary>
public sealed class SourceRow
{
    internal SourceRow(ContextSource s)
    {
        Label = s.Note is { Length: > 0 } note ? $"{s.Label}  ! {note}" : s.Label;
        FullPath = s.Path;
        Mode = ContextText.Mode(s);
        Size = ContextText.Size(s.Bytes);
        Tokens = TokenEstimate.Format(s.Tokens);
        Eager = s.EagerTokens > 0 ? TokenEstimate.Format(s.EagerTokens) : "—";
        EagerWeight = s.EagerTokens > 0 ? "SemiBold" : "Normal";
        Modified = s.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd");
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

    public static string Size(long bytes) => bytes < 1024
        ? L.T("context.size.bytes", bytes)
        : L.T("context.size.kb", (bytes / 1024.0).ToString("0.#", L.Culture));
}
