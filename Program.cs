using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClaudeTray;

// Manages the "start with Windows" entry under HKCU Run (per-user, no admin needed).
internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeTray";

    // Real .exe path, correct even for a single-file self-contained publish.
    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string v &&
               string.Equals(v.Trim('"'), ExePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{ExePath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

internal static class Program
{
    // Held for the whole process lifetime so a second launch can't spawn a duplicate tray icon.
    private static Mutex? _instanceMutex;

    [STAThread]
    private static void Main(string[] args)
    {
        // `--lang <code>` anywhere in the arguments overrides the display language for this process
        // only, leaving the saved preference alone. It exists for the i18n screenshot loop: verifying
        // that a window still fits in Spanish should not mean editing the user's settings and
        // restarting. Both tokens are removed before anything else parses the arguments, or a stray
        // "es" would be read as a project name.
        string? langOverride = null;
        int langAt = Array.IndexOf(args, "--lang");
        if (langAt >= 0 && langAt + 1 < args.Length)
        {
            langOverride = args[langAt + 1];
            args = args.Take(langAt).Concat(args.Skip(langAt + 2)).ToArray();
        }

        // Pick the UI language before anything localized is built (menus, dialogs, and XAML windows all
        // resolve strings at parse time): honor the saved Settings preference, falling back to the OS.
        L.Apply(langOverride ?? Settings.Load().Language);

        if (args.Length >= 1 && args[0] == "--render")
        {
            RenderTest(args.Length >= 2 ? args[1] : ".");
            return;
        }

        if (args.Length >= 1 && args[0] == "--makeicon")
        {
            MakeIcon(args.Length >= 2 ? args[1] : "ClaudeTray.ico");
            return;
        }

        if (args.Length >= 1 && args[0] == "--social")
        {
            string path = args.Length >= 2 ? args[1] : "docs/social-preview.png";
            using Bitmap bmp = IconRenderer.RenderSocial(1280, 640);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("wrote " + Path.GetFullPath(path));
            return;
        }

        if (args.Length >= 1 && args[0] == "--insights")
        {
            var d = UsageInsights.Compute(DateTimeOffset.UtcNow.UtcDateTime);
            if (d.Error != null) { Console.WriteLine("error: " + d.Error); return; }
            Console.WriteLine($"24h: {d.Requests} reqs  {d.Sessions} sessions");
            Console.WriteLine($"subagents: {d.SubagentPct:P0}   >150k ctx: {d.HeavyContextPct:P0}");
            foreach (var (model, pct) in d.ByModel)
                Console.WriteLine($"  {model}: {pct:P0}");
            return;
        }

        // Headless view of the transcript tail (T97): every assistant turn as it lands, plus what the
        // sweep cost. Exists so the engine behind the live rate can be verified without a window —
        // run it, start a Claude Code turn in any project, and the turn should print within a second
        // or two with only its own bytes read.
        if (args.Length >= 1 && args[0] == "--tail")
        {
            PrintTail(args.Skip(1).ToArray());
            return;
        }

        // The live rate itself (T98), printed once a second: the metric T99 will draw, verifiable
        // without a window and against `--tail`'s raw turns.
        if (args.Length >= 1 && args[0] == "--live")
        {
            PrintLive(args.Skip(1).ToArray());
            return;
        }

        // Headless view of the weekly activity shape behind the projection: 168 buckets of
        // p(active), the coverage that backs them, and what they predict for the hours ahead.
        if (args.Length >= 1 && args[0] == "--activity")
        {
            PrintActivity(args.Skip(1).ToArray());
            return;
        }

        // Headless view of the Context Load Inspector: what every session in a project costs before
        // the first prompt. `--context` lists projects; `--context <slug-or-name>` breaks one down
        // source by source; `--context --all` does that for every project; `--context --calibrate`
        // fits the estimate against what the transcripts actually measured.
        // The archival companion to the copy-a-prompt action: the whole picture as one markdown file,
        // paths and numbers only (see ContextReport).
        if (args.Length >= 1 && args[0] == "--context-report")
        {
            WriteContextReport(args.Length >= 2 ? args[1] : "context-report.md");
            return;
        }

        if (args.Length >= 1 && args[0] == "--context")
        {
            string[] contextFlags = args.Skip(1).ToArray();
            // `--context --window [slug]` opens the real window standalone instead of printing —
            // the same launch-and-screenshot loop as `--settings` (see the preview-ui skill). The
            // bare `--context` stays the headless developer report it has always been.
            if (contextFlags.Contains("--window"))
            {
                string? windowRoot = null;
                int at = Array.IndexOf(contextFlags, "--root");
                if (at >= 0 && at + 1 < contextFlags.Length) windowRoot = contextFlags[at + 1];
                if (contextFlags.Contains("--sample"))
                {
                    try { windowRoot = ContextFixture.Build(DateTimeOffset.UtcNow.UtcDateTime); }
                    catch (Exception e) { Console.WriteLine("error building the sample fixture: " + e.Message); return; }
                }

                var contextApp = new System.Windows.Application();
                // Topmost like the other previews: the capture script can't always take foreground
                // (Windows refuses the steal when another app owns it), and a screen-copy of an
                // occluded window is a screenshot of whatever covered it.
                // A slug/name may follow the flags; --root's value must not be mistaken for one.
                string? select = contextFlags.FirstOrDefault(f => !f.StartsWith("--") && f != windowRoot);
                contextApp.Run(new ContextWindow(select)
                {
                    ScanRoot = windowRoot,
                    Topmost = true,
                    // `--scroll` opens on the source table instead of the gauge, so the rows can be
                    // screenshotted — the pane is taller than the screen at the default size.
                    PreviewScrollToTable = contextFlags.Contains("--scroll"),
                    // `--simulate` pre-ticks the three heaviest removable sources, so the what-if
                    // banner can be screenshotted without a mouse.
                    PreviewSimulateTop = contextFlags.Contains("--simulate"),
                    // `--demo-history` draws the drift row from a synthetic series, so the sparkline
                    // can be screenshotted before weeks of real history exist.
                    PreviewDemoHistory = contextFlags.Contains("--demo-history"),
                });
                return;
            }
            PrintContext(contextFlags);
            return;
        }

        // Dev/preview helper: show a reset toast with sample data so the variants can be seen /
        // screenshotted standalone. Optional second arg: "scheduled", "credit", or "unexpected" (default).
        if (args.Length >= 1 && args[0] == "--simulate-reset")
        {
            SimulateReset(args.Length >= 2 ? args[1] : "unexpected");
            return;
        }

        // Dev/preview helper: render a reset toast (card + shadow + confetti) to a transparent PNG,
        // so the four variants can be documented cleanly on any background. Args: <variant> <outPath>.
        if (args.Length >= 1 && args[0] == "--capture-toast")
        {
            CaptureToast(args.Length >= 2 ? args[1] : "unexpected",
                args.Length >= 3 ? args[2] : "toast.png");
            return;
        }

        // Dev/preview helper: open just the Settings window, standalone, so the UI can be launched
        // and screenshotted deterministically without going through the tray menu.
        if (args.Length >= 1 && args[0] == "--settings")
        {
            var previewApp = new System.Windows.Application();
            var win = new SettingsWindow(Settings.Load(), _ => { }, args.Length >= 2 ? args[1] : null);
            previewApp.Run(win);
            return;
        }

        // Dev/preview helper: open just the Statistics window, standalone, for the same
        // launch-and-screenshot loop (see the preview-ui skill). Feeds a synthetic snapshot — a 5h
        // session burning ahead of pace, a 7d week comfortably on track — so both verdicts render.
        if (args.Length >= 1 && args[0] == "--stats")
        {
            var previewApp = new System.Windows.Application();
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var sample = new PaceSnapshot(
                Util5h: 0.72, Reset5h: now + 2 * 3600,      // 3h of 5h elapsed (60%), 72% used → ahead
                Util7d: 0.38, Reset7d: now + 3 * 86400);    // 4d of 7d elapsed (57%), 38% used → on track
            bool remaining = args.Length >= 2 && args[1].Equals("remaining", StringComparison.OrdinalIgnoreCase);
            // "--stats error" previews the API-error state (e.g. a 403 payment-past-due): charts drawn
            // from the last known local data, with an error banner. Any real gaps in the logged
            // readings are drawn as red "unavailable" spans on the usage line.
            // "--stats history" previews the offline fallback: no live reading, so the snapshot is
            // rebuilt from the last reading persisted on disk (usage-history.jsonl) — exactly what
            // TrayContext.CurrentSnapshot() does when signed out / the token expired at launch. Falls
            // back to the "connect" hint only when there's genuinely no history yet.
            if (args.Length >= 2 && args[1].Equals("history", StringComparison.OrdinalIgnoreCase))
            {
                PaceSnapshot? fromDisk = UsageHistory.Latest() is { } h
                    ? new PaceSnapshot(h.Util5h, h.Reset5h, h.Util7d, h.Reset7d)
                    : null;
                previewApp.Run(new StatisticsWindow(fromDisk, remaining) { Topmost = true });
            }
            else if (args.Length >= 2 && args[1].Equals("error", StringComparison.OrdinalIgnoreCase))
                previewApp.Run(new StatisticsWindow(sample, remaining,
                    "Your subscription payment is past due. Please pay your overdue invoice to restore access, or reach out to your company admin.")
                { Topmost = true });
            else if (args.Length >= 2 && args[1].Equals("gapdemo", StringComparison.OrdinalIgnoreCase))
            {
                // Deterministic preview of the recovered state: a hand-built report with a past data gap
                // that has since recovered — no error banner, but the outage still marked red on the
                // curve. Pass "ongoing" to also show the error banner (mid-outage).
                bool ongoing = args.Length >= 3 && args[2].Equals("ongoing", StringComparison.OrdinalIgnoreCase);
                var win = new StatisticsWindow(null) { Topmost = true };
                win.Loaded += (_, _) => win.PreviewReport(BuildGapDemoReport(now), ongoing
                    ? "Your subscription payment is past due. Please pay your overdue invoice to restore access, or reach out to your company admin."
                    : null);
                previewApp.Run(win);
            }
            else if (args.Length >= 2 && args[1].Equals("shape", StringComparison.OrdinalIgnoreCase))
            {
                // Preview the activity-aware weekly projection running out *before* the reset: the
                // default sample lands comfortably below 100%, which draws the staircase but never its
                // landing marker. The shape itself is real — it comes from this machine's own profile.
                // An idle 5h session so the window opens straight on the weekly tab — the shaped
                // projection is a weekly-only feature and shouldn't need a click to be looked at.
                var heavy = new PaceSnapshot(
                    Util5h: 0.0, Reset5h: now + 5 * 3600,
                    Util7d: 0.74, Reset7d: now + 3 * 86400);    // 4d of 7d elapsed, 74% used → runs out early
                // "--stats shape ghost" also draws a synthetic previous week behind it: the real ghost
                // needs two weeks of folded history, which a fresh machine hasn't got.
                previewApp.Run(new StatisticsWindow(heavy, remaining)
                {
                    Topmost = true,
                    PreviewDemoGhost = args.Any(a => a.Equals("ghost", StringComparison.OrdinalIgnoreCase)),
                });
            }
            // "--stats live" feeds the throughput strip a deterministic synthetic three minutes
            // instead of the real tail: the live row depends on whatever happens to be generating,
            // which cannot be screenshotted twice the same way.
            else if (args.Length >= 2 && args[1].Equals("live", StringComparison.OrdinalIgnoreCase))
            {
                previewApp.Run(new StatisticsWindow(sample, remaining)
                {
                    Topmost = true,
                    PreviewDemoLive = true,
                });
            }
            // "--stats method" opens the window with the method popup already up. It is its own
            // top-level window, so --capture-stats (RenderTargetBitmap over the content) cannot see it;
            // this is the path that gets it on screen for the capture script.
            else if (args.Length >= 2 && args[1].Equals("method", StringComparison.OrdinalIgnoreCase))
            {
                previewApp.Run(new StatisticsWindow(sample, remaining)
                {
                    Topmost = true,
                    PreviewDemoLive = true,
                    PreviewMethodOpen = true,
                });
            }
            else if (args.Length >= 2 && args[1].Equals("idle", StringComparison.OrdinalIgnoreCase))
            {
                // Preview the "not using Claude" state: the 5h session is idle (0% used → flat chart),
                // while the week still carries accumulated usage. The window should open on the weekly
                // tab, since the 5h chart has nothing interesting to show.
                var idle = new PaceSnapshot(
                    Util5h: 0.0, Reset5h: now + 5 * 3600,       // fresh/expired session, nothing used
                    Util7d: 0.38, Reset7d: now + 3 * 86400);    // week still has accumulated usage
                previewApp.Run(new StatisticsWindow(idle, remaining) { Topmost = true });
            }
            else
                previewApp.Run(new StatisticsWindow(sample, remaining) { Topmost = true });
            return;
        }

        // Dev/preview helper: render the Statistics window off-screen to PNGs (one per tab) via
        // RenderTargetBitmap, without needing it foreground — the screen-copy capture can't see a
        // window another app covers. Args: [outBase] (default docs\_preview\stats → -5h.png/-7d.png).
        if (args.Length >= 1 && args[0] == "--capture-stats")
        {
            string outBase = System.IO.Path.GetFullPath(args.Length >= 2 ? args[1] : @"docs\_preview\stats");
            var previewApp = new System.Windows.Application
            {
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
            };
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // A second argument of "shape" raises the weekly utilization until the activity-aware
            // projection runs out before the reset, so the staircase's landing marker and the
            // usually-idle bands are in the captured 7d tab (see `--stats shape`).
            bool heavyWeek = args.Any(a => a.Equals("shape", StringComparison.OrdinalIgnoreCase));
            var sample = new PaceSnapshot(
                Util5h: 0.72, Reset5h: now + 2 * 3600,
                Util7d: heavyWeek ? 0.74 : 0.38, Reset7d: now + 3 * 86400);
            var win = new StatisticsWindow(sample)
            {
                PreviewDemoGhost = args.Any(a => a.Equals("ghost", StringComparison.OrdinalIgnoreCase)),
                // Deterministic live strip, so the captured PNG is stable across runs.
                PreviewDemoLive = args.Any(a => a.Equals("live", StringComparison.OrdinalIgnoreCase)),
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                // Pin the theme for the capture — off-screen there is no system backdrop to follow,
                // so without this the snapshot renders dark-theme text over an unpainted background.
                ThemeMode = System.Windows.ThemeMode.Dark,
            };
            win.Show();
            // Let the async pace computation finish and the charts render, then snapshot each tab.
            var settle = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            settle.Tick += (_, _) =>
            {
                settle.Stop();
                try { win.SaveAllTabs(outBase); Console.WriteLine("wrote " + outBase + "-5h.png / -7d.png / -throughput.png"); }
                finally { previewApp.Shutdown(); }
            };
            settle.Start();
            previewApp.Run();
            return;
        }

        // Single instance: if the tray app is already running, just exit — don't add a second icon.
        _instanceMutex = new Mutex(initiallyOwned: true, @"Local\ClaudeTray.SingleInstance", out bool createdNew);
        if (!createdNew)
            return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }

    // Headless view of TranscriptTail: turns as they land, and the cost of noticing them. Flags:
    // `--tail <seconds>` to bound the run (default 30), `--root <dir>` to tail a stand-in for
    // ~/.claude/projects. The footer is the claim this task has to earn — bytes read should track the
    // bytes appended, not the size of the tree.
    private static void PrintTail(string[] flags)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* redirected output */ }
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        int rootAt = Array.IndexOf(flags, "--root");
        string? root = rootAt >= 0 && rootAt + 1 < flags.Length ? flags[rootAt + 1] : null;

        string? num = flags.FirstOrDefault(f => !f.StartsWith("--") && f != root);
        double seconds = double.TryParse(num, out double s) && s > 0 ? s : 30;

        using var tail = new TranscriptTail(root);
        Console.WriteLine($"Tailing {tail.Root}");
        Console.WriteLine($"for {seconds:0}s — start a turn in any project and it should appear here.");
        Console.WriteLine();

        tail.Appended += batch =>
        {
            foreach (TailSample smp in batch)
            {
                DateTime when = DateTimeOffset.FromUnixTimeSeconds((long)smp.Unix).LocalDateTime;
                Console.WriteLine($"{when:HH:mm:ss}  {Trim(smp.Project, 34),-34}  " +
                                  $"in {smp.Bits.Input,7:N0}  out {smp.Bits.Output,6:N0}  " +
                                  $"create {smp.Bits.CacheCreate,7:N0}  read {smp.Bits.CacheRead,9:N0}");
            }
        };

        tail.Start();
        Thread.Sleep(TimeSpan.FromSeconds(seconds));

        TailStats st = tail.Stats;
        Console.WriteLine();
        Console.WriteLine($"{st.Samples:N0} turns from {st.Tracked:N0} tracked files " +
                          $"({st.Files:N0} seen per sweep)");
        Console.WriteLine($"{st.Sweeps:N0} sweeps, last {st.LastSweepMs:0.0}ms, " +
                          $"{st.BytesRead / 1024.0:N1} KB read total");
        Console.WriteLine(st.Watching
            ? "watcher: live — appends land within ~" + TranscriptTail.DebounceMs + "ms"
            : $"watcher: off — falling back to the {TranscriptTail.SweepFloorMs}ms sweep floor");
    }

    private static string Trim(string s, int max)
        => s.Length <= max ? s : "…" + s[^(max - 1)..];

    // Headless view of LiveRate: one line a second, so the metric can be watched against real work
    // and diffed against `--tail`'s raw turns. Flags: `--live <seconds>` to bound the run (default
    // 90), `--root <dir>` for a stand-in tree, `--raw` to also print the unsmoothed box filter.
    private static void PrintLive(string[] flags)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* redirected output */ }
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        int rootAt = Array.IndexOf(flags, "--root");
        string? root = rootAt >= 0 && rootAt + 1 < flags.Length ? flags[rootAt + 1] : null;
        bool raw = flags.Contains("--raw");

        string? num = flags.FirstOrDefault(f => !f.StartsWith("--") && f != root);
        double seconds = double.TryParse(num, out double s) && s > 0 ? s : 90;

        using var tail = new TranscriptTail(root);
        var live = new LiveRate(tail);
        tail.Start();

        Console.WriteLine($"Live throughput — {tail.Root}");
        Console.WriteLine($"{LiveRate.WindowSeconds}s trailing window, cache reads excluded from the rate. " +
                          "This is throughput, NOT quota.");
        Console.WriteLine();

        double peak = 0;
        double[]? prev = null;
        long prevSec = 0;
        double worstDrift = 0;
        for (int i = 0; i < (int)seconds; i++)
        {
            Thread.Sleep(1000);
            double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            live.Tick(now);

            double rate = live.TokensPerSecond;
            if (rate > peak) peak = rate;
            TokenBits w = live.Window;

            // Does a second's *drawn* value stay put as it scrolls left (T117)? Compare this series
            // against the previous one, shifted by however many seconds actually elapsed — a late tick
            // shifts the series by two, and assuming one would report that jitter as drift. Every
            // overlapping point must agree, or the chart is redrawing history it has already drawn.
            double[] series = live.RateHistory(LiveChart.Samples);
            long sec = (long)now;
            int shift = prevSec > 0 ? (int)(sec - prevSec) : 0;
            if (prev is not null && prev.Length == series.Length && shift >= 1 && shift <= 3)
                for (int k = shift; k < series.Length; k++)
                {
                    double d = Math.Abs(series[k - shift] - prev[k]);
                    if (d > worstDrift) worstDrift = d;
                }
            prev = series;
            prevSec = sec;

            // Log-ish scale: real work spans three orders of magnitude between a one-line edit and a
            // long generation, and a linear bar spends its whole length on the top decade.
            int cells = rate <= 0 ? 0 : (int)Math.Clamp(Math.Log10(rate + 1) / 4 * 24, 1, 24);
            string bar = new string('█', cells) + new string('·', 24 - cells);

            // Per-project attribution (T100), so "which repo is burning it" is verifiable without a
            // window — and so the strip's stacking order can be checked against the ranking.
            // Per-project attribution with its *slot* (T114): the number in brackets is the fixed
            // colour/order the chart will draw that project at, and it must not move while the project
            // is on screen — printing it is how that gets checked without a window.
            string where = live.Quiet
                ? "quiet"
                : string.Join("  ", live.Projects().Select(p =>
                      $"[{(p.IsOthers ? "·" : p.Slot.ToString())}] {(p.IsOthers ? p.Display : Trim(p.Display, 18))} {p.TokensPerSecond,6:N0}/s"));

            Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {rate,8:N0} tok/s  {bar}  " +
                              (raw ? $"[raw {live.Instant,8:N0}]  " : "") +
                              $"{live.ActiveSessions} act  {where}");
        }

        // The same kernel as a *series* (T114) — what the chart is drawn from. Printed as a sparkline
        // plus its own last value, because the one property that must hold is that the right-hand end
        // equals the rate reported above: same filter, evaluated at 180 instants instead of one.
        double[] history = live.RateHistory(LiveChart.Seconds);
        Console.WriteLine();
        Console.WriteLine($"rolling rate, last {history.Length}s (one cell per {history.Length / 60}s):");
        Console.WriteLine("  " + Spark(history, 60) + $"   → {history[^1]:N0} tok/s at the right edge");
        foreach (ProjectSlice p in live.Projects(LiveChart.Seconds).Where(p => p.RatePerSecond.Length > 0))
            Console.WriteLine($"  [{(p.IsOthers ? "·" : p.Slot.ToString())}] {Trim(p.Display, 18),-18} " +
                              Spark(p.RatePerSecond, 60) + $"   → {p.RatePerSecond[^1]:N0} tok/s");

        TailStats st = tail.Stats;
        Console.WriteLine();
        Console.WriteLine($"worst redraw drift {worstDrift:N2} tok/s — a second's value must not change " +
                          "after it is drawn (T117)");
        Console.WriteLine($"peak {peak:N0} tok/s over {seconds:0}s — {live.Turns:N0} turns, " +
                          $"{st.BytesRead / 1024.0:N1} KB read, {st.Sweeps:N0} sweeps");
        Console.WriteLine("The window average this sits beside is in --stats; it answers a different " +
                          "question and both stay.");
    }

    // A series as one line of block characters, averaged down to `cells` columns and scaled to its own
    // peak. Enough to see the shape — whether the rate decays through a pause and rises again — which is
    // the whole claim T114 makes; the exact values are the numbers beside it.
    private static string Spark(double[] series, int cells)
    {
        const string ramp = " ▁▂▃▄▅▆▇█";
        double peak = series.Length == 0 ? 0 : series.Max();
        if (peak <= 0) return new string(' ', cells);
        var sb = new System.Text.StringBuilder(cells);
        for (int c = 0; c < cells; c++)
        {
            int from = c * series.Length / cells, to = Math.Max(from + 1, (c + 1) * series.Length / cells);
            double mean = 0;
            for (int i = from; i < to && i < series.Length; i++) mean += series[i];
            mean /= to - from;
            sb.Append(ramp[Math.Clamp((int)Math.Round(mean / peak * (ramp.Length - 1)), 0, ramp.Length - 1)]);
        }
        return sb.ToString();
    }

    // Headless view of ActivityProfile: the 168-bucket week the projection will be shaped by (T87),
    // printed as a shade grid so a wrong grid is visible at a glance rather than only as a wrong
    // marker on a chart. Flags: `--refresh` to force a rescan past the daily cache, `--numbers` for
    // raw percentages instead of shades, `--root <dir>` to read a stand-in for ~/.claude.
    private static void PrintActivity(string[] flags)
    {
        // Block-drawing characters and "≈" render as replacement chars on cmd.exe's default codepage.
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* redirected output */ }
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        int rootAt = Array.IndexOf(flags, "--root");
        string? root = rootAt >= 0 && rootAt + 1 < flags.Length ? flags[rootAt + 1] : null;
        bool numbers = flags.Contains("--numbers");

        DateTime nowUtc = DateTimeOffset.UtcNow.UtcDateTime;

        // `--fold` runs the aggregation the poll loop normally does only when the raw log ages past
        // its 8-day retention. It exists so the measured grid can be inspected today instead of in a
        // week; folding is idempotent per day, so this can be run at will.
        if (flags.Contains("--fold"))
        {
            List<UsageSample> raw = UsageHistory.Load(0);
            int before = HourlyUsage.Load().Count;
            HourlyUsage.Fold(raw, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            int after = HourlyUsage.Load().Count;
            Console.WriteLine($"folded {raw.Count:N0} readings — {after} days in the store (+{after - before})");
            Console.WriteLine();
        }

        ActivityProfile prof = ActivityProfile.Load(nowUtc, flags.Contains("--refresh"), root);
        if (prof.Error != null) { Console.WriteLine("error: " + prof.Error); return; }

        string source = prof.FromCache
            ? $"cached, built {prof.ComputedUtc.ToLocalTime():yyyy-MM-dd HH:mm}"
            : $"fresh scan, {prof.ElapsedMs:0}ms";
        Console.WriteLine($"Activity profile — {prof.CoverageWeeks:0.0} weeks of coverage, " +
                          $"{prof.Samples:N0} requests ({source})");
        if (root == null) Console.WriteLine("cache: " + ActivityProfile.CachePath);
        Console.WriteLine(prof.Confident
            ? $"confidence: usable — {prof.CoverageWeeks:0.0} weeks ≥ {ActivityProfile.ConfidentWeeks:0.0}, " +
              "the projection may follow this shape"
            : $"confidence: thin — {prof.CoverageWeeks:0.0} weeks < {ActivityProfile.ConfidentWeeks:0.0}, " +
              "the projection stays a straight line");
        Console.WriteLine();

        if (prof.Samples == 0) { Console.WriteLine("no requests in the last 12 weeks — nothing to shape"); return; }

        PrintGrid(prof.P, numbers);
        Console.WriteLine($"mean {prof.Mean * 100:0}% of all hours active — {prof.Mean * 168:0.0}h in a typical week");

        int best = 0;
        for (int b = 1; b < ActivityProfile.Buckets; b++) if (prof.P[b] > prof.P[best]) best = b;
        Console.WriteLine($"busiest bucket: {(DayOfWeek)(best / 24)} {best % 24:00}:00 ({prof.P[best] * 100:0}%)");

        // The accessor T87 actually spends quota against, exercised on real spans so a bad grid shows
        // up as a bad number here rather than as a wrong marker on the weekly chart.
        DateTime nowLocal = nowUtc.ToLocalTime();
        Console.WriteLine();
        Console.WriteLine("expected active hours ahead:");
        Console.WriteLine($"  rest of today  {prof.ExpectedActiveHours(nowLocal, nowLocal.Date.AddDays(1)),5:0.0}h");
        Console.WriteLine($"  next 24h       {prof.ExpectedActiveHours(nowLocal, nowLocal.AddDays(1)),5:0.0}h");
        Console.WriteLine($"  next 7 days    {prof.ExpectedActiveHours(nowLocal, nowLocal.AddDays(7)),5:0.0}h");

        if (flags.Contains("--measured")) PrintMeasuredActivity(prof, nowLocal, numbers);
    }

    // The same week, measured instead of inferred: built from the folded hourly aggregate
    // (HourlyUsage) rather than from transcript timestamps, and diffed against the inferred grid.
    // Two independent sources agreeing is the only real evidence the shape is right; where they
    // disagree, the measured one is the ground truth (it is what the rate limit itself recorded).
    private static void PrintMeasuredActivity(ActivityProfile prof, DateTime nowLocal, bool numbers)
    {
        Console.WriteLine();
        var (measured, weeks, days) = HourlyUsage.MeasuredProfile(nowLocal);
        Console.WriteLine($"Measured profile — {days} folded days, {weeks:0.0} weeks");
        Console.WriteLine("store: " + HourlyUsage.FilePath);

        if (days == 0)
        {
            Console.WriteLine();
            Console.WriteLine("nothing folded yet — days are folded out of usage-history.jsonl as they age past");
            Console.WriteLine("its 8-day retention, so this fills in from about a week of running the tray.");
            Console.WriteLine("(`--activity --fold` folds every complete day currently in the raw log now.)");
            return;
        }

        Console.WriteLine();
        PrintGrid(measured, numbers);

        // Agreement over buckets the measurement can actually speak to: an hour never covered by a
        // reading is unknown, and scoring it as a disagreement would just penalise having been offline.
        double sum = 0, worst = 0;
        int worstBucket = 0, counted = 0, wide = 0;
        for (int b = 0; b < ActivityProfile.Buckets; b++)
        {
            if (measured[b] <= 0 && prof.P[b] <= 0) continue;
            double diff = Math.Abs(measured[b] - prof.P[b]);
            sum += diff; counted++;
            if (diff > 0.5) wide++;
            if (diff > worst) { worst = diff; worstBucket = b; }
        }
        Console.WriteLine($"agreement with the inferred grid: mean |Δ| {(counted > 0 ? sum / counted : 0) * 100:0}pp " +
                          $"over {counted} buckets, {wide} disagree by more than 50pp");
        if (weeks < ActivityProfile.ConfidentWeeks)
            Console.WriteLine($"  (only {weeks:0.0} weeks folded — with about one observation per bucket the measured " +
                              "grid is still all-or-nothing, so a wide |Δ| here is sample size, not conflict)");
        if (counted > 0)
            Console.WriteLine($"widest gap: {(DayOfWeek)(worstBucket / 24)} {worstBucket % 24:00}:00 — " +
                              $"inferred {prof.P[worstBucket] * 100:0}%, measured {measured[worstBucket] * 100:0}%");
    }

    // One 7×24 heatmap: Monday-first, one character per hour, with each day's expected active hours.
    private static void PrintGrid(double[] p, bool numbers)
    {
        // Monday-first: a work week reads as one block instead of being split across the two ends.
        DayOfWeek[] days = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                             DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        // One character per hour in shade mode, so the week reads as a heatmap rather than as a
        // sparse table; the hour scale then needs two header rows (tens over units).
        int cell = numbers ? 4 : 1;
        Console.Write("     ");
        for (int h = 0; h < 24; h++) Console.Write((h >= 10 ? (h / 10).ToString() : " ").PadLeft(cell));
        Console.WriteLine();
        Console.Write("     ");
        for (int h = 0; h < 24; h++) Console.Write((h % 10).ToString().PadLeft(cell));
        Console.WriteLine("   active");

        foreach (DayOfWeek d in days)
        {
            Console.Write($" {d.ToString()[..3]} ");
            double dayHours = 0;
            for (int h = 0; h < 24; h++)
            {
                double v = p[ActivityProfile.Index(d, h)];
                dayHours += v;
                Console.Write((numbers ? $"{v * 100:0}" : Shade(v).ToString()).PadLeft(cell));
            }
            Console.WriteLine($"   {dayHours,5:0.0}h");
        }

        Console.WriteLine();
        if (!numbers)
            Console.WriteLine("legend: ' ' idle   · <10%   ░ <35%   ▒ <65%   ▓ <90%   █ ≥90%   (p = share of weeks active)");
    }

    // Five steps plus blank: enough to see the shape of a week, few enough that a glance reads it.
    private static char Shade(double p) => p switch
    {
        < 0.005 => ' ',
        < 0.10 => '·',
        < 0.35 => '░',
        < 0.65 => '▒',
        < 0.90 => '▓',
        _ => '█',
    };

    // Headless report for the context scanner — the CLI half of the Context Load Inspector, the same
    // role `--insights` plays for UsageInsights: the whole model is validated here before any XAML.
    // Flags: a project slug or directory name to drill into one project, `--all` for every project,
    // `--calibrate` for the estimate-vs-measured fit, `--no-cache` to force a cold scan.
    private static void PrintContext(string[] flags)
    {
        bool all = flags.Contains("--all");
        bool calibrate = flags.Contains("--calibrate");
        bool noCache = flags.Contains("--no-cache");
        string? filter = flags.FirstOrDefault(f => !f.StartsWith("--"));

        // The report is full of "≈" — without this the console renders every estimate as a
        // replacement character on a non-UTF-8 codepage (cmd.exe defaults to 850 here).
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* redirected output */ }
        // A developer report, read alongside the source: invariant numbers throughout, so
        // "2.79 chars/token" can't render as "2,79" and read like a thousands separator. Safe to set
        // process-wide here — this command prints and exits, it never reaches the localized UI.
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        // `--root <dir>` points the scan at a stand-in for ~/.claude. It exists so the scanner can be
        // exercised against a fixture tree (imports, cycles, orphans, a zero-state project) instead
        // of only against whatever the dev machine happens to contain.
        int rootAt = Array.IndexOf(flags, "--root");
        string? root = rootAt >= 0 && rootAt + 1 < flags.Length ? flags[rootAt + 1] : null;
        if (root != null) filter = flags.FirstOrDefault(f => !f.StartsWith("--") && f != root);

        // `--sample` builds a throwaway ~/.claude lookalike and scans that instead: every rule fires
        // on demand, and a screenshot taken from it carries no real project names.
        if (flags.Contains("--sample"))
        {
            try
            {
                root = ContextFixture.Build(DateTimeOffset.UtcNow.UtcDateTime);
                Console.WriteLine("sample fixture: " + root);
                Console.WriteLine();
            }
            catch (Exception e)
            {
                Console.WriteLine("error building the sample fixture: " + e.Message);
                return;
            }
        }

        var scan = ContextScanner.Scan(DateTimeOffset.UtcNow.UtcDateTime, new ContextScanner.Options
        {
            UseCache = !noCache && root == null,   // a fixture scan must never poison the real cache
            ClaudeRoot = root ?? ContextScanner.DefaultClaudeRoot,
        });
        if (scan.Error != null) { Console.WriteLine("error: " + scan.Error); return; }

        Console.WriteLine($"Context load — {scan.Projects.Count} projects, {scan.FilesWalked} files, " +
                          $"{scan.ElapsedMs:0}ms {(scan.FromCache ? "(cached)" : "(fresh scan)")}");
        if (scan.Truncated)
            Console.WriteLine("  ! the walk hit its file/directory cap — totals below are a floor, not a total");
        Console.WriteLine();

        // `--usage` is the evidence: which skills and agents were actually invoked, mined from the
        // transcripts. The one number that turns "trim your skills" into a decision.
        if (flags.Contains("--usage"))
        {
            PrintUsage(scan, root);
            return;
        }

        // `--prompt` prints the same cleanup prompt the window copies to the clipboard, so what gets
        // handed to Claude is inspectable (and pipeable) rather than only visible in a paste.
        if (flags.Contains("--prompt"))
        {
            PrintPrompt(scan, root, filter);
            return;
        }

        // `--check` is the advisor rather than the measurement: findings grouped by severity, each
        // with the concrete fix. It replaces the table because the point is what to do, not what is.
        if (flags.Contains("--check"))
        {
            PrintFindings(scan, root);
            return;
        }

        // Shared sources first: these load for every project, so they belong to no project and to
        // all of them. Counting them once here is what keeps a cross-project total honest.
        Console.WriteLine($"Shared (every session, every project) — eager {TokenEstimate.Format(scan.SharedEagerTokens)}:");
        // The 31 plugin skills are always folded here: they are the same list for every project, so
        // reading them one by one belongs to a skills view, not to a project's breakdown.
        PrintSources(scan.Shared, groupSkills: !flags.Contains("--skills"));
        Console.WriteLine();

        if (filter != null)
        {
            ContextProject? p = scan.Projects.FirstOrDefault(x =>
                x.Slug.Equals(filter, StringComparison.OrdinalIgnoreCase) ||
                x.Name.Equals(filter, StringComparison.OrdinalIgnoreCase) ||
                x.ShortPath.Equals(filter, StringComparison.OrdinalIgnoreCase) ||
                x.Path.Equals(filter, StringComparison.OrdinalIgnoreCase));
            if (p == null) { Console.WriteLine($"no project matching '{filter}'"); return; }
            PrintProjectDetail(scan, p);
            return;
        }

        // The summary table: one line per project, sorted by what it costs every session.
        Console.WriteLine($"{"project",-34} {"eager",8} {"observed",9} {"delta",8} {"src",4} {"mem",4}  state");
        foreach (ContextProject p in scan.Projects)
        {
            int est = scan.EstimatedSessionZero(p);
            string observed = p.Observed is { } o ? TokenEstimate.Format(o.Median) : "—";
            string delta = p.Observed is { } o2 ? $"{o2.Median - est:+#;-#;0}" : "—";
            int memory = p.Sources.Count(s => s.Kind is ContextKind.MemoryFile or ContextKind.MemoryIndex);
            Console.WriteLine($"{Clip(p.ShortPath, 34),-34} {TokenEstimate.Format(est),8} {observed,9} " +
                              $"{delta,8} {p.Sources.Count,4} {memory,4}  {StateWord(p)}{(p.Truncated ? " !capped" : "")}");
        }

        Console.WriteLine();
        long bytes = scan.Shared.Sum(s => s.Bytes) + scan.Projects.Sum(p => p.Bytes);
        Console.WriteLine($"footprint: {Kb(bytes)} across {scan.Projects.Sum(p => p.Sources.Count) + scan.Shared.Count} files");
        Console.WriteLine($"heaviest eager: {string.Join(", ", scan.Projects.Take(3).Select(p => $"{p.ShortPath} {TokenEstimate.Format(scan.EstimatedSessionZero(p))}"))}");
        int orphans = scan.Projects.Count(p => p.State != PathState.Resolved);
        if (orphans > 0) Console.WriteLine($"unresolved project dirs: {orphans} (see 'state' column)");

        if (all)
            foreach (ContextProject p in scan.Projects)
            {
                Console.WriteLine();
                PrintProjectDetail(scan, p);
            }

        if (calibrate) PrintCalibration(scan);
    }

    private static void PrintProjectDetail(ContextScan scan, ContextProject p)
    {
        int est = scan.EstimatedSessionZero(p);
        Console.WriteLine($"=== {p.Slug}");
        Console.WriteLine($"    path: {(p.Path.Length > 0 ? p.Path : "(not a filesystem path)")}" +
                          $"  [{StateWord(p)}{(p.FromTranscript ? ", from transcript cwd" : "")}" +
                          $"{(p.Truncated ? ", nested walk capped" : "")}]");
        Console.WriteLine($"    session zero (estimated): {TokenEstimate.Format(est)} " +
                          $"= shared {TokenEstimate.Format(scan.SharedEagerTokens)} + project {TokenEstimate.Format(p.EagerTokens)}");
        if (p.Observed is { } o)
            Console.WriteLine($"    session zero (observed):  {TokenEstimate.Format(o.Median)} median of {o.Samples} " +
                              $"session{(o.Samples == 1 ? "" : "s")} ({TokenEstimate.Format(o.Min)}–{TokenEstimate.Format(o.Max)}), " +
                              $"{o.Model}, ≈${o.Cost:0.000} per session start");
        else
            Console.WriteLine($"    session zero (observed):  no fresh session in the window " +
                              $"({p.TranscriptsRead} transcript{(p.TranscriptsRead == 1 ? "" : "s")} checked)");
        PrintSources(p.Sources, groupSkills: false);
    }

    // One line per source: kind, eager/lazy, bytes, estimated tokens, the eager share of those
    // tokens, last-modified, label. Grouped by kind so the eager block reads first.
    private static void PrintSources(List<ContextSource> sources, bool groupSkills)
    {
        if (sources.Count == 0) { Console.WriteLine("    (none)"); return; }

        foreach (var group in sources
                     .GroupBy(s => s.Kind)
                     .OrderBy(g => (int)g.Key))
        {
            // The 31 plugin skills would drown the summary; fold them into one line unless asked.
            if (groupSkills && group.Key is ContextKind.Skill or ContextKind.Agent && group.Count() > 4)
            {
                Console.WriteLine($"    {group.Key,-20} {"index",-9} {Kb(group.Sum(s => s.Bytes)),9} " +
                                  $"{TokenEstimate.Format(group.Sum(s => s.Tokens)),8} " +
                                  $"{TokenEstimate.Format(group.Sum(s => s.EagerTokens)),8}  " +
                                  $"{group.Count()} entries (bodies lazy, descriptions eager)");
                continue;
            }

            foreach (ContextSource s in group.OrderByDescending(s => s.EagerTokens).ThenByDescending(s => s.Bytes))
                Console.WriteLine($"    {s.Kind,-20} {Mode(s),-9} {Kb(s.Bytes),9} " +
                                  $"{TokenEstimate.Format(s.Tokens),8} {TokenEstimate.Format(s.EagerTokens),8}  " +
                                  $"{s.ModifiedUtc.ToLocalTime():yyyy-MM-dd}  {Clip(s.Label, 44)}" +
                                  $"{(s.Note != null ? "  ! " + s.Note : "")}");
        }
    }

    // `--context-report <file.md>`: scan, evaluate, mine the evidence, and write it all out as one
    // markdown document. Everything it prints about itself (path, size) is so the caller knows what it
    // got without opening the file.
    private static void WriteContextReport(string path)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* redirected output */ }
        DateTime now = DateTimeOffset.UtcNow.UtcDateTime;

        ContextScan scan = ContextScanner.Scan(now);
        if (scan.Error != null) { Console.WriteLine("error: " + scan.Error); return; }

        UsageEvidence evidence = ContextUsage.Compute(now);
        List<Finding> findings = ContextRules.Evaluate(scan, now, evidence);
        var debt = scan.Projects.ToDictionary(p => p.Slug, p => ContextRules.Debt(scan, p, findings));
        int baseTokens = ContextScanner.Calibrate(scan) is { Base: > 0 } c
            ? (int)Math.Round(c.Base)
            : ContextScanner.FallbackBaseTokens;

        string markdown = ContextReport.Build(scan, findings, evidence, debt, baseTokens,
            DateTimeOffset.Now.LocalDateTime);
        try
        {
            string full = Path.GetFullPath(path);
            if (Path.GetDirectoryName(full) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
            File.WriteAllText(full, markdown);
            // Invariant, like the rest of the developer CLI: "18.9 KB" must not render as "18,9 KB".
            Console.WriteLine(FormattableString.Invariant(
                $"wrote {full} ({markdown.Length / 1024.0:0.#} KB, {findings.Count} findings)"));
        }
        catch (Exception e)
        {
            Console.WriteLine("error: " + e.Message);
        }
    }

    // The advisor's headless half (`--context --check`): every finding from ContextRules, grouped by
    // severity, each on two lines — what is true, then what to do about it. Verified against a whole
    // machine here before any of it reaches the window.
    private static void PrintFindings(ContextScan scan, string? root)
    {
        DateTime now = DateTimeOffset.UtcNow.UtcDateTime;
        // The evidence pass is what lets a "never invoked" finding exist at all; without it the rule
        // simply doesn't fire, which is the right failure mode.
        UsageEvidence evidence = ContextUsage.Compute(now, new ContextUsage.Options
        {
            ClaudeRoot = root ?? ContextScanner.DefaultClaudeRoot,
            UseCache = root == null,
        });
        Console.WriteLine($"usage evidence: {ContextUsage.Summary(evidence)}");
        Console.WriteLine();

        List<Finding> findings = ContextRules.Evaluate(scan, now, evidence);
        if (findings.Count == 0)
        {
            Console.WriteLine("no findings — nothing to advise on");
            return;
        }

        Console.WriteLine($"{findings.Count} findings: " + string.Join(", ",
            Enum.GetValues<RuleSeverity>()
                .Select(s => (severity: s, count: findings.Count(f => f.Severity == s)))
                .Where(x => x.count > 0)
                .Select(x => $"{x.count} {x.severity.ToString().ToLowerInvariant()}")));

        foreach (var group in findings.GroupBy(f => f.Severity).OrderBy(g => (int)g.Key))
        {
            Console.WriteLine();
            Console.WriteLine($"--- {group.Key.ToString().ToUpperInvariant()} ({group.Count()})");
            foreach (Finding f in group)
            {
                // Unclipped: a duplication finding's scope is every project in the cluster, and
                // Clip keeps the tail — it would hide the first project's name.
                Console.WriteLine($"  [{f.RuleId}] {f.Scope}");
                Console.WriteLine($"      {f.Message}");
                Console.WriteLine($"      fix: {f.Fix}");
            }
        }
    }

    // The cleanup prompt (`--context --prompt [project]`): exactly what the window's "Copy cleanup
    // prompt" button puts on the clipboard. Without a project name it is the machine-wide one.
    private static void PrintPrompt(ContextScan scan, string? root, string? filter)
    {
        DateTime now = DateTimeOffset.UtcNow.UtcDateTime;
        UsageEvidence evidence = ContextUsage.Compute(now, new ContextUsage.Options
        {
            ClaudeRoot = root ?? ContextScanner.DefaultClaudeRoot,
            UseCache = root == null,
        });
        List<Finding> all = ContextRules.Evaluate(scan, now, evidence);

        ContextProject? project = filter is { Length: > 0 }
            ? scan.Projects.FirstOrDefault(x =>
                x.Slug.Equals(filter, StringComparison.OrdinalIgnoreCase) ||
                x.Name.Equals(filter, StringComparison.OrdinalIgnoreCase) ||
                x.ShortPath.Equals(filter, StringComparison.OrdinalIgnoreCase))
            : null;

        if (filter is { Length: > 0 } && project == null)
        {
            Console.WriteLine($"no project matching '{filter}'");
            return;
        }

        List<Finding> findings = project is null
            ? all.Where(f => f.Scope == "~/.claude" || f.RuleId is "memory-duplicated"
                or "project-dir-dead" or "project-dir-dead-empty").ToList()
            : all.Where(f => f.Scope == project.ShortPath).ToList();

        string title = project is null
            ? ContextScanner.DefaultClaudeRoot
            : project.Path.Length > 0 ? project.Path : project.ShortPath;

        Console.WriteLine(ContextPrompt.Build(title, findings, Array.Empty<ContextSource>(), 0));
    }

    // The evidence report (`--context --usage`): every skill and agent the scan found, with how often
    // it was actually invoked over the last 90 days. Sorted so the expensive-and-unused come first —
    // that is the actionable end of the list.
    private static void PrintUsage(ContextScan scan, string? root)
    {
        DateTime now = DateTimeOffset.UtcNow.UtcDateTime;
        UsageEvidence evidence = ContextUsage.Compute(now, new ContextUsage.Options
        {
            ClaudeRoot = root ?? ContextScanner.DefaultClaudeRoot,
            UseCache = root == null,
        });

        Console.WriteLine($"Evidence of use over {evidence.WindowDays} days — {ContextUsage.Summary(evidence)}");
        if (evidence.Error != null) Console.WriteLine("  ! " + evidence.Error);
        Console.WriteLine("  memory files carry no usage annotation on purpose: a recall leaves no");
        Console.WriteLine("  structured trace, and only message content would show it (never read).");
        Console.WriteLine();

        // One row per distinct skill/agent. The same skill can be visible from several projects, so
        // they are folded by label — the eager cost is paid once per session either way.
        var entries = scan.Shared.Concat(scan.Projects.SelectMany(p => p.Sources))
            .Where(s => s.Kind is ContextKind.Skill or ContextKind.Agent)
            .GroupBy(s => s.Label, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(s => (source: s, stat: evidence.For(s), unused: evidence.CanReportZero(s)))
            .OrderBy(x => x.stat?.Total ?? 0)
            .ThenByDescending(x => x.source.EagerTokens)
            .ToList();

        Console.WriteLine($"{"skill / agent",-42} {"used 90d",9} {"30d",5} {"eager",7}  last used");
        foreach (var (source, stat, unused) in entries)
        {
            string used = stat is { Total: > 0 }
                ? stat.Total.ToString()
                : unused ? "never" : "—";
            string last = stat is { Total: > 0 } ? stat.LastUsedUtc.ToLocalTime().ToString("yyyy-MM-dd") : "";
            Console.WriteLine($"{Clip(source.Label, 42),-42} {used,9} {stat?.Recent ?? 0,5} " +
                              $"{TokenEstimate.Format(source.EagerTokens),7}  {last}");
        }

        int neverTokens = entries.Where(x => x.unused && (x.stat?.Total ?? 0) == 0)
            .Sum(x => x.source.EagerTokens);
        int neverCount = entries.Count(x => x.unused && (x.stat?.Total ?? 0) == 0);
        Console.WriteLine();
        if (neverCount > 0)
            Console.WriteLine($"{neverCount} never invoked in the last {evidence.WindowDays} days of transcripts, " +
                              $"costing {TokenEstimate.Format(neverTokens)} of eager index in every session");
        else
            Console.WriteLine("nothing is provably unused");
    }

    // The estimate-vs-measurement fit: proves the token numbers are a measurement with a correction,
    // not a guess. `Base` is what no filesystem scan can see (system prompt + tool definitions).
    private static void PrintCalibration(ContextScan scan)
    {
        Console.WriteLine();
        if (ContextScanner.Calibrate(scan) is not { } c)
        {
            Console.WriteLine("calibration: not enough projects with observed sessions (need 3+)");
            return;
        }

        Console.WriteLine($"calibration over {c.Points} projects with observed sessions:");
        Console.WriteLine($"  base overhead, invisible to any filesystem scan (system prompt, tool");
        Console.WriteLine($"  definitions, MCP schemas): {TokenEstimate.Format((int)c.Base)} median " +
                          $"(p25 {TokenEstimate.Format((int)c.BaseP25)}, p75 {TokenEstimate.Format((int)c.BaseP75)})");
        Console.WriteLine($"  measured chars/token, from {c.SlopePairs} project pairs: {c.CharsPerToken:0.00}  " +
                          $"(estimator uses {TokenEstimate.ProseCharsPerToken:0.00} prose / " +
                          $"{TokenEstimate.CodeCharsPerToken:0.00} code / {TokenEstimate.TableCharsPerToken:0.00} table)");
        Console.WriteLine($"  corrected estimate (scan + base) vs observed: within ±{ContextScanner.Calibration.BandPct:0}% " +
                          $"for {c.WithinBand}/{c.Points} projects, median error {c.MedianErrorPct:0.#}%, worst {c.WorstErrorPct:0.#}%");
        Console.WriteLine();
        Console.WriteLine($"  {"project",-34} {"chars",9} {"scanned",9} {"corrected",10} {"observed",9} {"err",7}");
        foreach (var p in c.Samples)
            Console.WriteLine($"  {Clip(p.Slug, 34),-34} {p.Chars,9:0} {TokenEstimate.Format(p.Estimated),9} " +
                              $"{TokenEstimate.Format(p.Corrected(c.Base)),10} {TokenEstimate.Format(p.Observed),9} " +
                              $"{p.ErrorPct(c.Base),6:0.#}%");
    }

    private static string Mode(ContextSource s) => s.Mode switch
    {
        LoadMode.Eager => "eager",
        // A skill/agent body is lazy, but its description is in the always-loaded index — the
        // "index" word marks exactly that, rather than pretending the whole file is free.
        LoadMode.Lazy => s.EagerTokens > 0 ? "index" : "lazy",
        _ => "not-loaded",
    };

    private static string StateWord(ContextProject p) => p.State switch
    {
        PathState.Resolved => "ok",
        PathState.Missing => "MISSING",
        _ => "not-a-path",
    };

    private static string Kb(long bytes) => bytes < 1024
        ? $"{bytes} B"
        : (bytes / 1024.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " KB";

    private static string Clip(string s, int max) => s.Length <= max ? s : "…" + s[^(max - 1)..];

    // Dev helper: show a reset toast with sample data so it can be previewed / screenshotted
    // standalone. Uses the same display strings as the live notifier. `variant`: "scheduled" (routine
    // weekly reset), "credit" (partial mid-window drop 91% → 50%), "session" (routine 5h session
    // reset), or anything else for the early weekly reset.
    private static void SimulateReset(string variant)
    {
        var app = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
        };
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // "context" is not a reset at all: it previews the context-load nudge, which shares the toast
        // card but not its festive framing (ochre, no confetti).
        if (variant.Equals("context", StringComparison.OrdinalIgnoreCase))
        {
            var nudge = new ToastWindow("📇", L.T("toast.context.title"),
                L.T("toast.context.subtitle", "viglet/turing/2026.2"),
                0.27, 0.27,
                L.T("toast.context.caption", TokenEstimate.Format(54_000), (0.336).ToString("0.000", L.Culture)),
                L.T("toast.context.quotaLabel"), ToastWindow.ToastTheme.Context);
            nudge.Closed += (_, _) => app.Shutdown();
            nudge.Show();
            app.Run();
            return;
        }
        var (key, ev) = variant.ToLowerInvariant() switch
        {
            "scheduled" => ("7d", new BurnTracker.ResetEvent(BurnTracker.ResetKind.Scheduled, 0.79, 0.0, now, now + 7 * 86400)),
            "credit" => ("7d", new BurnTracker.ResetEvent(BurnTracker.ResetKind.Credit, 0.91, 0.50, now + 4 * 86400, now + 4 * 86400)),
            "session" => ("5h", new BurnTracker.ResetEvent(BurnTracker.ResetKind.Scheduled, 0.88, 0.0, now, now + 5 * 3600)),
            _ => ("7d", new BurnTracker.ResetEvent(BurnTracker.ResetKind.Unexpected, 0.79, 0.0, now + 3 * 86400, now + 7 * 86400)),
        };
        var (emoji, title, subtitle, fromUsage, toUsage, caption, quotaLabel, theme) = TrayContext.ResetToastContent(key, ev, now);

        var toast = new ToastWindow(emoji, title, subtitle, fromUsage, toUsage, caption, quotaLabel, theme);
        toast.Closed += (_, _) => app.Shutdown();
        toast.Show();
        app.Run();
    }

    // Dev/preview helper: same sample data as SimulateReset, but instead of leaving the toast on
    // screen it waits for the entrance + bar-fill animations to settle, snapshots the card to a
    // transparent PNG (so it composites on any README/site background), and exits.
    private static void CaptureToast(string variant, string outPath)
    {
        var app = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
        };
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // The context-load nudge shares the card but is not a reset event, so it is built directly.
        if (variant.Equals("context", StringComparison.OrdinalIgnoreCase))
        {
            var nudge = new ToastWindow("📇", L.T("toast.context.title"),
                L.T("toast.context.subtitle", "viglet/turing/2026.2"),
                0.27, 0.27,
                L.T("toast.context.caption", TokenEstimate.Format(54_000), (0.336).ToString("0.000", L.Culture)),
                L.T("toast.context.quotaLabel"), ToastWindow.ToastTheme.Context);
            nudge.Show();
            var settleNudge = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1700),
            };
            settleNudge.Tick += (_, _) =>
            {
                settleNudge.Stop();
                try
                {
                    nudge.SaveSnapshot(System.IO.Path.GetFullPath(outPath));
                    Console.WriteLine("wrote " + System.IO.Path.GetFullPath(outPath));
                }
                finally { app.Shutdown(); }
            };
            settleNudge.Start();
            app.Run();
            return;
        }
        var (key, ev) = variant.ToLowerInvariant() switch
        {
            "scheduled" => ("7d", new BurnTracker.ResetEvent(BurnTracker.ResetKind.Scheduled, 0.79, 0.0, now, now + 7 * 86400)),
            "credit" => ("7d", new BurnTracker.ResetEvent(BurnTracker.ResetKind.Credit, 0.91, 0.50, now + 4 * 86400, now + 4 * 86400)),
            "session" => ("5h", new BurnTracker.ResetEvent(BurnTracker.ResetKind.Scheduled, 0.88, 0.0, now, now + 5 * 3600)),
            _ => ("7d", new BurnTracker.ResetEvent(BurnTracker.ResetKind.Unexpected, 0.79, 0.0, now + 3 * 86400, now + 7 * 86400)),
        };
        var (emoji, title, subtitle, fromUsage, toUsage, caption, quotaLabel, theme) = TrayContext.ResetToastContent(key, ev, now);

        var toast = new ToastWindow(emoji, title, subtitle, fromUsage, toUsage, caption, quotaLabel, theme);
        toast.Show();
        var settle = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1700), // entrance (420ms) + bar fill (550+900ms)
        };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            try { toast.SaveSnapshot(System.IO.Path.GetFullPath(outPath)); Console.WriteLine("wrote " + System.IO.Path.GetFullPath(outPath)); }
            finally { app.Shutdown(); }
        };
        settle.Start();
        app.Run();
    }

    // Dev helper: dump sample icons as PNG at real tray sizes for visual inspection.
    private static void RenderTest(string dir)
    {
        Directory.CreateDirectory(dir);
        (double pct, IconRenderer.State st, bool fl, Projection verdict)[] cases =
        {
            (0.08, IconRenderer.State.Ok, false, Projection.Unknown),
            (0.08, IconRenderer.State.Ok, false, Projection.Ok),
            (0.54, IconRenderer.State.Ok, false, Projection.Danger),
            (1.00, IconRenderer.State.Ok, true, Projection.Danger),
        };
        foreach (int size in new[] { 16, 20, 32 })
            foreach (var (pct, st, fl, verdict) in cases)
                using (var bmp = IconRenderer.Render(pct, st, fl, size, verdict))
                    bmp.Save(Path.Combine(dir, $"icon_{(int)(pct * 100)}_{size}.png"));

        // Logo, plain and with the API-error badge (the 403 state), at real + large sizes.
        foreach (int size in new[] { 16, 20, 32, 128 })
        {
            using (var plain = IconRenderer.RenderLogo(size))
                plain.Save(Path.Combine(dir, $"logo_{size}.png"));
            using (var err = IconRenderer.RenderLogo(size, errorBadge: true))
                err.Save(Path.Combine(dir, $"logo_error_{size}.png"));
        }
        Console.WriteLine("rendered to " + Path.GetFullPath(dir));
    }

    // Dev helper: a hand-built pacing report with a data gap in the session curve, so the "unavailable"
    // outage rendering (red dashed span along the usage line) can be inspected deterministically.
    private static PaceReport BuildGapDemoReport(long now)
    {
        var r = new PaceReport { ComputedLocal = DateTimeOffset.FromUnixTimeSeconds(now).LocalDateTime };

        var s = r.Session;
        s.WindowSeconds = 5 * 3600;
        s.ResetUnix = now + 2 * 3600;
        s.SecondsToReset = 2 * 3600;
        s.ElapsedSeconds = 3 * 3600;
        s.Util = 0.72;
        s.HasWindow = true;
        s.Verdict = PaceVerdict.Ahead;
        s.ExhaustSeconds = s.ElapsedSeconds * (1 - s.Util) / s.Util;
        s.InputTokens = 200_000; s.OutputTokens = 560_000; s.CacheCreationTokens = 4_200_000;
        // A past outage in the middle of the window that has since recovered: readings stop at 32%,
        // resume at 48%, and continue normally to "now" (60%). The gap segment is drawn red.
        s.Curve = new() { (0, 0), (0.08, 0.10), (0.16, 0.18), (0.24, 0.26), (0.32, 0.32), (0.48, 0.50), (0.54, 0.60), (0.60, 0.72) };
        s.Gaps = new() { (0.32, 0.32, 0.48, 0.50) };   // no reading logged from 32% to 48% (the outage)

        var w = r.Weekly;
        w.WindowSeconds = 7 * 86400;
        w.ResetUnix = now + 3 * 86400;
        w.SecondsToReset = 3 * 86400;
        w.ElapsedSeconds = 4 * 86400;
        w.Util = 0.38;
        w.HasWindow = true;
        w.Verdict = PaceVerdict.Adequate;
        w.ExhaustSeconds = w.ElapsedSeconds * (1 - w.Util) / w.Util;
        w.InputTokens = 900_000; w.OutputTokens = 2_100_000; w.CacheCreationTokens = 18_000_000;
        w.Curve = new() { (0, 0), (0.14, 0.09), (0.29, 0.17), (0.43, 0.26), (0.571, 0.38) };

        return r;
    }

    // Dev helper: build a multi-resolution .ico for the app (PNG-compressed entries, valid on
    // Windows Vista+) from the GDI+ logo renderer.
    private static void MakeIcon(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        byte[][] pngs = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            using Bitmap bmp = IconRenderer.RenderLogo(sizes[i]);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngs[i] = ms.ToArray();
        }

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((short)0);              // reserved
        bw.Write((short)1);              // type: icon
        bw.Write((short)sizes.Length);   // image count

        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            bw.Write((byte)(s >= 256 ? 0 : s)); // width (0 = 256)
            bw.Write((byte)(s >= 256 ? 0 : s)); // height
            bw.Write((byte)0);                  // palette
            bw.Write((byte)0);                  // reserved
            bw.Write((short)1);                 // color planes
            bw.Write((short)32);                // bits per pixel
            bw.Write(pngs[i].Length);           // image size in bytes
            bw.Write(offset);                   // image offset
            offset += pngs[i].Length;
        }
        foreach (byte[] png in pngs) bw.Write(png);

        Console.WriteLine("wrote " + Path.GetFullPath(path));
    }
}

internal sealed class TrayContext : ApplicationContext
{
    private static readonly string[] Metrics = { "5h", "7d", "extra" };
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["5h"] = L.T("menu.metric.5h"), ["7d"] = L.T("menu.metric.7d"), ["extra"] = L.T("menu.metric.extra"),
    };

    private readonly NotifyIcon _tray;
    private readonly ApiClient _api = new();
    private readonly BurnTracker _burn = new();
    private readonly Updater _updater = new();
    private readonly Settings _settings = Settings.Load();
    private volatile InsightsData? _insights;
    private readonly System.Windows.Forms.Timer _poll = new(); // interval set from settings
    private readonly System.Windows.Forms.Timer _flash = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _updateCheck = new() { Interval = 21_600_000 }; // 6 h
    // Context is a weekly-scale phenomenon, so it is sampled four times a day rather than per poll.
    // This also keeps the drift history (ContextHistory) accumulating without the window being opened.
    private readonly System.Windows.Forms.Timer _contextCheck = new() { Interval = 21_600_000 }; // 6 h
    private readonly List<ToolStripMenuItem> _metricItems = new();
    private ToolStripMenuItem _updateItem = null!;

    private UsageData? _data;
    private DateTime? _lastRefresh;
    // The last successful live reading, kept so the Statistics window can still draw its charts from
    // local data (usage-history + transcripts) during an API outage. The chart marks the unavailable
    // stretch itself, from the gap in the logged readings (see UsageReport.WindowPace.Gaps).
    private PaceSnapshot? _lastGoodSnapshot;
    private UpdateInfo? _update;
    private string _metric;
    private bool _flashOn;
    private bool _updating;
    private bool _autoOpenedForAuth; // guards auto-open so we launch once per signed-out spell
    private int _consecutiveErrors;  // transient failures since the last good poll
    private IntPtr _iconHandle = IntPtr.Zero;

    private const int ErrorTolerance = 2; // transient blips to ride out before showing an error

    // At/above this utilization (0–1) a window is treated as maxed out — usage is blocked until it
    // resets, so the poll loop idles to the reset instead of hammering the API. Matches the 0.995
    // "at limit" threshold used elsewhere (the projection/ExhaustSeconds logic).
    private const double AtLimitThreshold = 0.995;
    private const int LimitResetBufferSeconds = 20; // wait past the predicted reset before double-checking
    private const int LimitDoubleCheckSeconds = 30; // retry cadence once a reset is due but not yet observed

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public TrayContext()
    {
        _metric = _settings.Metric; // restore the last-selected window (BuildMenu reads it)
        _tray = new NotifyIcon
        {
            Visible = true,
            Text = L.T("tip.connecting"),
            ContextMenuStrip = BuildMenu(),
        };
        Render(); // initial "connecting" icon

        // A left-click on the tray icon opens the Statistics window (the pace report). MouseClick with
        // a left-button filter keeps the right-click context menu untouched.
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenStatistics(); };

        _poll.Interval = _settings.RefreshSeconds * 1000;
        _poll.Tick += async (_, _) => await RefreshAsync();
        _poll.Start();
        _flash.Tick += (_, _) => { if (_settings.FlashNearLimit && CurrentPct() >= Settings.FlashWarnThreshold) { _flashOn = !_flashOn; Render(); } };
        _flash.Start();
        _updateCheck.Tick += async (_, _) => await CheckForUpdateAsync();
        _updateCheck.Start();
        _contextCheck.Tick += (_, _) => ScanContextInBackground();
        _contextCheck.Start();

        _tray.BalloonTipClicked += (_, _) => { if (_update != null) _ = ApplyUpdateAsync(); };

        _ = RefreshAsync(); // fire first fetch immediately
        _ = CheckForUpdateAsync(); // look for a newer release on launch
        RecomputeInsights(); // build the 24h usage breakdown in the background
        ScanContextInBackground(); // record context drift, and nudge if the user asked to be nudged
    }

    /// <summary>
    /// Scan the context sources off the UI thread, record the drift sample, and — only if the user
    /// opted in — nudge once when a project crosses their threshold. The scan is cached on a
    /// path+size+mtime fingerprint, so a warm pass is ~100ms of stats and nothing else.
    /// </summary>
    private async void ScanContextInBackground()
    {
        // The scan runs on the pool; the toast has to be created back on the STA thread that owns the
        // message pump, which is what awaiting here buys us (the same shape as RefreshAsync).
        (ContextProject project, int eager)? nudge = await Task.Run(() =>
        {
            try
            {
                DateTime now = DateTimeOffset.UtcNow.UtcDateTime;
                ContextScan scan = ContextScanner.Scan(now);
                if (scan.Error != null) return ((ContextProject, int)?)null;

                ContextHistory.Record(scan, now);
                if (!_settings.NotifyOnContextGrowth) return null;

                foreach (ContextProject p in scan.Projects)
                {
                    int eager = scan.EstimatedSessionZero(p);
                    if (eager < _settings.ContextNudgeTokens) continue;
                    if (!ContextNudges.ShouldNotify(p.Slug, now)) continue;

                    ContextNudges.Mark(p.Slug, now);
                    return (p, eager);   // one nudge per pass, never a burst of them
                }
                return null;
            }
            catch { return null; }   // a background nudge is never worth disturbing the app over
        });

        if (nudge is { } n) NudgeContext(n.project, n.eager);
    }

    // The nudge itself, in the existing toast style: same card, same bar, ochre instead of festive,
    // and no confetti (see ToastWindow.OnLoaded).
    private void NudgeContext(ContextProject project, int eager)
    {
        int window = ContextScanner.ContextWindowFor(project.Observed?.Model);
        double share = Math.Clamp((double)eager / window, 0, 1);
        double cost = eager * UsageInsights.Price(project.Observed?.Model ?? "").cw / 1_000_000.0;

        string title = L.T("toast.context.title");
        string subtitle = L.T("toast.context.subtitle", project.ShortPath);
        string caption = L.T("toast.context.caption",
            TokenEstimate.Format(eager), cost.ToString("0.000", L.Culture));
        try
        {
            EnsureWpfApp();
            new ToastWindow("📇", title, subtitle, share, share, caption,
                L.T("toast.context.quotaLabel"), ToastWindow.ToastTheme.Context).Show();
        }
        catch
        {
            _tray.BalloonTipTitle = title;
            _tray.BalloonTipText = $"{subtitle} — {caption}";
            _tray.BalloonTipIcon = ToolTipIcon.Info;
            _tray.ShowBalloonTip(10_000);
        }
    }

    // Scan local transcripts off the UI thread; the submenu reads the cached result.
    private void RecomputeInsights()
        => _ = Task.Run(() => _insights = UsageInsights.Compute(DateTimeOffset.UtcNow.UtcDateTime));

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var showOn = new ToolStripMenuItem(L.T("menu.showOnIcon"));
        foreach (string key in Metrics)
        {
            var item = new ToolStripMenuItem(Labels[key]) { Tag = key, Checked = key == _metric };
            item.Click += (_, _) => SetMetric((string)item.Tag!);
            _metricItems.Add(item);
            showOn.DropDownItems.Add(item);
        }
        menu.Items.Add(showOn);

        var insights = new ToolStripMenuItem(L.T("menu.insights"));
        insights.DropDownOpening += (_, _) => PopulateInsights(insights);
        insights.DropDownItems.Add(new ToolStripMenuItem(L.T("insights.loading")) { Enabled = false });
        menu.Items.Add(insights);

        // Opens the pacing report: 5h-session and 7d-week usage vs. the clock, with a projection.
        var stats = new ToolStripMenuItem(L.T("menu.statistics"));
        stats.Click += (_, _) => OpenStatistics();
        menu.Items.Add(stats);

        // Opens the Context Load window: what every session in a project costs before the first
        // prompt — the instruction chain, the memory index and the skill/agent index.
        var context = new ToolStripMenuItem(L.T("menu.context"));
        context.Click += (_, _) => OpenContext();
        menu.Items.Add(context);

        var refresh = new ToolStripMenuItem(L.T("menu.refreshNow"));
        refresh.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refresh);

        // Launches the Claude Code CLI so it can refresh the OAuth token in
        // ~/.claude/.credentials.json — the recovery path when a poll hits HTTP 401.
        var openClaude = new ToolStripMenuItem(L.T("menu.openClaude"));
        openClaude.Click += (_, _) => OpenClaudeCode();
        menu.Items.Add(openClaude);

        // Hidden until a newer release is found; then shows "Update to vX.Y.Z".
        _updateItem = new ToolStripMenuItem(L.T("menu.updateAvailable")) { Visible = false, Font = new Font(menu.Font, FontStyle.Bold) };
        _updateItem.Click += (_, _) => { if (!_updating) _ = ApplyUpdateAsync(); };
        menu.Items.Add(_updateItem);

        var settings = new ToolStripMenuItem(L.T("menu.settings"));
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);

        menu.Items.Add(new ToolStripSeparator());

        var quit = new ToolStripMenuItem(L.T("menu.quit"));
        quit.Click += (_, _) => ExitApp();
        menu.Items.Add(quit);

        return menu;
    }

    private void SetMetric(string key)
    {
        _metric = key;
        foreach (var it in _metricItems)
            it.Checked = (string)it.Tag! == key;
        _settings.Metric = key;
        try { _settings.Save(); } catch { /* non-fatal: selection still applies this session */ }
        Render();
    }

    // The settings and statistics windows are shown non-modally; keep references so we reuse an open
    // one instead of stacking duplicates.
    private SettingsWindow? _settingsWindow;
    private StatisticsWindow? _statsWindow;
    private ContextWindow? _contextWindow;

    // A WPF Application must exist before any WPF window (Settings, the reset toast) is shown on this
    // thread, for the Fluent theme and pack-URI resources to resolve. Hosted as a single instance for
    // the process lifetime but never Run() — the WinForms message loop (Application.Run(TrayContext))
    // already pumps messages for both UI stacks on this thread.
    private static System.Windows.Application? _wpfApp;

    private void EnsureWpfApp()
    {
        if (_wpfApp is null && System.Windows.Application.Current is null)
            _wpfApp = new System.Windows.Application
            {
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
            };
    }

    // Open the settings window (non-modal); on Save it calls ApplySettings to persist and apply.
    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == System.Windows.WindowState.Minimized)
                _settingsWindow.WindowState = System.Windows.WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        EnsureWpfApp();

        _settingsWindow = new SettingsWindow(_settings, ApplySettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    // Open the Statistics window (non-modal) with the current live usage as its snapshot, so the pace
    // report reflects the same numbers the icon does. Reuses an already-open window.
    private void OpenStatistics()
    {
        if (_statsWindow is not null)
        {
            if (_statsWindow.WindowState == System.Windows.WindowState.Minimized)
                _statsWindow.WindowState = System.Windows.WindowState.Normal;
            _statsWindow.Activate();
            return;
        }

        EnsureWpfApp();

        // Only pass a snapshot when we have a good reading; otherwise the window shows a "connect" hint,
        // or — on a live API error like a 403 — the API's own message so it isn't a blank page.
        _statsWindow = new StatisticsWindow(CurrentSnapshot(), _settings.ShowRemaining, CurrentError());
        _statsWindow.Closed += (_, _) => _statsWindow = null;
        _statsWindow.Show();
        _statsWindow.Activate();
    }

    // Open the Context Load window (non-modal): the per-project eager/lazy breakdown of everything
    // Claude Code loads before the first prompt. It scans on open, on its own background thread, so
    // there is nothing to hand it here. Reuses an already-open window.
    private void OpenContext()
    {
        if (_contextWindow is not null)
        {
            if (_contextWindow.WindowState == System.Windows.WindowState.Minimized)
                _contextWindow.WindowState = System.Windows.WindowState.Normal;
            _contextWindow.Activate();
            return;
        }

        EnsureWpfApp();

        _contextWindow = new ContextWindow();
        _contextWindow.Closed += (_, _) => _contextWindow = null;
        _contextWindow.Show();
        _contextWindow.Activate();
    }

    // The reading the Statistics window charts from: the live one when healthy, otherwise the last good
    // reading this session (so the charts stay populated from local data during an API outage), and
    // failing that the last reading persisted on disk — so a signed-out / expired-token launch still
    // draws the charts from yesterday's history instead of a blank "connect" hint. Null only when we've
    // genuinely never logged a reading (first run), which keeps that hint for a true cold start.
    private PaceSnapshot? CurrentSnapshot()
    {
        if (_data is { Error: null } d)
            return new PaceSnapshot(d.Session5h, d.Reset5h, d.Week7d, d.Reset7d);
        if (_lastGoodSnapshot is { } s)
            return s;
        return UsageHistory.Latest() is { } h
            ? new PaceSnapshot(h.Util5h, h.Reset5h, h.Util7d, h.Reset7d)
            : null;
    }

    // The live API error to surface in the Statistics window, if any. A signed-out (401) state is left
    // to the window's own "connect" hint; only a real API error (e.g. a 403 payment-past-due) is passed
    // through so the window shows the reason instead of appearing blank.
    private string? CurrentError()
        => _data is { Error: { } e, Unauthorized: false } ? e : null;

    // Persist the edited settings and apply the new values immediately.
    private void ApplySettings(Settings updated)
    {
        _settings.RefreshSeconds = updated.RefreshSeconds;
        _settings.ShowPercentage = updated.ShowPercentage;
        _settings.ShowRemaining = updated.ShowRemaining;
        _settings.FlashNearLimit = updated.FlashNearLimit;
        // Clear any in-progress flash frame so turning the setting off calms the icon on the next
        // Render() below, rather than leaving it stuck on the deep-slate half of the blink.
        if (!_settings.FlashNearLimit) _flashOn = false;
        _settings.NotifyOnUnexpectedReset = updated.NotifyOnUnexpectedReset;
        _settings.NotifyOnScheduledReset = updated.NotifyOnScheduledReset;
        _settings.NotifyOnSessionReset = updated.NotifyOnSessionReset;
        _settings.NotifyOnContextGrowth = updated.NotifyOnContextGrowth;
        _settings.ContextNudgeTokens = updated.ContextNudgeTokens;
        _settings.SessionResetMinPercent = updated.SessionResetMinPercent;
        _settings.ScheduledResetMinPercent = updated.ScheduledResetMinPercent;
        _settings.ClaudeCodeDirectory = updated.ClaudeCodeDirectory;
        _settings.AutoOpenOnUnauthenticated = updated.AutoOpenOnUnauthenticated;
        _settings.AuthRetrySeconds = updated.AuthRetrySeconds;

        // A language change only takes effect after a restart (localized strings resolve when a window
        // is parsed), so note whether the *active* language would change before saving the new value.
        bool languageChanged = L.Resolve(updated.Language) != L.Current;
        _settings.Language = updated.Language;

        try { _settings.Save(); }
        catch (Exception ex)
        {
            MessageBox.Show(L.T("dialog.saveFailed", ex.Message),
                L.T("dialog.appName"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Re-arm the poll cadence for the current auth state (handles a changed interval, retry
        // cadence, or a freshly enabled auto-open) and reflect a show-percentage change.
        AdjustForAuthState();
        Render();

        // If the Statistics window is open, flip its used/remaining framing to match right away.
        _statsWindow?.SetShowRemaining(_settings.ShowRemaining);

        // Offer to restart so the new language applies immediately; the saved preference is read back
        // on the next launch either way.
        if (languageChanged && MessageBox.Show(L.T("dialog.restartForLanguage"), L.T("dialog.appName"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            RestartApp();
    }

    // Relaunch the app to pick up a setting that only applies at startup (the UI language). Spawns a
    // detached shell that waits a beat for this instance to exit — releasing the single-instance mutex —
    // then starts a fresh copy, and quits immediately.
    private void RestartApp()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe is { Length: > 0 })
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    // `ping -n 2` (~1s) delays reliably even with no console — unlike `timeout`, which
                    // aborts when stdin is redirected. `start ""` launches the new copy detached so cmd
                    // exits right after instead of lingering for the whole session.
                    Arguments = $"/c ping -n 2 127.0.0.1 >nul & start \"\" \"{exe}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                });
        }
        catch { /* if the relaunch can't be scheduled, the user reopens it manually */ }
        ExitApp();
    }

    // Fill the "Usage insights" submenu from the cached scan; trigger a refresh for next time.
    private void PopulateInsights(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();
        InsightsData? d = _insights;

        if (d == null)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem(L.T("insights.computing")) { Enabled = false });
        }
        else if (d.Error != null)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem(L.T("insights.unavailable", d.Error)) { Enabled = false });
        }
        else if (d.Requests == 0)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem(L.T("insights.none")) { Enabled = false });
        }
        else
        {
            void Line(string text) => parent.DropDownItems.Add(new ToolStripMenuItem(text) { Enabled = false });

            Line(L.T("insights.summary", d.Requests, d.Sessions));
            Line(L.T("insights.subagents", Pct(d.SubagentPct)));
            Line(L.T("insights.heavyContext", Pct(d.HeavyContextPct)));
            if (d.ByModel.Count > 0)
            {
                parent.DropDownItems.Add(new ToolStripSeparator());
                Line(L.T("insights.byModel"));
                foreach (var (model, pct) in d.ByModel.Take(5))
                    Line($"   {model}: {Pct(pct)}");
            }
        }

        parent.DropDownItems.Add(new ToolStripSeparator());
        var refresh = new ToolStripMenuItem(L.T("insights.recompute"));
        refresh.Click += (_, _) => RecomputeInsights();
        parent.DropDownItems.Add(refresh);

        // Keep the cache reasonably fresh for the next open.
        RecomputeInsights();
    }

    private async Task RefreshAsync()
    {
        UsageData fresh = await _api.FetchAsync();
        bool ok = fresh.Error == null;
        bool transientHiccup = !ok && fresh.Transient && !fresh.Unauthorized;
        _consecutiveErrors = transientHiccup ? _consecutiveErrors + 1 : 0;

        // A single timeout or network blip shouldn't flip the icon to a scary red error: keep the
        // last good reading (or the "connecting…" state) on screen and quietly retry on the next
        // poll. Only surface the error once it persists across a few polls.
        bool keepLastGood = transientHiccup
                            && _consecutiveErrors < ErrorTolerance
                            && _data is null or { Error: null };
        if (!keepLastGood)
        {
            _data = fresh;
            _lastRefresh = DateTime.Now;
        }

        if (ok)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Remember this good reading so the Statistics window can keep drawing its charts from
            // local data during a later API outage, and mark when it was taken (the outage gap starts
            // here).
            _lastGoodSnapshot = new PaceSnapshot(fresh.Session5h, fresh.Reset5h, fresh.Week7d, fresh.Reset7d);

            // Log the live reading so the Statistics charts can draw the real utilization curve over
            // time, rather than inferring the burn shape from transcript token counts.
            UsageHistory.Append(now, fresh.Session5h, fresh.Reset5h, fresh.Week7d, fresh.Reset7d);

            foreach (string key in Metrics)
            {
                BurnTracker.ResetEvent? ev = _burn.Record(key, fresh.Metric(key), fresh.ResetOf(key), now);
                if (ev is not { } reset) continue;

                // Each window decides by its own opt-in. Weekly: the routine on-time reset uses the
                // quiet "scheduled" toggle, while the anomalous events (an early reset or a mid-window
                // credit) ride the "unexpected" toggle. Session (5h): a single toggle for any reset.
                // "extra" is overage, not a scheduled window — never notified.
                // The routine resets (weekly Scheduled, 5h session) also honor a minimum-usage floor:
                // a reset from a barely-touched window isn't worth a ping. PrevUtil is a 0–1 fraction.
                bool wanted = key switch
                {
                    "7d" => reset.Kind == BurnTracker.ResetKind.Scheduled
                        ? _settings.NotifyOnScheduledReset && reset.PrevUtil * 100 > _settings.ScheduledResetMinPercent
                        : _settings.NotifyOnUnexpectedReset,
                    "5h" => _settings.NotifyOnSessionReset && reset.PrevUtil * 100 > _settings.SessionResetMinPercent,
                    _ => false,
                };
                if (wanted) NotifyReset(key, reset, now);
            }
        }
        _flashOn = false;
        Render();
        // Push the fresh reading into an open Statistics window so it auto-refreshes on the same
        // cadence as the icon, rather than staying frozen until the user clicks Refresh.
        _statsWindow?.UpdateSnapshot(CurrentSnapshot(), CurrentError());
        RecomputeInsights();
        AdjustForAuthState();
    }

    // While signed out, re-poll on the (faster) retry cadence so a fresh token is noticed quickly,
    // and — if enabled — launch Claude Code once to prompt re-auth. As soon as a poll succeeds,
    // restore the normal cadence and re-arm the one-shot auto-open for the next signed-out spell.
    private void AdjustForAuthState()
    {
        bool unauthorized = _data is { Unauthorized: true };

        int desiredMs = DesiredPollMs();
        if (_poll.Interval != desiredMs)
        {
            _poll.Stop();
            _poll.Interval = desiredMs;
            _poll.Start();
        }

        if (!unauthorized)
        {
            _autoOpenedForAuth = false;
        }
        else if (_settings.AutoOpenOnUnauthenticated && !_autoOpenedForAuth)
        {
            _autoOpenedForAuth = true;
            OpenClaudeCode(forReauth: true);
        }
    }

    // The poll cadence for the current state. Auth-retry while signed out; the normal interval when
    // consuming; and — once a window is maxed out — a long idle until just past the known reset. When
    // a limit is hit, usage is blocked and consumption is frozen until that window resets, so polling
    // on the normal cadence just burns API calls to read the same 100%. Instead we sleep until the
    // reset is due and poll once to confirm it landed (see BlockedUntilUnix / the double-check floor).
    private int DesiredPollMs()
    {
        if (_data is { Unauthorized: true })
            return _settings.AuthRetrySeconds * 1000;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        double blockedUntil = BlockedUntilUnix(now);
        if (blockedUntil > 0)
        {
            // Idle until just past the predicted reset, then double-check. If we're already past it but
            // still reading at-limit (clock skew or a late server-side reset), fall to the short
            // double-check cadence so we catch the drop promptly rather than waiting a whole window.
            double waitMs = (blockedUntil - now + LimitResetBufferSeconds) * 1000;
            double floorMs = LimitDoubleCheckSeconds * 1000;
            return (int)Math.Min(Math.Max(waitMs, floorMs), int.MaxValue);
        }

        return _settings.RefreshSeconds * 1000;
    }

    // The next reset boundary to wake for while a window is maxed out: the soonest future reset among
    // the windows currently at their limit. Waking at the earliest (not the latest) means each window's
    // reset is polled as it happens — logged and, if enabled, notified — and if another window is still
    // maxed the next DesiredPollMs simply re-idles to its reset. 0 when nothing is blocked.
    private double BlockedUntilUnix(long now)
    {
        if (_data is not { Error: null } d) return 0;
        double soonest = double.PositiveInfinity;
        bool atLimit = false;
        if (d.Session5h >= AtLimitThreshold) { atLimit = true; if (d.Reset5h > now) soonest = Math.Min(soonest, d.Reset5h); }
        if (d.Week7d >= AtLimitThreshold) { atLimit = true; if (d.Reset7d > now) soonest = Math.Min(soonest, d.Reset7d); }
        if (!atLimit) return 0;
        // At limit but no known future reset (0 / stale header): keep checking on the short cadence
        // rather than idling forever.
        return double.IsPositiveInfinity(soonest) ? now : soonest;
    }

    // Ask GitHub for the latest release; if newer, surface it in the menu and notify once.
    private async Task CheckForUpdateAsync()
    {
        if (_updating) return;
        UpdateInfo? info = await _updater.CheckAsync();
        if (info == null || (_update != null && info.Version <= _update.Version)) return;

        _update = info;
        _updateItem.Text = L.T("menu.updateTo", info.Tag);
        _updateItem.Visible = true;

        _tray.BalloonTipTitle = L.T("update.title");
        _tray.BalloonTipText = L.T("update.body", info.Version, Updater.CurrentVersion);
        _tray.ShowBalloonTip(10_000);
    }

    // Download the installer and hand off to it; the app exits so its .exe can be replaced.
    private async Task ApplyUpdateAsync()
    {
        if (_updating || _update is not { } info) return;
        _updating = true;
        _updateItem.Text = L.T("menu.downloading", info.Tag);
        _updateItem.Enabled = false;

        try
        {
            string setup = await _updater.DownloadAsync(info);
            Updater.RunInstaller(setup);
            ExitApp(); // release the single-instance mutex and unlock the .exe for the installer
        }
        catch (Exception ex)
        {
            _updating = false;
            _updateItem.Text = L.T("menu.updateTo", info.Tag);
            _updateItem.Enabled = true;
            MessageBox.Show(L.T("dialog.downloadFailed", ex.Message),
                L.T("dialog.appName"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // A usage window fell — a full reset (early or on schedule) or a partial mid-window credit.
    // Show the matching toast and append a timestamped line to a local log so the event can be
    // reported later with concrete before/after numbers. `key` is the window ("5h" or "7d").
    private void NotifyReset(string key, BurnTracker.ResetEvent ev, long now)
    {
        LogResetEvent(key, ev, now);
        var (emoji, title, subtitle, fromUsage, toUsage, caption, quotaLabel, theme) = ResetToastContent(key, ev, now, _settings.ShowRemaining);
        try
        {
            EnsureWpfApp();
            new ToastWindow(emoji, title, subtitle, fromUsage, toUsage, caption, quotaLabel, theme).Show();
        }
        catch
        {
            // If the custom toast can't be shown, fall back to the plain system balloon so the
            // event is never silently dropped.
            _tray.BalloonTipTitle = $"{emoji} {title}";
            _tray.BalloonTipText = $"{subtitle} — {caption}";
            _tray.BalloonTipIcon = ToolTipIcon.Info;
            _tray.ShowBalloonTip(10_000);
        }
    }

    // The toast's display strings per window + kind, shared by the live notifier and the
    // --simulate-reset dev preview so the wording can never drift. All are framed as good news (quota
    // back); the early reset gets the loud "Surprise!", the routine one a calm "New week!"/"Fresh
    // session!", a partial credit a "Bonus!". Returns the before/after usage fractions too, so the
    // toast bar lands on the real new level, plus the quota-bar label for the window.
    internal static (string emoji, string title, string subtitle, double fromUsage, double toUsage,
        string caption, string quotaLabel, ToastWindow.ToastTheme theme)
        ResetToastContent(string key, BurnTracker.ResetEvent ev, long now, bool showRemaining = false)
    {
        bool weekly = key == "7d";
        string quotaLabel = L.T(weekly ? "toast.quotaLeft.weekly" : "toast.quotaLeft.session");
        string limitNoun = L.T(weekly ? "toast.limitNoun.weekly" : "toast.limitNoun.session");
        string scopeWord = L.T(weekly ? "toast.scope.weekly" : "toast.scope.session");
        string freshSuffix = L.T(weekly ? "toast.freshSuffix.weekly" : "toast.freshSuffix.session");
        string resetTitle = L.T(weekly ? "toast.resetTitle.weekly" : "toast.resetTitle.session");
        int fromPct = (int)Math.Round(Math.Clamp(ev.PrevUtil, 0, 1) * 100);
        int toPct = (int)Math.Round(Math.Clamp(ev.NewUtil, 0, 1) * 100);
        // In "remaining" mode the captions read in quota-left terms (the bar/number already do):
        // "Was 79% used" → "Had 21% left", "Usage dropped 91% → 50%" → "Quota left rose 9% → 50%".
        int fromLeftPct = 100 - fromPct;
        int toLeftPct = 100 - toPct;
        string ahead = ev.PrevReset > now ? L.T("toast.aheadDays", FmtDays(ev.PrevReset - now)) : L.T("toast.ahead");
        string earlyCaption = showRemaining ? L.T("toast.caption.earlyLeft", fromLeftPct, ahead) : L.T("toast.caption.earlyUsed", fromPct, ahead);
        string creditCaption = showRemaining ? L.T("toast.caption.creditLeft", fromLeftPct, toLeftPct) : L.T("toast.caption.creditUsed", fromPct, toPct);
        string routineCaption = showRemaining ? L.T("toast.caption.routineLeft", fromLeftPct, freshSuffix) : L.T("toast.caption.routineUsed", fromPct, freshSuffix);

        // Color theme: a session event is always blue; otherwise the weekly kind picks the color
        // (early reset = clay/Surprise, credit = violet/Bonus, routine = teal/Weekly).
        ToastWindow.ToastTheme theme = !weekly
            ? ToastWindow.ToastTheme.Session
            : ev.Kind switch
            {
                BurnTracker.ResetKind.Unexpected => ToastWindow.ToastTheme.Surprise,
                BurnTracker.ResetKind.Credit => ToastWindow.ToastTheme.Bonus,
                _ => ToastWindow.ToastTheme.Weekly,
            };

        return ev.Kind switch
        {
            BurnTracker.ResetKind.Unexpected => ("🎉", L.T("toast.title.surprise"), L.T("toast.sub.early", limitNoun),
                ev.PrevUtil, ev.NewUtil, earlyCaption, quotaLabel, theme),
            BurnTracker.ResetKind.Credit => ("🎉", L.T("toast.title.bonus"), L.T("toast.sub.credit", scopeWord),
                ev.PrevUtil, ev.NewUtil, creditCaption, quotaLabel, theme),
            _ => ("✨", resetTitle, L.T("toast.sub.routine", limitNoun),
                ev.PrevUtil, ev.NewUtil, routineCaption, quotaLabel, theme),
        };
    }

    // Best-effort append to %LocalAppData%\ClaudeTray\reset-events.log; never let it disrupt a poll.
    private static void LogResetEvent(string key, BurnTracker.ResetEvent ev, long now)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeTray");
            Directory.CreateDirectory(dir);
            static string Iso(double unix) => DateTimeOffset.FromUnixTimeSeconds((long)unix).UtcDateTime.ToString("o");
            string line = System.FormattableString.Invariant(
                $"{Iso(now)}\t{key} {ev.Kind.ToString().ToLowerInvariant()} {(int)Math.Round(ev.PrevUtil * 100)}%->{(int)Math.Round(ev.NewUtil * 100)}%\tprevReset={Iso(ev.PrevReset)}\tnewReset={Iso(ev.NewReset)}");
            File.AppendAllText(Path.Combine(dir, "reset-events.log"), line + Environment.NewLine);
        }
        catch { /* logging is best-effort */ }
    }

    private double CurrentPct()
        => _data != null && _data.Error == null ? Math.Min(1.0, _data.Metric(_metric)) : 0.0;

    // Projection for the currently displayed metric (session vs. week vs. extra).
    private (Projection verdict, double eta) CurrentProjection()
    {
        if (_data is not { Error: null }) return (Projection.Unknown, 0);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Both bounded windows use the proportional "pace line" for their verdict, matching the
        // Statistics chart's projection (average pace since the window started) so the tray and the
        // chart never disagree. "extra" is uncapped overage with no fixed window, so it keeps the
        // regression-based verdict (windowSeconds = 0).
        double window = _metric switch
        {
            "5h" => 5.0 * 3600,
            "7d" => 7.0 * 24 * 3600,
            _ => 0,
        };
        var (verdict, eta, _) = _burn.Project(_metric, _data.Metric(_metric), _data.ResetOf(_metric), now, window);
        return (verdict, eta);
    }

    private void Render()
    {
        IconRenderer.State state =
            _data == null ? IconRenderer.State.Connecting :
            _data.Error != null ? IconRenderer.State.Error :
            IconRenderer.State.Ok;

        bool flash = _settings.FlashNearLimit && CurrentPct() >= Settings.FlashWarnThreshold && _flashOn;
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);

        Projection verdict = CurrentProjection().verdict;

        // Before we're connected to Claude Code — either still connecting (no data yet) or signed out
        // on a 401 (expired token) — show the Claude Code Tray logo rather than a gray "0" or a play
        // triangle. On a live API error (e.g. an HTTP 403) show the same logo but with a red error dot
        // in the corner, instead of an amber tile with a misleading usage number. Once a poll succeeds
        // the usage icon takes over.
        bool apiError = state == IconRenderer.State.Error && _data is not { Unauthorized: true };
        using Bitmap bmp =
            _data is { Unauthorized: true } || state == IconRenderer.State.Connecting || apiError
                ? IconRenderer.RenderLogo(size, apiError)
                : IconRenderer.Render(CurrentPct(), state, flash, size, verdict, _settings.ShowPercentage, _settings.ShowRemaining);
        SetTrayIcon(bmp);
        _tray.Text = Truncate(BuildTooltip(), 127);
    }

    private void SetTrayIcon(Bitmap bmp)
    {
        IntPtr newHandle = bmp.GetHicon();
        Icon? old = _tray.Icon;
        IntPtr oldHandle = _iconHandle;

        _tray.Icon = Icon.FromHandle(newHandle);
        _iconHandle = newHandle;

        old?.Dispose();
        if (oldHandle != IntPtr.Zero) DestroyIcon(oldHandle);
    }

    private string BuildTooltip()
    {
        if (_data == null) return L.T("tip.connecting");
        if (_data.Error != null)
        {
            if (_data.Unauthorized)
                return _data.NeedsFullLogin
                    ? L.T("tip.notSignedIn")
                    : L.T("tip.willAppear");
            return L.T("tip.apiError", _data.Error);
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r5 = _data.Reset5h > 0 ? FmtCountdown(_data.Reset5h - now) : "--";
        string r7 = _data.Reset7d > 0 ? FmtDays(_data.Reset7d - now) : "--";

        // In "remaining" mode the two bounded windows show the complement and read "… left".
        // Extra is overage (no cap to have quota "left" from), so it always shows the amount used.
        string leftSuffix = _settings.ShowRemaining ? L.T("tip.leftSuffix") : "";
        var lines = new List<string>
        {
            $"{L.T("tip.session")}{leftSuffix}: {PctShown(_data.Session5h)}  ⟳ {r5}",
            $"{L.T("tip.week")}{leftSuffix}: {PctShown(_data.Week7d)}  ⟳ {r7}",
        };
        if (_data.Extra > 0.001)
        {
            string re = _data.ResetExtra > 0 ? FmtDays(_data.ResetExtra - now) : "--";
            lines.Add($"{L.T("tip.extra")}: {Pct(_data.Extra)}  ⟳ {re}");
        }

        var (verdict, eta) = CurrentProjection();
        string scope = Labels[_metric]; // make clear which window the projection is about
        bool hasEta = eta > 0 && !double.IsInfinity(eta);
        // The projection is about reaching the limit: "100%" used == "0% left" remaining.
        string limit = _settings.ShowRemaining ? L.T("tip.limitLeft") : L.T("tip.limitUsed");
        // The same target as a *future event*. In "remaining" mode "0% left" mirrors the current
        // saldo lines above ("Session 5h left: 97%"), so it reads as a present value; the plain-
        // language "runs out" marks it as something that happens later. Used mode keeps the "100%"
        // percentage it always showed, which reads naturally as a ceiling you climb toward.
        string hits = _settings.ShowRemaining ? L.T("tip.hitsLeft") : L.T("tip.hitsUsed");
        // Each projection verdict has a full form and a compact fallback for when the tooltip is
        // tight (see the 127-char cap note below). null => no projection line at all.
        (string full, string compact)? projection = CurrentPct() >= 0.995
            // Already maxed: state it plainly rather than "projecting" a limit you've reached.
            ? (L.T("tip.atLimitFull", scope, limit), L.T("tip.atLimitCompact", limit))
            : verdict switch
            {
                Projection.Danger => hasEta
                    ? (L.T("tip.dangerEtaFull", scope, hits, FmtDays(eta)), L.T("tip.dangerEtaCompact", hits, FmtDays(eta)))
                    : (L.T("tip.dangerPaceFull", scope), L.T("tip.dangerPaceCompact")),
                Projection.Ok => double.IsInfinity(eta)
                    ? (L.T("tip.okTrackFull", scope), L.T("tip.okTrackCompact"))
                    : (L.T("tip.okEtaFull", scope, hits, FmtDays(eta)), L.T("tip.okEtaCompact", hits, FmtDays(eta))),
                _ => null,
            };

        string updated = _lastRefresh is { } t ? $"  ⟳ {t:HH:mm:ss}" : "";
        string statusLine = L.T("tip.status", _data.Status, updated);

        // The Windows tray tooltip is capped at 127 chars (NOTIFYICONDATA.szTip). The refresh
        // time sits on the last line, so a blind end-truncation would chop it mid-value. Keep the
        // status/time line intact and fit the projection in: full form if it fits, else compact.
        int used = lines.Sum(l => l.Length + 1) + statusLine.Length;
        if (projection is { } p)
        {
            if (used + p.full.Length + 1 <= 127) lines.Add(p.full);
            else if (used + p.compact.Length + 1 <= 127) lines.Add(p.compact);
        }
        lines.Add(statusLine);
        return string.Join("\n", lines);
    }

    private static string Pct(double v) => $"{(int)Math.Round(Math.Min(v, 1.0) * 100)}%";

    // A window's percentage as displayed: the used fraction, or its complement in "remaining" mode.
    private string PctShown(double used)
        => Pct(_settings.ShowRemaining ? Math.Clamp(1.0 - used, 0.0, 1.0) : used);

    private static string FmtCountdown(double s)
    {
        if (s <= 0) return L.T("dur.now");
        int h = (int)(s / 3600), m = (int)(s % 3600 / 60);
        return h > 0 ? $"{h}h {m:00}m" : $"{m}m";
    }

    private static string FmtDays(double s)
    {
        if (s <= 0) return L.T("dur.now");
        int d = (int)(s / 86400), h = (int)(s % 86400 / 3600);
        return d > 0 ? $"{d}d {h}h" : FmtCountdown(s);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    // Open the Claude Code CLI in a terminal. Starting it makes Claude Code validate and refresh
    // the OAuth token, which clears an expired-token (401) state for the next poll. `claude` is on
    // PATH for anyone who uses Claude Code, so the shell resolves it regardless of install method.
    private void OpenClaudeCode(bool forReauth = false)
    {
        try
        {
            // Two re-auth paths, decided by whether a refresh token is on disk (ApiClient sets
            // NeedsFullLogin): with one, just launching `claude` silently refreshes the access token;
            // without one, only a full /login re-authenticates (there's no CLI flag to force it), so we
            // print a hint to type /login above the prompt before launching claude in the same window.
            string command = !forReauth
                ? "/k claude"
                : _data is { NeedsFullLogin: true }
                    ? $"/k echo {L.T("cli.loginHint")} & claude"
                    : $"/k echo {L.T("cli.refreshHint")} & claude";
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = command,
                UseShellExecute = true,
            };
            // Open in the configured working directory when set and still present; otherwise let
            // the OS default apply (the user's home directory).
            string dir = _settings.ClaudeCodeDirectory;
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                psi.WorkingDirectory = dir;
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                L.T("dialog.launchFailed", ex.Message),
                L.T("dialog.appName"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ExitApp()
    {
        _poll.Stop();
        _flash.Stop();
        _updateCheck.Stop();
        _tray.Visible = false;
        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
        _tray.Dispose();
        ExitThread();
    }
}
