using System.Text;

namespace ClaudeTray;

/// <summary>
/// `--selftest`: a deterministic self-check of the arithmetic behind the weekly projection (Block J), the
/// live rate (Block K) and the project-slug encoding both read from (T161), over synthetic inputs, exiting
/// non-zero on failure.
///
/// <para>Both blocks introduced properties that are cheap to assert and expensive to lose — a flat
/// profile must reproduce the straight average-pace line <em>exactly</em>, folding must be idempotent,
/// advice must never propose an hour that overshoots its own target, a burst must decay to a true zero
/// at exactly the window width — and every one of them was verified by a screenshot or a CLI read-out
/// by a human, once. Those are properties, not observations, and nothing stopped an edit from breaking
/// one silently.</para>
///
/// <para><b>Why in the binary and not a test project.</b> A test project means a third-party test
/// framework, which the non-goals rule out for a repo whose single self-contained <c>.exe</c> is a
/// feature (§I.3). This costs nothing at runtime, ships inside the same binary, and runs in CI as one
/// line. It is the same trade the fixtures already make (<see cref="ContextFixture"/>,
/// <see cref="ThroughputFixture"/>): synthetic input, real code path.</para>
///
/// <para><b>What it touches.</b> Everything is synthetic and removed afterwards: a temporary transcript
/// tree under the OS temp dir, and a <c>selftest</c> <em>profile</em> directory for the two stores that
/// have no in-memory seam (<see cref="HourlyUsage"/>). A real profile's stores are never read or
/// written — the same rule T128 set for fixture roots.</para>
/// </summary>
internal static class SelfTestCli
{
    /// <summary>The synthetic profile key. Not a hash like a real key (<c>acct-…</c>/<c>dir-…</c>), so it
    /// can never collide with an account's directory.</summary>
    private const string ProfileKey = "selftest";

    private static int _passed, _failed, _skipped;
    private static readonly List<string> Failures = new();

    /// <returns>Process exit code: 0 when every check passed.</returns>
    public static int Run(string[] flags)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* redirected output */ }
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        bool quick = flags.Contains("--quick");
        long started = DateTime.UtcNow.Ticks;

        Console.WriteLine("Claude Code Tray — self-check (synthetic inputs, no real profile is read or written)");
        Console.WriteLine();

        Section("pacing — the shape and the staircase (Block J)");
        Pacing();

        Section("kernel — the live rate (Block K)");
        Kernel();

        Section("stores — folding, the ghost, intensity (Block J)");
        string dir = ProfileStore.DirFor(ProfileKey);
        Console.WriteLine($"  scratch profile: {dir}");
        Remove(dir);            // a crashed earlier run must not decide what "already folded" means
        try { Stores(); }
        catch (Exception e) { Fail("the section threw", e.Message); }
        finally { Remove(dir); }

        Section("sweep — the transcript grid and the away-week exclusion (Block J)");
        Temp(Sweep);

        Section("slug — the projects/<slug> encoding (Blocks K and W)");
        Temp(Slug);

        if (quick)
        {
            Console.WriteLine();
            Console.WriteLine("tail — skipped (--quick); it waits on real sweeps and takes a few seconds");
        }
        else
        {
            Section("tail — the byte cursor (Block K)");
            Temp(Tail);

            Section("tail — the primed cursor over a large transcript (Block K)");
            Temp(Primed);
        }

        double ms = (DateTime.UtcNow.Ticks - started) / (double)TimeSpan.TicksPerMillisecond;
        Console.WriteLine();
        foreach (string f in Failures) Console.WriteLine("FAILED  " + f);
        Console.WriteLine($"{_passed} passed, {_failed} failed" +
                          (_skipped > 0 ? $", {_skipped} skipped" : "") + $" — {ms:0}ms");
        return _failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- Block J: the projection

    private static void Pacing()
    {
        // The claim T87 rests on: where the remaining hours have the same active/idle mix as the
        // elapsed ones, the staircase *is* the straight line. It is one line of arithmetic away from
        // being false, and a flat grid is the case where the two must agree to the last decimal.
        // T94's intensity rides on the same check: `I` is flat here, so every factor it introduces
        // must be exactly 1.
        foreach (double p in new[] { 0.10, 0.35, 1.00 })
        {
            ActivityProfile flat = Flat(p);

            WindowPace easy = Week(util: 0.40, elapsed: 0.5);
            if (Straddles(easy))
            {
                Skip($"flat profile p={p:0.00} reproduces the straight line",
                     "the synthetic window straddles a DST change");
                continue;
            }

            ActivityShape? shape = ActivityShape.Build(easy, flat, Now);
            if (!Check($"flat profile p={p:0.00} builds a shape", shape != null)) continue;

            Near($"flat profile p={p:0.00} lands where the average pace lands",
                 shape!.EndCum, easy.Util / easy.ElapsedFraction, 1e-9);
            Near($"flat profile p={p:0.00} keeps the intensity factor at exactly 1",
                 shape.RatePerOrdinaryHour, shape.RatePerActiveHour, 0);

            WindowPace heavy = Week(util: 0.74, elapsed: 4.0 / 7.0);
            ActivityShape? hot = ActivityShape.Build(heavy, flat, Now);
            if (!Check($"flat profile p={p:0.00} builds a running-out shape", hot is { RunsOut: true })) continue;

            Near($"flat profile p={p:0.00} runs out where the average pace runs out",
                 hot!.ExhaustFraction, heavy.ExhaustFraction, 1e-9);
        }

        // Σ over a span must be Σ over the grid — the accessor the projection spends quota against,
        // checked against the thing it is meant to be summing. Pure wall-clock arithmetic, so this one
        // holds across a DST change too.
        ActivityProfile varied = Varied();
        DateTime from = DateTime.Today.AddHours(3);
        Near("expected active hours over a week equals the sum of the grid",
             varied.ExpectedActiveHours(from, from.AddDays(7)), Sum(varied.P), 1e-9);
        Near("expected intensity hours over a week equals the sum of p·i",
             varied.ExpectedIntensityHours(from, from.AddDays(7)), Dot(varied.P, varied.I), 1e-9);

        ActivityProfile flatI = Varied();
        Array.Fill(flatI.I, 1.0);
        Near("a flat intensity grid leaves the expectation untouched",
             flatI.ExpectedIntensityHours(from, from.AddDays(7)),
             flatI.ExpectedActiveHours(from, from.AddDays(7)), 0);

        // T90: advice that lands on 100% is advice to get blocked, and advice to resume at 03:00 is
        // not advice. Both are properties of every answer it can give, not of the one it gave once.
        ActivityProfile working = WorkingHours();
        WindowPace burning = Week(util: 0.85, elapsed: 4.0 / 7.0);
        ActivityShape? advised = ActivityShape.Build(burning, working, Now);
        if (Check("a burning week with working hours builds a shape", advised is { RunsOut: true }))
        {
            if (advised!.HasAdvice)
            {
                Check("the resume hour closes the week under its own target",
                      advised.ResumeEndCum <= ActivityShape.AdviceTarget + 1e-9,
                      $"closes at {advised.ResumeEndCum:0.0000}, target {ActivityShape.AdviceTarget:0.00}");
                DateTime resume = DateTimeOffset.FromUnixTimeSeconds((long)advised.ResumeUnix).LocalDateTime;
                Check("the resume hour is one the user actually works",
                      working.At(resume) >= ActivityShape.IdleThreshold,
                      $"p({resume:ddd HH:mm}) = {working.At(resume):0.00}");
            }
            else Skip("the resume hour closes the week under its own target",
                      "no honest resume hour exists for this window, which is itself allowed");
        }

        // The gate in front of all of it: a confident-looking staircase drawn on one week of data is
        // worse than an honest line.
        Check("a thin profile refuses to shape the projection",
              ActivityShape.Build(Week(0.40, 0.5), Flat(0.35, weeks: 1), Now) == null);
        Check("weeks away are subtracted from the confidence gate",
              !new ActivityProfile { CoverageWeeks = 4, ExcludedWeeks = 2 }.Confident,
              $"4 weeks − 2 away is under {ActivityProfile.ConfidentWeeks:0.0}");
        Check("the measured span is subtracted the same way (T152)",
              !new ActivityProfile { MeasuredWeeks = 5, MeasuredExcludedWeeks = 3 }.Confident,
              $"5 folded weeks − 3 away is under {ActivityProfile.ConfidentWeeks:0.0}");

        // T159: the method note quotes the shape's own figure, so the shape has to carry what the gate
        // acted on — the span minus the weeks away — and the count that explains why it is smaller. The
        // whole defect was one accessor, which is exactly the kind an assertion pins for good.
        ActivityProfile withAway = Flat(0.35, weeks: 6);
        withAway.ExcludedWeeks = 2;
        ActivityShape? kept = ActivityShape.Build(Week(0.40, 0.5), withAway, Now);
        if (Check("a profile with weeks away still shapes the projection", kept != null))
        {
            Near("the shape reports the weeks the grid kept, not the span", kept!.EffectiveWeeks, 4, 1e-9);
            Check("the shape carries the excluded count the note needs", kept.ExcludedWeeks == 2,
                  $"{kept.ExcludedWeeks} excluded");
        }
    }

    // ---------------------------------------------------------------- Block K: the rate kernel

    private static void Kernel()
    {
        const int w = LiveRate.WindowSeconds;

        // A triangular window summed over discrete seconds has gain (W+1)/2, not W/2 — the integral of
        // the same shape. Normalising by the integral read every sustained rate 1/W high (T150), so the
        // assertion is that R reads as R exactly: the kernel claims a unit, and a fixed factor on a
        // number labelled tok/s is a wrong number, not a scale.
        var steady = new long[4 * w];
        Array.Fill(steady, 1000L);
        double[] flat = LiveRate.RateFrom(steady, 30);
        Near("a sustained rate reads as itself", flat[^1], 1000.0, 1e-9);
        Check("a sustained rate is flat across the reported span",
              Math.Abs(flat[0] - flat[^1]) < 1e-9);

        // One burst, evaluated from the second it landed, so the series is a pure fall and the
        // attack-only smoothing is transparent — which is what lets the decay be asserted exactly.
        var burst = new long[w + w + 1];
        burst[w] = 60_000;
        double[] decay = LiveRate.RateFrom(burst, w + 1);
        Near("a burst starts at its full weight", decay[0], 60_000 / LiveRate.KernelSum, 1e-9);
        Near("a burst decays linearly", decay[w / 2], decay[0] * 0.5, 1e-9);
        Check($"a burst reaches a true zero at exactly {w}s", decay[w] == 0,
              $"value at {w}s = {decay[w]:0.#####}");
        Check($"a burst is still above zero at {w - 1}s", decay[w - 1] > 0);

        bool linear = true;
        for (int k = 0; k <= w; k++) linear &= Math.Abs(decay[k] - decay[0] * (1 - (double)k / w)) < 1e-9;
        Check("every point of the decay sits on the line", linear);

        // Why per-project lines can sum to the headline: the kernel is linear. Checked where the
        // attack branch is transparent — during a rise it deliberately is not (T114), and the chart
        // draws separate lines rather than a stack precisely so nothing asks them to.
        var a = new long[w + w + 1];
        var b = new long[w + w + 1];
        var both = new long[w + w + 1];
        a[w] = 21_000; b[w] = 13_000; both[w] = 34_000;
        double[] ra = LiveRate.RateFrom(a, w), rb = LiveRate.RateFrom(b, w), rab = LiveRate.RateFrom(both, w);
        bool sums = true;
        for (int k = 0; k < w; k++) sums &= Math.Abs(ra[k] + rb[k] - rab[k]) < 1e-9;
        Check("two projects' rates sum to the rate of both together", sums);

        Check("an empty feed reads as zero, not as a stale plateau",
              LiveRate.RateFrom(new long[4 * w], 30).All(v => v == 0));

        ZeroFill();
    }

    /// <summary>
    /// T153: the zero-fill on a paused caller — one of the properties that justified building this
    /// check, and the one it could not make. <see cref="LiveRate.Tick"/> is caller-driven precisely so
    /// a hidden window costs nothing, which means a resumed caller is the normal case and not an edge:
    /// the strip must come back showing the gap, never the value it was left at.
    /// </summary>
    /// <remarks>Reaching <see cref="LiveRate.Add"/> used to mean standing up a real
    /// <see cref="TranscriptTail"/> and waiting for a sweep to raise its event — which is why this was
    /// left unasserted. The method is <c>internal</c> now (same visibility the fixtures use), so the
    /// burst can be pushed straight in and the clock moved by hand.</remarks>
    private static void ZeroFill()
    {
        const int w = LiveRate.WindowSeconds;
        // Never started, so it never touches the disk or arms a watcher: the tail is here only because
        // the rate subscribes to one.
        using var tail = new TranscriptTail(Path.Combine(Path.GetTempPath(), "claude-tray-selftest-nofeed"));
        var rate = new LiveRate(tail);

        double t0 = Math.Floor(Now);
        rate.Tick(t0);
        rate.Add(new[] { new TailSample(t0, new TokenBits(60_000, 0, 0, 0), "slug", "session", "slug") });
        rate.Tick(t0);

        Near("a burst lands at its full weight", rate.Instant, 60_000 / LiveRate.KernelSum, 1e-9);
        Check("and the strip holds the second it landed in", rate.Strip()[^1].Total == 60_000);

        // The caller goes away for longer than the rate window and comes back. Nothing arrived while it
        // was gone, and the honest reading of that is zero — not the last number it computed.
        rate.Tick(t0 + w + 1);
        Check($"a caller paused past the {w}s window resumes at a true zero",
              rate.TokensPerSecond == 0 && rate.Instant == 0,
              $"{rate.TokensPerSecond:0.####} tok/s smoothed, {rate.Instant:0.####} raw");
        Check("and reports itself quiet rather than merely small", rate.Quiet);
        Check("the burst is still in the strip, which is 5 minutes and not 1",
              rate.Strip().Any(b => b.Total == 60_000));

        // Gone for an hour: the zero-fill is capped at the ring, so what comes back is an empty strip
        // rather than whatever the ring's arithmetic left behind at that offset.
        rate.Tick(t0 + 3600);
        Check("a caller gone for an hour comes back to an empty strip",
              rate.Strip().All(b => b.Total == 0),
              $"{rate.Strip().Count(b => b.Total != 0)} of {LiveRate.HistorySeconds} buckets still hold tokens");
        Check("and to no project series at all", rate.Projects().Length == 0);
    }

    // ---------------------------------------------------------------- Block J: the stores

    private static void Stores()
    {
        // T88's whole claim: nothing is discarded before it is counted, and counting it twice is
        // impossible. A day already folded must be skipped, not re-added to.
        DateTime day = DateTime.Today.AddDays(-2);
        var samples = new List<UsageSample>();
        double reset = Now + 3 * 86400;
        for (int i = 0; i <= 10; i++)
            samples.Add(new UsageSample(Unix(day.AddHours(10).AddMinutes(6 * i)), 0, 0, 0.10 + 0.01 * i, reset));

        long nowUnix = (long)Now;
        HourlyUsage.Fold(ProfileKey, samples, nowUnix);
        List<HourlyDay> once = HourlyUsage.Load(ProfileKey);
        double spentOnce = Total(once);

        HourlyUsage.Fold(ProfileKey, samples, nowUnix);
        List<HourlyDay> twice = HourlyUsage.Load(ProfileKey);

        Check("folding a day writes it once", once.Count == 1, $"{once.Count} days");
        Near("folding is idempotent — the same readings twice spend the same", Total(twice), spentOnce, 1e-12);
        Near("a day's spend is the sum of its positive deltas", spentOnce, 0.10, 1e-9);
        Check("the day was folded whole", twice.Count == once.Count);

        // T89's two gates. A ghost that is really a record of the app having been closed would read as
        // a quiet week, which is the one thing it must not do.
        WriteStore(FoldedWeek(coverage: 1.0, perHour: 0.004));
        Check("a well-observed previous week draws a ghost",
              HourlyUsage.PreviousWeek(ProfileKey, Now, 7 * 86400, 0.5) != null);

        WriteStore(FoldedWeek(coverage: 0.30, perHour: 0.004));
        Check("a barely-observed week stays hidden",
              HourlyUsage.PreviousWeek(ProfileKey, Now, 7 * 86400, 0.5) == null,
              $"a third of the hours is under {HourlyUsage.MinGhostCoverage * 100:0}%");

        WriteStore(FoldedWeek(coverage: 1.0, perHour: 0.00001));
        Check("a week that barely moved stays hidden",
              HourlyUsage.PreviousWeek(ProfileKey, Now, 7 * 86400, 0.5) == null,
              $"under {HourlyUsage.MinGhostTotal * 100:0}% total");

        // T94: one heavy hour must not become a "4× heavy" bucket. The guards are the only thing
        // between a single incident and a projection that inherits it for weeks.
        WriteStore(IntensityDay());
        var prof = Flat(0.5);
        prof.BlendMeasured(ProfileKey, DateTime.Now);

        Check("the measured store supplies an intensity grid", prof.HasIntensity);
        Check("every intensity stays inside its clamp",
              prof.I.All(i => i >= ActivityProfile.MinIntensity - 1e-12 &&
                              i <= ActivityProfile.MaxIntensity + 1e-12),
              $"[{prof.I.Min():0.00}, {prof.I.Max():0.00}] vs " +
              $"[{ActivityProfile.MinIntensity:0.0}, {ActivityProfile.MaxIntensity:0.0}]");
        Check("a heavy hour reads heavier than a light one",
              prof.IntensityAt(HeavyHour) > prof.IntensityAt(HeavyHour.AddHours(1)));
        Check("a ~50× incident is capped at the clamp, not believed",
              prof.IntensityAt(HeavyHour) == ActivityProfile.MaxIntensity,
              $"{prof.IntensityAt(HeavyHour):0.00}×");
        Check("a near-zero hour is pulled back toward ordinary rather than clamped",
              prof.IntensityAt(HeavyHour.AddHours(1)) > ActivityProfile.MinIntensity &&
              prof.IntensityAt(HeavyHour.AddHours(1)) < 1,
              $"{prof.IntensityAt(HeavyHour.AddHours(1)):0.00}× from a raw ratio near zero");
        Check("an hour observed but never active stays exactly ordinary",
              prof.IntensityAt(HeavyHour.AddHours(5)) == 1.0,
              "no evidence is not 'a light hour'");

        // T152: the away-week exclusion, on the measured side. Six weeks, newest first — four ordinary,
        // one well covered but nearly idle (the holiday), and one whose readings cover too little of it
        // to say anything either way (the tray was closed for most of it).
        WriteStore(FoldedWeeks(new[] { (24, 8), (24, 8), (24, 8), (24, 1), (24, 8), (5, 0) }));
        HourlyUsage.MeasuredWeek m = HourlyUsage.MeasuredProfile(ProfileKey, DateTime.Now);
        string weeks = string.Join(", ", m.WeekHours.Select((h, i) =>
            $"{h}h/{m.WeekReadings[i]}c{(m.WeekAway[i] ? "*" : m.WeekCovered[i] ? "" : "?")}"));

        Check("the folded holiday is excluded from the measured grid", m.Excluded == 1,
              $"{m.Excluded} excluded of [{weeks}], median {m.Median:0.#}h");
        Check("a week too thinly covered to judge is not excluded",
              !m.WeekCovered[5] && !m.WeekAway[5],
              "twenty hours of readings cannot tell a holiday from a closed tray");
        Check("the median well-covered week is never excluded",
              m.WeekHours.Where((_, i) => m.WeekCovered[i] && !m.WeekAway[i]).DefaultIfEmpty(0).Max() >= m.Median);

        // The excluded week leaves no weight behind it, and the unjudged one keeps every bit of the
        // weight it had — which is the whole difference between the two verdicts.
        int b3 = ActivityProfile.Index(DateTime.Today.AddDays(-2).DayOfWeek, 3);
        double kept = 1 + 0.8 + 0.64 + Math.Pow(0.8, 4) + Math.Pow(0.8, 5);
        Near("an excluded week's hours are out of the measured evidence", m.Observed[b3], kept, 1e-9);
        Near("an unjudged quiet week still votes", m.P[b3], (kept - Math.Pow(0.8, 5)) / kept, 1e-9);

        // The same guard the transcript side has, counted over the weeks that can be judged: with three
        // of them the median is one of them.
        WriteStore(FoldedWeeks(new[] { (24, 8), (24, 8), (24, 1) }));
        HourlyUsage.MeasuredWeek thin = HourlyUsage.MeasuredProfile(ProfileKey, DateTime.Now);
        Check($"under {ActivityProfile.MinWeeksToExclude} well-covered weeks nothing is excluded",
              thin.Excluded == 0, $"{thin.Excluded} excluded");

        // T160: the bar itself. Against a round-the-clock store it is still half of what a week holds,
        // and the whole point is what happens when the tray only runs office hours — the case the
        // absolute half-of-168 bar could never judge, on the very machine that wrote it.
        Check("the coverage bar is half the median observed week", m.CoverageBar == 48,
              $"{m.CoverageBar} against weeks of [{string.Join(", ", m.WeekReadings)}] covered hours");

        WriteStore(FoldedWeeks(new[] { (10, 8), (10, 8), (10, 8), (10, 1), (10, 8) }));
        HourlyUsage.MeasuredWeek office = HourlyUsage.MeasuredProfile(ProfileKey, DateTime.Now);
        Check("a tray that runs ten hours a day can still judge its weeks",
              office.WeekCovered.Count(c => c) >= ActivityProfile.MinWeeksToExclude,
              $"{office.WeekCovered.Count(c => c)} of {office.WeekCovered.Length} weeks judged at a bar of " +
              $"{office.CoverageBar} against [{string.Join(", ", office.WeekReadings)}] covered hours");
        Check("and the holiday inside them is excluded", office.Excluded == 1,
              $"{office.Excluded} excluded of [{string.Join(", ", office.WeekHours)}] active hours, " +
              $"median {office.Median:0.#}h");

        // The floor under the comparison: relative alone would let a machine watched three hours a day
        // rest an away verdict on a day and a half of readings.
        WriteStore(FoldedWeeks(new[] { (3, 2), (3, 2), (3, 2), (3, 0), (3, 2) }));
        HourlyUsage.MeasuredWeek sparse = HourlyUsage.MeasuredProfile(ProfileKey, DateTime.Now);
        Check($"a week under {HourlyUsage.MinAwayWeekCoverageHours} covered hours is never judged",
              sparse.WeekCovered.All(c => !c) && sparse.Excluded == 0,
              $"bar {sparse.CoverageBar} against [{string.Join(", ", sparse.WeekReadings)}] covered hours");
    }

    // ---------------------------------------------------------------- Block J: the sweep

    private static void Sweep(string root)
    {
        // T95: a week away votes "these hours are idle" as confidently as a working week votes the
        // opposite. Built as transcripts rather than as a grid, so the exclusion is checked through
        // the same path a real machine takes.
        DateTime now = DateTime.Now;
        string dir = Path.Combine(root, "projects", "d--selftest");
        Directory.CreateDirectory(dir);

        // Five voting weeks (the sixth only extends the coverage span): 40 active buckets in a normal
        // week, 5 in the week that was nearly all away, 0 in the week that was entirely away. Dated
        // from yesterday backwards, so no synthetic turn ever lands in the future.
        var lines = new List<string>();
        for (int week = 0; week < 6; week++)
        {
            if (week == 4) continue;                       // away: nothing at all that week
            int hours = week == 3 ? 1 : 8;                 // away: one hour a day
            for (int day = 0; day < 5; day++)
                for (int h = 0; h < hours; h++)
                    lines.Add(Turn(now.AddDays(-7 * week - day - 1).Date.AddHours(9 + h)));
        }
        File.WriteAllLines(Path.Combine(dir, "session.jsonl"), lines);

        ActivityProfile prof = ActivityProfile.Compute(
            DateTimeOffset.UtcNow.UtcDateTime, Path.Combine(root, "projects"),
            ActivityProfile.SweepCacheMode.Off);

        if (!Check("the synthetic tree produces a grid", prof.Error == null && prof.Samples > 0,
                   prof.Error ?? $"{prof.Samples} lines")) return;

        Check("both away weeks are excluded", prof.ExcludedWeeks == 2,
              $"{prof.ExcludedWeeks} excluded of {prof.WeekHours.Length} weeks " +
              $"[{string.Join(", ", prof.WeekHours)}], median {prof.MedianWeekHours:0.#}h");
        Check("the median week is never excluded",
              prof.WeekHours.Where((_, i) => !prof.WeekAway[i]).DefaultIfEmpty(0).Max() >= prof.MedianWeekHours);
        Check("at least half the weeks survive",
              prof.WeekAway.Count(x => !x) * 2 >= prof.WeekAway.Length);

        DateTime worked = now.AddDays(-1).Date.AddHours(10);
        Check("a worked hour still reads as worked", prof.At(worked) > 0.5, $"p = {prof.At(worked):0.00}");

        // The guard in front of the exclusion: with too few weeks the median is one of them, and
        // dropping the quieter half of what little there is invents a shape rather than measuring one.
        File.WriteAllLines(Path.Combine(dir, "session.jsonl"),
            Enumerable.Range(0, 3).SelectMany(week => Enumerable.Range(0, 8)
                .Select(h => Turn(now.AddDays(-7 * week - 1).Date.AddHours(9 + h)))));

        ActivityProfile thin = ActivityProfile.Compute(
            DateTimeOffset.UtcNow.UtcDateTime, Path.Combine(root, "projects"),
            ActivityProfile.SweepCacheMode.Off);
        Check($"under {ActivityProfile.MinWeeksToExclude} weeks nothing is ever excluded",
              thin.ExcludedWeeks == 0, $"{thin.ExcludedWeeks} excluded");
    }

    // ---------------------------------------------------------------- Blocks K/W: the slug encoding

    /// <summary>
    /// <see cref="ProjectSlug"/>: the one reader and writer of a <b>lossy</b> encoding, behind two shipped
    /// defects already (T105's three divergent decoders, T154's three legend lines all labelled
    /// <c>2026.3</c>) and pure everywhere but <see cref="ProjectSlug.TryProbe"/> — strings in, strings out,
    /// no clock and no profile. Everything it claims is asserted here, including the two claims that are
    /// about being <em>wrong</em>: the naive readings exist only as last resorts, and the checks say so by
    /// pinning them against the exact answer.
    /// </summary>
    private static void Slug(string root)
    {
        // The path Claude Code's own directory name is built from, chosen for the trap: a literal hyphen
        // in the leaf encodes exactly like the separators around it.
        const string project = @"d:\Git\acme\claude-tray";
        const string encoded = "d--Git-acme-claude-tray";

        Check("the encoding is what Claude Code writes for a path with a literal hyphen",
              ProjectSlug.Encode(project) == encoded, ProjectSlug.Encode(project));
        Check("a separator and a literal hyphen encode identically",
              ProjectSlug.Encode(@"d:\Git\acme\claude\tray") == ProjectSlug.Encode(project),
              "the loss this class exists to contain — a slug alone cannot be split back into a path");
        Check("the encoding is length-preserving over an alphabet of [A-Za-z0-9-]",
              ProjectSlug.Encode(@"c:\Users\ação\Área de Trabalho\x.md").Length ==
                  @"c:\Users\ação\Área de Trabalho\x.md".Length &&
              ProjectSlug.Encode(@"c:\Users\ação\Área de Trabalho\x.md")
                  .All(c => char.IsAsciiLetterOrDigit(c) || c == '-'),
              ProjectSlug.Encode(@"c:\Users\ação\Área de Trabalho\x.md"));

        // RootFor verifies rather than guesses, which is what makes it immune to the recorded cwd being
        // the working directory *of that turn* — the defect T105 fixed.
        Check("the root is recovered from a cwd several levels deeper",
              ProjectSlug.RootFor(encoded, project + @"\src\Ui\pages\deep") == project,
              ProjectSlug.RootFor(encoded, project + @"\src\Ui\pages\deep") ?? "null");
        Check("a trailing separator on the cwd changes nothing",
              ProjectSlug.RootFor(encoded, project + @"\src\") == project);
        Check("the comparison is case-insensitive, as the filesystem is",
              ProjectSlug.RootFor(encoded, @"D:\GIT\ACME\CLAUDE-TRAY\src") is { Length: > 0 });
        Check("an unrelated cwd is null rather than a guess",
              ProjectSlug.RootFor(encoded, @"e:\somewhere\else") == null,
              "a transcript from another machine has no answer here, and inventing one is worse than none");
        Check("the cwd is answered without touching the filesystem",
              ProjectSlug.RootFor(encoded, project + @"\gone\deleted") == project,
              "a directory since deleted or unplugged still has a name");

        // The walk is bounded, so a pathological path cannot spin: 30 levels below the root is past it.
        string tooDeep = project + string.Concat(Enumerable.Repeat(@"\x", 30));
        Check("the upward walk is bounded", ProjectSlug.RootFor(encoded, tooDeep) == null);

        // T154: the leaf alone is not an identity on a real machine, and a legend that labels three lines
        // `2026.3` has labelled none of them.
        Check("a name on screen is the last two segments",
              ProjectSlug.ShortName(@"d:\Git\viglet\turing\2026.3") == "turing/2026.3",
              ProjectSlug.ShortName(@"d:\Git\viglet\turing\2026.3"));
        Check("two checkouts of the same release name differently",
              ProjectSlug.ShortName(@"d:\Git\viglet\turing\2026.3") !=
              ProjectSlug.ShortName(@"d:\Git\viglet\shio\2026.3"),
              "the T154 defect: three lines all labelled 2026.3");
        Check("a trailing separator changes no name",
              ProjectSlug.ShortName(@"d:\Git\acme\web\") == "acme/web");
        Check("a directory at a drive root degrades to its leaf",
              ProjectSlug.ShortName(@"d:\acme") == "acme", ProjectSlug.ShortName(@"d:\acme"));
        Check("a drive root names itself rather than nothing",
              ProjectSlug.ShortName(@"d:\") == "d:", ProjectSlug.ShortName(@"d:\"));

        // The two deliberately-ambiguous last resorts. Both are asserted *against* the exact answer: the
        // day one of them silently becomes the answer, these are the checks that say so.
        Check("the naive literal reading is the wrong path, by construction",
              ProjectSlug.Literal(encoded) == @"d:\Git\acme\claude\tray" &&
              ProjectSlug.Literal(encoded) != project,
              ProjectSlug.Literal(encoded));
        Check("a slug that is not a drive path is not reported as one",
              ProjectSlug.Literal("-Users-alexa-code").Length == 0,
              "a shared or virtual project dir is not a missing directory");
        Check("the tail is the ambiguous display name, not the verified one",
              ProjectSlug.Tail(encoded) == "tray" &&
              ProjectSlug.Tail(encoded) != ProjectSlug.ShortName(project),
              $"{ProjectSlug.Tail(encoded)} vs {ProjectSlug.ShortName(project)}");
        Check("a verified cwd names the project, an unverified one falls back to the tail",
              ProjectSlug.NameFor(encoded, project + @"\src") == "acme/claude-tray" &&
              ProjectSlug.NameFor(encoded, @"e:\elsewhere") == ProjectSlug.Tail(encoded),
              ProjectSlug.NameFor(encoded, project + @"\src"));
        Check("an unverifiable slug reports nothing rather than a guess",
              ProjectSlug.ShortNameFor(encoded, @"e:\elsewhere") == null);

        // TryProbe is the only part that needs a filesystem: `viglet\model` exists *and* so does
        // `viglet-model-catalog`, which is the case that fails without backtracking.
        Directory.CreateDirectory(Path.Combine(root, "viglet", "model"));
        Directory.CreateDirectory(Path.Combine(root, "viglet-model-catalog"));
        File.WriteAllText(Path.Combine(root, "notadir"), "");

        // The temp path itself has to survive the round trip, or nothing below it can be probed: a
        // character that is neither alphanumeric nor a separator — an 8.3 short name's `~`, a space, a dot —
        // encodes to a `-` that matches no directory on the way down. Tested on the path rather than by
        // probing it, so a broken probe fails these checks instead of skipping them.
        if (!Probeable(root))
        {
            Skip("the probe backtracks past a shorter directory that exists",
                 $"this machine's temp path has a segment the encoding cannot reconstruct ({root})");
            Skip("the probe returns only directories that exist", "same reason");
            return;
        }

        string catalog = Path.Combine(root, "viglet-model-catalog");
        Check("the probe backtracks past a shorter directory that exists",
              ProjectSlug.TryProbe(ProjectSlug.Encode(catalog), out string probed) && probed == catalog,
              probed);
        Check("the probe returns only directories that exist",
              !ProjectSlug.TryProbe(ProjectSlug.Encode(Path.Combine(root, "viglet", "absent")), out _) &&
              !ProjectSlug.TryProbe(ProjectSlug.Encode(Path.Combine(root, "notadir")), out _),
              "a missing directory and a file are both 'no answer', never a path");
    }

    /// <summary>Every segment of this path is spelled in the alphabet the slug encoding preserves, so a
    /// probe starting at the drive can rebuild it. False on a CI runner whose temp path is an 8.3 short
    /// name (<c>RUNNER~1</c>) — the directory exists as spelled, but <c>RUNNER-1</c> does not.</summary>
    private static bool Probeable(string path)
    {
        foreach (string segment in path.Split('\\', '/'))
        {
            if (segment.Length == 2 && segment[1] == ':') continue;          // the drive prefix
            if (!segment.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- Block K: the tail's cursor

    private static void Tail(string root)
    {
        string dir = Path.Combine(root, "projects", "d--selftest");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "session.jsonl");

        // A complete turn plus a half-written one: the fragment must wait for its newline rather than
        // be parsed as garbage and lost. The partial line is built once and split, or the two halves
        // would carry different timestamps and never rejoin into valid JSON.
        string first = Turn(DateTime.Now, "req-1");
        string partial = Turn(DateTime.Now, "req-2");
        File.WriteAllText(file, first + "\n" + partial[..40]);

        using var tail = new TranscriptTail(Path.Combine(root, "projects"));
        var seen = new List<TailSample>();
        tail.Appended += batch => { lock (seen) seen.AddRange(batch); };
        tail.Start();

        Check("the complete turn is reported", Wait(() => Count(seen) >= 1));
        Check("the half-written turn is held back", Count(seen) == 1, $"{Count(seen)} samples");

        // Completing the line must release exactly that turn.
        File.AppendAllText(file, partial[40..] + "\n");
        Check("completing the line releases the turn", Wait(() => Count(seen) >= 2));
        Check("and releases it exactly once", Count(seen) == 2, $"{Count(seen)} samples");

        // Two more appends, so the cursor is far enough into the file that the rewrite below is
        // genuinely shorter than it — a "truncation" the reader could seek past is not a truncation.
        File.AppendAllText(file, Turn(DateTime.Now, "req-3") + "\n" + Turn(DateTime.Now, "req-4") + "\n");
        Check("an append costs only the appended turns", Wait(() => Count(seen) >= 4));

        // Rotated or truncated mid-write: seeking past the end would silently stop the file forever,
        // so it restarts — and must not report again what it already reported. The new turn needs a
        // second the old ones did not occupy, since the filter after a shrink is "newer than what was
        // already reported" and transcripts are stamped to the second.
        Thread.Sleep(1100);
        int before = Count(seen);
        File.WriteAllText(file, Turn(DateTime.Now.AddSeconds(-30), "req-old") + "\n" +
                                Turn(DateTime.Now, "req-5") + "\n");
        Check("a shrunk file is picked up again", Wait(() => Count(seen) > before));
        Check("and does not re-report what it already had", Count(seen) == before + 1,
              $"{Count(seen) - before} new samples, expected 1");
    }

    // ---------------------------------------------------------------- Block K: the primed cursor

    /// <summary>
    /// T153: a transcript larger than <see cref="TranscriptTail.PrimeBytes"/>, so the first sweep
    /// resumes it 256 KB from the end — <em>mid-line</em> — instead of at offset 0.
    ///
    /// <para>Every fixture the tail has had is a few hundred bytes, so <c>NeedsAlign</c> has never
    /// executed in a check: the priming window, the fragment it lands in and the line boundary it has
    /// to skip forward to were all verified once, by hand, on a real machine. The file here is written
    /// so the prime offset lands inside a <em>fresh</em> turn, which is what makes the assertion sharp:
    /// that turn must be dropped (its first bytes are behind the cursor), every turn after it must
    /// arrive exactly once, and nothing before it may arrive at all.</para>
    /// </summary>
    private static void Primed(string root)
    {
        string dir = Path.Combine(root, "projects", "d--selftest");
        Directory.CreateDirectory(dir);

        // Built as bytes rather than as lines: the property is about a byte offset, so the check has to
        // know where every line starts. `\n` only — WriteAllLines would use CRLF and the arithmetic
        // below would describe a different file from the one on disk.
        DateTime now = DateTime.Now;
        var lines = new List<string>();
        var offsets = new List<long>();
        long at = 0;
        void Append(string line)
        {
            offsets.Add(at);
            lines.Add(line);
            at += Encoding.UTF8.GetByteCount(line) + 1;
        }

        // Bulk that is older than the freshness floor: it exists to push the prime offset past itself,
        // and reporting any of it would be a failure of two things at once.
        for (int i = 0; i < 200; i++) Append(Turn(now.AddDays(-2), $"old-{i}", 100 + i));
        int firstFresh = lines.Count;
        long freshStart = at;

        // Fresh turns, each carrying its own input-token count, so a reported sample says exactly which
        // line it came from. Enough of them to overfill the prime window by a few KB.
        for (int i = 0; at - freshStart < TranscriptTail.PrimeBytes + 4096; i++)
            Append(Turn(now.AddSeconds(-30), $"new-{i}", 1000 + i));

        string file = Path.Combine(dir, "session.jsonl");
        File.WriteAllText(file, string.Join("\n", lines) + "\n");

        // Where the tail will start, and the first line it can report whole: everything up to the next
        // newline is a fragment of a turn nobody is resuming.
        long from = at - TranscriptTail.PrimeBytes;
        int first = 0;
        while (first < offsets.Count && offsets[first] <= from) first++;
        int expected = lines.Count - first;

        if (!Check("the prime offset lands inside a fresh turn, not in the old bulk",
                   first > firstFresh && expected > 0,
                   $"line {first} of {lines.Count}, {at / 1024} KB file, prime at {from / 1024} KB")) return;

        using var tail = new TranscriptTail(Path.Combine(root, "projects"));
        var seen = new List<TailSample>();
        tail.Appended += batch => { lock (seen) seen.AddRange(batch); };
        tail.Start();

        if (!Check("the primed sweep reports the tail of a large transcript",
                   Wait(() => Count(seen) >= expected), $"{Count(seen)} of {expected}")) return;

        TailSample[] got;
        lock (seen) got = seen.ToArray();

        Check("a primed cursor reports every whole turn after it, exactly once",
              got.Length == expected, $"{got.Length} samples, expected {expected}");
        // Min and max rather than first and last: the sweep sorts by timestamp and these turns share a
        // second, so their order is not defined — the *set* is what the property is about.
        Check("the turn the cursor landed inside is dropped, and the next one is not",
              got.Min(s => s.Bits.Input) == 1000 + (first - firstFresh),
              $"oldest reported turn is #{got.Min(s => s.Bits.Input) - 1000}, expected #{first - firstFresh}");
        Check("and nothing behind the prime window is reported",
              got.Max(s => s.Bits.Input) == 1000 + (lines.Count - 1 - firstFresh) &&
              got.All(s => s.Bits.Input >= 1000));

        // The reason the priming is bounded at all: without the cap the first sweep of a long-running
        // session would read the whole history to draw three minutes of chart.
        Check("priming reads the window and not the file",
              tail.Stats.BytesRead == TranscriptTail.PrimeBytes,
              $"{tail.Stats.BytesRead / 1024} KB read of a {at / 1024} KB file " +
              $"({TranscriptTail.PrimeBytes / 1024} KB window)");

        // The alignment is a one-shot: it applies to the sweep that primed the cursor and to no other.
        // Left set, the next append would have its own first line eaten as if it were a fragment — the
        // failure this asserts against, and the only one of these that the flag alone can cause.
        File.AppendAllText(file, Turn(now, "appended", 9999) + "\n");
        Check("an append after a primed read is not re-aligned away",
              Wait(() => Count(seen) == expected + 1), $"{Count(seen)} samples, expected {expected + 1}");
        lock (seen) Check("and arrives whole", seen.Any(s => s.Bits.Input == 9999));
    }

    private static int Count(List<TailSample> seen) { lock (seen) return seen.Count; }

    /// <summary>Wait for a sweep to produce something. The tail is timer-driven (a watcher event, or
    /// the <see cref="TranscriptTail.SweepFloorMs"/> floor), so this is a deadline and not a sleep.</summary>
    private static bool Wait(Func<bool> until, int seconds = 12)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (until()) return true;
            Thread.Sleep(100);
        }
        return until();
    }

    // ---------------------------------------------------------------- synthetic inputs

    private static readonly double Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Local 10:00 two days ago — the hour the intensity fixture makes heavy.</summary>
    private static readonly DateTime HeavyHour = DateTime.Today.AddDays(-2).AddHours(10);

    private static ActivityProfile Flat(double p, double weeks = 6)
    {
        var prof = new ActivityProfile { CoverageWeeks = weeks, Samples = 1 };
        Array.Fill(prof.P, p);
        return prof;
    }

    /// <summary>A grid where no two buckets share a value, so a sum that ignores the index still shows.</summary>
    private static ActivityProfile Varied()
    {
        var prof = new ActivityProfile { CoverageWeeks = 6, Samples = 1 };
        for (int b = 0; b < ActivityProfile.Buckets; b++)
        {
            prof.P[b] = (b * 37 % 101) / 100.0;
            prof.I[b] = 0.5 + (b * 13 % 151) / 100.0;
        }
        return prof;
    }

    /// <summary>Nine-to-six on weekdays, near-idle otherwise — a profile with nights to skip, which is
    /// what makes the resume advice have a question to answer.</summary>
    private static ActivityProfile WorkingHours()
    {
        var prof = new ActivityProfile { CoverageWeeks = 6, Samples = 1 };
        for (int d = 0; d < 7; d++)
            for (int h = 0; h < 24; h++)
            {
                bool weekday = d is >= 1 and <= 5;
                prof.P[d * 24 + h] = weekday && h is >= 9 and <= 18 ? 0.92 : 0.02;
            }
        return prof;
    }

    private static WindowPace Week(double util, double elapsed)
    {
        double win = 7 * 86400;
        return new WindowPace
        {
            Label = "selftest",
            WindowSeconds = win,
            Util = util,
            HasWindow = true,
            ElapsedSeconds = elapsed * win,
            SecondsToReset = (1 - elapsed) * win,
            ResetUnix = Now + (1 - elapsed) * win,
            // Zero forces the profile-calibrated path, which is the one with a closed form to check.
            MeasuredActiveHours = 0,
        };
    }

    /// <summary>True when the synthetic window contains a DST change. The staircase converts local
    /// hours to unix seconds, so across a transition a wall-clock hour is not an hour and the closed
    /// form it is being compared against stops being the right answer — that is the grid working as
    /// designed (a habit is expressed in clock time), not a failure to report.</summary>
    private static bool Straddles(WindowPace w)
    {
        TimeZoneInfo tz = TimeZoneInfo.Local;
        return tz.IsDaylightSavingTime(DateTimeOffset.FromUnixTimeSeconds((long)(w.ResetUnix - w.WindowSeconds))) !=
               tz.IsDaylightSavingTime(DateTimeOffset.FromUnixTimeSeconds((long)w.ResetUnix));
    }

    /// <summary>One assistant line in the shape the transcript readers parse — a timestamp, a usage
    /// block and an id. No content, here as everywhere else.</summary>
    /// <param name="input">Input tokens for the line. Distinct per line where a check needs to say
    /// <em>which</em> line a reported sample came from (T153).</param>
    private static string Turn(DateTime local, string id = "req", int input = 120)
    {
        string ts = local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ",
            System.Globalization.CultureInfo.InvariantCulture);
        return $"{{\"type\":\"assistant\",\"timestamp\":\"{ts}\",\"requestId\":\"{id}-{ts}\"," +
               $"\"cwd\":\"D:\\\\selftest\",\"message\":{{\"id\":\"msg-{id}\",\"model\":\"claude-selftest\"," +
               $"\"usage\":{{\"input_tokens\":{input},\"output_tokens\":340,\"cache_creation_input_tokens\":0," +
               "\"cache_read_input_tokens\":0}}}";
    }

    /// <summary>Nine local days of folded readings ending today, at a given hour coverage and spend —
    /// enough to cover any previous weekly window the ghost asks for.</summary>
    private static List<HourlyDay> FoldedWeek(double coverage, double perHour)
    {
        var days = new List<HourlyDay>();
        for (int d = 8; d >= 0; d--)
        {
            DateTime date = DateTime.Today.AddDays(-d);
            var spend = new double[24];
            var count = new int[24];
            for (int h = 0; h < 24; h++)
            {
                if (h >= 24 * coverage) continue;
                count[h] = 3;
                spend[h] = perHour;
            }
            days.Add(new HourlyDay(date.Year * 10000 + date.Month * 100 + date.Day, spend, count));
        }
        return days;
    }

    /// <summary>
    /// Whole folded weeks, newest first: per week, how many hours of each day carry a reading and how
    /// many of those spent enough to count as active.
    ///
    /// <para>Four days per week rather than seven, at days 2–5 back from each week's start. The week a
    /// folded hour belongs to is <c>(now − then)/7</c>, so an hour at the very edge of a week can be
    /// pushed across it by the time of day the check happens to run — or by a DST change. Both ends are
    /// kept a full day clear of the boundary instead, which costs nothing here: the test is about how
    /// many hours a week holds, not about which day they fall on.</para>
    /// </summary>
    private static List<HourlyDay> FoldedWeeks((int covered, int active)[] weeks)
    {
        var days = new List<HourlyDay>();
        for (int w = 0; w < weeks.Length; w++)
            for (int d = 2; d <= 5; d++)
            {
                DateTime date = DateTime.Today.AddDays(-7 * w - d);
                var spend = new double[24];
                var count = new int[24];
                for (int h = 0; h < weeks[w].covered; h++)
                {
                    count[h] = 3;
                    spend[h] = h < weeks[w].active ? 0.004 : 0;
                }
                days.Add(new HourlyDay(date.Year * 10000 + date.Month * 100 + date.Day, spend, count));
            }
        return days;
    }

    /// <summary>One folded day with a single very heavy hour beside an ordinary one — the shape that
    /// would produce a "this hour is 4× heavy" bucket if nothing shrank or clamped it.</summary>
    private static List<HourlyDay> IntensityDay()
    {
        var spend = new double[24];
        var count = new int[24];
        for (int h = 9; h <= 13; h++) { count[h] = 4; spend[h] = 0.004; }
        spend[HeavyHour.Hour] = 0.20;          // ~50× an ordinary hour, before any guard
        spend[HeavyHour.Hour + 1] = 0.0015;    // active, but barely
        count[HeavyHour.Hour + 5] = 4;         // covered, never active: unknown, not light
        spend[HeavyHour.Hour + 5] = 0;
        DateTime d = HeavyHour.Date;
        return new List<HourlyDay> { new(d.Year * 10000 + d.Month * 100 + d.Day, spend, count) };
    }

    private static void WriteStore(List<HourlyDay> days)
    {
        var sb = new StringBuilder();
        foreach (HourlyDay d in days)
        {
            sb.Append(FormattableString.Invariant($"{{\"d\":{d.Key},\"s\":["));
            for (int h = 0; h < 24; h++) sb.Append(FormattableString.Invariant($"{(h > 0 ? "," : "")}{d.Spend[h]:0.#####}"));
            sb.Append("],\"c\":[");
            for (int h = 0; h < 24; h++) sb.Append($"{(h > 0 ? "," : "")}{d.Count[h]}");
            sb.AppendLine("]}");
        }
        File.WriteAllText(HourlyUsage.FilePath(ProfileKey), sb.ToString());
    }

    // ---------------------------------------------------------------- plumbing

    private static double Unix(DateTime local) => new DateTimeOffset(local).ToUnixTimeSeconds();

    private static double Sum(double[] a) { double s = 0; foreach (double v in a) s += v; return s; }

    private static double Dot(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    private static double Total(List<HourlyDay> days) { double s = 0; foreach (HourlyDay d in days) s += d.DaySpend; return s; }

    /// <summary>Run a body against a throwaway tree, removed whether it passed or threw.</summary>
    private static void Temp(Action<string> body)
    {
        string root = Path.Combine(Path.GetTempPath(), "claude-tray-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try { body(root); }
        catch (Exception e) { Fail("the section threw", e.Message); }
        finally { Remove(root); }
    }

    private static void Remove(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { Console.WriteLine($"  (could not remove {dir})"); }
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
    }

    private static bool Check(string name, bool ok, string detail = "")
    {
        if (ok) { _passed++; Console.WriteLine($"  ok    {name}"); }
        else Fail(name, detail);
        return ok;
    }

    private static void Near(string name, double actual, double expected, double tolerance)
    {
        double delta = Math.Abs(actual - expected);
        // Relative for anything but an exactness claim, so a tolerance means the same thing whether the
        // quantity is a fraction of a window or tokens per second.
        double allowed = tolerance * (tolerance > 0 ? Math.Max(1, Math.Abs(expected)) : 1);
        Check(name, delta <= allowed, $"{actual:0.##########} vs {expected:0.##########} (Δ {delta:0.###e+00})");
    }

    private static void Skip(string name, string why)
    {
        _skipped++;
        Console.WriteLine($"  skip  {name} — {why}");
    }

    private static void Fail(string name, string detail)
    {
        _failed++;
        string line = detail.Length > 0 ? $"{name}: {detail}" : name;
        Console.WriteLine("  FAIL  " + line);
        Failures.Add(line);
    }
}
