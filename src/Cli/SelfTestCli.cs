using System.Reflection;
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

        Section("poll — when the idle is right, and when it is a hole in the history (Block AE)");
        PollIdle();

        Section("states — in the quota, past it and billing, stopped (Block AE)");
        States();

        Section("stores — folding, the ghost, intensity (Block J)");
        string dir = ProfileStore.DirFor(ProfileKey);
        Console.WriteLine($"  scratch profile: {dir}");
        Remove(dir);            // a crashed earlier run must not decide what "already folded" means
        try
        {
            Stores();
            Section("overage — a reading with somewhere to be written (Block AE)");
            Overage();

            Section("probe — the headers verbatim, recorded on a change (Block AE)");
            Probe();
        }
        catch (Exception e) { Fail("the section threw", e.Message); }
        finally { Remove(dir); }

        Section("sweep — the transcript grid and the away-week exclusion (Block J)");
        Temp(Sweep);

        Section("slug — the projects/<slug> encoding (Blocks K and W)");
        Temp(Slug);

        Section("settings — the page's copy and the tray's carry (Blocks S and Z)");
        SettingsRoundTrip();

        Section("lang — five files, one key set and one set of holes (Block AF)");
        Translations();

        Section("out — the directory a capture flag was given (Block AF)");
        Temp(OutputPaths);

        Section("toasts — one colour vocabulary, one fact per colour (Block AF)");
        ToastColours();

        Section("series — what the chart is handed, from readings alone (Block AF)");
        Series();

        Section("names — one automation id, one control (Block AG)");
        AutomationIds();

        Section("format — one number convention per window (Block F)");
        Formatting();

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

    // ---------------------------------------------------------------- Block AE: the poll's own premise

    /// <summary>T180. The idle is right exactly when consumption is frozen, and an account with extra
    /// usage is the case where it is not. Being wrong in that direction is not a stale reading — the poll
    /// is the sampler, so it is a hole in the history across the one stretch that cost money, and no later
    /// task can fill it in.</summary>
    private static void PollIdle()
    {
        long now = (long)Now;
        double r5 = now + 3600, r7 = now + 5 * 86400;

        // The behaviour that must survive: a plain account at its limit still idles to the reset.
        Check("an account at the weekly limit idles until it resets",
              TrayContext.BlockedUntilUnix(0.30, r5, 1.00, r7, null, null, now) == r7);
        Check("and to the soonest reset when both windows are maxed",
              TrayContext.BlockedUntilUnix(1.00, r5, 1.00, r7, null, null, now) == r5);
        Check("an account under the limit never idles",
              TrayContext.BlockedUntilUnix(0.90, r5, 0.90, r7, null, null, now) == 0);
        Check("at the limit with no known reset, the short cadence rather than forever",
              TrayContext.BlockedUntilUnix(1.00, 0, 0.10, r7, null, null, now) == now);

        // T180 itself. Either signal is enough, and the flag alone must do it — an account that has
        // enabled extra usage but not yet spent any reads 0, which is the moment the gap would open.
        Check("extra usage enabled means the poll never sleeps at the limit",
              TrayContext.BlockedUntilUnix(1.00, r5, 1.00, r7, null, true, now) == 0);
        Check("and that holds before a single cent of it is spent",
              TrayContext.BlockedUntilUnix(1.00, r5, 1.00, r7, 0, true, now) == 0);
        Check("an overage figure above zero is enough on its own",
              TrayContext.BlockedUntilUnix(1.00, r5, 1.00, r7, 0.42, null, now) == 0);
        Check("an overage figure above zero outvotes a flag that says otherwise",
              TrayContext.BlockedUntilUnix(1.00, r5, 1.00, r7, 0.42, false, now) == 0);

        // The other direction, which is what keeps the API-cost story honest: nothing about extra usage
        // may make a profile that genuinely stops poll any harder than it did before.
        Check("extra usage explicitly off still idles to the reset",
              TrayContext.BlockedUntilUnix(1.00, r5, 1.00, r7, 0, false, now) == r5);
        Check("an unknown flag with a measured zero still idles",
              TrayContext.BlockedUntilUnix(1.00, r5, 1.00, r7, 0, null, now) == r5);
        Check("and an account nowhere near its limit is unaffected by the flag",
              TrayContext.BlockedUntilUnix(0.10, r5, 0.10, r7, null, true, now) == 0);
    }

    // ---------------------------------------------------------------- Block AE: the three states

    /// <summary>T182. Three surfaces ask the same question — the icon's colour, the tooltip's sentence and
    /// the Settings row — so the answer lives in one place and is asserted once. The state that had never
    /// been modelled is the middle one, and the failure it caused was telling somebody their work had
    /// stopped while they were paying to carry on.</summary>
    private static void States()
    {
        Check("under the limit is in-quota, whatever extra usage says",
              QuotaStates.Resolve(0.90, null, true) == QuotaState.InQuota);
        Check("at the limit with no extra usage is stopped",
              QuotaStates.Resolve(1.00, null, false) == QuotaState.Stopped);
        Check("at the limit with extra usage enabled is billing, not stopped",
              QuotaStates.Resolve(1.00, null, true) == QuotaState.Billing);
        Check("an overage figure above zero is billing on its own",
              QuotaStates.Resolve(1.00, 0.42, null) == QuotaState.Billing);
        Check("a measured zero is not, by itself, billing",
              QuotaStates.Resolve(1.00, 0, null) == QuotaState.Stopped);
        Check("an unknown flag and no figure is stopped — the honest default",
              QuotaStates.Resolve(1.00, null, null) == QuotaState.Stopped);
        Check("the threshold is the same one the poll idles on",
              QuotaStates.Resolve(QuotaStates.AtLimitThreshold, null, false) == QuotaState.Stopped
              && QuotaStates.Resolve(QuotaStates.AtLimitThreshold - 0.001, null, false) == QuotaState.InQuota);

        // T184's rule: the toast marks a *transition*, and both ways of getting it wrong are bad — a
        // missed start says nothing while money is spent, a false one cries wolf about a charge that
        // never began. `null` arms nothing, because a reading nobody took is not an observation of zero.
        Check("leaving a measured zero is the start of spending",
              QuotaStates.StartsSpending(0, 0.02));
        Check("a spell already under way is not a start",
              !QuotaStates.StartsSpending(0.02, 0.31));
        Check("an absent previous reading never fires it",
              !QuotaStates.StartsSpending(null, 0.42));
        Check("nor does an absent current one",
              !QuotaStates.StartsSpending(0, null));
        Check("nor two measured zeros",
              !QuotaStates.StartsSpending(0, 0));
        Check("falling back to zero is not a start either",
              !QuotaStates.StartsSpending(0.42, 0));
        Check("and the next rise after a return to zero is a start again",
              QuotaStates.StartsSpending(0, 0.05));

        // The two answers must agree by construction: "never idle" and "billing" are the same fact, and
        // a tray that sleeps through a state its icon is drawing is the defect T180 and T182 each half-fixed.
        foreach (double? x in new double?[] { null, 0, 0.42 })
            foreach (bool? f in new bool?[] { null, false, true })
                Check($"idle and state agree (extra={x?.ToString() ?? "absent"}, flag={f?.ToString() ?? "unknown"})",
                      (QuotaStates.Resolve(1.00, x, f) == QuotaState.Billing)
                      == (TrayContext.BlockedUntilUnix(1.00, Now + 60, 0.10, Now + 600, x, f, (long)Now) == 0));

        // T208. The overage window's own status is the response stating what the flag and the figure infer,
        // and the observed family is a prefix: `allowed`, `allowed_warning`. Everything else must fail to
        // affirm rather than be guessed at — including a refusal whose spelling nobody here has seen.
        Check("the permitted family is a prefix, not a literal",
              QuotaStates.Allows("allowed") && QuotaStates.Allows("allowed_warning"));
        Check("and nothing else affirms — not unknown, not blank, not absent, not a refusal",
              !QuotaStates.Allows("unknown") && !QuotaStates.Allows("") && !QuotaStates.Allows(null)
              && !QuotaStates.Allows("rejected") && !QuotaStates.Allows("blocked"));

        // The asymmetry is the whole point and must be asserted in both halves, or the next reader
        // "fixes" it into agreement. One reading cannot tell a permission apart from a value every
        // response carries, so the status may buy a poll and may not paint a screen.
        Check("an allowed overage status alone keeps the poll awake",
              TrayContext.BlockedUntilUnix(1.00, Now + 60, 0.10, Now + 600, null, false, (long)Now,
                                           "allowed") == 0);
        Check("and without it the same reading idles, as before",
              TrayContext.BlockedUntilUnix(1.00, Now + 60, 0.10, Now + 600, null, false, (long)Now) > 0);
        Check("a refusal does not keep it awake",
              TrayContext.BlockedUntilUnix(1.00, Now + 60, 0.10, Now + 600, null, false, (long)Now,
                                           "rejected") > 0);
        Check("but the display is unmoved: the status alone never says billing",
              QuotaStates.Resolve(1.00, null, false) == QuotaState.Stopped);

        // Three windows, three status strings, one keyed accessor — and a `_ =>` default arm is how a
        // fourth key would silently read as 5h's.
        // The sentinels are deliberately unpronounceable: a one-letter value collides with the localized
        // window labels the line is built from, which passed for 5h and failed the other two for a reason
        // that had nothing to do with the code under test.
        var statuses = new UsageData { Status = "STAT-5H", Status7d = "STAT-7D", StatusExtra = "STAT-EX" };
        Check("each window's status is reached by its own key",
              statuses.StatusOf("5h") == "STAT-5H" && statuses.StatusOf("7d") == "STAT-7D"
              && statuses.StatusOf("extra") == "STAT-EX");

        // T213. The tooltip's last line must be about the window the rest of the tooltip is about. The
        // three statuses are distinct on purpose: the defect was one window's word shown for another, and
        // only a value that could only have come from the wrong header catches it.
        foreach ((string metric, string want, string wrong) in new[]
                 { ("5h", "STAT-5H", "STAT-7D"), ("7d", "STAT-7D", "STAT-5H"),
                   ("extra", "STAT-EX", "STAT-5H") })
        {
            string line = TrayContext.StatusLine(statuses, metric, "");
            Check($"the status line for {metric} reports {metric}'s own status",
                  line.Contains(want) && !line.Contains(wrong), line);
        }
        Check("and it names the window, so the word is attributed even when the projection is dropped",
              TrayContext.StatusLine(statuses, "7d", "").Contains(L.T("menu.metric.7d")));
    }

    // ---------------------------------------------------------------- Block AE: the header probe

    /// <summary>T181. The probe is only useful if it is still readable after a week of polling, and that
    /// rests entirely on what counts as a change: utilizations move every poll, so recording on any
    /// difference would bury the two transitions the question is about under thousands of lines.</summary>
    private static void Probe()
    {
        const string Util5 = "anthropic-ratelimit-unified-5h-utilization";
        const string Over = "anthropic-ratelimit-unified-overage-utilization";
        const string Status = "anthropic-ratelimit-unified-5h-status";
        // The three headers the first real reading turned up that no name-by-name rule was watching.
        const string OverStatus = "anthropic-ratelimit-unified-overage-status";
        const string Reset5 = "anthropic-ratelimit-unified-5h-reset";
        const string Fallback = "anthropic-ratelimit-unified-fallback";

        static Dictionary<string, string> H(params string[] kv)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
            return d;
        }

        // The shape rule, on its own: what must and must not read as a change.
        Check("a moving utilization is not a change",
              HeaderProbe.Shape(H(Util5, "0.10", Status, "allowed"))
              == HeaderProbe.Shape(H(Util5, "0.97", Status, "allowed")));
        Check("a changed status is a change",
              HeaderProbe.Shape(H(Util5, "0.97", Status, "allowed"))
              != HeaderProbe.Shape(H(Util5, "0.97", Status, "allowed_warning")));
        Check("a header appearing is a change",
              HeaderProbe.Shape(H(Util5, "1.0")) != HeaderProbe.Shape(H(Util5, "1.0", Over, "0")));
        Check("overage leaving zero is a change — the transition the whole probe is for",
              HeaderProbe.Shape(H(Util5, "1.0", Over, "0"))
              != HeaderProbe.Shape(H(Util5, "1.0", Over, "0.02")));
        Check("but overage merely climbing is not",
              HeaderProbe.Shape(H(Util5, "1.0", Over, "0.02"))
              == HeaderProbe.Shape(H(Util5, "1.0", Over, "0.83")));
        Check("header order is not a change",
              HeaderProbe.Shape(H(Util5, "1.0", Over, "0")) == HeaderProbe.Shape(H(Over, "0", Util5, "1.0")));

        // The half of the question still open is what the overage window says while it is being spent, and
        // the account that can answer it sends a status of its own beside the two already watched. A rule
        // that names 5h alone reads that transition as no change at all.
        Check("a changed overage status is a change",
              HeaderProbe.Shape(H(Over, "0.02", OverStatus, "allowed"))
              != HeaderProbe.Shape(H(Over, "0.02", OverStatus, "allowed_warning")));
        Check("a fallback flag changing is a change",
              HeaderProbe.Shape(H(Util5, "1.0", Fallback, "available"))
              != HeaderProbe.Shape(H(Util5, "1.0", Fallback, "unavailable")));
        Check("a reset moving is not a change — it is a clock, not a state",
              HeaderProbe.Shape(H(Util5, "0.10", Reset5, "1785768000"))
              == HeaderProbe.Shape(H(Util5, "0.10", Reset5, "1785786000")));

        // And the same rule through the file, which is where a restart could quietly break it.
        Check("the first reading is always kept", HeaderProbe.Record(ProfileKey, (long)Now, H(Util5, "0.10")));
        Check("an unchanged shape writes nothing",
              !HeaderProbe.Record(ProfileKey, (long)Now + 60, H(Util5, "0.55")));
        Check("the transition into overage writes",
              HeaderProbe.Record(ProfileKey, (long)Now + 120, H(Util5, "1.0", Over, "0.02")));

        List<ProbeEntry> log = HeaderProbe.Load(ProfileKey);
        if (Check("two readings are on file, not three", log.Count == 2, $"{log.Count}"))
        {
            Check("and the values are kept verbatim, not reparsed", log[1].Get(Over) == "0.02");
            Check("with the reading's own timestamp", Math.Abs(log[1].T - (Now + 120)) < 1);
        }

        // An empty header set is an error, not a reading: recording it would put a line in the log for
        // every API outage and make the file a record of the network instead of the account.
        Check("a header-less response is never recorded",
              !HeaderProbe.Record(ProfileKey, (long)Now + 180, new Dictionary<string, string>()));
    }

    // ---------------------------------------------------------------- Block AE: the overage column

    /// <summary>T179's one invariant, and the only one no later task can recover: a reading that carried
    /// no overage figure must come back out of the store as <em>absent</em>, and a measured zero must come
    /// back as a zero. Both are <c>0.0</c> to anything that reads them as a plain double, which is exactly
    /// how a store loses the difference — and the transition worth notifying on is a first departure
    /// <em>from</em> zero, so history that fabricates zeros would announce a charge on a machine that never
    /// spent a cent.</summary>
    private static void Overage()
    {
        long t0 = (long)Now - 300;
        // A line in the shape written before the field existed, then a measured zero, then a real figure.
        UsageHistory.Append(ProfileKey, t0, 0.50, Now + 3600, 0.40, Now + 86400);
        UsageHistory.Append(ProfileKey, t0 + 60, 0.60, Now + 3600, 0.50, Now + 86400, extraUtil: 0);
        UsageHistory.Append(ProfileKey, t0 + 120, 1.00, Now + 3600, 1.00, Now + 86400,
                            extraUtil: 0.42, extraReset: Now + 7200);

        List<UsageSample> read = UsageHistory.Load(ProfileKey, t0 - 1);
        if (!Check("three readings go in and three come back", read.Count == 3, $"{read.Count} read")) return;

        Check("a reading with no overage figure stays absent, not zero", read[0].Extra == null);
        Check("a measured zero stays a measured zero", read[1].Extra is 0);
        Near("an overage figure survives the round trip", read[2].Extra ?? -1, 0.42, 1e-9);
        Near("and so does the deadline it resets on", read[2].ResetExtra, (long)(Now + 7200), 1);

        // The absence has to be in the *line*, not only in the parse: a "ux":0 written for a reading that
        // had none would satisfy every check above and still be a zero nobody measured.
        string first = File.ReadLines(ProfileStore.PathFor(ProfileKey, "usage-history.jsonl")).First();
        Check("an absent figure writes no field at all", !first.Contains("\"ux\""), first);

        Check("the newest reading is what Latest reports, overage included",
              UsageHistory.Latest(ProfileKey) is { Extra: 0.42 });
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

    // ---------------------------------------------------------------- Blocks S/Z: the settings round trip

    /// <summary>
    /// The Settings round trip, one check per property: the window is handed a total copy
    /// (<see cref="Settings.Clone"/>), hands one back, and the tray carries its own fields over
    /// (<see cref="Settings.CarryTrayOwnedFrom"/>). For every property, exactly one of two things must
    /// happen — the page's value survives, or, if the tray owns it, the live value does.
    ///
    /// <para>Driven by reflection over the property list rather than by a list written here, which is the
    /// whole point of T162: a field added tomorrow is covered tomorrow. A property whose type has no
    /// varying rule below <b>fails</b> rather than being skipped, so adding one that this cannot vary is a
    /// red check and not a quiet gap.</para>
    ///
    /// <para><b>Where this stops.</b> The sweep reads the attribute, so it cannot tell that a field
    /// <em>should</em> have been marked — for an unmarked field it asserts the page's value survives, which
    /// is exactly what happens. Nothing here knows which controls the page has. The four named checks above
    /// are therefore not redundant: they pin the fields whose omission has actually shipped a defect, and a
    /// genuinely new tray-owned field still depends on the author marking it (a rule in AGENTS.md).</para>
    /// </summary>
    private static void SettingsRoundTrip()
    {
        // The two shipped defects this replaces, named: both were a tray-owned field with no line in the
        // hand-maintained list, written back stale on every Save.
        foreach (string owned in new[] { nameof(Settings.MonitoredConfigDir), nameof(Settings.Metric),
                                         nameof(Settings.EnvironmentProfileOwned),
                                         nameof(Settings.EnvironmentProfileRestore) })
            Check($"{owned} is declared tray-owned",
                  Settings.TrayOwned.Any(p => p.Name == owned),
                  "T126 and T155 were both this field missing from the carry-over list");

        foreach (PropertyInfo p in typeof(Settings)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.CanWrite))
        {
            bool trayOwned = p.IsDefined(typeof(TrayOwnedAttribute));
            string what = trayOwned ? "the tray's value survives an older window snapshot"
                                    : "the page's edit survives the carry";

            if (Vary(p) is not { } varied)
            {
                Fail($"{p.Name}: {what}", "no varying rule for this property's type — add one");
                continue;
            }
            (object? edited, object? moved) = varied;

            // The window opened, so it holds a copy; then the *menu* moved the tray-owned fields, so the
            // live model and that copy disagree; then Save applies the copy.
            var live = new Settings();
            p.SetValue(live, moved);
            Settings snapshot = live.Clone();
            p.SetValue(snapshot, edited);          // whatever the page did to it, edit or not

            Settings applied = snapshot.Clone();
            applied.CarryTrayOwnedFrom(live);

            object? expected = trayOwned ? moved : edited;
            Check($"{p.Name}: {what}", Json(p.GetValue(applied)) == Json(expected),
                  $"{Json(p.GetValue(applied))} vs {Json(expected)}");
        }
    }

    /// <summary>Two distinct values for a property, both of which survive <see cref="Settings.Clone"/>'s
    /// clamping — null when this property's type (or validator) has no rule here, which the caller reports
    /// as a failure rather than skipping.</summary>
    private static (object? edited, object? moved)? Vary(PropertyInfo p)
    {
        (object? a, object? b)? pair = p.Name switch
        {
            // Validated values: an arbitrary string would be clamped back to the default, and a check
            // over a value the model rejects asserts nothing.
            nameof(Settings.Metric) => ("7d", "extra"),
            nameof(Settings.Language) => ("en", "fr"),
            nameof(Settings.Profiles) => (Profiles(@"d:\one"), Profiles(@"d:\two")),
            _ => p.PropertyType switch
            {
                var t when t == typeof(bool) => (false, true),
                // Off the default rather than from thin air: every int here is clamped to a range the
                // default sits inside, so default±1 is in range without this knowing the range.
                var t when t == typeof(int) => Ints(p),
                var t when t == typeof(string) => ("alpha", "beta"),
                _ => ((object?, object?)?)null,
            },
        };
        if (pair is not { } both) return null;
        (object? a, object? b) = both;

        // Only claim a pair the model actually keeps: setting a value the clamp rewrites would make the
        // check pass or fail for a reason that has nothing to do with the carry.
        foreach (object? v in new[] { a, b })
        {
            var probe = new Settings();
            p.SetValue(probe, v);
            if (Json(p.GetValue(probe.Clone())) != Json(v)) return null;
        }
        return (a, b);
    }

    private static (object? a, object? b) Ints(PropertyInfo p)
    {
        int d = (int)p.GetValue(new Settings())!;
        return (d + 1, d + 2);
    }

    private static List<ClaudeProfile> Profiles(string dir) => new() { new ClaudeProfile { ConfigDir = dir } };

    private static string Json(object? v) => System.Text.Json.JsonSerializer.Serialize(v);

    // ---------------------------------------------------------------- Block AF: the chart's series

    /// <summary>
    /// The three decisions <see cref="UsageReport.FillCurve"/> makes, asserted where they are made rather
    /// than looked for in a screenshot (T189). The one that motivated it: a stored reading carrying **no**
    /// overage figure is skipped, not plotted as zero — the same <c>absent ≠ zero</c> rule T179 built the
    /// store around, one layer up, and reading it wrong would draw a floor along the chart that nobody
    /// measured. A fixture with no nulls in it cannot demonstrate that path, so the screenshot never did.
    ///
    /// <para><c>Curve</c> and <c>Gaps</c> come out of the same method and were asserted by nothing either,
    /// so they are here too: which of the two shaping paths ran, and whether a hole in the readings became
    /// a gap span.</para>
    /// </summary>
    private static void Series()
    {
        const double window = 7 * 86400;
        double reset = 1_800_000_000;           // a fixed clock: the fractions below are then exact
        double start = reset - window;
        double now = start + 0.5 * window;      // half the window elapsed

        // A window as `Fill` would have left it, by hand — the seam is that this needs no profile.
        static WindowPace Win(double reset, double window, double now, double util) => new()
        {
            HasWindow = true, ResetUnix = reset, WindowSeconds = window, Util = util,
            ElapsedSeconds = now - (reset - window),
        };
        UsageSample At(double frac, double util, double? extra) =>
            new(start + frac * window, util, reset, util, reset, extra);

        // ---- the overage series: absent is not zero
        var w = Win(reset, window, now, 1.0);
        UsageReport.FillCurve(w, new(), new()
        {
            At(0.10, 0.30, null),               // no overage figure in this reading at all
            At(0.20, 0.60, 0.0),                // measured, and nothing spent past the quota
            At(0.30, 0.90, null),
            At(0.40, 1.00, 0.25),
            At(0.45, 1.00, 0.10),               // spending can fall back; ExtraMax is the peak, not the last
        }, s => (s.Util7d, s.Reset7d), now);

        Check("a reading with no overage figure is not plotted as a zero",
              w.ExtraCurve.Count == 3, $"{w.ExtraCurve.Count} points, expected the 3 that carry a figure");
        Check("a measured zero is plotted, because it is a measurement",
              w.ExtraCurve.Any(p => p.val == 0.0 && Math.Abs(p.frac - 0.20) < 1e-9));
        Near("the overage axis is scaled to the peak of the kept points", w.ExtraMax, 0.25, 0);
        Check("the overage points come out sorted by time",
              w.ExtraCurve.Select(p => p.frac).SequenceEqual(w.ExtraCurve.Select(p => p.frac).Order()));

        // A window whose readings all lack the figure must draw no second series at all — the state every
        // account was in before T179's column existed, and the one an absent-reads-as-zero bug would fill.
        var none = Win(reset, window, now, 0.5);
        UsageReport.FillCurve(none, new(), new() { At(0.1, 0.2, null), At(0.3, 0.4, null) },
                              s => (s.Util7d, s.Reset7d), now);
        Check("a window with no overage reading anywhere draws no overage series",
              none.ExtraCurve.Count == 0 && none.ExtraMax == 0, $"{none.ExtraCurve.Count} points, max {none.ExtraMax}");

        // ---- which shaping path ran
        Check($"the real-history path needs {UsageReport.MinRealSamples} logged points",
              w.Curve.Count > 2 && Math.Abs(w.Curve[0].frac) < 1e-9,
              "the curve should open at (0,0) and carry the logged points");
        Near("and it lands on the live reading, which is fresher than the last logged point",
             w.Curve[^1].cum, w.Util, 0);
        Near("at the elapsed fraction", w.Curve[^1].frac, 0.5, 1e-9);

        // One logged point is below the threshold, so the token samples shape it instead — scaled to land
        // on the live utilization, whatever the tokens happen to total.
        var thin = Win(reset, window, now, 0.60);
        UsageReport.FillCurve(thin,
            new() { (start + 0.1 * window, new TokenBits(1000, 0, 0, 0)),
                    (start + 0.3 * window, new TokenBits(3000, 0, 0, 0)) },
            new() { At(0.10, 0.20, null) }, s => (s.Util7d, s.Reset7d), now);
        Check("one logged point is not enough, so the token samples shape the curve",
              thin.Curve.Count == 3, $"{thin.Curve.Count} points, expected (0,0) plus the 2 samples");
        Near("and the token curve is scaled to end at the live utilization", thin.Curve[^1].cum, 0.60, 0);
        Check("the tokens themselves are reported, not only used for the shape",
              thin.TokensInWindow == 4000 && thin.RequestsInWindow == 2,
              $"{thin.TokensInWindow} tokens over {thin.RequestsInWindow} requests");

        // ---- gaps: a hole in the readings, not a stray missed poll
        var outage = Win(reset, window, now, 0.50);
        var readings = new List<UsageSample>();
        for (double f = 0.02; f <= 0.20; f += 0.02) readings.Add(At(f, f, null));   // a steady cadence
        for (double f = 0.42; f <= 0.48; f += 0.02) readings.Add(At(f, f, null));   // ...resuming after
        UsageReport.FillCurve(outage, new(), readings, s => (s.Util7d, s.Reset7d), now);
        Check("a stretch with no logged reading is marked as an outage", outage.Gaps.Count == 1,
              $"{outage.Gaps.Count} gaps, expected the one hole between 0.20 and 0.42");
        if (outage.Gaps.Count == 1)
            Check("and the gap is bracketed by the readings either side of the hole",
                  Math.Abs(outage.Gaps[0].f0 - 0.20) < 1e-6 && Math.Abs(outage.Gaps[0].f1 - 0.42) < 1e-6,
                  $"{outage.Gaps[0].f0:0.####} → {outage.Gaps[0].f1:0.####}");

        var steady = Win(reset, window, now, 0.50);
        var even = new List<UsageSample>();
        for (double f = 0.02; f <= 0.50; f += 0.02) even.Add(At(f, f, null));
        UsageReport.FillCurve(steady, new(), even, s => (s.Util7d, s.Reset7d), now);
        Check("an unbroken cadence is no outage at all", steady.Gaps.Count == 0,
              $"{steady.Gaps.Count} gaps over evenly spaced readings");
    }

    // ---------------------------------------------------------------- Block AF: the toast palette

    /// <summary>
    /// What T188 was: two toasts wearing the same clay for opposite news — quota back early, and you have
    /// started paying. A colour on these cards is a claim about what kind of news it is, so the rule is one
    /// row per <see cref="ToastWindow.ToastTheme"/> and no two rows alike, and it is asserted rather than
    /// remembered. Driven off the enum, so a theme added tomorrow is covered tomorrow.
    ///
    /// <para>Reading the palette needs no window: it is a static table of hex strings, which is why it was
    /// worth separating from the brush it feeds.</para>
    /// </summary>
    private static void ToastColours()
    {
        var seen = new Dictionary<string, ToastWindow.ToastTheme>(StringComparer.OrdinalIgnoreCase);
        foreach (ToastWindow.ToastTheme t in Enum.GetValues<ToastWindow.ToastTheme>())
        {
            (string light, string mid, string deep) = ToastWindow.Palette(t);
            string row = $"{light}/{mid}/{deep}";

            if (!Check($"{t} names its own three stops", light.Length > 0 && mid.Length > 0 && deep.Length > 0, row))
                continue;
            if (seen.TryGetValue(row, out ToastWindow.ToastTheme other))
                Fail($"{t} has a colour of its own",
                     $"it wears {row}, which is already {other}'s — the T188 collision, in a new pair");
            else
            {
                _passed++;
                Console.WriteLine($"  ok    {t} has a colour of its own ({mid})");
                seen[row] = t;
            }
        }

        // The one row whose value is a cross-surface agreement rather than a free choice: clay is what the
        // icon's bar and the chart's second axis mean by "past the included quota" (T182, T183, T184).
        Check("extra usage keeps the clay the icon and the chart use",
              ToastWindow.Palette(ToastWindow.ToastTheme.ExtraUsage).Mid.Equals("#D97757", StringComparison.OrdinalIgnoreCase),
              ToastWindow.Palette(ToastWindow.ToastTheme.ExtraUsage).Mid);
    }

    // ---------------------------------------------------------------- Block AF: the capture's output path

    /// <summary>
    /// What every capture flag owes the path it was handed (T187): the file appears, and the directories
    /// above it are created rather than being assumed. The defect this pins was one <c>File.Create</c> on a
    /// path nothing created, throwing from a <c>DispatcherTimer</c> tick <em>after</em> the window had
    /// rendered — so the check is here rather than in a screenshot, which is the one place it could not be
    /// seen. Held on <see cref="OutFile"/>, which the three <c>SaveSnapshot</c> bodies now share.
    /// </summary>
    private static void OutputPaths(string root)
    {
        string nested = Path.Combine(root, "does", "not", "exist", "capture.png");
        using (FileStream fs = OutFile.Create(nested)) fs.WriteByte(0x89);
        Check("a capture into a directory tree that does not exist writes the file",
              File.Exists(nested), $"nothing at {nested}");

        // Truncation, because a second capture over yesterday's PNG must not leave its tail behind.
        using (FileStream fs = OutFile.Create(nested)) fs.WriteByte(0x89);
        Check("and a second capture over the same path truncates it",
              new FileInfo(nested).Length == 1, $"{new FileInfo(nested).Length} bytes, expected 1");

        // A bare filename's GetDirectoryName is empty, which is the case a naive parent-of check skips.
        string cwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(root);
            using (FileStream fs = OutFile.Create("bare.png")) fs.WriteByte(0x89);
            Check("a bare filename resolves against the current directory, not to no parent at all",
                  File.Exists(Path.Combine(root, "bare.png")));
        }
        finally { Directory.SetCurrentDirectory(cwd); }
    }

    // ---------------------------------------------------------------- Block AF: the five string tables

    /// <summary>
    /// The rule <c>lang\*.json</c> exists to keep: a user-visible string is in all five files or in none.
    /// A key that lands only in <c>en</c> falls back silently (<see cref="L.T(string)"/>), so the app reads
    /// correctly in English and stays quietly untranslated everywhere else until somebody opens that screen
    /// in that language — and the stated verification was <c>--lang &lt;code&gt;</c>, which is a person
    /// remembering. Block AE put nineteen keys into five files by hand and ran it for three languages.
    ///
    /// <para>Placeholders are compared too, because <c>{0}</c> present in one language and absent in another
    /// is a formatted string that silently drops a number, and no comparison of key <em>sets</em> sees it.
    /// Every failure names the offending keys rather than counting them: a count is something people learn
    /// to live with.</para>
    ///
    /// <para><b>Where this stops.</b> Whether the Portuguese reads well is not something an assertion can
    /// hold, and this does not pretend to — it is a parity check, not a translation-quality one. It also
    /// cannot see a string hardcoded in XAML, which has no key to be missing.</para>
    /// </summary>
    private static void Translations()
    {
        IReadOnlyDictionary<string, string> en = L.Strings("en");

        // The guard first: a load that silently yielded nothing — a renamed resource, a parse error — would
        // make every comparison below pass over an empty set.
        if (!Check($"the base table loads ({en.Count} keys)", en.Count > 0,
                   "en.json parsed to no keys at all, so nothing below would have compared anything"))
            return;

        foreach (string code in L.Codes)
        {
            if (code == "en") continue;
            IReadOnlyDictionary<string, string> t = L.Strings(code);
            if (!Check($"{code} loads ({t.Count} keys)", t.Count > 0,
                       $"lang\\{code}.json parsed to no keys, and every key would fall back to English"))
                continue;

            string[] missing = Keys(en, k => !t.ContainsKey(k));
            Check($"{code} translates every key en has", missing.Length == 0,
                  Named(missing, "reaching only en"));

            string[] orphan = Keys(t, k => !en.ContainsKey(k));
            Check($"{code} carries no key en does not", orphan.Length == 0,
                  Named(orphan, $"existing in {code} alone, so nothing reads them"));

            string[] slipped = Keys(en, k => t.TryGetValue(k, out string? s) && Holes(en[k]) != Holes(s));
            Check($"{code} keeps every placeholder en has", slipped.Length == 0,
                  Named(slipped, "differing in their {0}-style holes, so a number is dropped or misplaced"));
        }
    }

    // The keys of one table matching a predicate, ordered so the same gap reads the same way twice.
    private static string[] Keys(IReadOnlyDictionary<string, string> table, Func<string, bool> bad) =>
        table.Keys.Where(bad).Order(StringComparer.Ordinal).ToArray();

    /// <summary>The <c>{0}</c>-style holes a string carries, deduplicated and ordered, as one comparable
    /// text. <c>{{</c> is <see cref="string.Format(string, object[])"/>'s escape for a literal brace, so it
    /// is stepped over rather than read as the start of a hole.</summary>
    private static string Holes(string s)
    {
        var found = new SortedSet<int>();
        for (int i = 0; i < s.Length - 1; i++)
        {
            if (s[i] != '{') continue;
            if (s[i + 1] == '{') { i++; continue; }
            int j = i + 1, n = 0;
            while (j < s.Length && char.IsAsciiDigit(s[j])) n = n * 10 + (s[j++] - '0');
            if (j > i + 1) found.Add(n);
        }
        return string.Join(",", found);
    }

    // A failure detail that names the keys instead of counting them, capped so one forgotten file cannot
    // bury the rest of the run.
    private static string Named(string[] keys, string what)
    {
        if (keys.Length == 0) return "";
        string list = string.Join(", ", keys.Take(12));
        return $"{keys.Length} {(keys.Length == 1 ? "key" : "keys")} {what} — {list}" +
               (keys.Length > 12 ? $", … (+{keys.Length - 12} more)" : "");
    }

    // ---------------------------------------------------------------- Block AG: one id, one control

    /// <summary>
    /// An <c>x:Name</c> is a control's identity to everything outside the compiler — UI Automation, a
    /// screen reader, <c>Check-Interaction.ps1</c> — and WPF scopes it per XAML file, so two pages could
    /// each call a control <c>ProfileCombo</c> and the C# in both would still compile. An id lookup then
    /// has two candidates and <c>FindFirst</c> returns whichever the tree reaches first, which depends on
    /// which destinations have been built: a page is built on its first visit and then kept collapsed, so
    /// the answer changes with the route a run took.
    ///
    /// <para>Nothing was wrong on screen when T192 was written, and that was the defect — <c>-Case Names</c>
    /// read the Statistics picker <em>before</em> navigating to Settings, and a comment saying so was the
    /// whole guarantee. The first case that visited Settings and then looked the picker up by id would have
    /// driven the other control and gone on passing. So the rule is asserted here rather than remembered:
    /// <b>an <c>x:Name</c> is unique across the app, not per XAML file.</b>
    ///
    /// <para>The types are <em>derived</em>, never listed: a page added later is covered without an edit
    /// here, because a hardcoded list's failure mode is silently not checking the thing it was written
    /// for (§XV.3). A XAML-backed type is the one that implements <c>IComponentConnector</c>, and its
    /// generated fields are exactly the named elements — <c>internal</c> and a <see cref="DependencyObject"/>,
    /// which is what separates them from the page's own hand-written state.</para>
    /// </summary>
    private static void AutomationIds()
    {
        Type connector = typeof(System.Windows.Markup.IComponentConnector);
        List<Type> pages = typeof(SelfTestCli).Assembly.GetTypes()
            .Where(t => t.IsClass && connector.IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        // The guard first, on the precondition and not on a weaker form of the property: reflection
        // yielding nothing would report a clean run over zero controls, which is §XV.3's defect again.
        if (!Check("every XAML-backed window and page is reachable by reflection", pages.Count >= 5,
                   $"found {pages.Count}: {string.Join(", ", pages.Select(p => p.Name))}"))
            return;

        Dictionary<string, List<string>> owners = new(StringComparer.Ordinal);
        foreach (Type page in pages)
            foreach (FieldInfo f in page.GetFields(BindingFlags.Instance | BindingFlags.NonPublic |
                                                   BindingFlags.Public | BindingFlags.DeclaredOnly))
                if (f.IsAssembly && typeof(System.Windows.DependencyObject).IsAssignableFrom(f.FieldType))
                {
                    if (!owners.TryGetValue(f.Name, out List<string>? on)) owners[f.Name] = on = new();
                    on.Add(page.Name);
                }

        if (!Check("and the named controls in them are found", owners.Count >= 50,
                   $"{owners.Count} named controls across {pages.Count} types — too few to be the real set"))
            return;

        string[] shared = owners.Where(kv => kv.Value.Count > 1)
                                .Select(kv => $"{kv.Key} ({string.Join(" + ", kv.Value)})")
                                .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Check($"no x:Name is carried by two controls ({owners.Count} across {pages.Count} types)",
              shared.Length == 0,
              shared.Length == 0 ? "" : $"{shared.Length} shared — {string.Join("; ", shared)}");

        // Every id `Check-Interaction.ps1` looks a control up by, because that lookup is a *string* and no
        // compiler checks it. Renaming `StatusText` while doing T192 broke the one behind T166's "the status
        // line must never be observed at all" — and a lookup that finds nothing makes that assertion pass by
        // seeing nothing, which is §XX.2's defect exactly. A rename is now a red `--selftest`, not a case
        // that quietly stops asserting. Keep this list and the script's lookups in step.
        string[] driven =
        {
            "DirectoryBox", "RetrySlider",                                   // -Case Keyboard
            "StatsStatusText", "UsedS", "UsedW", "ResetS", "ResetW",          // -Case Panes / Profiles
            "LiveHeadS", "LiveHeadW", "StatsProfileCombo",
            "NavSettings", "MethodInfo",                                     // -Case Names
            "LanguageCombo", "StartupCheck", "IntervalSlider",
        };
        string[] gone = driven.Where(id => !owners.ContainsKey(id)).ToArray();
        Check($"every id the interaction check drives still exists ({driven.Length})", gone.Length == 0,
              gone.Length == 0 ? "" : $"{gone.Length} gone — {string.Join(", ", gone)}: renamed without " +
                                      "updating scripts\\Check-Interaction.ps1, whose lookups are strings");
    }

    // ---------------------------------------------------------------- Block F: one number convention

    /// <summary>
    /// T167: the Statistics page states its numbers in one convention, whatever the machine's locale is.
    /// Thirteen formatters ended in <c>Fmt</c> and the method note's five interpolations did not, so on a
    /// pt-BR machine the English popup read <em>"4,7 weeks of local transcripts"</em> eight lines above
    /// <c>≈ 1,319 tok/s</c> and <c>40%</c> — in both verification screenshots of T159 and T163, unseen.
    ///
    /// <para>The formatters are <b>derived, never listed</b>, for the same reason the automation-id sweep
    /// derives its pages: a hardcoded list stops covering whatever is written next, which is the defect
    /// the note itself was. Every static method on the page that turns numbers into a string is run
    /// twice, once under each culture, and any that answers differently has read the OS.</para>
    ///
    /// <para>What this cannot reach is an interpolation written <em>inline</em> in <c>Render</c> rather
    /// than in a formatter — the note's own shape until this task, and the reason §XV.2 (T168) wants that
    /// composition out of UI code where an assertion can call it.</para>
    /// </summary>
    private static void Formatting()
    {
        System.Globalization.CultureInfo before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // Cloned from invariant so *only* the number conventions differ: a real locale would also
            // change month names, which `DateFmt` owns deliberately and this sweep is not about.
            var hostile = (System.Globalization.CultureInfo)
                System.Globalization.CultureInfo.InvariantCulture.Clone();
            hostile.NumberFormat.NumberDecimalSeparator = ",";
            hostile.NumberFormat.NumberGroupSeparator = ".";
            hostile.NumberFormat.NegativeSign = "~";

            // Gate on the precondition, not on a weaker form of the property (§XV.3): a clone that did not
            // take would leave every comparison below invariant against invariant, passing by asserting
            // nothing at all.
            string probe = 4.7.ToString("0.#", hostile);
            if (!Check("the probe culture really writes numbers differently", probe == "4,7", $"got \"{probe}\""))
                return;

            static bool Numeric(Type t) => t == typeof(double) || t == typeof(int) || t == typeof(long);

            MethodInfo[] formatters = typeof(StatisticsPage)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public |
                            BindingFlags.DeclaredOnly)
                .Where(m => m.ReturnType == typeof(string) && !m.Name.Contains('<') &&
                            m.GetParameters().Length > 0 &&
                            m.GetParameters().All(p => Numeric(p.ParameterType) || p.HasDefaultValue))
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToArray();

            if (!Check("the page's own number formatters are reachable by reflection", formatters.Length >= 10,
                       $"found {formatters.Length}: {string.Join(", ", formatters.Select(m => m.Name))}"))
                return;

            // Spread across every branch these have: under a k, under a M, a fraction, a whole, a unix
            // second. A single value would exercise one arm of a ternary and call the rest checked.
            double[] values = { 0, 0.5, 1.5, 4.7, 42, 99.5, 1234.5, 1_500_000, 1_700_000_000 };
            List<string> leaked = new(), threw = new();
            int compared = 0;

            foreach (MethodInfo m in formatters)
                foreach (double v in values)
                {
                    object?[] args = m.GetParameters()
                        .Select(p => Numeric(p.ParameterType)
                            ? Convert.ChangeType(v, p.ParameterType)
                            : p.DefaultValue)
                        .ToArray();
                    try
                    {
                        System.Globalization.CultureInfo.CurrentCulture =
                            System.Globalization.CultureInfo.InvariantCulture;
                        string a = (string)m.Invoke(null, args)!;
                        System.Globalization.CultureInfo.CurrentCulture = hostile;
                        string b = (string)m.Invoke(null, args)!;
                        compared++;
                        if (a != b) leaked.Add($"{m.Name}({v:0.###}) → \"{a}\" / \"{b}\"");
                    }
                    catch (Exception e) { threw.Add($"{m.Name}({v:0.###}): {(e.InnerException ?? e).Message}"); }
                }

            System.Globalization.CultureInfo.CurrentCulture =
                System.Globalization.CultureInfo.InvariantCulture;

            // A throw is not a pass. A formatter that blew up on a probe was never compared, and counting
            // it as clean is the same silence as a Skip that hides what it guards.
            Check($"every formatter answered both cultures ({formatters.Length} × {values.Length})",
                  threw.Count == 0, string.Join("; ", threw.Take(4)));
            Check($"no formatter on the page reads the OS number format ({compared} comparisons)",
                  leaked.Count == 0,
                  leaked.Count == 0 ? "" : $"{leaked.Count} leaked — {string.Join("; ", leaked.Take(6))}");

            // And the helper itself, by name, since everything above only says the page agrees with itself.
            System.Globalization.CultureInfo.CurrentCulture = hostile;
            Check("Num keeps the decimal point a point", StatisticsPage.Num(4.7) == "4.7", StatisticsPage.Num(4.7));
            Check("Num's whole form takes no group separator", StatisticsPage.Num(1234, "0") == "1234",
                  StatisticsPage.Num(1234, "0"));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
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
