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

/// <summary>Part of <see cref="StatisticsWindow"/> — split out by T133, moved verbatim.</summary>
internal partial class StatisticsWindow : Window
{
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

        head.Text = ThroughputHead(w);
        if (!w.HasWindow || w.ElapsedSeconds <= 0 || sum <= 0) return;

        double elapsed = w.ElapsedSeconds;
        Color[] palette = LiveChart.Colors(IsDarkTheme());
        var types = new (string label, long tokens, Color color)[]
        {
            (L.T("stats.tps.input"),  input,  palette[0]),
            (L.T("stats.tps.output"), output, palette[1]),
            (L.T("stats.tps.cacheCreate"), cache, palette[2]),
        };

        // Legend: colored swatch + type + its own rate, with the absolute total on hover. The swatches
        // are unconditional again since T111 — the per-project strip that used to sit next to this row,
        // and would have made the same three hues mean two different things, is on its own tab now.
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

    // "N tok/s over M tokens" for one window — the headline of the row above, without its legend.
    private static string ThroughputHead(WindowPace w)
    {
        long sum = w.InputTokens + w.OutputTokens + w.CacheCreationTokens;
        return !w.HasWindow || w.ElapsedSeconds <= 0 || sum <= 0
            ? L.T("stats.tps.none")
            : L.T("stats.tps.head", Rate(w.TokensPerSecond), Big(sum));
    }

    // The Throughput tab's slow half: both window averages, one line each, so the live number above them
    // has something to be read against. One line and no legend on purpose — the per-type split of a
    // *window* average belongs on that window's own tab, and repeating it here is what made this pane
    // scroll and clip its last row.
    private void PopulateThroughputTab()
    {
        if (_session is { } s) TpsHeadT5.Text = ThroughputHead(s);
        if (_weekly is { } w) TpsHeadT7.Text = ThroughputHead(w);
    }

    // ------------------------------------------------------------------ live throughput (T99)

    // The strip and the number above it are the only things in this window on a local clock. They are
    // built once the layout exists (the strip needs a real width) and are torn down with the window,
    // so a closed Statistics window tails nothing.
    private void StartLive()
    {
        if (_chartProjects is not null) return;
        bool dark = IsDarkTheme();
        _chartProjects = new LiveChart(ChartProjects, dark, AxisTick, ScaleTip);
        _chartTypes = new LiveChart(ChartTypes, dark, AxisTick, ScaleTip);
        // The reading under the pointer (T104). The chart knows which second and what it drew; naming
        // the series, and finding the raw tokens that landed in that second, is this window's job.
        _chartProjects.Readout = ProjectReadout;
        _chartTypes.Readout = TypeReadout;

        // The charts are drawn in device pixels against their host's width, so a resize (and the very
        // first layout pass, where the width is still zero) has to redraw them. Static, not animated:
        // a resize is not a second passing.
        ChartProjects.SizeChanged += (_, _) => RenderLive(animate: false);
        ChartTypes.SizeChanged += (_, _) => RenderLive(animate: false);

        if (PreviewDemoLive) { RenderDemoLive(); return; }

        try
        {
            // This profile's transcripts, not the default dir's: another profile's live rate is a
            // different config dir's work (T128).
            _tail = new TranscriptTail(_profile.ProjectsDir);
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
            _chartProjects?.Stop();
            _chartTypes?.Stop();
        }
    }

    private void StopLive(bool dispose)
    {
        _liveTimer?.Stop();
        _chartProjects?.Stop();
        _chartTypes?.Stop();
        if (!dispose) return;
        _liveTimer = null;
        try { _tail?.Dispose(); } catch { /* best-effort */ }
        _tail = null;
        _live = null;
    }

    private void LiveTick()
    {
        if (PreviewDemoLive) return;   // the synthetic strip owns the row; a tab switch must not clear it
        if (_live is null) { LiveHeadS.Text = LiveHeadW.Text = LiveHeadT.Text = L.T("stats.live.off"); return; }

        _live.Tick(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        _lastTypeRates = _live.TypeRates(LiveChart.Samples);
        // The raw per-second buckets beside the rate the line draws — the two readings the hover
        // reports together, which is the distinction a rolling rate necessarily smooths over (T104).
        // The per-project equivalent already rides along in ProjectSlice.PerSecond.
        TokenBits[] strip = _live.Strip();
        _lastTypeTokens = new[]
        {
            strip.Select(b => b.Input).ToArray(),
            strip.Select(b => b.Output).ToArray(),
            strip.Select(b => b.CacheCreate).ToArray(),
        };
        // The drawn span, so a project is on the chart exactly while the chart can show it — and keeps
        // its slot through a pause instead of blinking out and back (T115).
        _lastProjects = _live.Projects(LiveChart.Samples);
        int sessions = _live.ActiveSessions;

        string head = _live.Quiet
            ? L.T("stats.live.quiet")
            : L.T("stats.live.head", Rate(_live.TokensPerSecond),
                  Big(_live.Window.Total - _live.Window.CacheRead)) +
              (sessions > 0 ? "  ·  " + L.T(sessions == 1 ? "stats.live.session1" : "stats.live.sessionN", sessions) : "");
        LiveHeadS.Text = LiveHeadW.Text = LiveHeadT.Text = head;
        ShowCacheReadNote(_live.CacheReadPerSecond, _live.TokensPerSecond);

        // The geometry is only worth building while its tab is on screen; the number above is on every
        // tab, and is cheap text.
        RenderLive(animate: true);
    }

    // The strip answers one of two questions, and which one depends on how many projects are running.
    // Across repos, "where is it going" dominates and the stack is per project — that is the whole
    // point of T100. With a single project there is nothing to attribute, and folding the strip to one
    // flat colour would throw away the input/output/cache-create mix for no gain, so the by-type view
    // stays. The legend under the strip always names which view is on screen, so the switch can never
    // be silent.
    private void RenderLive(bool animate)
    {
        if (_chartProjects is null || _chartTypes is null) return;
        // Off its tab there is nothing to draw into — and the geometry is the expensive part of this row.
        if (PanesBody.SelectedIndex != ThroughputTab) return;
        bool dark = IsDarkTheme();

        // The second the series end at: what lets each chart append rather than re-import a recomputed
        // past (T119). The fixture pins it, so its single render is a wholesale adopt.
        long second = _live?.HeadSecond ?? 1;
        if (_lastProjects is { Length: > 0 } projects)
            _chartProjects.Render(projects.Select(p => p.RatePerSecond).ToArray(),
                                  ProjectPalette(projects, dark), animate, second);
        if (_lastTypeRates is { Length: > 0 } types)
            _chartTypes.Render(types, LiveChart.Colors(dark), animate, second);

        BuildProjectLegend();
        BuildTypeLegend();
    }

    /// <summary>Index of the Throughput tab in <c>PanesBody</c> — the only tab the strip is drawn on.</summary>
    private const int ThroughputTab = 2;

    private static Color[] ProjectPalette(ProjectSlice[] projects, bool dark)
    {
        Color[] hues = LiveChart.ProjectColors(dark);
        // Colour follows the project's *slot*, not its position in this array — the slot is sticky for as
        // long as the project is on screen (T114), so a hue never changes hands under the reader. The
        // residual is always the neutral; never a generated hue for a fifth series.
        return projects.Select(p => p.IsOthers
                                    ? LiveChart.Others(dark)
                                    : hues[Math.Clamp(p.Slot, 0, hues.Length - 1)])
                       .ToArray();
    }

    // Identity is never colour-alone: every line in either chart is direct-labelled here with its own
    // rate. Order follows the sticky slot, so an entry doesn't move while it is being read (T114).
    private void BuildProjectLegend()
    {
        LegendProjects.Children.Clear();
        if (_lastProjects is not { Length: > 0 } projects) return;

        bool dark = IsDarkTheme();
        Color[] palette = ProjectPalette(projects, dark);
        for (int i = 0; i < projects.Length; i++)
        {
            ProjectSlice p = projects[i];
            string name = p.IsOthers ? L.T("stats.live.others", p.Slug.TrimStart('+')) : p.Display;
            LegendProjects.Children.Add(LegendEntry(palette[i], L.T("stats.tps.legend", name, Rate(p.TokensPerSecond)),
                L.T("stats.live.projectTip", p.IsOthers ? name : p.Slug, Big(p.WindowTokens))));
        }
    }

    // What the cache re-read costs (T107). `LiveRate` has separated cache reads from real work since
    // T98 and showed the result to nobody — and the result is the startling one: measured on ordinary
    // traffic here, ~30,000 tok/s of cache read against ~150 tok/s of work, a 200× ratio. Excluding it
    // from every rate above is right (it barely weighs on the limit and would drown the signal), but an
    // exclusion that large has to be *said*, in the place where it is being made.
    //
    // Deliberately a sentence and not a fourth line on the chart: a fourth series in a legend of three
    // reads as something that was drawn, and the number is only meaningful as a ratio anyway. It is
    // also the per-turn price of a large eager context, which is what makes it the sentence that joins
    // this tab to the Context Load Inspector rather than a fourth number nobody asked for (T107).
    private void ShowCacheReadNote(double cacheRead, double work)
    {
        if (cacheRead <= 0)
        {
            CacheReadNote.Visibility = Visibility.Collapsed;
            return;
        }
        // The ratio needs a denominator worth dividing by: through a pause the work rate decays toward
        // zero, and "∞× the rate above" is a division artefact, not a reading.
        CacheReadNote.Text = work >= MinWorkForRatio
            ? L.T("stats.live.cacheRead", Rate(cacheRead), Ratio(cacheRead / work))
            : L.T("stats.live.cacheReadAlone", Rate(cacheRead));
        // Why it is excluded, and what it is the price *of*, on hover: the reading has to fit on one
        // line or it pushes the window averages below the fold — the defect T111 already fixed once.
        CacheReadNote.ToolTip ??= L.T("stats.live.cacheReadTip");
        CacheReadNote.Visibility = Visibility.Visible;
    }

    /// <summary>Below this rate of real work the cache-read ratio is not reported: it would be
    /// arithmetic about an idle minute rather than a measurement of anything.</summary>
    private const double MinWorkForRatio = 1.0;

    // A multiplier reads as a magnitude, so it is rounded like one — "200×", not "197.4×".
    private static string Ratio(double x)
        => x >= 100 ? Math.Round(x / 10) * 10 + "×"
        : x >= 10 ? Math.Round(x) + "×"
        : x.ToString("0.0", Fmt) + "×";

    // The second chart's legend: the same three token types the window average is split by, but at the
    // live rate — the last point of each line, which is what the chart above ends on.
    private void BuildTypeLegend()
    {
        LegendTypes.Children.Clear();
        if (_lastTypeRates is not { Length: 3 } rates || rates[0].Length == 0) return;

        Color[] palette = LiveChart.Colors(IsDarkTheme());
        string[] labels = { L.T("stats.tps.input"), L.T("stats.tps.output"), L.T("stats.tps.cacheCreate") };
        for (int i = 0; i < 3; i++)
            LegendTypes.Children.Add(LegendEntry(palette[i],
                L.T("stats.tps.legend", labels[i], Rate(rates[i][^1])), null));
    }

    // ------------------------------------------------------------------ the reading (T104)

    // What the crosshair is standing on, per project: the second itself, then one line per series with
    // the rate the chart drew and the tokens that actually landed in that second. The two are different
    // quantities and that is the point — a rolling rate is what is still ageing out of the window, so a
    // second that carried nothing can sit high on the line, and only saying both explains it.
    private string? ProjectReadout(int secondsBack, double[] drawn)
    {
        if (_lastProjects is not { Length: > 0 } projects) return null;

        var rows = new List<string>();
        long landed = 0;
        for (int i = 0; i < projects.Length && i < drawn.Length; i++)
        {
            ProjectSlice p = projects[i];
            long raw = TokensAt(p.PerSecond, secondsBack);
            landed += raw;
            string name = p.IsOthers ? L.T("stats.live.others", p.Slug.TrimStart('+')) : p.Display;
            rows.Add(L.T("stats.live.readSeries", name, Rate(drawn[i]), Big(raw)));
        }
        return Card(secondsBack, rows, landed);
    }

    // The same reading for the by-type chart. Cache reads are absent here exactly as they are absent
    // from the lines and from the headline — this reports what is drawn, not a fourth series.
    private string? TypeReadout(int secondsBack, double[] drawn)
    {
        if (_lastTypeTokens is not { Length: 3 } tokens || drawn.Length < 3) return null;

        string[] labels = { L.T("stats.tps.input"), L.T("stats.tps.output"), L.T("stats.tps.cacheCreate") };
        var rows = new List<string>();
        long landed = 0;
        for (int i = 0; i < 3; i++)
        {
            long raw = TokensAt(tokens[i], secondsBack);
            landed += raw;
            rows.Add(L.T("stats.live.readSeries", labels[i], Rate(drawn[i]), Big(raw)));
        }
        return Card(secondsBack, rows, landed);
    }

    // Header + rows, and the sentence that turns a surprising reading into an explained one.
    private string Card(int secondsBack, List<string> rows, long landed)
    {
        var sb = new System.Text.StringBuilder(Moment(secondsBack));
        foreach (string r in rows) sb.Append('\n').Append(r);
        if (landed == 0) sb.Append('\n').Append(L.T("stats.live.readNothing"));
        return sb.ToString();
    }

    // "14:32:07 · 12 s ago", or just the age when there is no real clock behind the series — the
    // fixture pins its head second, so printing 1970 there would be worse than printing nothing.
    private string Moment(int secondsBack)
    {
        string ago = secondsBack == 0 ? L.T("stats.live.readNow") : L.T("stats.live.readAgo", secondsBack);
        long head = _live?.HeadSecond ?? 0;
        if (head < 1_000_000_000) return ago;
        DateTime when = DateTimeOffset.FromUnixTimeSeconds(head - secondsBack).LocalDateTime;
        return L.T("stats.live.readAt", when.ToString("HH:mm:ss", CultureInfo.CurrentCulture), ago);
    }

    // The raw arrays are oldest-first and may be longer than the drawn span (the ring is 300s, the
    // chart 182), so an age is counted back from the end rather than indexed from the start.
    private static long TokensAt(long[] perSecond, int secondsBack)
    {
        int i = perSecond.Length - 1 - secondsBack;
        return i >= 0 && i < perSecond.Length ? perSecond[i] : 0;
    }

    // One swatch + label pair, shared by both legends so they cannot drift apart visually.
    private StackPanel LegendEntry(Color color, string text, string? tip)
    {
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0) };
        row.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width = 10, Height = 10, RadiusX = 3, RadiusY = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = Freeze(new SolidColorBrush(color)),
        });
        row.Children.Add(new TextBlock
        {
            Text = text,
            Margin = new Thickness(6, 0, 0, 0), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
            ToolTip = tip,
        });
        return row;
    }

    // Render the reproducible three minutes behind `--stats live`. The shaping itself lives in
    // `ThroughputFixture` (T108) — this method is the presentation of it, which is all a code-behind
    // should own: fields, text, one render, and the pose the readout is held in.
    private void RenderDemoLive()
    {
        ThroughputDemo demo = ThroughputFixture.Build();

        _lastTypeRates = demo.TypeRates;
        _lastTypeTokens = demo.TypeTokens;
        _lastProjects = demo.Projects;

        LiveHeadS.Text = LiveHeadW.Text = LiveHeadT.Text =
            L.T("stats.live.head", Rate(demo.HeadRate), Big(demo.HeadTokens)) + "  ·  " +
            L.T("stats.live.sessionN", demo.Sessions);
        ShowCacheReadNote(demo.CacheReadPerSecond, demo.HeadRate);
        RenderLive(animate: false);

        // A captured window has no pointer in it, so the reading is posed on the second where the two
        // numbers a hover separates differ most.
        _chartProjects?.Pin(demo.PinSecondsBack);
        _chartTypes?.Pin(demo.PinSecondsBack);
    }
}
