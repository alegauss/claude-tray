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

    // A reproducible three minutes for the screenshot: the real charts depend on whatever happens to be
    // running, which cannot be captured twice the same way. Deterministic by construction — no
    // randomness, just shaped bursts — so the published image is stable across runs.
    private void RenderDemoLive()
    {
        // Four repos plus a residual, so the per-project chart is drawn with the crossings that make the
        // fixed slots worth having.
        (string slug, string name, int phase, double weight)[] demo =
        {
            ("d--Git-acme-web-console", "web-console", 0, 1.00),
            ("d--Git-acme-billing-api", "billing-api", 5, 0.62),
            ("c--Users-dev-notes",      "notes",       2, 0.34),
            ("d--Git-acme-infra",       "infra",       8, 0.21),
        };

        // Long enough for both filters to be warm at the left edge: every drawn point needs the 60s of
        // buckets behind it, and the attack lag needs its own run-up before the first drawn one (T117) —
        // a fixture that started exactly at the left edge would ramp in from zero and then settle.
        const int span = LiveChart.Samples + LiveRate.WindowSeconds + LiveRate.SmoothWarmUp;
        var perType = new long[3][];
        for (int i = 0; i < 3; i++) perType[i] = new long[span];
        var perProject = demo.Select(_ => new long[span]).ToArray();
        var others = new long[span];

        for (int i = 0; i < span; i++)
        {
            int t = span - 1 - i;                              // seconds ago
            bool burst = t is (< 22) or (> 40 and < 78) or (> 96 and < 128) or (> 150 and < 205);
            if (!burst) continue;
            double k = 1 - t / (double)span;                   // newer bursts a little heavier

            for (int p = 0; p < demo.Length; p++)
            {
                if ((i + demo[p].phase) % 3 != 0) continue;
                perProject[p][i] += (long)((900 * k + 120 * (i % 5) + 1400 * k) * demo[p].weight);
                perType[0][i] += (long)(40 * k * demo[p].weight);
                perType[1][i] += (long)((900 * k + 120 * (i % 5)) * demo[p].weight);
                perType[2][i] += (long)((1400 * k + 300 * (i % 4)) * demo[p].weight);
            }
            if (i % 7 == 0) others[i] = (long)(260 * k);
        }

        // One deliberate cache write, because real traffic has them: a turn that writes a large block
        // lands tens of thousands of tokens in a single second. Sized to what it *becomes* — the kernel
        // spreads it over the following minute, so a 240k second (which happens) draws a hump 25× the
        // ordinary rate and flattens everything else in the fixture to a baseline. A moderate one shows
        // the same shape and still leaves the four projects legible, which is what this image is for.
        int spike = span - 1 - 41;                             // 41 seconds ago
        perType[0][spike] += 2_400;
        perType[1][spike] += 600;
        perType[2][spike] += 44_000;
        perProject[0][spike] += 47_000;

        _lastTypeRates = perType.Select(s => LiveRate.RateFrom(s, LiveChart.Samples)).ToArray();
        _lastProjects = demo
            .Select((d, p) => new ProjectSlice(d.slug, d.name, perProject[p],
                                               LiveRate.RateFrom(perProject[p], LiveChart.Samples),
                                               Sum(perProject[p]), 870 * d.weight, false, p))
            .Append(new ProjectSlice("+3", "+3", others, LiveRate.RateFrom(others, LiveChart.Samples),
                                     Sum(others), 41, true, -1))
            .ToArray();

        LiveHeadS.Text = LiveHeadW.Text = LiveHeadT.Text =
            L.T("stats.live.head", Rate(870), Big(52_000)) + "  ·  " + L.T("stats.live.sessionN", 5);
        RenderLive(animate: false);

        static long Sum(long[] a) { long s = 0; foreach (long v in a) s += v; return s; }
    }
}
