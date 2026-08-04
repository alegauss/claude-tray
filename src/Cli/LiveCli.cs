namespace ClaudeTray;

/// <summary>The `--tail` and `--live` families: what is burning right now, from the transcripts. Split out of `Program.cs` by T132 —
/// moved verbatim.</summary>
internal static class LiveCli
{
    // Headless view of TranscriptTail: turns as they land, and the cost of noticing them. Flags:
    // `--tail <seconds>` to bound the run (default 30), `--root <dir>` to tail a stand-in for
    // ~/.claude/projects. The footer is the claim this task has to earn — bytes read should track the
    // bytes appended, not the size of the tree.
    internal static void PrintTail(string[] flags)
    {
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
                          $"({st.Files:N0} in the tree)");
        // Full vs cheap is the T103 claim, so print both: a cheap sweep looks only at what the
        // watcher named, and the whole tree is walked every ReconcileMs to reconcile.
        Console.WriteLine($"{st.Sweeps:N0} sweeps ({st.FullSweeps:N0} full, every " +
                          $"{TranscriptTail.ReconcileMs / 1000}s), last {st.LastSweepMs:0.00}ms, " +
                          $"last full {st.LastFullSweepMs:0.00}ms, {st.BytesRead / 1024.0:N1} KB read total");
        Console.WriteLine(st.Watching
            ? "watcher: live — appends land within ~" + TranscriptTail.DebounceMs + "ms"
            : $"watcher: off — falling back to the {TranscriptTail.SweepFloorMs}ms sweep floor");
    }

    /// <summary>Column width for a project name in the per-project lines. Wide enough for the two
    /// segments the app names a directory by since T154 ("alegauss/claude-tray"), so the disambiguating
    /// half is not what gets cut.</summary>
    private const int NameWidth = 24;

    // Elided in the *middle*, not from the front: a name is "parent/leaf" since T154 and the parent is
    // the half that tells two checkouts of the same release folder apart, so cutting the head would
    // throw away the reason it is there.
    private static string Trim(string s, int max)
    {
        if (s.Length <= max) return s;
        int tail = (max - 1) * 2 / 3;
        return s[..(max - 1 - tail)] + "…" + s[^tail..];
    }

    // Headless view of LiveRate: one line a second, so the metric can be watched against real work
    // and diffed against `--tail`'s raw turns. Flags: `--live <seconds>` to bound the run (default
    // 90), `--root <dir>` for a stand-in tree, `--raw` to also print the unsmoothed box filter.
    internal static void PrintLive(string[] flags)
    {
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
                      $"[{(p.IsOthers ? "·" : p.Slot.ToString())}] {(p.IsOthers ? p.Display : Trim(p.Display, NameWidth))} {p.TokensPerSecond,6:N0}/s"));

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
            Console.WriteLine($"  [{(p.IsOthers ? "·" : p.Slot.ToString())}] {Trim(p.Display, NameWidth),-NameWidth} " +
                              Spark(p.RatePerSecond, 60) + $"   → {p.RatePerSecond[^1]:N0} tok/s");

        TailStats st = tail.Stats;
        Console.WriteLine();
        Console.WriteLine($"worst redraw drift {worstDrift:N2} tok/s — a second's value must not change " +
                          "after it is drawn (T117)");
        Console.WriteLine($"peak {peak:N0} tok/s over {seconds:0}s — {live.Turns:N0} turns, " +
                          $"{st.BytesRead / 1024.0:N1} KB read, {st.Sweeps:N0} sweeps " +
                          $"({st.FullSweeps:N0} full over {st.Files:N0} transcripts)");
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
}
