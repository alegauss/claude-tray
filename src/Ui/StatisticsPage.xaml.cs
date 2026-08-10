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
/// The Statistics page — the destination <see cref="MainWindow"/> opens on, and what a click on the
/// tray icon lands you in: a pacing report for the 5-hour session and
/// 7-day weekly rate-limit windows, built from <see cref="UsageReport.ComputePace"/>. Each window gets
/// a burn-up chart — real consumption vs. the even-pace line, plus a dashed projection of where the
/// current pace lands — so it's visually obvious whether the quota is being spent evenly or will run
/// out before the reset.
///
/// Same WPF + built-in Fluent theme as <see cref="SettingsPage"/> — owned by the shell, since a page
/// has no chrome of its own. The
/// live utilization/reset numbers are passed in from the tray as a <see cref="PaceSnapshot"/>; the
/// transcript scan that shapes the curves runs off the UI thread, and a generation counter drops
/// stale results if the report is refreshed while a scan is in flight.
/// </summary>
internal partial class StatisticsPage : System.Windows.Controls.UserControl
{
    // Dates read more naturally in the display language (localized month names), so format them with
    // the active language's culture rather than the invariant one every number goes through (T216).
    // A property, not a captured field: `L.Apply` runs before any window is built, but a field would
    // pin whatever the language was when this type was first touched, which is a different guarantee.
    private static CultureInfo DateFmt => L.DateCulture;

    // Projection line color — a warm amber that reads on both light and dark backgrounds.
    private static readonly Brush ProjectionBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x30)));

    // "Unavailable" (API-outage) markers: a vivid red for the dashed span, and a faint red wash for the
    // band behind it — the stretch of the window where no live reading could be taken.
    private static readonly Brush OutageBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE3, 0x3B, 0x30)));
    private static readonly Brush OutageBandBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x22, 0xE3, 0x3B, 0x30)));

    /// <summary>The overage series and its own axis (T183) — the same clay the tray icon wears for the
    /// paying state, so one colour means "past the included quota" across the whole app.</summary>
    private static readonly Brush BillingBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x57)));

    // Projected-idle bands: the projection's own amber at band strength. Deliberately *not* red —
    // red already means "no reading was logged", and "we measured, and it was zero" is the opposite
    // claim. Past idle gets no band at all: the curve is already visibly flat there.
    private static readonly Brush IdleBandBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x1E, 0xE0, 0xA0, 0x30)));

    // The live rate-limit reading, refreshed in place by the tray on each poll (see UpdateSnapshot),
    // so the report tracks the same cadence configured in Settings without polling the API itself.
    private PaceSnapshot? _snapshot;

    // The monitored profile's own reading, held aside for as long as the window is showing a different
    // one (T164). `_snapshot` is whichever profile is on screen, so switching away overwrites it with the
    // other account's numbers — and switching *back* used to keep them, because the monitored branch read
    // `_snapshot` and found what the other profile had left there. The charts were then this profile's
    // history scaled to another profile's utilization, until the next poll happened to correct it (five
    // minutes by default). Kept fresh even while another profile is shown: the tray goes on polling this
    // one for the icon regardless, so coming back must not have to wait for the poll after that.
    private PaceSnapshot? _monitoredSnapshot;
    private string? _monitoredError;

    private int _generation;
    private WindowPace? _session;  // last-rendered data, kept so the charts can redraw on resize
    private WindowPace? _weekly;

    // The last report each profile rendered, keyed by profile (T166). T164 clears the panes on the way
    // out of a profile — correctly, since leaving them up puts the previous account's curves under this
    // account's name — and the cost is that coming back blanks the window for a whole transcript scan,
    // which is the exact blink T118 removed for the poll refresh. A report is *that profile's own*, so
    // putting it straight back is neither a relabelling nor a guess; the recompute lands on top of it a
    // second or two later. Never keyed by anything but the profile: the entry a switch reads must be the
    // one the same profile wrote, which is precisely what T165's round trip asserts.
    private readonly Dictionary<string, PaceReport> _lastReport = new();

    // How stale a re-shown report may be. The numbers in one are true as of `ComputedLocal` and the
    // footer says so — but two of them are countdowns ("until reset", "even-pace target now") that drift
    // against a fixed report, and `Dur` renders them to the minute. A minute is therefore the longest an
    // honest re-show can last, and it is the same tolerance T165's check already treats as one reading.
    // It also covers the case the field report is about: switch away, look, switch back.
    private const double CachedReportMaxAgeSeconds = 60;

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
    // Two charts of the same rate on one tab (T116): per project, and per token type. Both are always
    // drawn — the single chart they replaced switched between the two views as soon as a second repo
    // started generating, so its colours changed meaning while it was being read.
    private LiveChart? _chartProjects, _chartTypes;
    private System.Windows.Threading.DispatcherTimer? _liveTimer;
    // Kept so a resize or a tab switch can repaint without waiting for the next tick.
    private double[][]? _lastTypeRates;
    private ProjectSlice[]? _lastProjects;
    // The raw per-second tokens behind the type chart's three lines, oldest first — what the hover
    // reports beside the rate (T104). Per project the same counts ride along in ProjectSlice.PerSecond.
    private long[][]? _lastTypeTokens;

    /// <summary>Dev/preview seam: feed the strip a deterministic synthetic minute instead of the real
    /// tail, so the screenshot of a *moving* row is reproducible. See <c>--stats live</c>.</summary>
    internal bool PreviewDemoLive { get; init; }

    /// <summary>Dev/preview seam: open the method popup on first render. It is a separate top-level
    /// window, so the off-screen <c>RenderTargetBitmap</c> path cannot capture it — this is how it gets
    /// looked at. See <c>--stats method</c>.</summary>
    internal bool PreviewMethodOpen { get; init; }

    /// <summary>Which profile this window is reporting on (T128). Everything it draws — the charts, the
    /// projection, the activity shape, the ghost week and the live throughput — comes from this profile's
    /// own stores and its own transcripts.</summary>
    private ProfileRef _profile = ProfileStore.MonitoredRef;

    /// <summary>The profiles offered in the picker, monitored first. Empty or single ⇒ no picker.</summary>
    private List<ClaudeInfo> _profiles = new();

    /// <summary>True while the window is showing the profile the tray polls for the icon. Only then may a
    /// pushed reading be adopted — a snapshot is one account's utilization, and applying the monitored
    /// account's numbers to another profile's charts would be a silent lie.</summary>
    private bool _showingMonitored = true;

    public StatisticsPage(PaceSnapshot? snapshot, bool showRemaining = false, string? error = null)
    {
        _snapshot = _monitoredSnapshot = snapshot;
        _remaining = showRemaining;
        _error = _monitoredError = error;
        InitializeComponent();

        Loaded += (_, _) => { HookHost(); StartLive(); };
        // The page outlives every navigation — the shell only toggles visibility — so the tail is torn
        // down when the shell itself goes away, which is the one thing that unloads the page.
        Unloaded += (_, _) => StopLive(dispose: true);
        // Minimizing leaves IsVisible true in WPF, so both signals are needed to honour
        // "hidden ⇒ stopped". Navigating away from this destination collapses the page, which is the
        // same claim as hiding the window used to be, and arrives through the same event.
        IsVisibleChanged += (_, _) => SyncLiveClock();
        // The strip is only painted while its tab is on screen, so arriving on that tab has to repaint
        // immediately rather than leaving it blank until the next second. RenderLive as well as the
        // tick, because the preview fixture short-circuits the tick and would otherwise show nothing.
        PanesBody.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource != PanesBody) return;
            LiveTick();
            RenderLive(animate: false);
        };
        WireSessionsTab();

        if (_snapshot is not null)
            Reload();
        else
            ShowStatus(error is { Length: > 0 } ? L.T("stats.apiError", error) : L.T("stats.connect"));
    }

    /// <summary>True once the host window's <c>StateChanged</c> has been subscribed to. Minimizing is a
    /// property of the window, not of the page, but "hidden ⇒ the live tail stops" is this page's rule —
    /// so it listens to whichever window it ends up in, once (Loaded can fire again on a re-parent).</summary>
    private bool _hostHooked;

    private void HookHost()
    {
        if (_hostHooked || Window.GetWindow(this) is not { } host) return;
        _hostHooked = true;
        host.StateChanged += (_, _) => SyncLiveClock();
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
        // The tray polls the monitored profile for the icon on its own cadence, whichever profile this
        // window happens to be showing — so the reading is *recorded* either way. Displaying it is the
        // part that is conditional: while another profile is on screen this is a different account's
        // utilization, and adopting it would relabel someone else's usage.
        _monitoredSnapshot = snapshot;
        _monitoredError = error;
        if (!_showingMonitored) return;
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
            PopulateThroughputTab();
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
        using FileStream fs = OutFile.Create(path);
        encoder.Save(fs);
    }

    // An opaque background for snapshots: the theme's base surface if present, else a Fluent-dark gray.
    private Brush CaptureBackdrop()
    {
        foreach (string key in new[] { "SolidBackgroundFillColorBaseBrush", "ApplicationBackgroundBrush" })
            if (TryFindResource(key) is Brush b) return b;
        return Freeze(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)));
    }

    /// <summary>Snapshot each tab in turn to <c>{basePath}-5h.png</c> / <c>-7d.png</c> /
    /// <c>-throughput.png</c> / <c>-sessions.png</c>. Selecting a tab realizes its chart (its
    /// <c>SizeChanged</c> draws it), so each one renders fully.</summary>
    /// <remarks>The loop stops at the shorter of the two lengths, so a tab added without a suffix is
    /// silently not captured — which is how T328's pane would have been missed. Adding the suffix is
    /// part of adding a pane.</remarks>
    internal void SaveAllTabs(string basePath)
    {
        string[] suffixes = { "-5h.png", "-7d.png", "-throughput.png", "-sessions.png" };
        for (int i = 0; i < PanesBody.Items.Count && i < suffixes.Length; i++)
        {
            PanesBody.SelectedIndex = i;
            // The one pane whose content arrives asynchronously. Photographing it before the scan
            // lands writes a PNG of the placeholder and calls it a capture (T286/T298's defect).
            if (i == SessionsTab) WaitForSessions();
            UpdateLayout();
            // Flush the render queue so the chart drawn on this tab's SizeChanged is present.
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            SaveSnapshot(basePath + suffixes[i]);
        }
    }

    // The footer's Close closes the shell, not the page: a destination has nothing to close.
    private void Close_Click(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();

    private void Reload()
    {
        if (_snapshot is not { } snap) return;
        int gen = ++_generation;
        // Only take the screen away when there is nothing on it yet (T118). The tray pushes a fresh
        // reading every poll — 5 minutes by default — and swapping the whole pane for a "computing…"
        // line and back made the window blink on that cadence. On the Throughput tab it was worse than
        // cosmetic: that tab does not depend on the API at all, so its live charts were being blanked and
        // rebuilt for a recomputation of numbers that live on the other two tabs. Keeping the last good
        // view up while the new one is computed is also the honest reading — nothing about it is stale
        // until the replacement arrives, and the footer timestamp says when that was.
        if (_session is null) ShowStatus(L.T("stats.computing"));

        DateTime nowUtc = DateTime.UtcNow;
        // Marshal the result back through the window's Dispatcher rather than a sync-context scheduler:
        // Reload() runs from the constructor, before a WPF SynchronizationContext exists on this thread.
        ProfileRef profile = _profile;
        Task.Run(() => UsageReport.ComputePace(nowUtc, snap, profile)).ContinueWith(t =>
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
        StatsStatusText.Text = message;
        StatsStatusText.Visibility = Visibility.Visible;
        PanesBody.Visibility = Visibility.Collapsed;
        // No report, nothing to explain: the "i" goes with the panes it describes, and its popup is
        // closed rather than left hanging over a status message.
        MethodInfo.IsChecked = false;
        MethodInfo.Visibility = Visibility.Collapsed;
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

    /// <summary>Preview seam: report as if the local history were too thin to shape the projection, so the
    /// straight-line paragraph (T163) can be read in every language on a machine whose profile is already
    /// confident. Drops the shape only — the numbers stay this machine's. See <c>--stats thin</c>.</summary>
    internal bool PreviewDemoThin { get; init; }

    /// <summary>Preview seam: draw a synthetic previous-week ghost when the real one isn't there yet.
    /// A real ghost needs two weeks of folded history (see <see cref="HourlyUsage"/>), so without this
    /// the line could not be screenshotted until the machine had been running that long.</summary>
    internal bool PreviewDemoGhost { get; init; }

    /// <summary>Preview seam: a synthetic overage series on the weekly chart (T183). The real one needs an
    /// account that has actually gone past its included quota, which is a state no machine can be put into
    /// on demand — so without this the second axis could never be looked at before shipping it. Same rule
    /// as the other fixtures: synthetic input, real drawing code. See <c>--stats overage</c>.</summary>
    internal bool PreviewDemoOverage { get; init; }

    /// <summary>Preview seam: the spell with no figure behind it (T275). See <c>--stats overage-noamount</c>
    /// and <see cref="FillDemoOverSpell"/>.</summary>
    internal bool PreviewDemoOverSpell { get; init; }

    /// <summary>Put the demo ghost past its included quota <em>without</em> putting the week in front of it
    /// there (T311). Every other route to a ghost mark goes through an overage variant, which also shades
    /// this week — so the ceiling mark had never been drawn without a band behind it, and that is the state
    /// most weeks are in and the one the legend used to describe wrongly.</summary>
    internal bool PreviewDemoGhostOver { get; init; }

    /// <summary>The shape of a week that ran out mid-way and kept working: flat nothing until the quota is
    /// spent, then a climb that does not stop at the ceiling because it is not measured against one.</summary>
    /// <remarks>
    /// <b>Gated on there being nothing to scale, not on the list being empty</b> (T264). The list is
    /// non-empty on any machine whose history holds a <em>measured zero</em> for overage — which is most of
    /// them, since <c>UsageHistory</c> writes <c>ux</c> whenever the header was present and 0 is a reading
    /// (T179). So the old guard bailed, the real series had <c>ExtraMax == 0</c>, the chart's own
    /// <c>hasExtra</c> was false, and <c>--stats overage</c> drew a week with no second axis and no clay
    /// series: the preview whose entire reason for existing is a state no machine can be put into, silently
    /// showing the state every machine is already in. It is how <c>docs/statistics-overage.png</c> became a
    /// picture nothing could re-take — it was captured where that history did not exist yet.
    /// <para>An all-zero curve is also replaced rather than appended to, or the demo would draw its climb
    /// on top of a flat line of real zeros and mean two things at once.</para>
    /// </remarks>
    internal static void FillDemoOverage(WindowPace w)
    {
        if (!w.HasWindow || w.ExtraMax > 0) return;
        w.ExtraCurve.Clear();
        // Relative to how much of the window has actually elapsed, not an absolute fraction: the preview
        // is only ever as wide as "now", so a fixed onset past it produces an empty series and a chart
        // that silently draws nothing.
        double onset = w.ElapsedFraction * 0.55;         // where the included quota ran out
        double span = Math.Max(0.001, w.ElapsedFraction - onset);
        for (int i = 0; i <= 40; i++)
        {
            double t = i / 40.0, f = onset + t * span;
            w.ExtraCurve.Add((f, 0.47 * t * (0.75 + 0.25 * Math.Sin(t * 9))));
        }
        foreach (var (_, v) in w.ExtraCurve) w.ExtraMax = Math.Max(w.ExtraMax, v);
        // The band the figure cannot draw (T275). It covers the same stretch here, which is the *easy*
        // case — a spell with a figure. The one this exists for is the opposite: an account the header
        // said was over while `ux` stayed at 0, which `--stats overage-noamount` is the preview of.
        w.ExtraSpans = new List<(double, double)> { (onset, w.ElapsedFraction) };
    }

    /// <summary>The spell as it was actually measured (T275): the API saying the account is past its
    /// included quota for a stretch of the week, with the overage figure reading <c>0</c> the whole time.
    ///
    /// <para>Its own preview rather than a variation of the one above, because the two draw different
    /// pictures and only this one is the state that went unrecorded. There is no clay curve, no second
    /// axis and no maximum to label — every one of those needs a figure, and T247 is right that a series
    /// of zeros is not a series. What there is is the band, which is the whole claim: this is when it was
    /// happening, and the app does not know how much it amounted to.</para></summary>
    internal static void FillDemoOverSpell(WindowPace w)
    {
        if (!w.HasWindow) return;
        w.ExtraCurve.Clear();
        w.ExtraMax = 0;
        double onset = w.ElapsedFraction * 0.55;
        w.ExtraSpans = new List<(double, double)> { (onset, w.ElapsedFraction) };
    }

    private void Render(PaceReport r)
    {
        ComputedText.Text = L.T("stats.updated", Dates.MonthDayTime(r.ComputedLocal));

        if (r.Error != null)
        {
            ShowStatus(L.T("stats.buildFailed", r.Error));
            return;
        }

        StatsStatusText.Visibility = Visibility.Collapsed;
        PanesBody.Visibility = Visibility.Visible;
        MethodInfo.Visibility = Visibility.Visible;
        ApplyErrorBanner();

        // Under the profile it was computed for, and only once it is known to be renderable — an errored
        // report is not a view worth coming back to. The recompute that follows every switch overwrites
        // this entry, so nothing else has to invalidate it (T166).
        _lastReport[_profile.Key] = r;

        _session = r.Session;
        _weekly = r.Weekly;

        // Both windows, because being past the included quota is one fact about the account and `InUse` says
        // so on every reading either window selects (T308). Filling only the week is what kept the 5-hour
        // chart's clay states out of every preview and every capture, which is how the legend they had no
        // entry in went unnoticed. The session's snapshot on these variants is partway through and at its
        // limit for the same reason: a window with nothing elapsed leaves the band zero-width.
        if (PreviewDemoOverage) { FillDemoOverage(r.Weekly); FillDemoOverage(r.Session); }
        if (PreviewDemoOverSpell) { FillDemoOverSpell(r.Weekly); FillDemoOverSpell(r.Session); }

        // The demo ghost follows the week in front of it: on an overage preview it is a week that also ran
        // out and kept working, which is the only reading on which T295's shaded stretch exists to be
        // looked at — and the picture that shows the band and the stretch are not the same claim.
        if (PreviewDemoGhost && r.Weekly.Ghost is null && r.Weekly.HasWindow)
        {
            bool ghostOver = PreviewDemoOverage || PreviewDemoOverSpell || PreviewDemoGhostOver;
            r.Weekly.Ghost = HourlyUsage.Demo(
                DateTimeOffset.FromUnixTimeSeconds((long)(r.Weekly.ResetUnix - r.Weekly.WindowSeconds)).LocalDateTime,
                r.Weekly.WindowSeconds, r.Weekly.ElapsedFraction, 0.86, ghostOver);
        }

        // On the first render, open the weekly tab instead of the 5-hour one when the session is idle
        // (expired / not using Claude): the 5-hour chart is then flat at 100% and the weekly chart, with
        // its accumulated usage, is the more interesting view. Any usage in the session keeps the 5-hour
        // default. Guarded so subsequent auto-refreshes leave the user's current tab alone.
        if (!_defaultTabPicked)
        {
            _defaultTabPicked = true;
            bool sessionIdle = !r.Session.HasWindow || r.Session.Util <= 0;
            // The live preview is *about* the Throughput tab, so it opens there — the same reasoning
            // `--stats shape` uses to open on the weekly tab rather than needing a click first.
            if (PreviewDemoLive) PanesBody.SelectedIndex = ThroughputTab;
            else if (sessionIdle && r.Weekly.HasWindow)
                PanesBody.SelectedIndex = 1;
        }

        ApplyModeLabels();
        Populate(r.Session, ChipS, ChipTextS, UsedS, IdealS, ResetS, ProjectionS, ChartS, TpsHeadS, TpsLegendS);
        Populate(r.Weekly, ChipW, ChipTextW, UsedW, IdealW, ResetW, ProjectionW, ChartW, TpsHeadW, TpsLegendW);
        PopulateThroughputTab();
        // The conversation list is about the profile, not about the reading, so it is loaded once per
        // reload rather than per poll — and off the UI thread, because a cold cache is seconds (T328).
        LoadSessions();

        // Which paragraphs the note yields is a real decision over four inputs, and it lives in
        // `MethodNoteParts` where `--selftest` can call it (T168). What is left here is the rendering:
        // `L.T` and a space between the paragraphs, and nothing that could be wrong on screen without
        // being wrong in an assertion first.
        MethodNote.ItemsSource = MethodNoteLines(r, PreviewDemoThin);

        // Only now, and only after a layout pass: a `Popup` fixes its placement when it opens, and the
        // paragraphs assigned on the line above have not been measured yet — so it is placed as if it
        // were empty, flips below the glyph at the bottom of the window and grows off the screen, out of
        // every capture. A human clicking the ⓘ never hits this (the note has been laid out for minutes);
        // the preview does, every time, which is why it opens at `Loaded` priority (T170).
        if (PreviewMethodOpen)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                // Holding it open is `PageWindow`'s job now, for every popup rather than this one (T217).
                MethodInfo.IsChecked = true;

                // ...and once it is open and measured, say where it landed, so the capture script can
                // assert the note is inside the pixels it copies instead of reporting a correct copy of a
                // window with no note in it (T217). Render priority: the popup places itself on open.
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
                {
                    if (MethodPopup.Child is FrameworkElement note)
                        PreviewSurface.Report("method-note", note);
                });
            });

        // The idle bands are the *chart's* legend rather than the note's, but they turn on the same
        // question the note's first branch asks, so it is asked the same way.
        bool shaped = r.Weekly.Shape is not null && !PreviewDemoThin;
        LegendIdleW.Visibility = shaped && r.Weekly.Shape!.IdleBands.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
        LegendGhostW.Visibility = r.Weekly.Ghost is not null ? Visibility.Visible : Visibility.Collapsed;
        // The clay pair, on both tabs (T300, T308). One question per entry, asked of whichever window the
        // legend sits under: `hasExtra` is what DrawChart rules a second axis by, and HasOverQuotaMark is
        // the predicate the marks themselves are drawn under.
        LegendExtraW.Visibility = HasExtraAxis(r.Weekly) ? Visibility.Visible : Visibility.Collapsed;
        LegendExtraS.Visibility = HasExtraAxis(r.Session) ? Visibility.Visible : Visibility.Collapsed;
        ApplyOverLegend(r.Weekly, _remaining, LegendOverW, OverSwatchBandW, OverSwatchCeilingW);
        ApplyOverLegend(r.Session, _remaining, LegendOverS, OverSwatchBandS, OverSwatchCeilingS);
    }

    /// <summary>Put one window's clay entry on screen: shown or not, the halves of the swatch its chart
    /// actually drew, the one sentence true of those (T311), and the edge the ceiling bar sits against
    /// (T316). Both tabs, one reader — the tab differs only in the window it is handed. <paramref
    /// name="remaining"/> is passed rather than read off the page, so the whole decision is in the
    /// arguments and <c>--selftest</c> can drive both modes.</summary>
    private static void ApplyOverLegend(WindowPace w, bool remaining, FrameworkElement entry,
                                        UIElement band, FrameworkElement ceiling)
    {
        OverLegend g = OverLegendFor(w);
        entry.Visibility = g.Show ? Visibility.Visible : Visibility.Collapsed;
        band.Visibility = g.Band ? Visibility.Visible : Visibility.Collapsed;
        ceiling.Visibility = g.Ceiling ? Visibility.Visible : Visibility.Collapsed;
        // The ceiling is the top of the plot in used mode and the bottom in remaining mode, and the swatch
        // has to say the same thing the mark does — one fact, read once (T316).
        ceiling.VerticalAlignment = CeilingSwatchEdge(remaining);
        // Only when there is an entry: a tooltip left on a collapsed one is a string nobody can reach, and
        // `TipKey` is deliberately empty in that state rather than carrying a stale sentence. Typed as the
        // element that *has* the property (T313), rather than tested for it — every caller passes a panel,
        // so the test could not fail and its else was a tooltip quietly not set.
        entry.ToolTip = g.Show ? L.T(g.TipKey) : null;
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

        // The overage line's own entry (T318). It belongs here and not with the visibility, because what
        // changes is the *wording* and the mode is what changes it — the same reason the caption above is
        // here. The axis caption it has to agree with is drawn from `ExtraAxisKey`, its pair.
        string extra = L.T(ExtraLegendKey(_remaining));
        LegendExtraTextS.Text = extra;
        LegendExtraTextW.Text = extra;
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

        // The state outranks the pace, and the colour is why (T323): #C43E3E is this page's at-limit red and
        // red on a quota surface means *stopped*, so a paying account wore "work has halted" over a sentence
        // saying the opposite. Clay is what that state wears on every other surface here, and the predicate is
        // the sentence's own — see StatisticsPage.Chart's BillingNow, which both read so they cannot disagree
        // about one pane. A pace verdict about a window whose limit is no longer what gates the work is the
        // thing worth displacing; the sentence below still reports it.
        var (chipBg, chipLabel) = BillingNow(w)
            ? (Color.FromRgb(0xD9, 0x77, 0x57), L.T("stats.verdict.billing"))
            : w.Verdict switch
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

}
