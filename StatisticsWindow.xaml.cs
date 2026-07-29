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
    private static readonly CultureInfo DateFmt = L.Culture;

    // Projection line color — a warm amber that reads on both light and dark backgrounds.
    private static readonly Brush ProjectionBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x30)));

    // "Unavailable" (API-outage) markers: a vivid red for the dashed span, and a faint red wash for the
    // band behind it — the stretch of the window where no live reading could be taken.
    private static readonly Brush OutageBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE3, 0x3B, 0x30)));
    private static readonly Brush OutageBandBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x22, 0xE3, 0x3B, 0x30)));

    // Projected-idle bands: the projection's own amber at band strength. Deliberately *not* red —
    // red already means "no reading was logged", and "we measured, and it was zero" is the opposite
    // claim. Past idle gets no band at all: the curve is already visibly flat there.
    private static readonly Brush IdleBandBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x1E, 0xE0, 0xA0, 0x30)));

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

    // The default tab (5-hour session vs. weekly) is auto-picked once, on the first successful render:
    // when Claude isn't currently being used the 5-hour chart is flat/empty, so the weekly view — with
    // its accumulated usage — opens as the more useful default. Only the initial open is steered; later
    // auto-refreshes must not yank the user off whichever tab they're reading.
    private bool _defaultTabPicked;

    // Set while the live API is unavailable (e.g. a 403): the charts are still drawn from the last known
    // local data, and this drives the error banner above them. The chart's red "unavailable" spans come
    // from gaps in the logged readings themselves (WindowPace.Gaps), so they persist after recovery too.
    private string? _error;

    // The live throughput row (T99). Owned by the window, so nothing tails the transcripts while the
    // window is closed; created on Loaded and torn down on Closed.
    private TranscriptTail? _tail;
    private LiveRate? _live;
    private LiveStrip? _stripS, _stripW;
    private System.Windows.Threading.DispatcherTimer? _liveTimer;
    private TokenBits[]? _lastStrip;   // kept so a resize or a tab switch can repaint without a tick
    private ProjectSlice[]? _lastProjects;
    private bool _lastByProject;

    /// <summary>Dev/preview seam: feed the strip a deterministic synthetic minute instead of the real
    /// tail, so the screenshot of a *moving* row is reproducible. See <c>--stats live</c>.</summary>
    internal bool PreviewDemoLive { get; init; }

    public StatisticsWindow(PaceSnapshot? snapshot, bool showRemaining = false, string? error = null)
    {
        _snapshot = snapshot;
        _remaining = showRemaining;
        _error = error;
        InitializeComponent();

        Loaded += (_, _) => StartLive();
        Closed += (_, _) => StopLive(dispose: true);
        // Minimizing leaves IsVisible true in WPF, so both signals are needed to honour
        // "hidden ⇒ stopped".
        IsVisibleChanged += (_, _) => SyncLiveClock();
        StateChanged += (_, _) => SyncLiveClock();
        // Only the visible tab's strip is painted, so switching tabs has to repaint immediately
        // rather than leaving the other one blank until the next second.
        PanesBody.SelectionChanged += (_, e) => { if (e.OriginalSource == PanesBody) LiveTick(); };

        try
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri(Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath));
        }
        catch { /* fall back to the default window icon */ }

        if (_snapshot is not null)
            Reload();
        else
            ShowStatus(error is { Length: > 0 } ? L.T("stats.apiError", error) : L.T("stats.connect"));
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
    internal void UpdateSnapshot(PaceSnapshot? snapshot, string? error = null)
    {
        _snapshot = snapshot;
        _error = error;
        if (_snapshot is not null)
            Reload();
        else
            ShowStatus(error is { Length: > 0 } ? L.T("stats.apiError", error) : L.T("stats.connect"));
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
            Populate(s, ChipS, ChipTextS, UsedS, IdealS, ResetS, ProjectionS, ChartS, TpsHeadS, TpsLegendS);
            Populate(w, ChipW, ChipTextW, UsedW, IdealW, ResetW, ProjectionW, ChartW, TpsHeadW, TpsLegendW);
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

    // Dev/preview seam: render a hand-built report directly (bypassing the transcript scan), so the
    // outage rendering can be inspected deterministically without a matching reading on disk.
    internal void PreviewReport(PaceReport r, string? error)
    {
        _error = error;
        Render(r);
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
        PanesBody.Visibility = Visibility.Collapsed;
        MethodNote.Visibility = Visibility.Collapsed;
        ErrorBanner.Visibility = Visibility.Collapsed;
    }

    // Show or hide the outage banner above the charts, driven by the current _error.
    private void ApplyErrorBanner()
    {
        if (_error is { Length: > 0 } e)
        {
            ErrorBannerText.Text = L.T("stats.errorBanner", e);
            ErrorBanner.Visibility = Visibility.Visible;
        }
        else
        {
            ErrorBanner.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Preview seam: draw a synthetic previous-week ghost when the real one isn't there yet.
    /// A real ghost needs two weeks of folded history (see <see cref="HourlyUsage"/>), so without this
    /// the line could not be screenshotted until the machine had been running that long.</summary>
    internal bool PreviewDemoGhost { get; init; }

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
        ApplyErrorBanner();

        _session = r.Session;
        _weekly = r.Weekly;

        if (PreviewDemoGhost && r.Weekly.Ghost is null && r.Weekly.HasWindow)
            r.Weekly.Ghost = HourlyUsage.Demo(
                DateTimeOffset.FromUnixTimeSeconds((long)(r.Weekly.ResetUnix - r.Weekly.WindowSeconds)).LocalDateTime,
                r.Weekly.WindowSeconds, r.Weekly.ElapsedFraction, 0.86);

        // On the first render, open the weekly tab instead of the 5-hour one when the session is idle
        // (expired / not using Claude): the 5-hour chart is then flat at 100% and the weekly chart, with
        // its accumulated usage, is the more interesting view. Any usage in the session keeps the 5-hour
        // default. Guarded so subsequent auto-refreshes leave the user's current tab alone.
        if (!_defaultTabPicked)
        {
            _defaultTabPicked = true;
            bool sessionIdle = !r.Session.HasWindow || r.Session.Util <= 0;
            if (sessionIdle && r.Weekly.HasWindow)
                PanesBody.SelectedIndex = 1;
        }

        ApplyModeLabels();
        Populate(r.Session, ChipS, ChipTextS, UsedS, IdealS, ResetS, ProjectionS, ChartS, TpsHeadS, TpsLegendS);
        Populate(r.Weekly, ChipW, ChipTextW, UsedW, IdealW, ResetW, ProjectionW, ChartW, TpsHeadW, TpsLegendW);

        // The weekly projection changes meaning when it follows the activity shape, so the method note
        // has to say so — including the part that can't be measured locally: usage from another
        // machine or from claude.ai spends the same quota while leaving no transcript here.
        // The live strip has the same blind spot the shaped projection discloses, and it needs saying
        // separately: a rate built from local transcripts cannot see another machine or claude.ai, so
        // an empty strip is "no local turn landed", never proof that nothing is running.
        bool shaped = r.Weekly.Shape is not null;
        MethodNote.Text = (shaped
            ? L.T("stats.methodNote") + " " + L.T("stats.methodNote.shape", $"{r.Weekly.Shape!.CoverageWeeks:0.#}")
            : L.T("stats.methodNote")) + " " + L.T("stats.methodNote.live");
        LegendIdleW.Visibility = shaped && r.Weekly.Shape!.IdleBands.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
        LegendGhostW.Visibility = r.Weekly.Ghost is not null ? Visibility.Visible : Visibility.Collapsed;
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
        TextBlock tpsHead, StackPanel tpsLegend)
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
        PopulateThroughput(w, tpsHead, tpsLegend);
    }

    // Throughput breakdown: average tokens/second over the window, split into input / output /
    // cache-creation (cache reads are excluded — see WindowPace.TokensPerSecond). The mix used to be
    // a rounded stacked bar here; that bar is now the live strip below (T99), which carries the same
    // split against a time axis instead of against nothing. The legend still direct-labels each type
    // with its own tokens/sec, so identity is never color-alone per the palette's relief rule.
    private void PopulateThroughput(WindowPace w, TextBlock head, StackPanel legend)
    {
        legend.Children.Clear();

        long input = w.InputTokens, output = w.OutputTokens, cache = w.CacheCreationTokens;
        long sum = input + output + cache;

        if (!w.HasWindow || w.ElapsedSeconds <= 0 || sum <= 0)
        {
            head.Text = L.T("stats.tps.none");
            return;
        }

        double elapsed = w.ElapsedSeconds;
        head.Text = L.T("stats.tps.head", Rate(w.TokensPerSecond), Big(sum));

        Color[] palette = LiveStrip.Colors(IsDarkTheme());
        var types = new (string label, long tokens, Color color)[]
        {
            (L.T("stats.tps.input"),  input,  palette[0]),
            (L.T("stats.tps.output"), output, palette[1]),
            (L.T("stats.tps.cacheCreate"), cache, palette[2]),
        };

        // Legend: colored swatch + type + its own rate, with the absolute total on hover. The swatches
        // are dropped while the strip above is stacked by *project*, because the same three hues would
        // then mean token types here and projects there — one coloured legend on screen at a time.
        bool byProject = _lastProjects is { Length: > 1 };
        if (byProject)
            legend.Children.Add(new TextBlock
            {
                Text = L.T("stats.tps.windowAvg"), FontSize = 12,
                Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextFillColorTertiaryBrush"),
            });

        foreach (var t in types)
        {
            var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0) };
            if (!byProject)
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
                Margin = new Thickness(byProject ? 0 : 6, 0, 0, 0), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
                ToolTip = L.T("stats.tps.tip", t.label, Big(t.tokens), Pct(share)),
            });
            legend.Children.Add(row);
        }
    }

    // ------------------------------------------------------------------ live throughput (T99)

    // The strip and the number above it are the only things in this window on a local clock. They are
    // built once the layout exists (the strip needs a real width) and are torn down with the window,
    // so a closed Statistics window tails nothing.
    private void StartLive()
    {
        if (_stripS is not null) return;
        bool dark = IsDarkTheme();
        _stripS = new LiveStrip(LiveStripS, dark);
        _stripW = new LiveStrip(LiveStripW, dark);

        // The strip is drawn in device pixels against its host's width, so a resize (and the very
        // first layout pass, where the width is still zero) has to redraw it. Static, not animated:
        // a resize is not a second passing.
        LiveStripS.SizeChanged += (_, _) => RenderLive(_stripS, LiveLegendS, animate: false);
        LiveStripW.SizeChanged += (_, _) => RenderLive(_stripW, LiveLegendW, animate: false);

        if (PreviewDemoLive) { RenderDemoLive(); return; }

        try
        {
            _tail = new TranscriptTail();
            _live = new LiveRate(_tail);
            _tail.Start();
        }
        catch { _tail = null; _live = null; }   // no tail is a supported state: the row stays quiet

        _liveTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _liveTimer.Tick += (_, _) => LiveTick();
        SyncLiveClock();
        LiveTick();
    }

    // "Hidden ⇒ stopped." A minimized or closed-but-not-disposed window must cost what it cost before
    // this row existed. The tail keeps running (it is a few ms every 3s off-thread and it is what lets
    // the strip have history the moment the window comes back); only the render clock stops.
    private void SyncLiveClock()
    {
        if (_liveTimer is null) return;
        bool onScreen = IsVisible && WindowState != WindowState.Minimized;
        if (onScreen && !_liveTimer.IsEnabled) { _liveTimer.Start(); LiveTick(); }
        else if (!onScreen && _liveTimer.IsEnabled)
        {
            _liveTimer.Stop();
            _stripS?.Stop();
            _stripW?.Stop();
        }
    }

    private void StopLive(bool dispose)
    {
        _liveTimer?.Stop();
        _stripS?.Stop();
        _stripW?.Stop();
        if (!dispose) return;
        _liveTimer = null;
        try { _tail?.Dispose(); } catch { /* best-effort */ }
        _tail = null;
        _live = null;
    }

    private void LiveTick()
    {
        if (PreviewDemoLive) return;   // the synthetic strip owns the row; a tab switch must not clear it
        if (_live is null) { LiveHeadS.Text = LiveHeadW.Text = L.T("stats.live.off"); return; }

        _live.Tick(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        _lastStrip = _live.Strip();
        _lastProjects = _live.Projects();
        int sessions = _live.ActiveSessions;

        // The window-average legend below the strip shows swatches only while the strip is *not*
        // stacked by project. That state is decided here, on the live clock, but the legend is built
        // by Render() on the API clock — so a flip has to rebuild it, or the two legends end up
        // showing the same three hues meaning different things.
        bool byProject = _lastProjects is { Length: > 1 };
        if (byProject != _lastByProject)
        {
            _lastByProject = byProject;
            if (_session is { } sp) PopulateThroughput(sp, TpsHeadS, TpsLegendS);
            if (_weekly is { } wp) PopulateThroughput(wp, TpsHeadW, TpsLegendW);
        }

        string head = _live.Quiet
            ? L.T("stats.live.quiet")
            : L.T("stats.live.head", Rate(_live.TokensPerSecond),
                  Big(_live.Window.Total - _live.Window.CacheRead)) +
              (sessions > 0 ? "  ·  " + L.T(sessions == 1 ? "stats.live.session1" : "stats.live.sessionN", sessions) : "");
        LiveHeadS.Text = LiveHeadW.Text = head;

        // Only the tab on screen is painted; the other one is re-rendered when it is selected.
        bool weekly = PanesBody.SelectedIndex == 1;
        RenderLive(weekly ? _stripW : _stripS, weekly ? LiveLegendW : LiveLegendS, animate: true);
        // The legend on the hidden tab would otherwise go stale behind the user's back, and it is
        // cheap text, unlike the geometry.
        BuildLiveLegend(weekly ? LiveLegendS : LiveLegendW);
    }

    // The strip answers one of two questions, and which one depends on how many projects are running.
    // Across repos, "where is it going" dominates and the stack is per project — that is the whole
    // point of T100. With a single project there is nothing to attribute, and folding the strip to one
    // flat colour would throw away the input/output/cache-create mix for no gain, so the by-type view
    // stays. The legend under the strip always names which view is on screen, so the switch can never
    // be silent.
    private void RenderLive(LiveStrip? strip, StackPanel legend, bool animate)
    {
        if (strip is null || _lastStrip is null) return;
        bool dark = IsDarkTheme();

        if (_lastProjects is { Length: > 1 } projects)
        {
            Color[] palette = ProjectPalette(projects, dark);
            strip.Render(projects.Select(p => p.PerSecond).ToArray(), palette, animate);
        }
        else
        {
            strip.Render(_lastStrip, dark, animate);
        }
        BuildLiveLegend(legend);
    }

    private static Color[] ProjectPalette(ProjectSlice[] projects, bool dark)
    {
        Color[] hues = LiveStrip.ProjectColors(dark);
        // Colour follows the entity's rank in a fixed order and the residual is always the neutral —
        // never a generated hue for a fifth series.
        return projects.Select((p, i) => p.IsOthers ? LiveStrip.Others(dark) : hues[Math.Min(i, hues.Length - 1)])
                       .ToArray();
    }

    // Identity is never colour-alone: every series in the strip is direct-labelled here with its own
    // rate. In the by-type view this row is empty and the window-average legend below keeps its
    // swatches; in the by-project view it carries them and that one drops its own, so two legends can
    // never show the same hues meaning different things.
    private void BuildLiveLegend(StackPanel legend)
    {
        legend.Children.Clear();
        if (_lastProjects is not { Length: > 1 } projects) return;

        bool dark = IsDarkTheme();
        Color[] palette = ProjectPalette(projects, dark);
        for (int i = 0; i < projects.Length; i++)
        {
            ProjectSlice p = projects[i];
            var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0) };
            row.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = 10, Height = 10, RadiusX = 3, RadiusY = 3,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = Freeze(new SolidColorBrush(palette[i])),
            });
            row.Children.Add(new TextBlock
            {
                Text = L.T("stats.tps.legend",
                           p.IsOthers ? L.T("stats.live.others", p.Slug.TrimStart('+')) : p.Display,
                           Rate(p.TokensPerSecond)),
                Margin = new Thickness(6, 0, 0, 0), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
                ToolTip = L.T("stats.live.projectTip", p.IsOthers ? L.T("stats.live.others", p.Slug.TrimStart('+')) : p.Slug,
                              Big(p.WindowTokens)),
            });
            legend.Children.Add(row);
        }
    }

    // A reproducible minute of work for the screenshot: the real strip depends on whatever happens to
    // be running, which cannot be captured twice the same way. Deterministic by construction — no
    // randomness, just a shaped burst — so the published image is stable across runs.
    private void RenderDemoLive()
    {
        // Four repos plus a residual, so the multi-project view — the one worth publishing — is what
        // gets captured. Deterministic by construction: no randomness, just shaped bursts.
        (string slug, string name, int phase, double weight)[] demo =
        {
            ("d--Git-acme-web-console", "web-console", 0, 1.00),
            ("d--Git-acme-billing-api", "billing-api", 5, 0.62),
            ("c--Users-dev-notes",      "notes",       2, 0.34),
            ("d--Git-acme-infra",       "infra",       8, 0.21),
        };

        var strip = new TokenBits[LiveStrip.Seconds];
        var perProject = demo.Select(_ => new long[LiveStrip.Seconds]).ToArray();
        var others = new long[LiveStrip.Seconds];

        for (int i = 0; i < LiveStrip.Seconds; i++)
        {
            int t = LiveStrip.Seconds - 1 - i;                 // seconds ago
            bool burst = t is (< 22) or (> 40 and < 78) or (> 96 and < 128);
            if (!burst) continue;
            double k = 1 - t / (double)LiveStrip.Seconds;      // newer bursts a little heavier

            for (int p = 0; p < demo.Length; p++)
            {
                if ((i + demo[p].phase) % 3 != 0) continue;
                long tokens = (long)((900 * k + 120 * (i % 5) + 1400 * k) * demo[p].weight);
                perProject[p][i] += tokens;
                strip[i] = new TokenBits(
                    strip[i].Input + (long)(40 * k * demo[p].weight),
                    strip[i].Output + (long)((900 * k + 120 * (i % 5)) * demo[p].weight),
                    strip[i].CacheCreate + (long)((1400 * k + 300 * (i % 4)) * demo[p].weight),
                    0);
            }
            if (i % 7 == 0) others[i] = (long)(260 * k);
        }

        _lastStrip = strip;
        _lastProjects = demo
            .Select((d, p) => new ProjectSlice(d.slug, d.name, perProject[p], Sum(perProject[p]), 870 * d.weight, false))
            .Append(new ProjectSlice("+3", "+3", others, Sum(others), 41, true))
            .ToArray();

        LiveHeadS.Text = LiveHeadW.Text =
            L.T("stats.live.head", Rate(870), Big(52_000)) + "  ·  " + L.T("stats.live.sessionN", 5);
        RenderLive(_stripS, LiveLegendS, animate: false);
        RenderLive(_stripW, LiveLegendW, animate: false);

        static long Sum(long[] a) { long s = 0; foreach (long v in a) s += v; return s; }
    }

    // Plain-language read of the pace + projection for one window.
    private string ProjectionText(WindowPace w)
    {
        if (!w.HasWindow)
            return L.T("stats.proj.noWindow");

        string toReset = Dur(w.SecondsToReset);

        // With a shape to follow, the sentence follows it too — and hedges. The model is a habit, not
        // a schedule, so it says "around", never a to-the-minute landing.
        if (w.Shape is { } shape)
        {
            double now = w.ResetUnix - w.SecondsToReset;
            if (!shape.RunsOut)
                return L.T(_remaining ? "stats.proj.shapeOk.left" : "stats.proj.shapeOk",
                           Pct(Disp(shape.EndCum)), toReset);

            string warning = L.T(_remaining ? "stats.proj.shapeEta.left" : "stats.proj.shapeEta",
                LocalTime(shape.ExhaustUnix), Dur(w.ResetUnix - shape.ExhaustUnix), Dur(shape.ExhaustUnix - now));

            // The window has always reported the problem; this is the part that says what to do about
            // it. Only when there is a real answer — no advice beats invented advice.
            return shape.HasAdvice
                ? warning + " " + L.T(_remaining ? "stats.proj.shapeResume.left" : "stats.proj.shapeResume",
                                      LocalTime(shape.ResumeUnix), Pct(Disp(shape.ResumeEndCum)))
                : warning;
        }

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

        // Day boundaries: a faint dashed vertical at each local midnight, so a multi-day span (the 7-day
        // weekly chart) reads as day-sized columns instead of one long ramp. Skipped for short windows
        // like the 5-hour session, where day marks would be meaningless.
        if (w.WindowSeconds >= 2 * 86400)
        {
            double dayStart = w.ResetUnix - w.WindowSeconds;
            DateTime startLocal = DateTimeOffset.FromUnixTimeSeconds((long)dayStart).LocalDateTime;
            DateTime resetLocal = DateTimeOffset.FromUnixTimeSeconds((long)w.ResetUnix).LocalDateTime;

            // The start/reset axis labels own the two ends of this same bottom strip; a day label that
            // would run into either is dropped (its divider line still shows) so the dates never collide.
            double startRight = left + MeasureText(L.T("stats.chart.start", LocalTime(dayStart)), 10);
            double resetLeft = X(1) - MeasureText(L.T("stats.chart.reset", LocalTime(w.ResetUnix)), 10);

            // First local midnight after the window opens, then step a day at a time until the reset.
            for (DateTime day = startLocal.Date.AddDays(1); day < resetLocal; day = day.AddDays(1))
            {
                double frac = (new DateTimeOffset(day).ToUnixTimeSeconds() - dayStart) / w.WindowSeconds;
                if (frac <= 0.001 || frac >= 0.999) continue; // don't double the start/reset edges
                double x = X(frac);
                c.Children.Add(new Line
                {
                    X1 = x, Y1 = top, X2 = x, Y2 = top + ph,
                    Stroke = grid, StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 4 }, Opacity = 0.7,
                });
                // Date of the division, centered under its line on the bottom axis — unless it would
                // overlap the start/reset labels sharing that strip.
                double dw = MeasureText(day.ToString("d/M", DateFmt), 9);
                if (x - dw / 2 < startRight + 4 || x + dw / 2 > resetLeft - 4) continue;
                var dl = new TextBlock { Text = day.ToString("d/M", DateFmt), FontSize = 9, Foreground = axisFg };
                Canvas.SetLeft(dl, x - dw / 2);
                Canvas.SetTop(dl, top + ph + 4);
                c.Children.Add(dl);
            }
        }

        // Last week, faintly, behind everything else: same metric, same axes, same color as this
        // week's curve at a third of the strength — so it reads as "this, previously" rather than as
        // a new quantity. Drawn first so the live curve always wins where they overlap.
        if (w.Ghost is { } ghost && ghost.Curve.Count >= 2)
        {
            var gpts = new PointCollection(ghost.Curve.Select(p => new Point(X(p.frac), Yc(p.cum))));
            c.Children.Add(new Polyline
            {
                Points = gpts, Stroke = accent, StrokeThickness = 1.5, Opacity = 0.32,
            });
            AddHit(c, X(1), Yc(ghost.Total),
                L.T("stats.chart.lastWeek", Pct(Disp(ghost.Total)), Pct(Disp(ghost.AtSameFraction))));
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

        // Projected-idle bands: the stretches ahead this machine is usually not working. Drawn under
        // the projection so the flat steps have a visible reason, and only when the staircase is what
        // is being drawn — a band without a staircase would explain nothing.
        if (w.Shape is { } bands)
            foreach (var (f0, f1) in bands.IdleBands)
            {
                double xa = X(f0), xb = X(f1);
                var band = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(0, xb - xa), Height = ph, Fill = IdleBandBrush,
                };
                Canvas.SetLeft(band, xa);
                Canvas.SetTop(band, top);
                c.Children.Add(band);
                band.ToolTip = L.T("stats.chart.idleBand",
                    LocalTime(w.ResetUnix - w.WindowSeconds + f0 * w.WindowSeconds),
                    LocalTime(w.ResetUnix - w.WindowSeconds + f1 * w.WindowSeconds));
            }

        // Projection, activity-aware when there is a trustworthy shape to follow: quota spent along
        // the usual-hours curve (flat overnight, sloped through working hours) rather than uniformly.
        if (w.Shape is { } shape && shape.Curve.Count >= 2)
        {
            var stair = new PointCollection(shape.Curve.Select(p => new Point(X(p.frac), Yc(p.cum))));
            c.Children.Add(new Polyline
            {
                Points = stair, Stroke = ProjectionBrush, StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 4 },
            });

            double endX = X(shape.RunsOut ? shape.ExhaustFraction : 1);
            double endY = Yc(shape.RunsOut ? 1 : shape.EndCum);
            string tip = shape.RunsOut
                ? L.T(_remaining ? "stats.chart.projShapeHitZero" : "stats.chart.projShapeHit",
                      LocalTime(shape.ExhaustUnix), Dur(shape.ExhaustUnix - (w.ResetUnix - w.WindowSeconds + ef * w.WindowSeconds)))
                : L.T("stats.chart.projShapeReset", Pct(Disp(shape.EndCum)));

            var landing = new Ellipse { Width = 7, Height = 7, Fill = ProjectionBrush };
            Canvas.SetLeft(landing, endX - 3.5);
            Canvas.SetTop(landing, endY - 3.5);
            c.Children.Add(landing);

            // The landing clock time, as with the straight projection — hedged elsewhere in words, but
            // the marker itself still has to point somewhere.
            if (shape.RunsOut)
            {
                string clock = ShortTime(shape.ExhaustUnix, w.WindowSeconds);
                var cl = new TextBlock
                {
                    Text = clock, FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = ProjectionBrush,
                };
                double cw = MeasureText(clock, 10);
                Canvas.SetLeft(cl, Math.Clamp(endX - cw / 2, left, X(1) - cw));
                Canvas.SetTop(cl, _remaining ? endY - 16 : endY + 4);
                c.Children.Add(cl);
            }

            AddHit(c, endX, endY, tip);
        }
        // Projection: extend the average pace from the current point to 100% (or to the reset).
        else if (util > 0 && ef > 0)
        {
            var proj = new PointCollection { new Point(X(ef), Yc(util)) };
            double endX, endY;
            string projTip;
            // Clock time of the landing point, drawn in-plot when the quota runs out early — "in 1h 20m"
            // alone makes you do the arithmetic; the wall-clock time is what you plan around. Only for
            // the early-exhaust branch: landing at the reset, the time is already the reset axis label.
            string? projClock = null;
            if (w.ExhaustFraction <= 1)
            {
                proj.Add(new Point(X(w.ExhaustFraction), Yc(1)));
                proj.Add(new Point(X(1), Yc(1)));
                endX = X(w.ExhaustFraction); endY = Yc(1);
                double exhaustUnix = w.ResetUnix - w.WindowSeconds + w.ExhaustFraction * w.WindowSeconds;
                projClock = ShortTime(exhaustUnix, w.WindowSeconds);
                projTip = L.T(_remaining ? "stats.chart.projHitZero" : "stats.chart.projHit",
                    Dur(w.ExhaustSeconds), LocalTime(exhaustUnix));
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

            // The landing time, centered on the dot and kept inside the plot: below it in "used" mode
            // (the dot sits on the 100% line at the top), above it in "remaining" mode (it sits on the
            // 0%-left line at the bottom).
            if (projClock != null)
            {
                var cl = new TextBlock
                {
                    Text = projClock, FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = ProjectionBrush,
                };
                double cw = MeasureText(projClock, 10);
                Canvas.SetLeft(cl, Math.Clamp(endX - cw / 2, left, X(1) - cw));
                Canvas.SetTop(cl, _remaining ? endY - 16 : endY + 4);
                c.Children.Add(cl);
            }

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

        // Outage spans: stretches where no live reading was logged (an API error like a 403, or the app
        // not running). Redraw that part of the usage line in red dashed — it *is* the usage line, just
        // interpolated across a gap we couldn't measure — with a faint band and an "unavailable since …"
        // hover, so it doesn't read as smooth, real consumption.
        double windowStart = w.ResetUnix - w.WindowSeconds;
        foreach (var (f0, c0, f1, c1) in w.Gaps)
        {
            double xa = X(f0), xb = X(f1);
            var band = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(0, xb - xa), Height = ph, Fill = OutageBandBrush,
            };
            Canvas.SetLeft(band, xa);
            Canvas.SetTop(band, top);
            c.Children.Add(band);
            c.Children.Add(new Line
            {
                X1 = xa, Y1 = Yc(c0), X2 = xb, Y2 = Yc(c1),
                Stroke = OutageBrush, StrokeThickness = 2.5,
                StrokeDashArray = new DoubleCollection { 4, 3 },
            });
            AddHit(c, (xa + xb) / 2, Yc((c0 + c1) / 2),
                L.T("stats.chart.unavailable", LocalTime(windowStart + f0 * w.WindowSeconds)));
        }

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

    // Rendered width of a chart label, for laying out / collision-testing the bottom axis strip.
    private static double MeasureText(string text, double fontSize)
    {
        var t = new TextBlock { Text = text, FontSize = fontSize };
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return t.DesiredSize.Width;
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

    // Clock time for a label drawn inside the plot, where space is tight: just "18:40" in a window that
    // can't span days (the 5-hour session), with the date prefixed on the multi-day weekly chart — same
    // "d/M" form as its day dividers — where the time alone wouldn't say which day.
    private static string ShortTime(double unix, double windowSeconds)
    {
        if (unix <= 0) return "—";
        DateTime local = DateTimeOffset.FromUnixTimeSeconds((long)unix).LocalDateTime;
        return windowSeconds >= 2 * 86400
            ? local.ToString("d/M HH:mm", DateFmt)
            : local.ToString("HH:mm", DateFmt);
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
