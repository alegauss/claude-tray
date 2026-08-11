using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

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

    private static int _passed, _failed;
    private static readonly List<string> Failures = new();

    /// <summary>What did not run, by name (T169). The count alone cannot tell <em>"this run straddled a
    /// DST change"</em> from <em>"this environment has skipped these two forever"</em>, and the second is
    /// lost coverage that reads exactly like a clean run.</summary>
    private static readonly List<(string Name, string Why)> Skipped = new();

    /// <summary>
    /// The skips this suite is allowed to have, by name — so an <b>unexpected</b> one is a red run and a
    /// known one is not (T218).
    ///
    /// <para>T169 made every skip print its name and deliberately left this policy open: whether an
    /// unexpected skip should fail the build was "a judgement to make with the list in hand", and the
    /// list now exists. Until now <c>--selftest</c> exited 0 with any number of skips and
    /// <c>check.yml</c> read only the exit code, so a green run and a green run that checked two fewer
    /// things were the same colour — which is T169's own defect moved from one guard to the whole
    /// suite.</para>
    ///
    /// <para><b>Failing on any skip would be wrong.</b> Two of these are genuinely conditional on the
    /// machine and the moment, so a blunt rule makes the suite flaky, and a flaky suite gets ignored —
    /// which costs more coverage than the skips do. A count (<c>--max-skips</c>) is the cheap form and
    /// nobody maintains a number; naming them means a skip that <em>stops</em> happening is as visible as
    /// one that starts, which is why the reasons are here rather than in a comment.</para>
    ///
    /// <para>Each entry says what it costs to allow it. A skip that is <em>always</em> expected where this
    /// runs is a check that does not exist there, and the honest response is to read that line and decide,
    /// not to let the exit code stay quiet about it.</para>
    /// </summary>
    private static readonly (string Name, string Why)[] AllowedSkips =
    {
        ("the resume hour closes the week under its own target",
         "the synthetic week does not always produce a profile whose resume hour lands under its target"),
        ("the probe backtracks past a shorter directory that exists",
         "a temp path whose encoding cannot be rebuilt - unrepresentable, not an unwritten case (T169)"),
        ("the probe returns only directories that exist",
         "the same temp path, in the same run"),
        // The repository family is not here on purpose (T247): `Repo` carries its own allowance, so the
        // seven sentences that used to sit in this list are one, and the eighth check inherits it.
    };

    /// <returns>Process exit code: 0 when every check passed.</returns>
    public static int Run(string[] flags)
    {
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

        Section("flags — the preview and capture surface (Block AI)");
        Flags();

        Section("tooltip — what fits in the tray's 127 characters (Block AI)");
        Tooltip();

        Section("spell — how far back the overage reaches (Block A)");
        Spell();

        Section("switch — what a change of monitored account drops (Block A)");
        MonitoredHandover();

        Section("picker — the report follows the icon by key, not by position (Block F)");
        ProfilePicker();

        Section("out — the directory a capture flag was given (Block AF)");
        Temp(OutputPaths);

        Section("toasts — one colour vocabulary, one fact per colour (Block AF)");
        ToastColours();

        Section("series — what the chart is handed, from readings alone (Block AF)");
        Series();

        Section("names — one automation id, one control (Block AG)");
        AutomationIds();

        Section("effective — which profile the environment selects (Block AC)");
        EffectiveProfile();

        Section("probe — what the flag was asked to do, before it does any of it (Block AI)");
        ProbePlanning();

        Section("probe — which arriving headers this app actually reads (Block AI)");
        Readership();

        Section("glyphs — every card's emoji is in the font the card names (Block E)");
        ToastGlyphs();

        Section("the extra-usage card — a bar only for a figure it has (Block E)");
        ExtraCardBar();

        Section("the extra-usage alarm — a seed, a rise, and one announcement per spell (Block E)");
        ExtraAlarm();

        Section("projects — a count of directories, not of keys (Block N)");
        ProjectCount();

        Section("note — which paragraphs the method note yields (Block F)");
        MethodNote();

        Section("format — one number convention, on every surface that states one (Blocks F and G)");
        Formatting();

        Section("ledger — every block heading has a row in its own index (Block AJ)");
        LedgerIndex();

        Section("map — every source file has a row, and every row a file (Block AJ)");
        FileMap();

        Section("intensity — the line that names its own axis (Block J)");
        IntensityAxis();

        Section("warnings — the gate that keeps a build log worth reading (Block AI)");
        WarningGate();

        Section("report — one label, one quantity (Block I)");
        ReportLabels();

        Section("prompt — what the cleanup prompt carries out of the app (Block I)");
        CleanupPrompt();

        Section("fixture — the overage preview produces the state it exists for (Block AI)");
        OveragePreview();

        Section("dates — the fields are in the order the culture puts them (Block G)");
        DateOrder();

        Section("names — a page or destination this build does not have is refused (Block AI)");
        PageNames();

        Section("read-out — a scan that could not start exits 1 (Block I)");
        ReadOutExit();

        Section("lang — a code this build does not ship is refused, not defaulted (Block AI)");
        LangOverride();

        Section("scripts — every .ps1 is one PowerShell 5.1 can read (Block AI)");
        ScriptEncoding();

        Section("console — the code page is set where the flags are dispatched (Block AI)");
        ConsoleCodePage();

        Section("source — what a scan of this repository reads as code (Block AI)");
        CodeReading();

        Section("parser — the names it spells and the names it reports are one set (Block AI)");
        ParserNames();

        Section("scanning — the flag scan reads code, not the prose about it (Block AI)");
        FlagScanning();

        Section("catalogue — every flag the sources accept is declared (Block AJ)");
        FlagCatalogue();

        Section("anchoring — a transcript reached by a route nobody walked (Block AK)");
        Anchoring();

        Section("surfaces — what a stranger reads: the README and the page (Block AJ)");
        Repo("the README and the published page point at things that exist", UserSurfaces,
             "README.md", "docs/index.html", "docs");

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

            Section("tail — where a fan-out's agents file (Block K)");
            Temp(FanOut);

            Section("tail — what 'sessions active' counts (Block K)");
            Temp(Active);

            Section("sessions — one row per conversation (Block AK)");
            Temp(Sessions);

            Section("sessions — the one field a person wrote (Block AK)");
            Temp(OpeningPrompt);

            Section("sessions — the call tree under one conversation (Block AK)");
            Temp(Tasks);
        }

        // Last, and nothing may be added after it: sampling the environment is one-way for the process
        // (T231), so every section that reads the machine's real CLAUDE_CONFIG_DIR has to be above.
        Section("sampled environment — the states this machine is never in (Block AI)");
        SampledEnvironment();

        // After the sampling section and after everything else: Observe() is one-way too, and it is the
        // stricter of the two — from here on this process writes nothing at all (T239).
        Section("observing tray — a check that adds nothing to the user's files (Block AI)");
        ObservingTray();

        double ms = (DateTime.UtcNow.Ticks - started) / (double)TimeSpan.TicksPerMillisecond;
        Console.WriteLine();
        foreach (string f in Failures) Console.WriteLine("FAILED  " + f);

        // Beside the counts, where the exit code is read: a name is what makes lost coverage legible.
        // Each one says what allowing it costs, so the line is a decision and not a note (T218).
        foreach ((string name, string why) in Skipped)
        {
            string? allowed = AllowedSkips.FirstOrDefault(a => a.Name == name).Why
                              ?? (RepoSkipped.Contains(name) && !OnCi ? "an installed copy carries no repository (T247)" : null);
            Console.WriteLine($"SKIPPED {name} — {why}");
            if (allowed is not null) Console.WriteLine($"        allowed: {allowed}");
        }

        (string Name, string Why)[] unexpected = Skipped
            .Where(s => AllowedSkips.All(a => a.Name != s.Name))
            .Where(s => !RepoSkipped.Contains(s.Name) || OnCi)   // allowed off a checkout, never on CI (T247)
            .ToArray();
        foreach ((string name, string why) in unexpected)
            Console.WriteLine($"UNEXPECTED SKIP  {name} — {why}: nobody decided to lose this one. " +
                              (RepoSkipped.Contains(name)
                                  ? "CI runs this build from the checkout, so the repository is here and " +
                                    "RepoFile stopped finding it (T247)."
                                  : "Fix the reason, or add it to AllowedSkips with what allowing it costs."));

        // The other direction, and the reason this is a list of names rather than a number: an allowed
        // skip that has stopped happening is coverage regained, or a check that has quietly been renamed
        // out from under this list. Reported, never failed — most of these are absent on most machines.
        string[] absent = AllowedSkips.Where(a => Skipped.All(s => s.Name != a.Name))
                                      .Select(a => a.Name).ToArray();
        if (absent.Length > 0)
            Console.WriteLine($"({absent.Length} allowed skip(s) did not happen here: {string.Join("; ", absent)})");

        Console.WriteLine($"{_passed} passed, {_failed} failed" +
                          (Skipped.Count > 0 ? $", {Skipped.Count} skipped" : "") +
                          (unexpected.Length > 0 ? $", {unexpected.Length} UNEXPECTED" : "") + $" — {ms:0}ms");
        return _failed == 0 && unexpected.Length == 0 ? 0 : 1;
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

        // T276: the same rule asked of the header that actually moves. The spell of 2026-08-04 is the
        // pair at the top — a figure that never left zero while the boolean crossed — and every asymmetry
        // above is repeated here rather than assumed to carry over.
        Check("the figure route is blind to a spell whose utilization never leaves zero",
              !QuotaStates.StartsSpending(0.0, 0.0) && QuotaStates.StartsSpending(false, true));
        Check("a boolean already true is a spell in progress, not its beginning",
              !QuotaStates.StartsSpending(true, true));
        Check("an absent previous reading never fires it — absent is not false",
              !QuotaStates.StartsSpending((bool?)null, true));
        Check("nor does an absent current one",
              !QuotaStates.StartsSpending(false, (bool?)null));
        Check("nor the crossing running backwards",
              !QuotaStates.StartsSpending(true, false));

        // The latch both routes share is released by a reading measured back inside the quota, and by
        // nothing weaker: a figure climbing mid-spell must not re-arm the announcement.
        Check("a measured no on either header is back inside the quota",
              QuotaStates.BackInsideQuota(false, null) && QuotaStates.BackInsideQuota(null, 0.0));
        Check("a spell in progress is not, on either header",
              !QuotaStates.BackInsideQuota(true, 0.0) && !QuotaStates.BackInsideQuota(false, 0.42));
        Check("and a reading carrying neither header says nothing",
              !QuotaStates.BackInsideQuota(null, null));

        // The two answers must agree by construction: "never idle" and "billing" are the same fact, and
        // a tray that sleeps through a state its icon is drawing is the defect T180 and T182 each half-fixed.
        // The refusal is swept with them (T224): it is the one signal both sides now read, so it is the one
        // that could put them back into disagreement. The affirmative is deliberately left out — there the
        // two must differ, which is asserted on its own below.
        foreach (double? x in new double?[] { null, 0, 0.42 })
            foreach (bool? f in new bool?[] { null, false, true })
                foreach (string? why in new string?[] { null, "org_level_disabled" })
                {
                    string status = why is null ? "unknown" : "rejected";
                    Check($"idle and state agree (extra={x?.ToString() ?? "absent"}, " +
                          $"flag={f?.ToString() ?? "unknown"}, refused={why is not null})",
                          (QuotaStates.Resolve(1.00, x, f, status, why) == QuotaState.Billing)
                          == (TrayContext.BlockedUntilUnix(1.00, Now + 60, 0.10, Now + 600, x, f, (long)Now,
                                                           status, why) == 0));
                }

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

        // T224. The negative the affirmative could not take. Both signals were measured on the same account
        // on 2026-08-03 — `rejected` with no overage utilization or reset at all, and a reason header that
        // exists only because something said no — so either is enough, and neither may be inferred.
        Check("a rejected status is a refusal, and so is the reason header on its own",
              QuotaStates.Refuses("rejected", null) && QuotaStates.Refuses(null, "org_level_disabled")
              && QuotaStates.Refuses("rejected", "org_level_disabled"));
        Check("and nothing else refuses — not allowed, not unknown, not blank, not absent",
              !QuotaStates.Refuses("allowed", null) && !QuotaStates.Refuses("unknown", null)
              && !QuotaStates.Refuses("", "") && !QuotaStates.Refuses(null, null)
              && !QuotaStates.Refuses(null, "   "));
        Check("a refusal beats the local flag: enabled in .claude.json, disabled by the organisation",
              QuotaStates.Resolve(1.00, null, true, "rejected", "org_level_disabled") == QuotaState.Stopped
              && QuotaStates.Resolve(1.00, null, true) == QuotaState.Billing);
        Check("but it does not beat an account observed spending — a pair nothing has ever sent",
              QuotaStates.Resolve(1.00, 0.42, true, "rejected", "org_level_disabled") == QuotaState.Billing);
        Check("and under the limit it changes nothing: a refusal is not a state of its own",
              QuotaStates.Resolve(0.90, null, true, "rejected", "org_level_disabled") == QuotaState.InQuota);
        Check("the poll idles on a refusal it would have stayed awake for",
              TrayContext.BlockedUntilUnix(1.00, Now + 60, 0.10, Now + 600, null, true, (long)Now,
                                           "rejected", "org_level_disabled") > 0
              && TrayContext.BlockedUntilUnix(1.00, Now + 60, 0.10, Now + 600, null, true, (long)Now) == 0);

        // T273. The affirmative that can be told from a default. `overage-status: allowed` arrives on an
        // account inside its quota, so it may buy a poll and may not paint a screen; `overage-in-use` was
        // absent on nine readings inside the quota — one at 0.91 — and true on the first past it. The two
        // are held apart here or the next reader collapses them into one rule.
        Check("the in-use header paints the screen the allowed status may not",
              QuotaStates.Resolve(1.00, null, false, "allowed", null, extraInUse: true) == QuotaState.Billing
              && QuotaStates.Resolve(1.00, null, false, "allowed") == QuotaState.Stopped);
        Check("it beats the local flag, which is read out of a file Claude Code writes",
              QuotaStates.Resolve(1.00, null, false, null, null, extraInUse: true) == QuotaState.Billing);
        Check("and only its affirmative moves anything — absent and false both leave the flag answering",
              QuotaStates.Resolve(1.00, null, false, null, null, extraInUse: false) == QuotaState.Stopped
              && QuotaStates.Resolve(1.00, null, false, null, null, extraInUse: null) == QuotaState.Stopped
              && QuotaStates.Resolve(1.00, null, true, null, null, extraInUse: false) == QuotaState.Billing);
        Check("a measured refusal still outranks it — nothing has sent rejected and in-use together",
              QuotaStates.Resolve(1.00, null, true, "rejected", "org_level_disabled", true) == QuotaState.Stopped);
        Check("under the limit it is not a state of its own either",
              QuotaStates.Resolve(0.90, null, false, null, null, extraInUse: true) == QuotaState.InQuota);
        Check("and the poll stays awake wherever the display says billing on it",
              TrayContext.BlockedUntilUnix(1.00, Now + 60, 0.10, Now + 600, null, false, (long)Now,
                                           null, null, true) == 0
              && TrayContext.BlockedUntilUnix(1.00, Now + 60, 0.10, Now + 600, null, false, (long)Now,
                                              "rejected", null, true) > 0);

        // T274. The verdict is about the account, not about the window the icon happens to show. The
        // reading is the one measured on 2026-08-04: 5h at 1.02 rejected beside 7d at 0.47 allowed, with
        // the overage figure at zero throughout — so nothing but the bounded windows can carry it.
        var crossed = new UsageData
        {
            Session5h = 1.02, Week7d = 0.47, Extra = 0, HasExtra = true,
            Status = "rejected", Status7d = "allowed", StatusExtra = "allowed",
        };
        Check("an account past its quota on the session reads as billing whichever window is shown",
              QuotaStates.Resolve(crossed, true) == QuotaState.Billing);
        Check("and the week alone would have said in-quota, which is the display option that hid it",
              QuotaStates.Resolve(crossed.Week7d, crossed.ExtraUtil, true) == QuotaState.InQuota);
        Check("the overage window does not vote: its 100% denominates nothing any header states",
              QuotaStates.Resolve(new UsageData { Session5h = 0.40, Week7d = 0.40, Extra = 1.0,
                                                  HasExtra = true }, true) == QuotaState.InQuota);
        Check("and an account inside both bounded windows is still in quota",
              QuotaStates.Resolve(new UsageData { Session5h = 0.40, Week7d = 0.90 }, true)
              == QuotaState.InQuota);

        // T320. The other half of the same split: the figure on the icon, not just the verdict behind it.
        // The reading is the one measured on 2026-08-07 — a session with room behind a week that crossed,
        // with `overage-in-use` carrying the news and no overage figure at all.
        var weekGone = new UsageData
        {
            Session5h = 0.58, Week7d = 1.00, Extra = 0, HasExtra = true,
            Status = "allowed", Status7d = "allowed", ExtraInUse = true,
        };
        Check("inside the quota the menu's pick is what the number is about, all three of them",
              QuotaStates.IconWindow(weekGone, "5h", QuotaState.InQuota) == "5h"
              && QuotaStates.IconWindow(weekGone, "7d", QuotaState.InQuota) == "7d"
              && QuotaStates.IconWindow(weekGone, "extra", QuotaState.InQuota) == "extra");
        Check("past it the figure moves to the window that crossed, not the one the menu picked",
              QuotaStates.IconWindow(weekGone, "5h", QuotaState.Billing) == "7d");
        Check("and it is the crossed window either way round: a rejected session behind a week with room",
              QuotaStates.IconWindow(crossed, "7d", QuotaState.Billing) == "5h");
        Check("stopped moves it too — work having actually halted is not a reason to show a window with room",
              QuotaStates.IconWindow(weekGone, "5h", QuotaState.Stopped) == "7d");
        Check("`extra` is exempt: it is the one metric that exists for exactly this state",
              QuotaStates.IconWindow(weekGone, "extra", QuotaState.Billing) == "extra"
              && QuotaStates.IconWindow(weekGone, "extra", QuotaState.Stopped) == "extra");
        // The pairing that makes the number honest, asserted rather than reasoned: whenever the state says
        // the included quota is gone, the window the icon names is one whose own reading says so. Held over
        // both measured crossings, in either order, because `WorstBounded` is what Resolve maximised.
        foreach (UsageData d in new[] { weekGone, crossed })
            foreach (string picked in new[] { "5h", "7d" })
            {
                QuotaState st = QuotaStates.Resolve(d, true);
                Check($"the window the icon names is one that crossed ({d.Session5h:0.00}/{d.Week7d:0.00}, {picked})",
                      st == QuotaState.InQuota
                      || d.Metric(QuotaStates.IconWindow(d, picked, st)) >= QuotaStates.AtLimitThreshold);
            }

        // T281. The threshold the window crossed, named by the API instead of chosen here. The readings are
        // the ones on file: absent inside the quota, 0.9 beside the first `allowed_warning` at 0.91, 1.0
        // beside `rejected` at 1.02.
        Check("with no header the constant answers, exactly as before",
              QuotaStates.Warns(0.91, null) && !QuotaStates.Warns(0.89, null));
        Check("and the threshold the response named outranks it, in both directions",
              QuotaStates.Warns(0.72, 0.70) && !QuotaStates.Warns(0.95, 0.98));
        Check("the measured pair still warns: 0.9 named beside a utilization of 0.91",
              QuotaStates.Warns(0.91, 0.9));
        Check("a threshold nobody here has seen needs no mapping — it is a number, not a word",
              QuotaStates.Warns(0.63, 0.60) && QuotaStates.Warns(1.00, 1.0)
              && !QuotaStates.Warns(0.55, 0.60));
        Check("and an absent header is not a threshold of zero, which every reading would be past",
              !QuotaStates.Warns(0.10, null));

        // The cause, as words. Only one value has ever been sent, so only one is translated: anything else
        // is shown verbatim rather than explained, because a wrong reason for stopped work is worse than a
        // raw token — and `RefusalReason` must stay null where there is nothing to explain, or the sub-line
        // on the System page appears under every account that was never refused.
        Check("the measured reason gets words, and an unseen one is quoted rather than guessed",
              QuotaStates.RefusalReason("org_level_disabled") == L.T("quota.refused.orgLevelDisabled")
              && QuotaStates.RefusalReason("some_new_reason") == L.T("quota.refused.other", "some_new_reason"));
        Check("and no reason is no sentence",
              QuotaStates.RefusalReason(null) is null && QuotaStates.RefusalReason("  ") is null);

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

        Vocabulary();
    }

    /// <summary>T210. Three headers here are read by nothing because their vocabulary is one value from one
    /// account, and "has a second value arrived?" was a question answered by eye across up to 500 readings.
    /// The read-out only replaces that eye if it collapses a value repeated every poll, keeps a value seen
    /// once, and refuses to summarise the figures that take a new value every poll — which would bury the
    /// four entries it exists to show under the log's own length.</summary>
    private static void Vocabulary()
    {
        const string Claim = "anthropic-ratelimit-unified-representative-claim";
        const string Util5 = "anthropic-ratelimit-unified-5h-utilization";
        const string Reset5 = "anthropic-ratelimit-unified-5h-reset";
        const string Fallback = "anthropic-ratelimit-unified-fallback";

        static ProbeEntry E(double t, params string[] kv)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
            return new ProbeEntry(t, d);
        }

        // The state one real account is in: the same claim on every reading, while the utilization moves.
        var oneAccount = new List<ProbeEntry>
        {
            E(Now,       Claim, "five_hour", Util5, "0.10", Reset5, "1785768000", Fallback, "available"),
            E(Now + 60,  Claim, "five_hour", Util5, "0.55", Reset5, "1785768000", Fallback, "available"),
            E(Now + 120, Claim, "five_hour", Util5, "0.97", Reset5, "1785786000", Fallback, "available"),
        };
        List<HeaderVocab> v = HeaderProbe.Vocabulary(oneAccount);

        Check("a utilization has no vocabulary — it takes a new value every poll",
              v.All(h => h.Name != Util5));
        Check("nor does a reset, for the same reason", v.All(h => h.Name != Reset5));
        Check("the categorical headers all have one", v.Count == 2, $"{v.Count}");

        HeaderVocab claim = v.First(h => h.Name == Claim);
        Check("one value repeated on every reading is one entry, not three",
              claim.Values.Count == 1, $"{claim.Values.Count}");
        Check("and it has not moved — the state that keeps three headers unparsed", !claim.Moved);
        if (Check("counted over every reading it appeared in", claim.Values[0].Count == 3,
                  $"{claim.Values[0].Count}"))
        {
            Check("spanning first sighting to last",
                  Math.Abs(claim.Values[0].First - Now) < 1
                  && Math.Abs(claim.Values[0].Last - (Now + 120)) < 1);
            Check("with the value kept verbatim", claim.Values[0].Value == "five_hour");
        }

        // The reading the whole instrument is for: a second value, arriving once, late.
        var moved = new List<ProbeEntry>(oneAccount) { E(Now + 180, Claim, "seven_day", Util5, "1.00") };
        HeaderVocab after = HeaderProbe.Vocabulary(moved).First(h => h.Name == Claim);
        Check("a second value is reported rather than averaged away",
              after.Values.Count == 2, $"{after.Values.Count}");
        Check("and the header says it has moved", after.Moved);
        Check("oldest first sighting first, so the newcomer reads last",
              after.Values[0].Value == "five_hour" && after.Values[1].Value == "seven_day");
        Check("a value seen once is kept, not rounded off", after.Values[1].Count == 1);

        // Order of the readings is the log's, not the caller's: Load sorts, but nothing else promises to.
        var shuffled = new List<ProbeEntry> { moved[3], moved[1], moved[0], moved[2] };
        HeaderVocab unsorted = HeaderProbe.Vocabulary(shuffled).First(h => h.Name == Claim);
        Check("an unsorted log yields the same first sighting",
              Math.Abs(unsorted.Values[0].First - Now) < 1
              && unsorted.Values[0].Value == "five_hour");

        // A header absent from a reading is absent from its own count, or "seen on every reading" would be
        // indistinguishable from "seen once and never again" — the difference the whole question rests on.
        var partial = new List<ProbeEntry> { E(Now, Claim, "five_hour"), E(Now + 60, Util5, "0.55") };
        Check("a reading that carried no value adds nothing to the count",
              HeaderProbe.Vocabulary(partial).First(h => h.Name == Claim).Values[0].Count == 1);
        Check("an empty log has no vocabulary at all",
              HeaderProbe.Vocabulary(new List<ProbeEntry>()).Count == 0);

        Spread();
    }

    /// <summary>T211. The reading that reframed <c>unified-fallback</c> is an <em>absence</em>: it is sent
    /// to an account whose overage window exists and never sent to one whose organisation disabled it,
    /// while <c>unified-fallback-percentage</c> goes to both. A vocabulary of values cannot express that —
    /// one reading in seven and seven in seven both read as "one value, seen" — and the two samples are two
    /// accounts, so the comparison does not fit inside one log either.</summary>
    private static void Spread()
    {
        const string Fallback = "anthropic-ratelimit-unified-fallback";
        const string Pct = "anthropic-ratelimit-unified-fallback-percentage";
        const string OverUtil = "anthropic-ratelimit-unified-overage-utilization";
        const string Util5 = "anthropic-ratelimit-unified-5h-utilization";

        static ProbeEntry E(double t, params string[] kv)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
            return new ProbeEntry(t, d);
        }

        // One log whose later readings carry a name the first did not — the within-profile case.
        var arriving = new List<ProbeEntry>
        {
            E(Now,      Util5, "0.10", Pct, "0.5"),
            E(Now + 60, Util5, "0.99", Pct, "0.5", OverUtil, "0.02"),
        };
        List<HeaderPresence> presence = HeaderProbe.Presence(arriving);
        // Looked up rather than indexed, and each lookup asserted before it is read: a name missing from
        // the result is the defect these are about, and it must fail by name instead of throwing the rest
        // of the section away with it.
        HeaderPresence always = presence.FirstOrDefault(p => p.Name == Pct);
        if (Check("a name every reading carried is in the result", always.Name != null))
        {
            Check("and is not intermittent", !always.Intermittent);
            Check("counted against the log's own length",
                  always.Readings == 2 && always.Total == 2, $"{always.Readings}/{always.Total}");
        }

        // The suffix rule excuses a figure from having a vocabulary. It must not excuse it from being
        // counted: overage-utilization appearing is one of the two divergences this task measured.
        HeaderPresence late = presence.FirstOrDefault(p => p.Name == OverUtil);
        if (Check("a moving figure has presence even though it has no vocabulary",
                  late.Name != null && HeaderProbe.Vocabulary(arriving).All(v => v.Name != OverUtil)))
        {
            Check("a name that arrived partway through is intermittent", late.Intermittent);
            Check("counted on the readings that carried it, not all of them",
                  late.Readings == 1 && late.Total == 2, $"{late.Readings}/{late.Total}");
        }
        Check("an empty log has no presence at all",
              HeaderProbe.Presence(new List<ProbeEntry>()).Count == 0);

        // The cross-profile case, shaped like the two real accounts: one is offered the fallback, the
        // other is never sent the name at all, and both are sent the percentage.
        var offered = new List<ProbeEntry> { E(Now, Util5, "0.76", Pct, "0.5", Fallback, "available") };
        var never = new List<ProbeEntry> { E(Now, Util5, "0.25", Pct, "0.5") };
        List<HeaderSpread> spread = HeaderProbe.Spread(new List<(string, IReadOnlyList<ProbeEntry>)>
        {
            ("offered", offered), ("never", never),
        });

        HeaderSpread fb = spread.FirstOrDefault(h => h.Name == Fallback);
        if (Check("the name only one profile is sent is in the spread", fb.Name != null))
        {
            Check("and it is divergent", fb.Divergent);
            Check("the profile that has it reads its value",
                  fb.ByProfile.First(p => p.Profile == "offered").Value == "available");
            Check("and the one that does not reads absent, not empty",
                  fb.ByProfile.First(p => p.Profile == "never").Value is null);
        }

        HeaderSpread pct = spread.FirstOrDefault(h => h.Name == Pct);
        if (Check("the name both profiles are sent is in the spread", pct.Name != null))
        {
            Check("and it is not divergent", !pct.Divergent);
            Check("with the same value on both — the measured half of T211",
                  pct.ByProfile.All(p => p.Value == "0.5"));
        }

        // A figure has no vocabulary to compare, and printing one account's 0.76 beside another's 0.25 as
        // if it were a difference of kind is exactly the misreading the spread exists to avoid.
        HeaderSpread util = spread.FirstOrDefault(h => h.Name == Util5);
        if (Check("a moving figure reaches the spread at all", util.Name != null))
            Check("spread by presence, not by its number", util.ByProfile.All(p => p.Value == "(figure)"));
        Check("every name either profile sent is in the spread", spread.Count == 3, $"{spread.Count}");
        Check("and each name carries a row per profile", spread.All(h => h.ByProfile.Count == 2));
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

        // T275. The spell measured on 2026-08-04 wrote `ux:0` on every reading past the threshold, so the
        // figure carries no trace of it at all — the header does, and it has to survive the same round trip
        // with the same three states the figure has. Written on a line whose `ux` is a measured zero,
        // because that is the shape the real spell took and the one a boolean read off the figure misses.
        long t1 = t0 + 180;
        UsageHistory.Append(ProfileKey, t1, 1.02, Now + 3600, 0.47, Now + 86400,
                            extraUtil: 0, extraReset: Now + 7200, extraInUse: true);
        UsageHistory.Append(ProfileKey, t1 + 60, 0.30, Now + 3600, 0.47, Now + 86400,
                            extraUtil: 0, extraReset: Now + 7200, extraInUse: false);

        List<UsageSample> both = UsageHistory.Load(ProfileKey, t1);
        if (!Check("the two readings around the crossing come back", both.Count == 2, $"{both.Count} read"))
            return;
        Check("the header that says the account is over survives the store", both[0].InUse == true);
        Check("and so does its denial, which is not the same as never having been asked", both[1].InUse == false);
        Check("a spell is recorded even where the figure beside it is a measured zero",
              both[0] is { InUse: true, Extra: 0 });
        Check("a reading written before the field existed reads back as neither", read[0].InUse == null);
        Check("an absent header writes no field at all", !first.Contains("\"ix\""), first);

        // The stretch the chart shades, from the readings themselves. A poll every five minutes is one
        // spell; the app being closed between two of them is two.
        const double week = 7 * 86400;
        double Frac(double seconds) => seconds / week;      // readings placed in seconds, not in guesses

        // A five-minute cadence, which is the default, so the bridge is the floor rather than the measure.
        double bridge = UsageReport.BridgeSeconds(new List<double> { 0, 300, 600, 900 });
        Near("a five-minute poll bridges on the floor, not on three of its own intervals", bridge, 900, 0);
        Check("a quarter-hour poll widens the bridge instead of combing the stretch",
              UsageReport.BridgeSeconds(new List<double> { 0, 900, 1800, 2700 }) > 900);

        var close = new List<double> { Frac(0), Frac(300), Frac(600) };
        List<(double f0, double f1)> one = UsageReport.MergeSpans(close, week, bridge);
        Check("consecutive readings are one stretch, not three", one.Count == 1, $"{one.Count} spans");
        Near("which starts at the first of them", one[0].f0, Frac(0), 1e-9);
        Near("and ends at the last", one[0].f1, Frac(600), 1e-9);

        var apart = new List<double> { Frac(0), Frac(300), Frac(6 * 3600), Frac(6 * 3600 + 300) };
        Check("a silence long enough to be the app being closed ends the stretch",
              UsageReport.MergeSpans(apart, week, bridge).Count == 2);
        Check("a lone reading is still a stretch, wide enough to be drawn",
              UsageReport.MergeSpans(new List<double> { 0.40 }, week, bridge) is [var lone]
              && lone.f1 > lone.f0);
        Check("and no readings is no stretch at all",
              UsageReport.MergeSpans(new List<double>(), week, bridge).Count == 0);

        // T317 removed a `= GapFloorSeconds` default no caller used, so the omission it invited is now a
        // compile error and cannot be asserted. What can is *why* it had to go: with the poll interval set
        // slower than the floor, the constant splits a spell the measured bridge keeps whole. The difference
        // is the argument's whole reason for existing, and it lived only in a comment.
        const double slowPoll = 20 * 60;                       // slower than the 15-minute floor
        var slowSpell = new List<double>();
        for (int i = 0; i < 5; i++) slowSpell.Add(Frac(i * slowPoll));
        double measured = UsageReport.BridgeSeconds(slowSpell.Select(f => f * week).ToList());
        Check("a spell polled slower than the floor is one stretch on the measured bridge",
              UsageReport.MergeSpans(slowSpell, week, measured).Count == 1,
              $"bridge {measured}s over a {slowPoll}s cadence");
        Check("...and five, a comb, on the constant the default used to supply",
              UsageReport.MergeSpans(slowSpell, week, 15 * 60).Count == 5,
              "the floor alone ends the spell at every reading — the picture the default invited");
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

        // T287. `usage-history.jsonl` is pruned at eight days, so the spell T275 records there has to leave
        // a bit behind in the fold or a week reviewed a fortnight later is a ceiling with no account of the
        // account having worked on past it. The column is written even when no hour was over, precisely so
        // that a line without it keeps meaning "folded before this existed".
        Check("a day where nothing was over still carries the column",
              once is [{ OverKnown: true, OverHours: 0 }]);

        // Folded into a store whose days were written before the column existed, so both halves are tested
        // at once: the new day keeps its bits, and rewriting the file invents no "never over" for the old.
        WriteStore(FoldedWeek(coverage: 1.0, perHour: 0.004));
        DateTime spellDay = DateTime.Today.AddDays(-10);
        int spellKey = spellDay.Year * 10000 + spellDay.Month * 100 + spellDay.Day;
        HourlyUsage.Fold(ProfileKey, new List<UsageSample>
        {
            new(Unix(spellDay.AddHours(8)), 0, 0, 0.20, reset),
            new(Unix(spellDay.AddHours(9)), 0, 0, 0.21, reset),                                 // no header
            new(Unix(spellDay.AddHours(13)), 0, 0, 0.30, reset, Extra: 0, InUse: true),          // the spell
            new(Unix(spellDay.AddHours(13).AddMinutes(5)), 0, 0, 0.31, reset, Extra: 0, InUse: true),
            new(Unix(spellDay.AddHours(15)), 0, 0, 0.33, reset, InUse: false),                   // measured no
        }, nowUnix);

        List<HourlyDay> withSpell = HourlyUsage.Load(ProfileKey);
        HourlyDay spellFold = withSpell.FirstOrDefault(d => d.Key == spellKey);
        if (Check("the day the account went over folds down", spellFold.Key == spellKey, $"{withSpell.Count} days"))
        {
            Check("the hour a reading said it was over keeps a bit", spellFold.WasOver(13));
            Check("an hour whose readings carried no header is not a spell", !spellFold.WasOver(9));
            Check("nor is an hour the API said was inside the quota", !spellFold.WasOver(15));
            // Four, not three: the 08:00 reading opens the batch and T296 is what gave it its hour back.
            Check("the hours around it are covered either way — the bit is not coverage",
                  spellFold.Covered == 4, $"{spellFold.Covered} covered");
            Check("two readings in one hour are one bit, not a count",
                  spellFold.OverHours == 1, $"{spellFold.OverHours} hours over");
        }

        // The absence has to be in the *line*: an "x" of zeros written for a day nobody asked would satisfy
        // OverKnown and still assert a fortnight of quota nobody measured.
        List<string> lines = File.ReadLines(HourlyUsage.FilePath(ProfileKey)).ToList();
        Check("the bit is in the line, not only in the parse",
              lines.Any(l => l.Contains($"\"d\":{spellKey}") && l.Contains("\"x\":")));
        Check("a day folded before the column existed keeps no column when the store is rewritten",
              lines.Any(l => !l.Contains($"\"d\":{spellKey}") && !l.Contains("\"x\":")));
        Check("...and reads back as unknown rather than as never over",
              withSpell.Any(d => d.Key != spellKey && !d.OverKnown && !d.WasOver(13)));

        // T296. The oldest reading of a batch has no predecessor, which is a statement about spend and
        // about nothing else: it was still taken, at a known hour, and the API still answered. Its hour is
        // deliberately the only one in this batch, because an hour observed once is where losing the
        // reading turns "idle" into "unknown" — and where the bit is the only surviving trace of a spell.
        DateTime lone = DateTime.Today.AddDays(-11);
        int loneKey = lone.Year * 10000 + lone.Month * 100 + lone.Day;
        HourlyUsage.Fold(ProfileKey, new List<UsageSample>
        {
            new(Unix(lone.AddHours(7)), 0, 0, 0.40, reset, Extra: 0, InUse: true),   // oldest: no pair
            new(Unix(lone.AddHours(12)), 0, 0, 0.42, reset),
            new(Unix(lone.AddHours(12).AddMinutes(5)), 0, 0, 0.44, reset),
        }, nowUnix);

        HourlyDay loneFold = HourlyUsage.Load(ProfileKey).FirstOrDefault(d => d.Key == loneKey);
        if (Check("a batch whose oldest reading stands alone in its hour folds", loneFold.Key == loneKey))
        {
            Check("the oldest reading marks the hour it was taken in", loneFold.Count[7] == 1,
                  $"{loneFold.Count[7]} readings");
            Check("and the spell it carried is not lost with it", loneFold.WasOver(7));
            Near("while its spend waits for a pair it does not have", loneFold.Spend[7], 0, 1e-12);
            Near("which the hour that has one still gets", loneFold.Spend[12], 0.04, 1e-9);
        }

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

        // T295. An hour is a slot of the window and not a point in it, so the arithmetic is pinned on the
        // pure function first: a run of hours is one span covering those hours whole.
        var run = new bool[24];
        run[4] = run[5] = run[6] = true;
        List<(double f0, double f1)> spans = HourlyUsage.Spans(run);
        Check("three consecutive over-hours are one span", spans.Count == 1, $"{spans.Count} spans");
        Near("opening where the first of them opens", spans[0].f0, 4 / 24.0, 1e-12);
        Near("and closing where the first hour that was not over begins", spans[0].f1, 7 / 24.0, 1e-12);

        var split = new bool[24];
        split[1] = split[20] = split[21] = split[22] = split[23] = true;
        List<(double f0, double f1)> two = HourlyUsage.Spans(split);
        Check("an hour on its own and a run are two spans", two.Count == 2, $"{two.Count} spans");
        Near("the lone hour is one hour wide, not a zero-width line", two[0].f1 - two[0].f0, 1 / 24.0, 1e-12);
        Near("and a run that reaches the end of the window closes on it", two[1].f1, 1.0, 1e-12);
        Check("no over-hour is no span", HourlyUsage.Spans(new bool[24]).Count == 0);

        // Then the ghost itself, which is what the chart reads. Qualitative on purpose: which fractions the
        // spans land on depends on the hour of day the check runs at, and the claim under test is not that.
        WriteStore(FoldedWeek(coverage: 1.0, perHour: 0.004, column: true, overFrom: 10, overTo: 13));
        HourlyUsage.GhostWeek? shaded = HourlyUsage.PreviousWeek(ProfileKey, Now, 7 * 86400, 0.5);
        if (Check("a previous week that went over still draws its ghost", shaded is not null)
            && shaded is { } g)
        {
            Check("and the ghost carries the stretches it was over", g.OverSpans.Count > 0);
            Check("each of them a real interval inside the window",
                  g.OverSpans.All(s => s.f1 > s.f0 && s.f0 >= 0 && s.f1 <= 1 + 1e-12));
            Check("the week is one that can answer the question at all", g.OverKnown);
        }

        WriteStore(FoldedWeek(coverage: 1.0, perHour: 0.004, column: true));
        Check("a week measured inside its quota shades nothing, and says so",
              HourlyUsage.PreviousWeek(ProfileKey, Now, 7 * 86400, 0.5) is { OverSpans.Count: 0, OverKnown: true });

        WriteStore(FoldedWeek(coverage: 1.0, perHour: 0.004));
        Check("a week folded before the column existed shades nothing and knows nothing — not the same claim",
              HourlyUsage.PreviousWeek(ProfileKey, Now, 7 * 86400, 0.5) is { OverSpans.Count: 0, OverKnown: false });

        // T299. The two halves of the fold can disagree — the bit says the header called the account over,
        // the curve is a sum of deltas the fold drops one of at every reset — and the chart has to know
        // which weeks those are, because on them the stretch it draws sits above its own line.
        var flat = new List<(double frac, double cum)>();
        for (int i = 0; i <= 24; i++) flat.Add((i / 24.0, 0.80 * i / 24.0));   // a week that peaks at 80%
        var mid = new List<(double f0, double f1)> { (10 / 24.0, 13 / 24.0) };
        Check("a stretch over hours the curve never lifted to the ceiling is a disagreement",
              new HourlyUsage.GhostWeek(flat, 1, 0.4, 0.80, mid, true).ShadedAboveCurve);
        Check("and a week with nothing shaded has nothing to disagree with",
              !new HourlyUsage.GhostWeek(flat, 1, 0.4, 0.80, new List<(double, double)>(), true).ShadedAboveCurve);

        var ceiling = new List<(double frac, double cum)>();
        // At the ceiling by hour 8, i.e. before the stretch opens — which is the ordinary case, since being
        // told you are over is what a week that already spent its quota gets.
        for (int i = 0; i <= 24; i++) ceiling.Add((i / 24.0, Math.Min(1, 3.0 * i / 24.0)));
        Check("a week whose line is at the ceiling under its stretch agrees with it",
              !new HourlyUsage.GhostWeek(ceiling, 1, 0.9, 1.0, mid, true).ShadedAboveCurve);
        Check("...and the demo week the previews draw is one of those",
              !HourlyUsage.Demo(DateTime.Today.AddDays(-7), 7 * 86400, 0.5, 0.86, over: true).ShadedAboveCurve);

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
    /// The Settings round trip, <b>two</b> checks per property: the window is handed a total copy
    /// (<see cref="Settings.Clone"/>), hands back that copy and the untouched one it opened with, and the
    /// tray merges by which write is newer (<see cref="Settings.CarryUnchangedFrom"/>). Every property has
    /// to answer both ways — the page edited it and its value survives, or the page left it alone and a
    /// value the menu moved meanwhile survives.
    ///
    /// <para><b>Both directions, per field, is what T229 needed.</b> T162's sweep asked one question per
    /// property and read the answer off the attribute, so an unmarked field was asserted to keep the page's
    /// value — which is what happened, including for the two fields with a control on <em>both</em>
    /// surfaces, whose menu edit was silently reverted. Nothing here knows which controls a page has, and
    /// it no longer has to: a field the page did not touch is indistinguishable from one it cannot touch,
    /// and both want the live value.</para>
    ///
    /// <para>Driven by reflection over <see cref="Settings.Fields"/> rather than a list written here: a
    /// field added tomorrow is covered tomorrow. A property whose type has no varying rule below
    /// <b>fails</b> rather than being skipped, so adding one this cannot vary is a red check, not a gap.</para>
    /// </summary>
    private static void SettingsRoundTrip()
    {
        foreach (PropertyInfo p in Settings.Fields)
        {
            if (Vary(p) is not { } varied)
            {
                Fail($"{p.Name}: the merge decides which write is newer",
                     "no varying rule for this property's type — add one");
                continue;
            }
            (object? edited, object? moved) = varied;

            // One shape, both directions. The window opened over `opened`; the menu then moved the field
            // to `moved` in the live model; Save hands back what the page holds plus what it opened with.
            static Settings Merge(PropertyInfo p, object? opened, object? onPage, object? live)
            {
                var liveModel = new Settings();
                p.SetValue(liveModel, live);
                var openedCopy = new Settings();
                p.SetValue(openedCopy, opened);
                Settings edited = openedCopy.Clone();
                p.SetValue(edited, onPage);

                Settings applied = edited.Clone();
                applied.CarryUnchangedFrom(liveModel, openedCopy.Clone());
                return applied;
            }

            // The page left it where it found it, and the menu moved it meanwhile: the menu's value wins.
            // This is every field T162 marked, plus the two it could not — and it is T126 and T155.
            object? untouched = p.GetValue(Merge(p, opened: edited, onPage: edited, live: moved));
            Check($"{p.Name}: a value the menu moved survives a window that did not touch it",
                  Json(untouched) == Json(moved), $"{Json(untouched)} vs {Json(moved)}");

            // The page edited it: the page's value wins, whatever the live model holds.
            object? touched = p.GetValue(Merge(p, opened: moved, onPage: edited, live: moved));
            Check($"{p.Name}: and an edit made on the page is not carried away",
                  Json(touched) == Json(edited), $"{Json(touched)} vs {Json(edited)}");
        }

        // The two fields the ownership split could not express, named, because they are the defect: a
        // control on the Claude Code page *and* a toggle in the menu. If either loses its page control the
        // merge still holds — this is here so the case that produced T229 is pinned by name.
        foreach (string shared in new[] { nameof(Settings.FollowActiveProfile),
                                          nameof(Settings.SyncEnvironmentProfile) })
        {
            PropertyInfo p = Settings.Fields.First(f => f.Name == shared);
            var live = new Settings();
            p.SetValue(live, true);                       // the menu turned it on
            var opened = new Settings();                  // the window was built before that: false
            Settings applied = opened.Clone();
            applied.CarryUnchangedFrom(live, opened.Clone());
            Check($"{shared}: flipped in the menu, Save in an open window does not undo it",
                  Equals(p.GetValue(applied), true),
                  $"got {Json(p.GetValue(applied))} — T171's environment write reconciles off this");
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
        Check("and nothing to shade either", none.ExtraSpans.Count == 0);

        // T275. The spell as measured: the header true across a stretch with the figure at a measured zero
        // throughout. The band has to appear where the series cannot, or the state that went unrecorded
        // goes on being invisible one layer further along.
        UsageSample Over(double frac, bool? inUse) =>
            new(start + frac * window, 1.0, reset, 1.0, reset, 0.0, reset, inUse);
        double poll = 300 / window;             // the default cadence, as a fraction of the week
        var spell = Win(reset, window, now, 1.0);
        UsageReport.FillCurve(spell, new(), new()
        {
            Over(0.30 - poll, false), Over(0.30, true), Over(0.30 + poll, true),
            Over(0.30 + 2 * poll, true), Over(0.30 + 3 * poll, null),
        }, s => (s.Util7d, s.Reset7d), now);
        Check("the stretch the account was over is shaded from the header, not from the figure",
              spell.ExtraSpans.Count == 1, $"{spell.ExtraSpans.Count} spans");
        Near("opening where the header first said so", spell.ExtraSpans[0].f0, 0.30, 1e-9);
        Check("and no second axis is invented for it: every figure on those readings was zero",
              spell.ExtraMax == 0, $"max {spell.ExtraMax}");

        // T300. The legend's one entry for the clay pair turns on the same predicate the marks are drawn
        // under, and all four states matter: the interesting ones are the two where only one mark is on the
        // chart, which is most weeks. A legend entry for a mark nobody drew names a colour the reader cannot
        // find — the defect T300 is about, one step further on.
        var ghostFlat = new List<(double frac, double cum)>();
        for (int i = 0; i <= 24; i++) ghostFlat.Add((i / 24.0, i / 24.0));
        var ghostOver = new List<(double f0, double f1)> { (0.5, 0.6) };
        HourlyUsage.GhostWeek GhostWith(List<(double f0, double f1)> spans)
            => new(ghostFlat, 1, 0.5, 1.0, spans, true);

        Check("this week's shaded stretch alone names the clay in the legend",
              StatisticsPage.HasOverQuotaMark(spell));
        var ghostOnly = Win(reset, window, now, 0.8);
        ghostOnly.Ghost = GhostWith(ghostOver);
        Check("...and so does last week's mark alone, with nothing shaded for this one",
              ghostOnly.ExtraSpans.Count == 0 && StatisticsPage.HasOverQuotaMark(ghostOnly));
        var neither = Win(reset, window, now, 0.8);
        neither.Ghost = GhostWith(new List<(double f0, double f1)>());
        Check("a week with a ghost that never went over names nothing",
              !StatisticsPage.HasOverQuotaMark(neither));
        Check("and neither does one with no ghost and nothing shaded",
              !StatisticsPage.HasOverQuotaMark(none));
        var tooThin = Win(reset, window, now, 0.8);
        tooThin.Ghost = GhostWith(ghostOver) with { Curve = new List<(double frac, double cum)>() };
        Check("a ghost too thin to draw takes its mark out of the legend with it",
              !StatisticsPage.HasOverQuotaMark(tooThin));

        // T308. Both predicates are asked of whichever window the legend sits under, and the 5-hour one is
        // the case that had no entry at all: `InUse` is a fact about the account, so FillCurve fills
        // Session.ExtraSpans from the same readings and the shared DrawChart shades them.
        // Its own clock: a 5-hour window resetting two hours from now, so three of it have elapsed and the
        // readings below land inside it. Sharing the week's `reset` would put the session's start days after
        // `now` and select nothing, which is a fixture asserting the absence it built.
        double sReset = now + 2 * 3600;
        var session = Win(sReset, UsageReport.SessionSeconds, now, 1.0);
        UsageSample Sat(double secondsAgo) =>
            new(now - secondsAgo, 1.0, sReset, 1.0, sReset, 0.0, sReset, true);
        UsageReport.FillCurve(session, new(), new() { Sat(1800), Sat(1500), Sat(1200) },
                              s => (s.Util5h, s.Reset5h), now);
        Check("a session past the included quota is shaded, as the week is",
              session.ExtraSpans.Count > 0, $"{session.ExtraSpans.Count} spans");
        Check("...and the 5-hour legend names it, from the same predicate",
              StatisticsPage.HasOverQuotaMark(session));

        // T323. The state the chip and the sentence both read, and the fixtures are two: `spell` and `session`
        // above are stretches that ended twenty minutes before `now`, which is exactly the case this must NOT
        // call present tense — the same readings that put a band on the chart. So the live one is built here,
        // running up to `now`.
        var liveSpell = Win(reset, window, now, 1.0);
        UsageSample OverAt(double secondsAgo, bool? inUse) =>
            new(now - secondsAgo, 1.0, reset, 1.0, reset, 0.0, reset, inUse);
        UsageReport.FillCurve(liveSpell, new(), new()
        {
            OverAt(900, true), OverAt(600, true), OverAt(300, true), OverAt(0, true),
        }, s => (s.Util7d, s.Reset7d), now);
        Check("a window whose newest reading says the account is over reads as billing now",
              StatisticsPage.BillingNow(liveSpell), $"{liveSpell.ExtraSpans.Count} spans");
        var liveSession = Win(sReset, UsageReport.SessionSeconds, now, 1.0);
        UsageReport.FillCurve(liveSession, new(), new() { Sat(600), Sat(300), Sat(0) },
                              s => (s.Util5h, s.Reset5h), now);
        Check("...on the session too, which is the pane that showed a pace verdict instead",
              StatisticsPage.BillingNow(liveSession), $"{liveSession.ExtraSpans.Count} spans");
        Check("and a window with no overage reading anywhere does not",
              !StatisticsPage.BillingNow(none));
        // The recency rule is what makes this present tense rather than "at some point this week", and the
        // shapes it has to tell apart are already on file: a spell that ended earlier still has its band.
        Check("a spell that ended earlier in the window is not billing *now*",
              spell.ExtraSpans.Count > 0 && !StatisticsPage.BillingNow(spell),
              $"{spell.ExtraSpans.Count} spans, last ends at {(spell.ExtraSpans.Count > 0 ? spell.ExtraSpans[^1].f1 : -1):0.####} of {spell.ElapsedFraction:0.####} elapsed");
        // The figure route follows the same rule, and only on the *newest* figure: ExtraCurve carries a point
        // for every reading that had the header, zeros included, so a recent zero is a spell that is over.
        var figureStale = Win(reset, window, now, 1.0);
        figureStale.ExtraCurve.Add((0.20, 0.42));
        figureStale.ExtraCurve.Add((figureStale.ElapsedFraction, 0.0));
        figureStale.ExtraMax = 0.42;
        Check("an overage figure back at zero is not billing now, whatever the window's peak was",
              !StatisticsPage.BillingNow(figureStale) && StatisticsPage.HasBillingFigure(figureStale));
        var figureLive = Win(reset, window, now, 1.0);
        figureLive.ExtraCurve.Add((figureLive.ElapsedFraction, 0.42));
        figureLive.ExtraMax = 0.42;
        Check("and a figure above zero on the newest reading is, with no span needed",
              StatisticsPage.BillingNow(figureLive) && figureLive.ExtraSpans.Count == 0);

        // T288: the opposite scope, deliberately. `BillingNow` asks about ONE pane because that pane draws
        // the band; whether the account is refused is not a pane's property at all, and the pane that has to
        // say so is the one with no evidence on it.
        var roomy7d = Win(reset, window, now, 0.47);
        var gone5h = Win(sReset, UsageReport.SessionSeconds, now, 1.0);
        Check("a week with room behind a session at its limit is a blocked account",
              StatisticsPage.StoppedNow(gone5h, roomy7d));
        Check("...and asked of either pane, since neither owns the answer",
              StatisticsPage.StoppedNow(roomy7d, gone5h));
        Check("two windows with room are not blocked",
              !StatisticsPage.StoppedNow(roomy7d, Win(sReset, UsageReport.SessionSeconds, now, 0.6)));
        // The one that keeps this from firing on every paying account, which is the state T182 split off:
        // at the limit and still working is not stopped, and `liveSession` is that window exactly.
        Check("and a window at its limit that is still paying past it is not blocked either",
              !StatisticsPage.StoppedNow(liveSession, roomy7d) && StatisticsPage.BillingNow(liveSession));
        Check("nor is a window that has no reading at all",
              !StatisticsPage.StoppedNow(null, null));

        // The second axis is one question too, and the entry that says the percentage is of a different
        // denominator must appear exactly when the axis it explains does.
        Check("a window with no overage figure rules no second axis", !StatisticsPage.HasExtraAxis(none));
        Check("and one measured figure is not a series either",
              !StatisticsPage.HasExtraAxis(spell), $"max {spell.ExtraMax}");
        Check("a real overage curve rules one, so the legend can name its scale",
              StatisticsPage.HasExtraAxis(w), $"{w.ExtraCurve.Count} points, max {w.ExtraMax}");

        // T309. The legend counts what the chart draws because both read one enumerator, so what is asserted
        // is the enumerator: which kinds it yields, and that its count is what the predicate answers from.
        var both = Win(reset, window, now, 1.0);
        both.ExtraSpans = new List<(double, double)> { (0.2, 0.3), (0.5, 0.55) };
        both.Ghost = GhostWith(ghostOver);
        var marks = StatisticsPage.OverQuotaMarks(both).ToList();
        Check("a window carrying both shapes yields both, band before ceiling",
              marks.Count == 3
              && marks.Take(2).All(m => m.kind == StatisticsPage.OverMark.Band)
              && marks[2].kind == StatisticsPage.OverMark.Ceiling,
              string.Join(", ", marks.Select(m => m.kind)));
        Check("the legend's answer is that list being non-empty, not a second walk of the same fields",
              StatisticsPage.HasOverQuotaMark(both) == marks.Count > 0);
        Check("a zero-width span is yielded by neither, so neither draws nor names it",
              StatisticsPage.OverQuotaMarks(
                  new WindowPace { ExtraSpans = new List<(double, double)> { (0.4, 0.4) } }).Any() == false);
        Check("and a ghost too thin to draw keeps its spans out of the list",
              StatisticsPage.OverQuotaMarks(tooThin).All(m => m.kind == StatisticsPage.OverMark.Band));

        // T311. The entry's *content*, which T300 left fixed while making its visibility conditional. The
        // claim is that no state describes a shape the chart did not draw, so each of the three is asserted
        // on both halves of the swatch and on which sentence it gets.
        var bandOnly = Win(reset, window, now, 1.0);
        bandOnly.ExtraSpans = new List<(double, double)> { (0.2, 0.3) };
        var ceilOnly = Win(reset, window, now, 0.5);
        ceilOnly.Ghost = GhostWith(ghostOver);

        var gBoth = StatisticsPage.OverLegendFor(both);
        Check("both shapes drawn: both halves of the swatch, and the tip that distinguishes the weeks",
              gBoth is { Show: true, Band: true, Ceiling: true, TipKey: "stats.legend.overQuota.tip" },
              $"{gBoth}");
        var gBand = StatisticsPage.OverLegendFor(bandOnly);
        Check("a shaded stretch alone draws no bar at the ceiling and promises none",
              gBand is { Show: true, Band: true, Ceiling: false, TipKey: "stats.legend.overQuota.tipBand" },
              $"{gBand}");
        // The state that is most weeks, and the one T300 was wrong about: last week over, this week not.
        var gCeil = StatisticsPage.OverLegendFor(ceilOnly);
        Check("a ghost mark alone draws no band and promises no shaded stretch",
              gCeil is { Show: true, Band: false, Ceiling: true, TipKey: "stats.legend.overQuota.tipCeiling" },
              $"{gCeil}");
        var gNone = StatisticsPage.OverLegendFor(neither);
        Check("no mark, no entry — and no sentence left behind on it",
              gNone is { Show: false, Band: false, Ceiling: false, TipKey: "" }, $"{gNone}");
        Check("the entry's own visibility is that same reading, not a second one",
              StatisticsPage.HasOverQuotaMark(ceilOnly) == gCeil.Show
              && StatisticsPage.HasOverQuotaMark(neither) == gNone.Show);

        // T313. The claim the fix rests on, asserted as a claim: no legend entry wears a sentence written
        // for a hit target on the chart. Those say "over this stretch", which has a referent under a cursor
        // on the stretch and none under one on a swatch — so borrowing one back is a red build, not a review
        // note. Named by key rather than by wording: what is wrong is where the string is used.
        string[] chartHoverTips = { "stats.chart.overSpan", "stats.chart.lastWeekOverSpan" };
        Check("no legend entry borrows a chart hover's own sentence",
              new[] { gBoth, gBand, gCeil }.All(g => !chartHoverTips.Contains(g.TipKey)),
              string.Join(", ", new[] { gBoth, gBand, gCeil }.Select(g => g.TipKey)));

        // T316. The mark's offset and its swatch's edge are two types carrying one fact, so what is asserted
        // is that they agree — either alone would pass while the legend pointed the wrong way, which is the
        // state remaining mode was in. The lift is signed *into* the plot, so its sign is the claim.
        Check("used mode puts the mark and its swatch bar on the same edge",
              StatisticsPage.CeilingLift(false) > 0
              && StatisticsPage.CeilingSwatchEdge(false) == System.Windows.VerticalAlignment.Top,
              $"lift {StatisticsPage.CeilingLift(false)}, edge {StatisticsPage.CeilingSwatchEdge(false)}");
        Check("and remaining mode flips both of them, not one",
              StatisticsPage.CeilingLift(true) < 0
              && StatisticsPage.CeilingSwatchEdge(true) == System.Windows.VerticalAlignment.Bottom,
              $"lift {StatisticsPage.CeilingLift(true)}, edge {StatisticsPage.CeilingSwatchEdge(true)}");
        Check("the ceiling is the top of the plot exactly when the axis is not flipped",
              StatisticsPage.CeilingAtTop(false) && !StatisticsPage.CeilingAtTop(true));

        // T318. The overage axis does not flip with the consumption one, so in remaining mode both surfaces
        // naming its line have to say what the line counts — and they have to say it together. One of them
        // wording it while the other did not is the shape T311, T313 and T316 each shipped against.
        Check("used mode leaves both the overage axis and its legend entry plain",
              StatisticsPage.ExtraAxisKey(false) == "stats.chart.extraAxis"
              && StatisticsPage.ExtraLegendKey(false) == "stats.legend.extra");
        Check("and remaining mode moves both of them, not one",
              StatisticsPage.ExtraAxisKey(true) != StatisticsPage.ExtraAxisKey(false)
              && StatisticsPage.ExtraLegendKey(true) != StatisticsPage.ExtraLegendKey(false),
              $"{StatisticsPage.ExtraAxisKey(true)}, {StatisticsPage.ExtraLegendKey(true)}");
        // And the pair a mode picks is a pair `en.json` actually holds, which is the one direction the
        // parity check cannot see (T314) and the way a key becomes its own caption on screen.
        foreach (bool rem in new[] { false, true })
            foreach (string key in new[] { StatisticsPage.ExtraAxisKey(rem), StatisticsPage.ExtraLegendKey(rem) })
                Check($"{key} is a string and not its own name", L.Strings("en").ContainsKey(key));

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

        // T321. The digits' own colour, on the same rule as the table above: three windows, three colours,
        // no two alike. Driven off the metric list the tray offers, so a fourth window would arrive here
        // uncoloured rather than silently wearing the session's cream — which is the one failure a contact
        // sheet cannot show, because a scope reading as the session looks exactly like a session.
        var inks = new Dictionary<int, string>();
        foreach (string scope in new[] { "5h", "7d", "extra" })
        {
            int argb = IconRenderer.NumberInk(scope).ToArgb();
            if (inks.TryGetValue(argb, out string? other))
                Fail($"the {scope} number has a colour of its own",
                     $"it is drawn in {other}'s ink, so the two windows are one number on screen");
            else
            {
                _passed++;
                Console.WriteLine($"  ok    the {scope} number has a colour of its own");
                inks[argb] = scope;
            }
        }
        Check("the session keeps the cream the icon always drew, so an ordinary tray is unchanged",
              IconRenderer.NumberInk("5h").ToArgb() == Color.White.ToArgb());
        Check("and a scope nobody here spells falls back to it rather than picking a colour",
              IconRenderer.NumberInk("90d").ToArgb() == IconRenderer.NumberInk("5h").ToArgb());

        // T322. Which fact the ink states when there are two: an account that is paying outranks the window
        // the figure happens to be about, because T320 moves that figure onto the week and the state the
        // colour exists for would otherwise read as an ordinary week — with no bar to say otherwise, since
        // "0 left" draws none.
        foreach (string scope in new[] { "5h", "7d", "extra", "90d" })
            Check($"billing outranks the window the number is about ({scope})",
                  IconRenderer.NumberInk(scope, billing: true).ToArgb()
                  == IconRenderer.NumberInk("extra").ToArgb());
        // And it is billing alone: stopped work is not paid work, so it keeps the window's own colour rather
        // than claiming money is being spent (T182's rule, in the one place a colour could break it).
        Check("a window that is merely spent keeps its own colour, since orange means paying",
              IconRenderer.NumberInk("7d").ToArgb() == IconRenderer.NumberInk("7d", billing: false).ToArgb()
              && IconRenderer.NumberInk("7d").ToArgb() != IconRenderer.NumberInk("extra").ToArgb());
        // The pale pair T322 replaced, named by value: a hue this washed out is a white with a cast at 16px,
        // and nothing but a number here can keep the two from being chosen again on an 8x sheet.
        Check("neither ink is one of the washed-out values the tray was reported as drawing white",
              IconRenderer.NumberInk("7d").ToArgb() != Color.FromArgb(255, 255, 233, 160).ToArgb()
              && IconRenderer.NumberInk("extra").ToArgb() != Color.FromArgb(255, 255, 179, 71).ToArgb());
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

    // ---------------------------------------------------------------- Block AI: the tooltip's budget

    /// <summary>
    /// The 127 characters of <c>NOTIFYICONDATA.szTip</c>, and what the tooltip does with them (T215).
    ///
    /// <para>This budget rations the most-seen text this app produces, and until T214 moved the
    /// composition off <see cref="TrayContext"/> nothing could reach it: a tray cannot be constructed
    /// headlessly, so the rule was held up by whoever next hovered an icon. T213 spent about eight of
    /// these characters in five languages without being able to check what it spent.</para>
    ///
    /// <para><b>What measuring it found.</b> French with an overage line composed 129 characters — over
    /// the cap, so Windows truncated the end, which is the status line carrying the time of the reading.
    /// Nobody had measured it because nobody could. The composition now sheds the bounded window the icon
    /// is <em>not</em> about before that can happen, and these assertions are what keeps it true in a
    /// language nobody here reads.</para>
    /// </summary>
    private static void Tooltip()
    {
        // The app's own list, not a copy of it: a sixth language is covered here with no edit.
        string[] codes = L.Codes.ToArray();
        IReadOnlyList<TooltipCli.Variant> variants = TooltipCli.Catalogue;
        const long Now = 1_800_000_000;

        if (!Check("the tooltip catalogue has its states", variants.Count >= 8, $"{variants.Count}"))
            return;

        // Restored however this section exits: every later section reads L, and leaving the process in
        // Spanish would rewrite what they assert against.
        L.Lang saved = L.Current;
        try
        {
            var over = new List<string>();
            var noStatus = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                foreach (TooltipCli.Variant v in variants)
                {
                    string text = TooltipText.Compose(v.Build(Now));
                    if (text.Length > TooltipText.Cap) over.Add($"{code}/{v.Name} ({text.Length})");
                    // The last line is the status line, and it is the one a blind end-truncation would
                    // cut mid-value. Whole means whole: it is never the thing that gets shortened.
                    string last = text.Split('\n')[^1];
                    if (!text.EndsWith(last, StringComparison.Ordinal) || last.Length == 0)
                        noStatus.Add($"{code}/{v.Name}");
                }
            }
            Check($"no composed tooltip exceeds {TooltipText.Cap} chars, in any of the five languages " +
                  $"({codes.Length * variants.Count} combinations)", over.Count == 0,
                  $"{string.Join(", ", over)} — Windows truncates the end, which is the reading's time");
            Check("and every one of them ends on a whole status line", noStatus.Count == 0,
                  string.Join(", ", noStatus));

            L.Apply("fr");   // the longest of the five, so the budget actually bites

            // The label is what the user typed: the one input with no bound of its own, and an unbounded
            // line makes the cap a claim nothing can keep.
            string huge = new('W', 400);
            string withHuge = TooltipText.Compose(Long(Now) with { ProfileLabel = huge });
            Check("a profile label of any length cannot overrun the cap",
                  withHuge.Length <= TooltipText.Cap, $"{withHuge.Length} chars from a {huge.Length}-char label");

            // §XX.13's own asks, in order. The label is kept when the projection cannot be: knowing whose
            // quota this is outranks a wordier sentence, and that trade is the reason the budget is
            // spent in this order rather than the other.
            string tight = TooltipText.Compose(Long(Now) with { ProfileLabel = "Trabalho" });
            Check("the profile label survives when the projection cannot",
                  tight.Contains("Trabalho", StringComparison.Ordinal),
                  "the line naming whose quota this is was dropped to make room for a sentence");

            // The compact form exists to be taken. A run where it would have fitted and the line was
            // dropped anyway is the budget failing at the one job it has.
            var dropped = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                string composed = TooltipText.Compose(Long(Now) with { ProfileLabel = "Trabalho" });
                int room = TooltipText.Cap - composed.Length;
                string compact = L.T("tip.okEtaCompact", "100%", "1d 2h");
                if (!composed.Contains(compact, StringComparison.Ordinal) && room >= compact.Length + 1)
                    dropped.Add($"{code} (room {room}, compact {compact.Length})");
            }
            Check("the compact projection is taken whenever it fits, never dropped", dropped.Count == 0,
                  string.Join(", ", dropped));

            // What goes first when the readings alone are over budget: the bounded window the icon is not
            // reporting. The watched one is the whole reason the tooltip is being read.
            L.Apply("fr");
            string overflowing = TooltipText.Compose(Overflowing(Now));
            Check("an over-budget reading sheds the window the icon is not about",
                  overflowing.Contains(L.T("tip.week"), StringComparison.Ordinal) &&
                  !overflowing.Contains(L.T("tip.session"), StringComparison.Ordinal),
                  $"kept the wrong line: {overflowing.Replace("\n", " | ")}");

            // T222. The state where extra usage is paying is the only state whose news was never on
            // screen: its overage reading exists *only* there and cost about what its one sentence needed,
            // so in all five languages `atlimit` rendered a sentence and this rendered none — the two
            // opposite pieces of news T182 split apart looked identical. The property is not that a
            // particular line exists; it is that the news arrives, in every language, whatever the budget.
            var mute = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                string billing = TooltipText.Compose(Overflowing(Now));
                if (!billing.Contains(L.T("tip.extraPaying", "47%"), StringComparison.Ordinal))
                    mute.Add($"{code} ({billing.Length}/{TooltipText.Cap})");
            }
            Check("the state where extra usage is paying says so, in every language", mute.Count == 0,
                  $"{string.Join(", ", mute)} — the one state whose news never reached the screen");

            // T288, the same property for the opposite outcome. The account is refused and the icon is on
            // the window that is fine, which is exactly where the sentence used to read "on track". Asked of
            // every language because the first draft of this fix rendered nothing at all in es and fr: the
            // natural sentence cost more than those readings had left, and a dropped line and a wrong line
            // are equally silent about being blocked.
            var quiet = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                string stopped = TooltipText.Compose(StoppedElsewhere(Now));
                if (!stopped.Contains(L.T("tip.atLimitUnscoped"), StringComparison.Ordinal) &&
                    !stopped.Contains(L.T("tip.atLimitUnscopedCompact"), StringComparison.Ordinal))
                    quiet.Add($"{code} ({stopped.Length}/{TooltipText.Cap})");
            }
            Check("a blocked account says so even when the icon is on the window with room", quiet.Count == 0,
                  $"{string.Join(", ", quiet)} — neither rung fitted, so the reading is mute about being blocked");

            // And it says it *unscoped*. The whole defect is a caption about the wrong window, so a fix that
            // captions the wrong window with a different word has not fixed anything: at 47% neither "Week
            // 7d: at limit" nor the on-track projection may appear.
            L.Apply("en");
            string blocked = TooltipText.Compose(StoppedElsewhere(Now));
            Check("and names neither the window with room nor its pace",
                  !blocked.Contains(L.T("tip.atLimitFull", TrayContext.MetricLabel("7d"), L.T("tip.limitUsed")),
                                    StringComparison.Ordinal) &&
                  !blocked.Contains(L.T("tip.okTrackFull", TrayContext.MetricLabel("7d")), StringComparison.Ordinal) &&
                  !blocked.Contains(L.T("tip.okTrackCompact"), StringComparison.Ordinal),
                  blocked.Replace("\n", " | "));

            // The other side of the same rule, which is T274's and is what stops this becoming a blanket
            // unscoping: where the metric IS the window that crossed, the figure and the caption agree and
            // the scoped sentence is the better one. Nothing here should have taken that away.
            string onIt = TooltipText.Compose(StoppedElsewhere(Now) with { Metric = "5h" });
            Check("while the window that actually crossed keeps its scoped sentence",
                  onIt.Contains(L.T("tip.atLimitFull", TrayContext.MetricLabel("5h"), L.T("tip.limitUsed")),
                                StringComparison.Ordinal),
                  onIt.Replace("\n", " | "));

            // And it must arrive *once*: the merge exists because the reading and the sentence were the
            // same fact twice. At the real cap this cannot be tested — there is no room for both, so a
            // composition emitting both would still render one, and the assertion would pass on a broken
            // rule. Given room, what keeps it single has to be the rule itself, so the room is asserted
            // first rather than assumed.
            L.Apply("en");
            string roomy = TooltipText.Compose(Roomy(Now));
            string sentence = L.T("tip.billingCompact");
            // Measured over the readings rather than over the output: a composition that wrongly emitted
            // the sentence has already spent the room, and judging it by what is left would fail the
            // precondition instead of the property — the guard reporting that it could not look, when what
            // it is looking at is exactly the defect.
            int spare = TooltipText.Cap - roomy.Length
                        + (roomy.Contains(sentence, StringComparison.Ordinal) ? sentence.Length + 1 : 0);
            if (Check($"a billing reading with room to spare has room for a second sentence",
                      spare >= sentence.Length + 1, $"{spare} spare < {sentence.Length + 1} needed"))
                Check("and the news is still said once, on the reading it is about",
                      !roomy.Contains(sentence, StringComparison.Ordinal)
                      && roomy.Contains(L.T("tip.extraPaying", "47%"), StringComparison.Ordinal),
                      roomy.Replace("\n", " | "));

            // The other half of the same state, and the reason the sentence is kept rather than deleted:
            // extra usage enabled with nothing spent yet is billing with no overage reading to merge into.
            // Nothing else in the tooltip would say so, and here the sentence is affordable.
            var silent = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                string unspent = TooltipText.Compose(Unspent(Now));
                if (!unspent.Contains(L.T("tip.billingCompact"), StringComparison.Ordinal)
                    && !unspent.Contains(L.T("tip.billingFull", L.T("tip.week")), StringComparison.Ordinal))
                    silent.Add(code);
            }
            Check("billing with nothing spent yet still has a sentence to say it with", silent.Count == 0,
                  $"{string.Join(", ", silent)} — merged away the line that had nothing to merge into");

            // T274. The verdict is about the account and the caption is about the window on the icon, so
            // the one case where they disagree is the one that has to be read: a rejected session behind a
            // week at 47%. The news must arrive in every language — that is T222's property, and the whole
            // point of rescoping the state is that it now arrives here too — and it must arrive *without*
            // the week's name on it, because "Week 7d: past your included quota" captions 47%.
            var unscoped = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                string elsewhere = TooltipText.Compose(Elsewhere(Now));
                if (!elsewhere.Contains(L.T("tip.billingCompact"), StringComparison.Ordinal)
                    || elsewhere.Contains(L.T("tip.billingFull", L.T("tip.week")), StringComparison.Ordinal))
                    unscoped.Add($"{code}: {elsewhere.Replace("\n", " | ")}");
            }
            Check("billing on a window the icon is not showing says so, and captions no percentage",
                  unscoped.Count == 0, string.Join("  //  ", unscoped));

            L.Apply("en");
            // And the scoped form is not gone — it is what the window that actually crossed still gets.
            // Asked with the budget slack for the reason the merge is: at the real cap the full form does
            // not fit either way, so a rule that had stopped choosing it would pass on the fallback.
            string crossedHere = TooltipText.Compose(Roomy(Now) with
            {
                Data = new UsageData { Session5h = 0.40, Week7d = 1.0, Status = "allowed", Status7d = "allowed" },
            });
            Check("the window that did cross keeps the scoped sentence, given room for it",
                  crossedHere.Contains(L.T("tip.billingFull", L.T("tip.week")), StringComparison.Ordinal),
                  crossedHere.Replace("\n", " | "));

            // T280. A spell is an event, and "since when" is the half no line carried. Deliberately NOT
            // T222's property: that one is "the news arrives whatever the budget", and this line is not news
            // — it qualifies news the sentence above it carries, so it is the last rung of the ladder and the
            // first thing dropped. What can be asserted is that the ladder is honest about it: a form that
            // would have fitted is never dropped anyway, which is the same rule the compact projection keeps
            // above, and the one a translation spending four more characters breaks silently.
            var wasted = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                foreach (TooltipCli.Variant v in variants)
                {
                    TooltipText.Input built = v.Build(Now);
                    if (built.SpellSince <= 0) continue;
                    string dur = TrayContext.FmtDays(built.Now - built.SpellSince);
                    string[] rungs =
                    {
                        L.T("tip.spellFull", dur), L.T("tip.spellCompact", dur), L.T("tip.spellBare", dur),
                    };
                    string text = TooltipText.Compose(built);
                    if (rungs.Any(r => text.Contains(r, StringComparison.Ordinal))) continue;
                    // Nothing was emitted, so every character of the room is still there to measure — and the
                    // cheapest rung is the one the room has to be judged against (T302).
                    int room = TooltipText.Cap - text.Length;
                    if (room >= rungs[^1].Length + 1)
                        wasted.Add($"{code}/{v.Name} (room {room}, cheapest {rungs[^1].Length})");
                }
            }
            Check("the spell's duration is taken whenever it fits, never dropped", wasted.Count == 0,
                  string.Join(", ", wasted));

            // T302 got this to "somewhere in every language"; T306 makes it every state, by letting the one
            // reading the user's own display setting did NOT choose be given up rather than say nothing.
            // Counted rather than named: which language runs out of room is a fact about translations, and a
            // list of the ones that do is a list that goes stale on the next rewording.
            var unsaid = new List<string>();
            // The same readings with no spell to state, so the two compositions differ only by this line —
            // which is how "did a reading get given up for it" can be asked at all.
            int Readings(string text)
            {
                string s5 = L.T("tip.session"), w7 = L.T("tip.week");
                return text.Split('\n').Count(l => l.Contains('%')
                    && (l.StartsWith(s5, StringComparison.Ordinal) || l.StartsWith(w7, StringComparison.Ordinal)));
            }

            var paidForAGlyph = new List<string>();
            var shedWithoutNeed = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                foreach (TooltipCli.Variant v in variants)
                {
                    TooltipText.Input built = v.Build(Now);
                    if (built.SpellSince <= 0) continue;
                    string dur = TrayContext.FmtDays(built.Now - built.SpellSince);
                    string full = L.T("tip.spellFull", dur), compact = L.T("tip.spellCompact", dur);
                    string bare = L.T("tip.spellBare", dur);

                    string with = TooltipText.Compose(built);
                    string without = TooltipText.Compose(built with { SpellSince = 0 });
                    bool worded = with.Contains(full, StringComparison.Ordinal)
                                  || with.Contains(compact, StringComparison.Ordinal);
                    if (!worded && !with.Contains(bare, StringComparison.Ordinal))
                    {
                        unsaid.Add($"{code}/{v.Name}");
                        continue;
                    }

                    if (Readings(with) >= Readings(without)) continue;
                    // A reading was given up for this line, and both halves of T306's rule bind here. It must
                    // buy words — and it may only happen where the alternative was silence, which `without`
                    // answers: it is this same composition carrying both readings and no spell line, so the
                    // room it leaves is exactly the room a rung would have had if nothing were shed.
                    if (!worded) paidForAGlyph.Add($"{code}/{v.Name}");
                    if (TooltipText.Cap - without.Length >= bare.Length + 1)
                        shedWithoutNeed.Add($"{code}/{v.Name} (room was {TooltipText.Cap - without.Length})");
                }
            }
            Check($"every billing state says how long, in every language " +
                  $"({codes.Length * variants.Count(v => v.Build(Now).SpellSince > 0)} combinations)",
                  unsaid.Count == 0,
                  $"silent: {string.Join(", ", unsaid)} — a state that costs money, with the duration behind " +
                  "it unsayable");
            Check("a reading is never given up for a wordless duration", paidForAGlyph.Count == 0,
                  $"{string.Join(", ", paidForAGlyph)} — a measured percentage traded for a glyph");
            Check("nor given up where the duration would have fitted anyway", shedWithoutNeed.Count == 0,
                  $"{string.Join(", ", shedWithoutNeed)} — §XXXIII's rule stands except against silence, so a " +
                  "state with room for a rung keeps both windows");

            // The cheapest rung is a glyph and a duration, and the glyph carries the whole meaning. It must
            // not be the one that already means "resets in": the line above ends in `⟳ 3d 0h`, so reusing it
            // would put two identical markers on one card pointing in opposite directions in time.
            var confusable = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                if (L.T("tip.spellBare", "3h 20m").Contains('⟳')) confusable.Add(code);
            }
            Check("and the marker it leads with is not the one that means 'resets in'",
                  confusable.Count == 0,
                  $"{string.Join(", ", confusable)} — elapsed and remaining cannot wear one glyph");

            // And where it is taken it is taken in full whenever the full form fits, so the compact rung is a
            // fallback rather than what every language silently ends up with.
            var shortchanged = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                string dated = TooltipText.Compose(Roomy(Now) with { SpellSince = Now - 3 * 3600 - 20 * 60 });
                string full = L.T("tip.spellFull", "3h 20m"), compact = L.T("tip.spellCompact", "3h 20m");
                if (!dated.Contains(full, StringComparison.Ordinal)
                    && dated.Contains(compact, StringComparison.Ordinal)
                    && TooltipText.Cap - dated.Length + compact.Length >= full.Length)
                    shortchanged.Add($"{code} ({dated.Length}/{TooltipText.Cap})");
            }
            Check("and in full whenever the full form fits", shortchanged.Count == 0,
                  string.Join(", ", shortchanged));

            // And the duration ages with the clock rather than freezing at the poll that measured it: the
            // input is the moment, not the elapsed time, which is the whole reason it is stored that way.
            L.Apply("en");
            string later = TooltipText.Compose(Roomy(Now) with
            {
                SpellSince = Now - 3 * 3600 - 20 * 60,
                Now = Now + 3600,
            });
            Check("and the same crossing an hour later reads an hour longer",
                  later.Contains(L.T("tip.spellCompact", "4h 20m"), StringComparison.Ordinal)
                  || later.Contains(L.T("tip.spellFull", "4h 20m"), StringComparison.Ordinal),
                  later.Replace("\n", " | "));

            // The line is about a spell, so a reading not in one may not carry it whatever the budget allows.
            // Both halves matter: an account inside its quota, and a billing account whose crossing is not on
            // file — the field-report shape, where the honest answer is to say nothing.
            string inQuota = TooltipText.Compose(Roomy(Now) with
            {
                State = QuotaState.InQuota,
                SpellSince = Now - 3 * 3600,
            });
            string nothingOnFile = TooltipText.Compose(Roomy(Now));
            Check("a reading with no spell to date carries no duration",
                  !inQuota.Contains(L.T("tip.spellCompact", "3h 0m"), StringComparison.Ordinal)
                  && !nothingOnFile.Contains(L.T("tip.spellCompact", "3h 20m"), StringComparison.Ordinal)
                  && !nothingOnFile.Contains(L.T("tip.spellFull", "3h 20m"), StringComparison.Ordinal),
                  $"{inQuota.Replace("\n", " | ")}  //  {nothingOnFile.Replace("\n", " | ")}");

            // And it is the rung that goes first. The billing sentence is the news; the duration qualifies
            // it, so a tooltip that could afford only one of them must keep the sentence.
            L.Apply("fr");
            string tightSpell = TooltipText.Compose(Elsewhere(Now) with { SpellSince = Now - 26 * 3600 });
            Check("at the cap the duration is dropped before the sentence it qualifies",
                  tightSpell.Length <= TooltipText.Cap
                  && tightSpell.Contains(L.T("tip.billingCompact"), StringComparison.Ordinal),
                  $"{tightSpell.Length}/{TooltipText.Cap}: {tightSpell.Replace("\n", " | ")}");
        }
        finally { L.Apply(L.Codes[(int)saved]); }
    }

    // ---------------------------------------------------------------- Block A: the handover between accounts

    /// <summary>
    /// What a change of monitored account drops (T293).
    ///
    /// <para>§XXXV argued no assertion was available, because a check would have to know which fields are the
    /// monitored account's — the same knowledge the old method failed to keep. That is true of the <em>list</em>
    /// and it is not true of the <em>rule</em>: once the list is one object, the property the whole design
    /// rests on is that <c>RefreshWatched</c> is its only assigner. A partial reset re-added to
    /// <c>AdoptMonitored</c>, or a second place that builds one, is exactly how this decays back into prose —
    /// and it is a thing the source text can be asked about, the way the header parse already is (T284).</para>
    ///
    /// <para>The rest is the constructor's own promise: a fresh instance carries nothing of the outgoing
    /// account, and its alarm is seeded from the incoming account's history rather than left blank.</para>
    /// </summary>
    private static void MonitoredHandover()
    {
        // The state is drawn from the key, so an invented profile's key is safe to build one for: nothing
        // here writes, and AccountFixture's own key is a hash of an invented account uuid.
        var fresh = new MonitoredAccount("acct-selftest-nonexistent");
        Check("a fresh monitored account carries no reading, and no time one was taken",
              fresh.Data is null && fresh.LastRefresh is null && fresh.LastGood is null,
              "one assignment has to be a complete drop, or the switch is back to being a list");
        Check("nor a spell, an error count, or a fired auth prompt",
              fresh.SpellSince == 0 && fresh.ConsecutiveErrors == 0 && !fresh.AutoOpenedForAuth,
              "the three fields building this object found — two of them live defects (T293)");
        // T304. The poll gate is per account for the switch's sake, so the incoming one must arrive able to
        // poll. A carried `Polling` would not be a stale reading — it would be an account that never polls
        // again, because nothing but a completed poll clears it.
        Check("and no poll in flight, so the incoming account can start one at once",
              !fresh.Polling && !fresh.PollAgain,
              "a carried flag is not a stale number, it is a tray that stops refreshing");
        Check("and its burn tracker is a new one rather than a cleared one",
              fresh.Burn.Project("7d", 0.5, 0, 1_800_000_000, 7 * 24 * 3600).verdict == Projection.Unknown,
              "a tracker with history behind it projects the outgoing account's slope");
        Check("it knows whose state it is", fresh.Key == "acct-selftest-nonexistent", fresh.Key);

        // The rule the design rests on, asked of the source text. Counting assignments rather than parsing:
        // one `_monitored =` in the file, and it is inside RefreshWatched.
        Repo("the monitored account is replaced in one place and nowhere else", root =>
        {
            string[] lines = File.ReadAllLines(Path.Combine(root, "src/Tray/TrayContext.cs"));
            var assigns = new List<int>();
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].TrimStart();
                if (t.StartsWith("//") || t.StartsWith("///")) continue;
                // The declaration itself is `private MonitoredAccount _monitored = null!;` — a declaration,
                // not a handover, and the one line allowed to look like one.
                if (t.Contains("_monitored =", StringComparison.Ordinal)
                    && !t.Contains("MonitoredAccount _monitored", StringComparison.Ordinal))
                    assigns.Add(i + 1);
            }

            if (!Check("the monitored account is assigned exactly once", assigns.Count == 1,
                       $"lines {string.Join(", ", assigns)} — a second assigner is a second opinion about " +
                       "what a switch drops, which is the prose T293 removed"))
                return;

            // Which method it is in: the nearest preceding line that declares one.
            int at = assigns[0] - 1;
            string owner = "(none found)";
            for (int i = at; i >= 0; i--)
            {
                string t = lines[i].TrimStart();
                if (!t.StartsWith("private ") && !t.StartsWith("internal ") && !t.StartsWith("public ")) continue;
                if (!t.Contains('(') || t.Contains(" _") || t.EndsWith(";", StringComparison.Ordinal)) continue;
                owner = t;
                break;
            }
            Check("and it is RefreshWatched that assigns it",
                  owner.Contains("RefreshWatched", StringComparison.Ordinal),
                  $"{owner} — the switch has to be detected and dropped in the same place, or a route that " +
                  "changes the monitored profile without going through a click carries the old state across");
        }, "src/Tray/TrayContext.cs");

        // T303. The poll awaits a fetch, and a switch can land inside that await — so the reading that comes
        // back is a fact about the account the poll *started* on, not about whichever one is monitored when it
        // finishes. Two things keep that true and neither can be reached without a tray, so both are asked of
        // the source: the guard exists, and nothing in the body attributes anything to the live monitored key.
        Repo("the poll attributes its reading to the account it started on", root =>
        {
            // The poll's own body, which T304 split out of RefreshAsync — and this precondition is what said
            // so, by failing rather than passing over a method it could no longer find.
            string[] body = MethodBody(File.ReadAllLines(Path.Combine(root, "src/Tray/TrayContext.cs")),
                                       "private async Task PollOnceAsync(MonitoredAccount account)");
            if (!Check("the poll's body can be read", body.Length > 0,
                       "the method was renamed or its signature moved — this check is reading nothing"))
                return;

            string[] code = body.Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)).ToArray();
            Check("the poll compares the account it started on with the one it ended on",
                  code.Any(l => l.Contains("ReferenceEquals(account, _monitored)", StringComparison.Ordinal)),
                  "no guard: a reading fetched for one account is written into the state of the next");

            string[] live = code.Where(l => l.Contains("ProfileStore.Monitored", StringComparison.Ordinal))
                                .Select(l => l.Trim()).ToArray();
            Check("and nothing in it is keyed on whichever account is monitored now", live.Length == 0,
                  $"{string.Join(" // ", live)} — resolved after the await, so a switch mid-flight files one " +
                  "account's reading under another's key, in the one store that cannot be rebuilt");
        }, "src/Tray/TrayContext.cs");

        // T304. The gate's whole design is an asymmetry between two callers, and an asymmetry is the one
        // thing a source check can hold that a headless run cannot: the behaviour needs a message pump, while
        // "which caller is marked" is a fact about two lines. Marking the tick would make every tick coalesce
        // and the gate would stop gating, silently, with the timer still ticking.
        Repo("the timer and the button ask for different things", root =>
        {
            string[] lines = File.ReadAllLines(Path.Combine(root, "src/Tray/TrayContext.cs"));
            // Both matched on the call as well as the wiring: there is a second `refresh.Click +=` in this
            // file — the insights menu's — and picking the right one by which comes first is a check that
            // passes for a reason nothing states.
            string? tick = lines.FirstOrDefault(l => l.Contains("_poll.Tick +=", StringComparison.Ordinal)
                                                     && l.Contains("RefreshAsync", StringComparison.Ordinal));
            string? click = lines.FirstOrDefault(l => l.Contains("refresh.Click +=", StringComparison.Ordinal)
                                                      && l.Contains("RefreshAsync", StringComparison.Ordinal));

            if (!Check("both wirings are still here to compare", tick != null && click != null,
                       $"tick: {tick ?? "(gone)"} // click: {click ?? "(gone)"}"))
                return;

            Check("a tick asks for no reading of its own",
                  !tick!.Contains("userAsked", StringComparison.Ordinal),
                  $"{tick.Trim()} — a coalescing tick re-runs the poll it was meant to skip");
            Check("and Refresh now says a person asked",
                  click!.Contains("userAsked: true", StringComparison.Ordinal),
                  $"{click.Trim()} — a click landing during a poll is dropped, so the menu does nothing");
        }, "src/Tray/TrayContext.cs");
    }

    /// <summary>The lines inside one method, found by its signature and closed by brace depth. Crude on
    /// purpose: it is reading this repository's own C#, where a brace inside a string literal at method scope
    /// does not occur, and a parser would be a second language implementation to keep.</summary>
    private static string[] MethodBody(string[] lines, string signature)
    {
        int start = Array.FindIndex(lines, l => l.Contains(signature, StringComparison.Ordinal));
        if (start < 0) return Array.Empty<string>();

        var body = new List<string>();
        int depth = 0;
        for (int i = start; i < lines.Length; i++)
        {
            int before = depth;
            depth += lines[i].Count(c => c == '{') - lines[i].Count(c => c == '}');
            if (before > 0 || depth > 0) body.Add(lines[i]);
            if (before > 0 && depth == 0) break;
        }
        return body.ToArray();
    }

    // ---------------------------------------------------------------- Block F: the report's profile picker

    /// <summary>
    /// Where the Statistics window lands when the tray hands it a rebuilt watch list (T319).
    ///
    /// <para>The picker was filled once, when the window was built, and everything after that rested on
    /// "index 0 is the account the poll is about" — which stops being true the moment the icon changes
    /// hands with the window open, because the list is monitored-first and the switch reorders it. The
    /// reported symptom was a work account's 86% drawn under a personal account's name, while picking the
    /// account actually being worked in read as the empty one.</para>
    ///
    /// <para>Two halves, and each is asserted where it can be. The decision is
    /// <see cref="StatisticsPage.PickerIndex"/> — pure, over keys, no window — and the wiring is a call
    /// site that has to exist in the one method that detects a switch, which is asked of the source the
    /// way T293's single assigner already is.</para>
    /// </summary>
    private static void ProfilePicker()
    {
        static ClaudeInfo P(string label, string dir) => new() { Label = label, ConfigDir = dir };
        static string K(ClaudeInfo p) => ProfileStore.KeyFor(p);

        ClaudeInfo personal = P("Pessoal", @"C:\Users\x\.claude");
        ClaudeInfo work = P("VILT", @"C:\Users\x\.claude-vilt");
        ClaudeInfo third = P("Cliente", @"C:\Users\x\.claude-cliente");

        // Before the switch: the icon is on Pessoal, so that is what the list leads with.
        var before = new List<ClaudeInfo> { personal, work };
        // After it: the same two accounts, and the tray puts the monitored one first.
        var after = new List<ClaudeInfo> { work, personal };

        Check("a window following the icon follows it through the switch",
              StatisticsPage.PickerIndex(after, K(personal), followingMonitored: true) == 0,
              "the icon moved and the report did not: the pushed reading lands under the old name");

        Check("and a profile picked by hand is found where the reorder put it",
              StatisticsPage.PickerIndex(after, K(personal), followingMonitored: false) == 1,
              "position 1 after a switch is not the profile position 1 named before it — this is the " +
              "whole defect: by index, the pick silently becomes the other account");

        Check("a pick that is no longer on the machine falls back to the icon's",
              StatisticsPage.PickerIndex(new List<ClaudeInfo> { work, third }, K(personal),
                                         followingMonitored: false) == 0,
              "an unregistered profile must not leave the picker pointing at nothing");

        Check("and an empty list answers 0 rather than throwing",
              StatisticsPage.PickerIndex(new List<ClaudeInfo>(), K(personal), followingMonitored: false) == 0);

        // The other half of the rebuild: when it may be skipped. The tray re-discovers on every menu open
        // (T137), so most calls change nothing, and rebuilding the items would shut an open dropdown.
        Check("two discoveries of the same machine build the same picker",
              StatisticsPage.Same(before, new List<ClaudeInfo> { P("Pessoal", @"C:\Users\x\.claude"), work }),
              "a fresh ClaudeInfo per discovery must compare by what the picker shows, not by reference");
        Check("a switch is not the same picker, because the order is the answer",
              !StatisticsPage.Same(before, after),
              "the reorder IS the switch — skipping it here is the frozen list all over again");
        Check("and neither is a renamed profile",
              !StatisticsPage.Same(before, new List<ClaudeInfo> { P("Casa", @"C:\Users\x\.claude"), work }),
              "the label is the whole of what an entry shows");
        Check("nor a profile added to the machine",
              !StatisticsPage.Same(before, new List<ClaudeInfo> { personal, work, third }));

        // The wiring. Behaviour here needs a window and a tray; that the call exists, in the method that
        // detects a switch, and above the early return that only a switch passes, does not.
        Repo("the open report is told where the switch is detected", root =>
        {
            string[] body = MethodBody(File.ReadAllLines(Path.Combine(root, "src/Tray/TrayContext.cs")),
                                       "private void RefreshWatched()");
            if (!Check("RefreshWatched's body can be read", body.Length > 0,
                       "the method was renamed — this check is reading nothing"))
                return;

            string[] code = body.Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)).ToArray();
            int told = Array.FindIndex(code, l => l.Contains("Statistics.SetProfiles(", StringComparison.Ordinal));
            if (!Check("the watch list reaches an open Statistics page from here", told >= 0,
                       "filled once at window-open, the picker names accounts the poll is no longer about"))
                return;

            // The early return is the switch check: below it, only a change of monitored account runs.
            int switched = Array.FindIndex(code, l => l.Contains("== before) return;", StringComparison.Ordinal));
            Check("and before the return that only a switch gets past",
                  switched < 0 || told < switched,
                  "a profile added or removed reorders the picker without the icon moving, and a rebuild " +
                  "below that return never sees it");
        }, "src/Tray/TrayContext.cs");
    }

    // ---------------------------------------------------------------- Block A: dating the overage spell

    /// <summary>
    /// <see cref="OverageSpell"/>: which reading the current spell started at, and when there is no answer
    /// (T280).
    ///
    /// <para>The tooltip's half of this is asserted above, from an <c>Input</c> carrying a moment. This is
    /// the half that decides whether there <em>is</em> a moment, and it is a walk backwards through a run of
    /// readings with three ways to end — which is exactly the kind of rule that goes unchecked when it can
    /// only be reached through a resident tray. Fixtures rather than the store, so no test writes to a real
    /// profile's log.</para>
    /// </summary>
    private static void Spell()
    {
        const long Now = 1_800_000_000;
        const long Step = 300;   // the default poll cadence, so the runs read like real ones

        // `ix` on and `ux` flat at zero the whole way — the spell of 2026-08-04, and the shape the figure
        // route is blind to (T276). Two quiet readings before the crossing, so the event is observed.
        UsageSample Inside(int n) => new(Now - (10 - n) * Step, 0.4, 0, 0.9, 0, Extra: 0, InUse: false);
        UsageSample Over(int n) => new(Now - (10 - n) * Step, 0.4, 0, 1.0, 0, Extra: 0, InUse: true);

        var run = new List<UsageSample> { Inside(0), Inside(1), Over(2), Over(3), Over(4) };
        Check("the spell is dated at the first reading past the threshold, not the latest one",
              OverageSpell.StartedAt(run) == (long)run[2].T,
              $"{OverageSpell.StartedAt(run)} vs {(long)run[2].T}");

        // The figure route, on an account whose utilization does climb — the same walk, the other signal.
        var byFigure = new List<UsageSample>
        {
            Inside(0),
            new(Now - 8 * Step, 0.4, 0, 1.0, 0, Extra: 0.02, InUse: null),
            new(Now - 7 * Step, 0.4, 0, 1.0, 0, Extra: 0.05, InUse: null),
        };
        Check("a spell measured only by the overage figure is dated the same way",
              OverageSpell.StartedAt(byFigure) == (long)byFigure[1].T,
              $"{OverageSpell.StartedAt(byFigure)} vs {(long)byFigure[1].T}");

        // Not in a spell now: whatever happened last week, there is no current one to date.
        var ended = new List<UsageSample> { Inside(0), Over(1), Over(2), Inside(3) };
        Check("a reading back inside the quota dates nothing", OverageSpell.StartedAt(ended) is null,
              $"{OverageSpell.StartedAt(ended)}");

        // The three ways there is no *event* on file. A run reaching the log's own beginning is the one a
        // restart mid-spell produces, and dating it from the first line would report the day the log starts
        // as the crossing. A reading carrying neither header is not an observation of quiet (T179, T276).
        Check("a run reaching the oldest line on file is not a dated crossing",
              OverageSpell.StartedAt(new List<UsageSample> { Over(0), Over(1), Over(2) }) is null);
        Check("an empty log dates nothing", OverageSpell.StartedAt(new List<UsageSample>()) is null);
        var absent = new List<UsageSample>
        {
            new(Now - 10 * Step, 0.4, 0, 0.9, 0),   // no `ux`, no `ix` — a line written before either existed
            Over(1), Over(2),
        };
        Check("and a reading carrying neither header is not the quiet side of a crossing",
              OverageSpell.StartedAt(absent) is null, $"{OverageSpell.StartedAt(absent)}");

        // A gap inside the run is not a break: readings stop while the tray is closed, and the alternative
        // would be to invent a return to the quota nobody measured.
        var gapped = new List<UsageSample>
        {
            Inside(0), Over(1),
            new(Now, 0.4, 0, 1.0, 0, Extra: 0, InUse: true),   // a day later, still over
        };
        Check("a gap in the readings does not restart the spell",
              OverageSpell.StartedAt(gapped) == (long)gapped[1].T,
              $"{OverageSpell.StartedAt(gapped)} vs {(long)gapped[1].T}");

        // One predicate, two callers (T280 moved it): the alarm's latch and this walk must agree about what
        // a reading says, or a spell can be announced and then not datable, or the reverse.
        Check("`Spending` reads absent as neither yes nor no",
              !QuotaStates.Spending(null, null)
              && QuotaStates.Spending(null, true)
              && QuotaStates.Spending(0.02, null)
              && !QuotaStates.Spending(0, false));
    }

    /// <summary>A reading whose projection has both a full and a compact form, so the budget has
    /// something to ration.</summary>
    private static TooltipText.Input Long(long now) => new(
        Data: new UsageData
        {
            Session5h = 0.61, Week7d = 0.88, Reset5h = now + 2 * 3600, Reset7d = now + 3 * 86400,
            Status = "allowed", Status7d = "allowed",
        },
        Metric: "5h", ShowRemaining: false, ProfileLabel: null,
        Verdict: Projection.Danger, Eta: 26 * 3600, State: QuotaState.InQuota,
        Updated: "  ⟳ 14:32:05", Now: now);

    /// <summary>The shape that overran: an overage line on top of both windows, which is the one state
    /// that adds a fourth reading — and the state French composed 129 characters for.</summary>
    private static TooltipText.Input Overflowing(long now) => Long(now) with
    {
        Metric = "7d",
        Data = new UsageData
        {
            Session5h = 0.40, Week7d = 1.0, Extra = 0.47, HasExtra = true,
            Reset5h = now + 2 * 3600, Reset7d = now + 3 * 86400, ResetExtra = now + 3 * 86400,
            Status = "allowed", Status7d = "allowed", StatusExtra = "allowed",
        },
        Verdict = Projection.Unknown,
        Eta = 0,
        State = QuotaState.Billing,
    };

    /// <summary>T288's reading, and the one measured on 2026-08-04: the session rejected at 102% behind a
    /// week at 47%, with the icon on the week. The account is <see cref="QuotaState.Stopped"/> and the
    /// metric is the window that did <em>not</em> cross — which is the whole case, since a caption naming
    /// this window would name 47%.</summary>
    private static TooltipText.Input StoppedElsewhere(long now) => Long(now) with
    {
        Metric = "7d",
        Data = new UsageData
        {
            Session5h = 1.02, Week7d = 0.47,
            Reset5h = now + 2 * 3600, Reset7d = now + 3 * 86400,
            Status = "rejected", Status7d = "allowed",
        },
        Verdict = Projection.Ok,
        Eta = 4 * 3600,
        State = QuotaState.Stopped,
    };

    /// <summary>The billing state with the budget deliberately slack — no refresh time, no known resets —
    /// so there is room for a second sentence and the rule, not the cap, is what keeps the news single.
    /// </summary>
    private static TooltipText.Input Roomy(long now) => Overflowing(now) with
    {
        Updated = "",
        Data = new UsageData
        {
            Session5h = 0.40, Week7d = 1.0, Extra = 0.47, HasExtra = true,
            Reset5h = 0, Reset7d = 0, ResetExtra = 0,
            Status = "allowed", Status7d = "allowed", StatusExtra = "allowed",
        },
    };

    /// <summary>The same state before a cent of the allowance is spent: billing, and no overage reading to
    /// carry the news — so the sentence T222 merges away everywhere else is the only thing that says it.
    /// Asserted because the merge would otherwise have made this state mute (T222).</summary>
    private static TooltipText.Input Unspent(long now) => Overflowing(now) with
    {
        Data = new UsageData
        {
            Session5h = 0.40, Week7d = 1.0, Extra = 0, HasExtra = false,
            Reset5h = now + 2 * 3600, Reset7d = now + 3 * 86400,
            Status = "allowed", Status7d = "allowed", StatusExtra = "unknown",
        },
    };

    /// <summary>The field report of 2026-08-04 as a tooltip input (T274): the session rejected at 102%, the
    /// week at 47%, the icon on the week, and the overage figure at zero the whole way through — so nothing
    /// on this reading except the state itself can say money is being spent.</summary>
    private static TooltipText.Input Elsewhere(long now) => Overflowing(now) with
    {
        Data = new UsageData
        {
            Session5h = 1.02, Week7d = 0.47, Extra = 0, HasExtra = true,
            Reset5h = now + 2 * 3600, Reset7d = now + 3 * 86400,
            Status = "rejected", Status7d = "allowed", StatusExtra = "allowed",
        },
        Verdict = Projection.Ok,
        Eta = 4 * 3600,
    };

    // ---------------------------------------------------------------- Block AI: the flag surface

    /// <summary>
    /// The preview and capture flags — the surface every visual verification in this repository goes
    /// through, and the one thing none of the other assertions here touch (T207). Everything else is
    /// arithmetic, stores and rules; the variant tables and the refusals around them were held up by
    /// whoever next ran a flag by hand and read the result.
    ///
    /// <para>Each refusal asserted here has a failure mode that is silent by construction: a table that
    /// stops resolving a name renders <em>the default</em> instead, which is a screenshot of the wrong
    /// thing that looks exactly like a screenshot of the right one (T186, T198, T200), and
    /// <c>--sample</c> failing open publishes a real account (T205). None of them would be noticed by
    /// anything else in this file.</para>
    ///
    /// <para>They are cheap because they are pure: a table lookup and a resolver, returning a value or
    /// null, with no window and no file. The refusals print their catalogue to the console, which is
    /// their job and not this section's output, so those calls run through <see cref="Quietly"/>.</para>
    /// </summary>
    private static void Flags()
    {
        IReadOnlyList<StatsPreviews.Variant> stats = StatsPreviews.Catalogue;
        IReadOnlyList<ToastPreviews.Variant> toasts = ToastPreviews.Catalogue;

        // The guard on the precondition first: an empty table resolves nothing and refuses nothing, and
        // every claim below would hold vacuously over it.
        if (!Check("both preview tables carry their rows", stats.Count >= 8 && toasts.Count >= 5,
                   $"{stats.Count} stats variants, {toasts.Count} toasts"))
            return;

        static string[] Words(string name) => name.Length == 0 ? Array.Empty<string>() : new[] { name };
        static string Named(string name) => name.Length == 0 ? "(default)" : name;

        string[] deadStats = stats.Where(v => StatsPreviews.Resolve(Words(v.Name), capturing: false) is null)
                                  .Select(v => Named(v.Name)).ToArray();
        Check($"every --stats variant the table declares resolves ({stats.Count})", deadStats.Length == 0,
              $"{string.Join(", ", deadStats)} resolved to null — the flag would refuse a name it prints");

        // The asymmetry is the point: --capture-stats must refuse the variants whose content it cannot
        // render, and no others. Anchored on the two that ARE the method popup rather than on
        // `Variant.Capturable` — Resolve decides *by* that flag, so comparing the two is one expression
        // compared with itself, and stays green when the flag is inverted. Confirmed: with
        // `Capturable => true` the earlier version of this assertion passed.
        string[] popup = { "method", "thin" };
        if (!Check("the method-popup variants are still named that", popup.All(n => stats.Any(v => v.Name == n)),
                   $"{string.Join(", ", popup.Where(n => stats.All(v => v.Name != n)))} — renamed, and every " +
                   "claim below would hold by resolving nothing"))
            return;

        string[] leaked = popup
            .Where(n => Quietly(() => StatsPreviews.Resolve(new[] { n }, capturing: true)) is not null).ToArray();
        Check("--capture-stats refuses the method-popup variants it cannot render", leaked.Length == 0,
              $"{string.Join(", ", leaked)} — the PNG would be the page with no popup in it (T186)");

        string[] overRefused = stats.Where(v => !popup.Contains(v.Name))
            .Where(v => Quietly(() => StatsPreviews.Resolve(Words(v.Name), capturing: true)) is null)
            .Select(v => Named(v.Name)).ToArray();
        Check("and refuses nothing else", overRefused.Length == 0, string.Join(", ", overRefused));

        Check("while --stats still shows them, which is the whole asymmetry",
              popup.All(n => StatsPreviews.Resolve(new[] { n }, capturing: false) is not null),
              "the interactive flag refuses them too, so the popup cannot be got on screen at all");

        string[] deadModifiers = StatsPreviews.ModifierRows
            .Where(m => StatsPreviews.Resolve(new[] { m.Name }, capturing: false) is null)
            .Select(m => m.Name).ToArray();
        Check($"every --stats modifier composes ({StatsPreviews.ModifierRows.Count})", deadModifiers.Length == 0,
              string.Join(", ", deadModifiers));

        Check("a --stats name that names no row is refused, not defaulted",
              Quietly(() => StatsPreviews.Resolve(new[] { "nosuchvariant" }, capturing: false)) is null,
              "it resolved — an unknown name renders the default and calls it what was asked for (T186)");

        // A real variant beside an invented word must still be refused: taking the one it recognises is
        // the same defect wearing a legal-looking argument.
        Check("and so is a real variant beside one that is not",
              Quietly(() => StatsPreviews.Resolve(new[] { "shape", "nosuchmodifier" }, capturing: false)) is null,
              "it resolved 'shape' and dropped the word it did not know");

        string[] deadToasts = toasts.Where(v => ToastPreviews.Resolve(v.Name) is null)
                                    .Select(v => v.Name).ToArray();
        Check($"every toast variant the table declares resolves ({toasts.Count})", deadToasts.Length == 0,
              string.Join(", ", deadToasts));

        Check("a bare toast flag is the first row, not a refusal",
              ToastPreviews.Resolve(null)?.Name == toasts[0].Name,
              $"got '{ToastPreviews.Resolve(null)?.Name}', expected '{toasts[0].Name}'");

        Check("an unknown toast name is refused, not defaulted",
              Quietly(() => ToastPreviews.Resolve("nosuchcard")) is null,
              "it resolved — a screenshot of the wrong card looks like one of the right card (T198)");

        // The privacy guard (T200): a week that cannot be honoured must stop the run, because the
        // fallback is this machine's real account rendered into a file that gets published.
        string[] deadWeeks = Enum.GetNames<AccountFixture.SampleWeek>()
            .Where(w => AccountFixture.ResolveWeek(w) is null).ToArray();
        Check($"every week= name resolves ({Enum.GetNames<AccountFixture.SampleWeek>().Length})",
              deadWeeks.Length == 0, string.Join(", ", deadWeeks));
        Check("week= is case-insensitive, the way the flag is typed",
              AccountFixture.ResolveWeek("SPENDING") == AccountFixture.SampleWeek.Spending);
        Check("an absent week= is the default, not a refusal",
              AccountFixture.ResolveWeek(null) == AccountFixture.SampleWeek.Spending &&
              AccountFixture.ResolveWeek("") == AccountFixture.SampleWeek.Spending);
        Check("an unknown week= is refused, so --sample stops instead of falling back",
              Quietly(() => AccountFixture.ResolveWeek("nosuchweek")) is null,
              "it resolved — the run would build the default week and publish the wrong branch (T200)");

        // T205's refusal, which this block added: the one page whose content is an account, on the one
        // path that writes a file.
        Check("capturing the System page without --sample is refused",
              AccountFixture.CaptureRefusal("System", sampled: false) is not null);
        Check("and is refused however it is spelled",
              AccountFixture.CaptureRefusal("system", sampled: false) is not null);
        Check("with --sample it proceeds",
              AccountFixture.CaptureRefusal("System", sampled: true) is null);
        Check("and no other page is touched by the rule",
              AccountFixture.CaptureRefusal("General", sampled: false) is null &&
              AccountFixture.CaptureRefusal(null, sampled: false) is null);

        IReadOnlyList<TooltipCli.Variant> tips = TooltipCli.Catalogue;
        string[] deadTips = tips.Where(v => TooltipText.Compose(v.Build(1_800_000_000)).Length == 0)
                                .Select(v => v.Name).ToArray();
        Check($"every tooltip variant composes text ({tips.Count})", deadTips.Length == 0,
              string.Join(", ", deadTips));

        // The cap is the tooltip's whole design constraint: past it Windows truncates, and the line it
        // would cut is the one carrying the refresh time.
        string[] overCap = tips
            .Where(v => TooltipText.Compose(v.Build(1_800_000_000)).Length > TooltipText.Cap)
            .Select(v => $"{v.Name} ({TooltipText.Compose(v.Build(1_800_000_000)).Length})").ToArray();
        Check($"and none of them exceeds the {TooltipText.Cap}-char cap", overCap.Length == 0,
              string.Join(", ", overCap));

        SkillCatalogue(toasts, tips);
    }

    /// <summary>
    /// The one assertion here with reach: it turns "write the flag down in <c>dev-flags</c>" from a
    /// convention into something a build checks (T207). A catalogue in prose and a table in code are two
    /// sources of truth for one list, and the drift is always the same direction — the table gains a row
    /// and the document keeps describing the old set.
    ///
    /// <para>Both directions are asserted, because they catch opposite mistakes: every row must be named
    /// in the skill (a variant added and never documented), and every name the skill lists in a
    /// <c>| name</c> position must exist in the table (one renamed or removed, leaving the document
    /// promising a flag that now refuses).</para>
    /// </summary>
    private static void SkillCatalogue(IReadOnlyList<ToastPreviews.Variant> toasts,
                                       IReadOnlyList<TooltipCli.Variant> tips)
    {
        Repo("the dev-flags catalogue names what the tables declare",
             root => SkillCatalogue(root, toasts, tips), DevFlags);
    }

    /// <summary>The catalogue two checks read, spelled once.</summary>
    private const string DevFlags = ".claude/skills/dev-flags/SKILL.md";

    private static void SkillCatalogue(string root, IReadOnlyList<ToastPreviews.Variant> toasts,
                                       IReadOnlyList<TooltipCli.Variant> tips)
    {
        string[] lines = File.ReadAllLines(Path.Combine(root, DevFlags));
        string toastBlock = SkillBlock(lines, "--simulate-reset");
        string weekBlock = SkillBlock(lines, "--settings System --sample week=");

        if (!Check("the dev-flags catalogue still has the blocks these tables document",
                   toastBlock.Length > 0 && weekBlock.Length > 0,
                   $"--simulate-reset {(toastBlock.Length > 0 ? "found" : "MISSING")}, " +
                   $"week= {(weekBlock.Length > 0 ? "found" : "MISSING")} — the check cannot read what it compares"))
            return;

        string[] undocumented = toasts.Select(v => v.Name)
            .Where(n => !toastBlock.Contains(n, StringComparison.Ordinal)).ToArray();
        Check($"every toast the table declares is named in dev-flags ({toasts.Count})",
              undocumented.Length == 0,
              $"{string.Join(", ", undocumented)} — a card the catalogue does not mention");

        string[] promised = ListedNames(toastBlock)
            .Where(n => toasts.All(v => !v.Name.Equals(n, StringComparison.Ordinal))).ToArray();
        Check("and every toast dev-flags lists exists in the table", promised.Length == 0,
              $"{string.Join(", ", promised)} — documented, and the flag would refuse it");

        string[] weeks = Enum.GetNames<AccountFixture.SampleWeek>();
        string[] weeksMissing = weeks
            .Where(w => !weekBlock.Contains(w.ToLowerInvariant(), StringComparison.Ordinal)).ToArray();
        Check($"every week= name is named in dev-flags ({weeks.Length})", weeksMissing.Length == 0,
              string.Join(", ", weeksMissing));

        string[] weeksPromised = ListedNames(weekBlock)
            .Where(n => !weeks.Any(w => w.Equals(n, StringComparison.OrdinalIgnoreCase))).ToArray();
        Check("and every week= dev-flags lists exists", weeksPromised.Length == 0,
              string.Join(", ", weeksPromised));

        // The table this block itself added, held to the rule it just wrote (T214): a flag documented
        // the day it ships and never again is the drift this assertion exists to stop.
        string tipBlock = SkillBlock(lines, "--tooltip");
        if (!Check("the dev-flags catalogue has the --tooltip block", tipBlock.Length > 0,
                   "MISSING — the check cannot read what it compares"))
            return;

        string[] tipsUndocumented = tips.Select(v => v.Name)
            .Where(n => !tipBlock.Contains(n, StringComparison.Ordinal)).ToArray();
        Check($"every tooltip variant is named in dev-flags ({tips.Count})", tipsUndocumented.Length == 0,
              string.Join(", ", tipsUndocumented));

        string[] tipsPromised = ListedNames(tipBlock)
            .Where(n => tips.All(v => !v.Name.Equals(n, StringComparison.Ordinal))).ToArray();
        Check("and every tooltip dev-flags lists exists", tipsPromised.Length == 0,
              string.Join(", ", tipsPromised));
    }

    /// <summary>One flag's entry in the skill's fenced catalogue: the line that opens with it, plus the
    /// indented <c>#</c> continuations under it, which is how every entry in that file is written.</summary>
    private static string SkillBlock(string[] lines, string flag)
    {
        int start = Array.FindIndex(lines, l => l.StartsWith(flag, StringComparison.Ordinal));
        if (start < 0) return "";

        var block = new StringBuilder(lines[start]);
        for (int i = start + 1; i < lines.Length && lines[i].TrimStart().StartsWith('#'); i++)
            block.Append(' ').Append(lines[i].TrimStart());
        return block.ToString();
    }

    /// <summary>The names a catalogue entry lists as alternatives — the <c>| name</c> positions, which is
    /// the shape every such entry in that file uses.</summary>
    private static string[] ListedNames(string block) =>
        System.Text.RegularExpressions.Regex.Matches(block, @"\|\s*([a-z][a-z0-9-]*)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>A refusal prints its whole catalogue, which is the right behaviour for a person at a
    /// prompt and noise in the middle of a self-check. Runs one and keeps the return value.</summary>
    private static T Quietly<T>(Func<T> f)
    {
        TextWriter saved = Console.Out;
        try { Console.SetOut(TextWriter.Null); return f(); }
        finally { Console.SetOut(saved); }
    }

    // ---------------------------------------------------------------- Block AJ: the ledger's own index

    /// <summary>
    /// <c>CHANGELOG.md</c> opens with a table mapping every block letter to its theme, and that table is
    /// what the next task is filed against — a letter missing from it reads exactly like a letter that
    /// does not exist, which is how this repository reached AH by opening a block per batch of findings
    /// instead of reusing the theme (T223).
    ///
    /// <para>The rule was already written down: the row is added by hand in the same commit as the
    /// block's first task, and the <c>roadmap-docs</c> skill calls it the one hand-edit to a governed
    /// file the discipline allows. What was missing is anything that notices when it is not done —
    /// <c>roadkeep lint</c> passes, because the table is prose to it. Two of the thirty-six headings had
    /// no row when this was written, one of them carrying five shipped tasks.</para>
    ///
    /// <para>Both directions are asserted, because they catch opposite mistakes: a heading with no row
    /// (the block shipped and the index never learned) and a row naming no heading (a letter renamed or
    /// retired, leaving the index pointing at nothing). The <b>anchor</b> is asserted too — it is
    /// derived from the heading exactly as GitHub derives it, so a heading reworded without its row is a
    /// link that lands on the top of the page rather than on the block.</para>
    ///
    /// <para>The <c>roadmap-docs</c> skill's own theme table is deliberately <em>not</em> a third source
    /// to check for completeness: it lists the themes that are meant to be reused, not every historical
    /// batch letter, so demanding a row per heading there would argue for exactly the sprawl it exists to
    /// stop. Only the one direction that cannot be right — a letter the skill names and the ledger does
    /// not declare — is asserted.</para>
    ///
    /// <para><b>The one part of a row that is a claim about another file (T244).</b>
    /// <c>(active — see ROADMAP)</c> says a block still has open work, and T223 checked everything about a
    /// row except that. It was wrong in both directions on six of the thirty-six: five blocks carried the
    /// marker with nothing open, and <c>G</c> had an open task and no marker. It is the only part of the
    /// index that answers <em>should I look here</em> — a wrong marker sends the next reader to a heading
    /// with nothing under it, or reads an active theme as a finished one, which is the habit that grew this
    /// ledger a letter per batch of findings.</para>
    ///
    /// <para>§XXII.6 left open whether to require the marker or only refuse a wrong one. It is
    /// <b>required</b>, both ways: unlike the entries under a heading, the marker is not frozen prose about
    /// what shipped — it is a live reading of <c>ROADMAP.md</c>, and every row that had drifted could be
    /// corrected in the commit that added this. Open is derived rather than spelled: the roadmap holds
    /// unshipped work only, so <em>any</em> task line under a heading is an open one, and the check needs
    /// no copy of the marker vocabulary to go stale beside <c>roadkeep.toml</c>'s.</para>
    /// </summary>
    private static void LedgerIndex() =>
        Repo("the ledger's index of blocks lists every block it holds", LedgerIndex,
             "CHANGELOG.md", "ROADMAP.md", RoadmapDocs);

    /// <summary>The skill whose theme table this check reads.</summary>
    private const string RoadmapDocs = ".claude/skills/roadmap-docs/SKILL.md";

    private static void LedgerIndex(string root)
    {
        string[] lines = File.ReadAllLines(Path.Combine(root, "CHANGELOG.md"));
        var headings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            Match m = Regex.Match(line, @"^## Block ([A-Z]+) [—-] .+$");
            if (m.Success) headings[m.Groups[1].Value] = Anchor(line[3..]);
        }

        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        var themes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            Match m = Regex.Match(line, @"^\| \[([A-Z]+)\]\(#([^)]+)\) \| (.+) \|$");
            if (!m.Success) continue;
            rows[m.Groups[1].Value] = m.Groups[2].Value;
            themes[m.Groups[1].Value] = m.Groups[3].Value;
        }

        // The precondition, first: an unreadable file parses to two empty lists, over which every claim
        // below holds vacuously — the failure mode this whole section exists to refuse.
        if (!Check("the ledger still has block headings and an index table",
                   headings.Count >= 30 && rows.Count >= 30,
                   $"{headings.Count} headings, {rows.Count} rows — the check cannot read what it compares"))
            return;

        string[] unlisted = headings.Keys.Where(b => !rows.ContainsKey(b)).OrderBy(b => b, StringComparer.Ordinal).ToArray();
        Check($"every block heading has a row in the ledger's index ({headings.Count})", unlisted.Length == 0,
              $"{string.Join(", ", unlisted)} — shipped under a letter the index does not list at all");

        string[] phantom = rows.Keys.Where(b => !headings.ContainsKey(b)).OrderBy(b => b, StringComparer.Ordinal).ToArray();
        Check("and every row in the index names a heading that exists", phantom.Length == 0,
              $"{string.Join(", ", phantom)} — indexed, and the link lands nowhere");

        string[] adrift = rows.Where(r => headings.TryGetValue(r.Key, out string? a) && a != r.Value)
                              .Select(r => $"{r.Key} (#{r.Value} → #{headings[r.Key]})")
                              .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Check("and every row's anchor still resolves to its heading", adrift.Length == 0,
              $"{string.Join(", ", adrift)} — the heading was reworded and the row was not");

        // The roadmap holds unshipped work only, so a task line under a heading IS an open one — no copy
        // of roadkeep.toml's marker vocabulary to drift out of step with it.
        var open = new HashSet<string>(StringComparer.Ordinal);
        string? block = null;
        foreach (string line in File.ReadAllLines(Path.Combine(root, "ROADMAP.md")))
        {
            Match head = Regex.Match(line, @"^## Block ([A-Z]+) ");
            if (head.Success) { block = head.Groups[1].Value; continue; }
            if (block is not null && Regex.IsMatch(line, @"^- .*\*\*T\d+\*\*")) open.Add(block);
        }

        string[] mismarked = themes
            .Where(t => open.Contains(t.Key) != t.Value.Contains("(active", StringComparison.Ordinal))
            .Select(t => open.Contains(t.Key) ? $"{t.Key} (open, unmarked)" : $"{t.Key} (marked, nothing open)")
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Check($"every row's active marker matches what the roadmap holds open ({open.Count} active)",
              mismarked.Length == 0,
              $"{string.Join(", ", mismarked)} — the index answers 'should I look here' and is wrong");

        string skill = Path.Combine(root, RoadmapDocs);

        // One direction only, and the doc comment says why the other would be wrong.
        string[] invented = Regex.Matches(File.ReadAllText(skill), @"^\|[^|]+\| \*\*([A-Z]+)\*\*", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Where(b => !headings.ContainsKey(b))
            .OrderBy(b => b, StringComparer.Ordinal).ToArray();
        Check("the theme table names only blocks the ledger declares", invented.Length == 0,
              $"{string.Join(", ", invented)} — a theme filed under a letter no block heading declares");
    }

    /// <summary>
    /// The per-file map against the tree it maps (T242). T219 moved it to the <c>file-map</c> skill on the
    /// argument that reference material is consulted rather than read — which is exactly when a hole in it
    /// costs something, because it is opened by a reader who does not already know the answer.
    ///
    /// <para>The map is a hand-written copy of <c>src\</c> and the tree is edited daily, so it drifts in
    /// three ways nothing would report: a new file with no row, a row naming a file that was renamed or
    /// deleted, and a row left under the folder a file has since moved out of. One of each was already
    /// there when this was written — eight undocumented files, and <c>ThroughputFixture</c> filed under the
    /// Context heading for a file that lives in <c>src\Usage\</c>.</para>
    ///
    /// <para><b>What counts as documented.</b> A row names one type, and this repository deliberately
    /// spreads one class over several files — <c>StatisticsPage.{Throughput,Chart,…}.cs</c> is one class in
    /// six files (T133–T134), and a XAML page is a pair. So a file is covered by its own row <em>or</em> by
    /// the row for its stem, everything before the first dot; a page's row is written
    /// <c>Foo.xaml(.cs)</c> and both halves of the pair must exist. Requiring a row per file would make the
    /// map longer than the tree and would argue against the partial-file convention.</para>
    ///
    /// <para>The folder table left in <c>AGENTS.md</c> is checked the same way and for a sharper reason: a
    /// new subsystem folder is precisely the moment placement matters, and that table — not the skill — is
    /// what a reader hits first, being in the file loaded every turn.</para>
    /// </summary>
    private static void FileMap() =>
        Repo("the file map names every source file, and only files that exist", FileMap,
             ".claude/skills/file-map/SKILL.md", "AGENTS.md", "src");

    private static void FileMap(string root)
    {
        string skill = Path.Combine(root, ".claude/skills/file-map/SKILL.md");
        string agents = Path.Combine(root, "AGENTS.md");
        string src = Path.Combine(root, "src");
        string[] files = Directory.Exists(src)
            ? Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories)
                       .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).ToArray()
            : Array.Empty<string>();

        string[] rows = Regex.Matches(File.ReadAllText(skill), @"^\| `(src/[^`]+)`", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // The precondition, first: an unreadable file or a renamed folder parses to two empty lists, over
        // which both claims below hold vacuously.
        if (!Check("the file map and the source tree are both readable",
                   files.Length >= 40 && rows.Length >= 40,
                   $"{files.Length} .cs files, {rows.Length} rows — the check cannot read what it compares"))
            return;

        // `Foo.xaml(.cs)` is the pair; every other row is one path. Both halves have to be on disk, or the
        // shorthand is hiding half a page that no longer exists.
        string[] absent = rows
            .SelectMany(r => r.EndsWith(".xaml(.cs)", StringComparison.Ordinal)
                ? new[] { r[..^"(.cs)".Length], r[..^"(.cs)".Length] + ".cs" }
                : new[] { r })
            .Where(p => !File.Exists(Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar))))
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Check($"every path the file map names exists ({rows.Length} rows)", absent.Length == 0,
              $"{string.Join(", ", absent)} — documented, and renamed or deleted since");

        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string row in rows)
        {
            string path = row.Replace(".xaml(.cs)", ".xaml.cs");
            covered.Add(path);
            covered.Add(Stem(path));   // so a page's row covers the `partial` files spread beside it
        }
        string[] undocumented = files.Where(f => !covered.Contains(f) && !covered.Contains(Stem(f)))
                                     .OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Check($"every source file is named by a row or by its stem's ({files.Length} files)",
              undocumented.Length == 0,
              $"{string.Join(", ", undocumented)} — in the tree and in no row of the map");

        string[] folders = Directory.Exists(src)
            ? Directory.GetDirectories(src).Select(d => "src/" + Path.GetFileName(d) + "/")
                       .OrderBy(d => d, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        string[] listed = Regex.Matches(File.ReadAllText(agents), @"^\| `(src/[A-Za-z]+/)` \|",
                                        RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();

        string[] unplaced = folders.Where(f => !listed.Contains(f, StringComparer.Ordinal)).ToArray();
        Check($"AGENTS.md's folder table names every subsystem folder ({folders.Length})", unplaced.Length == 0,
              $"{string.Join(", ", unplaced)} — a folder with no stated place for a new file in it");

        string[] gone = listed.Where(f => !folders.Contains(f, StringComparer.Ordinal))
                              .OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Check("and every folder it names is still there", gone.Length == 0,
              $"{string.Join(", ", gone)} — a placement rule for a folder that no longer exists");
    }

    /// <summary>
    /// T271. <c>--activity</c> draws one grid — how <b>often</b> an hour is active — and the line under it
    /// reports how <b>hard</b> hours are worked, an axis with no picture. Measured on the machine that
    /// produced the task: Sunday 16:00 and Sunday 22:00 both rendered <c>▓</c> while being called 2.00× and
    /// 0.79×, the two ends of that line. So the line names its axis now, and this holds it to that.
    ///
    /// <para>Asserted on the sentence, not through the command: the read-out reads a real profile's store,
    /// which this suite may not touch, so the line was pulled out as a pure function of a synthetic
    /// profile. Both branches, because a flat profile still prints a claim about the projection.</para>
    /// </summary>
    private static void IntensityAxis()
    {
        var flat = new ActivityProfile { HasIntensity = false };
        string flatLine = ActivityCli.IntensityLine(flat);
        Check("a profile with nothing folded yet says the pacing is flat",
              flatLine.Contains("flat", StringComparison.Ordinal)
              && flatLine.Contains("same rate", StringComparison.Ordinal), flatLine);

        // Two buckets deliberately set to the clamp ends, and both on the same day, which is the shape that
        // produced the task: one picture, one glyph, two opposite claims.
        var shaped = new ActivityProfile { HasIntensity = true };
        shaped.I[(int)DayOfWeek.Sunday * 24 + 16] = ActivityProfile.MaxIntensity;
        shaped.I[(int)DayOfWeek.Sunday * 24 + 22] = ActivityProfile.MinIntensity;
        string line = ActivityCli.IntensityLine(shaped);

        Check("it reports the heaviest and the lightest bucket it was given",
              line.Contains("heaviest Sunday 16:00", StringComparison.Ordinal)
              && line.Contains("lightest Sunday 22:00", StringComparison.Ordinal), line);
        Check("and says which axis that is, so the darkest cell is not read as the heaviest hour",
              line.Contains("not the grid's axis", StringComparison.Ordinal)
              && line.Contains("not how often", StringComparison.Ordinal), line);
    }

    /// <summary>
    /// T268. The report used to label a summary row <c>Measured files</c> while, nine lines below, a column
    /// headed <c>Measured</c> meant the median startup context read from transcripts — one word, two labels,
    /// one document, and the prose defined only the second. Read on the generated document, where a person
    /// meets it: the header of the same scan said 1021 and that row said 881, both right and neither saying
    /// which was which.
    ///
    /// <para>What is held is narrower than the first draft of it, and the narrowing is the interesting part.
    /// "No summary row may be labelled with a word the Projects table uses as a column" sounds like the
    /// invariant and is not: written that way the check fires on <c>Findings</c>, which is the same quantity
    /// at two scopes — the machine's total and one project's — and misleads nobody. Same word for the
    /// <em>same</em> thing is fine; same word for two different things is the defect, and no regex can tell
    /// those apart. So the assertion names the word that was actually wrong, and the positive rows carry the
    /// rest: the two counts exist, by their own names, and are free to differ.</para>
    /// </summary>
    private static void ReportLabels()
    {
        var scan = new ContextScan { ScannedUtc = DateTime.UtcNow, FilesWalked = 1021, Truncated = true };
        scan.Shared.Add(new ContextSource { Path = @"C:\u\CLAUDE.md", Kind = ContextKind.UserInstructions });

        string doc = ContextReport.Build(scan, Array.Empty<Finding>(), null,
                                        new Dictionary<string, ContextDebt>(), 32_000, DateTime.Now);

        Check("the summary reports what the walk visited, by that name",
              doc.Contains("| Files walked | 1021 |", StringComparison.Ordinal));
        Check("and what it kept, by that name — one source here, and they are allowed to differ",
              doc.Contains("| Sources kept | 1 |", StringComparison.Ordinal));

        // Scoped to the Summary block: read over the whole document this also matches the Projects table's
        // own header row and reports it colliding with itself, which is how the first draft of this check
        // failed on `Project`.
        int from = doc.IndexOf("## Summary", StringComparison.Ordinal);
        int to = doc.IndexOf("## Projects", StringComparison.Ordinal);
        if (!Check("the summary block is readable", from >= 0 && to > from, $"{from}..{to}")) return;

        string[] rowLabels = Regex.Matches(doc[from..to], @"(?m)^\| ([A-Z][^|]*?) \| ")
                                  .Select(m => m.Groups[1].Value).ToArray();
        if (!Check($"the summary's rows are readable ({rowLabels.Length})", rowLabels.Length >= 6,
                   string.Join(" / ", rowLabels)))
            return;

        // The word the Projects table owns, and the one this row used to steal.
        string[] stolen = rowLabels.Where(r => r.Contains("Measured", StringComparison.Ordinal)).ToArray();
        Check($"no summary row calls itself Measured — the column owns that word ({rowLabels.Length} rows)",
              stolen.Length == 0,
              $"{string.Join(", ", stolen)} — and the column nine lines down means something else");
    }

    /// <summary>
    /// T267. The cleanup prompt is the only text this app writes that <b>leaves</b> it — clipboard, then
    /// Claude Code, then something that edits files. So what it carries is a property worth holding, and the
    /// one it was missing is that the list may be partial: a capped walk means findings never reached, and
    /// the caveat was printed in the scan header, above the <c>#</c>, which is outside what a person copies.
    ///
    /// <para>Both directions, because a caveat that is always there is noise a reader learns to skip. And
    /// the two promises the class doc already makes are asserted with it — no file contents, and it asks
    /// before deleting — since those are the reasons this text is safe to hand to an agent at all.</para>
    /// </summary>
    private static void CleanupPrompt()
    {
        var findings = new List<Finding>
        {
            new("eager-large", RuleSeverity.High, "acme/atlas", "AGENTS.md is 23 KB", "Trim it",
                @"D:\acme\atlas\AGENTS.md"),
        };
        var none = Array.Empty<ContextSource>();

        string capped = ContextPrompt.Build("acme/atlas", findings, none, 0, truncated: true);
        string whole = ContextPrompt.Build("acme/atlas", findings, none, 0, truncated: false);

        Check("a capped scan says so inside the text, not above it",
              capped.Contains("floor, not a total", StringComparison.Ordinal));
        Check("and tells the reader to say the list is partial before acting on it",
              capped.Contains("say that this list is partial", StringComparison.Ordinal));
        Check("an uncapped scan says neither — a caveat that is always there is one nobody reads",
              !whole.Contains("floor, not a total", StringComparison.Ordinal)
              && !whole.Contains("partial", StringComparison.Ordinal));

        // The two promises the prompt exists under, on both shapes: this text is handed to something that
        // edits files, and these are the reasons that is safe.
        foreach ((string what, string text) in new[] { ("capped", capped), ("whole", whole) })
        {
            Check($"{what}: it says what was measured and what was not",
                  text.Contains("never file contents", StringComparison.Ordinal));
            Check($"{what}: and it asks before anything is deleted or moved",
                  text.Contains("Do not delete or move any file without", StringComparison.Ordinal)
                  && text.Contains("showing me the plan first", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// T264. The overage preview exists because an account past its included quota is a state no machine can
    /// be put into on demand — so a preview that quietly declines to produce it is worse than none, and that
    /// is what it had become. Its guard was <c>ExtraCurve.Count &gt; 0</c>, and the list is non-empty on any
    /// machine whose history holds a <b>measured zero</b> for overage, which is most of them (T179 writes one
    /// whenever the header was present). So the demo bailed, the real series scaled to nothing, the chart's
    /// <c>hasExtra</c> was false, and <c>--stats overage</c> drew an ordinary week with no second axis. It is
    /// why <c>docs/statistics-overage.png</c> could not be re-taken: it was captured before that history
    /// existed.
    ///
    /// <para>Asserted on the seam itself rather than through a rendered page: the three states it has to
    /// tell apart are a curve of measured zeros, a curve with a real maximum, and no window at all — and
    /// the first is the one every machine is in and the one that was wrong.</para>
    /// </summary>
    private static void OveragePreview()
    {
        static WindowPace Week()
        {
            var w = new WindowPace { HasWindow = true, WindowSeconds = 7 * 86400, ElapsedSeconds = 4 * 86400 };
            w.Util = 1.0;
            return w;
        }

        // The state every machine with overage headers is in: readings on file, every one of them zero.
        WindowPace measuredZeros = Week();
        for (int i = 0; i <= 10; i++) measuredZeros.ExtraCurve.Add((i / 10.0 * 0.57, 0.0));
        StatisticsPage.FillDemoOverage(measuredZeros);
        Check("a curve of measured zeros is filled, so the preview draws the state it is for",
              measuredZeros.ExtraMax > 0, $"ExtraMax {measuredZeros.ExtraMax}, {measuredZeros.ExtraCurve.Count} points");
        Check("and the zeros are replaced rather than drawn under it",
              measuredZeros.ExtraCurve.All(p => p.val > 0 || p.frac > 0),
              "a flat line of real zeros left in front of the demo climb means two things at once");

        // A real spell must be left exactly alone — the seam is for the absence, not for overwriting.
        WindowPace real = Week();
        real.ExtraCurve.Add((0.30, 0.10));
        real.ExtraCurve.Add((0.60, 0.42));
        real.ExtraMax = 0.42;
        StatisticsPage.FillDemoOverage(real);
        Check("a real overage series is untouched", real.ExtraCurve.Count == 2 && real.ExtraMax == 0.42,
              $"{real.ExtraCurve.Count} points, max {real.ExtraMax}");

        // No window is nothing to draw on, and inventing a series over it would be a chart about nothing.
        var noWindow = new WindowPace();
        StatisticsPage.FillDemoOverage(noWindow);
        Check("and a window that does not exist gets no series",
              noWindow.ExtraCurve.Count == 0 && noWindow.ExtraMax == 0);
    }

    /// <summary>
    /// T263. A date's <b>order</b> comes from the culture, not from a pattern written in the source. It did
    /// not: <c>"MMM d"</c> rendered <em>"août 3"</em> in French — where all four non-English cultures put
    /// the day first — and <c>"d/M"</c> rendered <c>3/8</c> to an American, whose short date is <c>M/d</c>,
    /// on the very chart the published screenshots are of.
    ///
    /// <para><b>Asserted against each culture's own patterns</b>, over every language this build ships, so
    /// a language added later is covered by having been added. The claim is the one §XXX.1 named: whichever
    /// of day and month comes first in what the page renders is the one that comes first in that culture's
    /// <c>MonthDayPattern</c> — read off the rendered string by where the two numbers land, which is what
    /// makes this a check about output rather than about the pattern the code happens to hold.</para>
    ///
    /// <para>T167's number sweep cannot reach this and was never meant to: it varies
    /// <c>CurrentCulture</c> and demands the answer not move, while a date is the one thing that must.</para>
    /// </summary>
    private static void DateOrder()
    {
        System.Globalization.CultureInfo before = System.Globalization.CultureInfo.CurrentCulture;
        string beforeLang = L.Codes.FirstOrDefault(c => L.Resolve(c) == L.Current) ?? "en";
        try
        {
            // A day and a month that cannot be confused for each other, or "3/8" and "8/3" read the same.
            var when = new DateTime(2026, 8, 23, 16, 26, 0);
            foreach (string code in L.Codes)
            {
                L.Apply(code);
                var culture = L.DateCulture;
                bool monthFirstInCulture =
                    culture.DateTimeFormat.MonthDayPattern.IndexOf('M', StringComparison.Ordinal)
                    < culture.DateTimeFormat.MonthDayPattern.IndexOf('d', StringComparison.Ordinal);

                string named = Dates.MonthDay(when);
                bool monthFirstAsRendered = named.IndexOf("23", StringComparison.Ordinal) > 0;
                Check($"{code}: the named month sits where this culture puts it ('{named}')",
                      monthFirstAsRendered == monthFirstInCulture,
                      $"culture wants month-first={monthFirstInCulture}, rendered '{named}'");

                string digits = Dates.DayMonthDigits(when);
                bool monthFirstInShort =
                    culture.DateTimeFormat.ShortDatePattern.IndexOf('M', StringComparison.Ordinal)
                    < culture.DateTimeFormat.ShortDatePattern.IndexOf('d', StringComparison.Ordinal);
                Check($"{code}: and so do the digits ('{digits}')",
                      digits.StartsWith(monthFirstInShort ? "8" : "23", StringComparison.Ordinal),
                      $"short date is '{culture.DateTimeFormat.ShortDatePattern}', rendered '{digits}'");

                Check($"{code}: both fields are actually there",
                      named.Contains("23", StringComparison.Ordinal)
                      && digits.Contains("23", StringComparison.Ordinal)
                      && digits.Contains('8', StringComparison.Ordinal), $"'{named}' / '{digits}'");
            }

            // The precondition: if every shipped culture agreed on the order, the sweep above would pass
            // over a hardcoded pattern too and prove nothing.
            string[] orders = L.Codes.Select(c =>
            {
                L.Apply(c);
                return L.DateCulture.DateTimeFormat.MonthDayPattern.IndexOf('M', StringComparison.Ordinal)
                       < L.DateCulture.DateTimeFormat.MonthDayPattern.IndexOf('d', StringComparison.Ordinal)
                           ? "month" : "day";
            }).Distinct(StringComparer.Ordinal).ToArray();
            Check("the shipped languages disagree about the order — so the sweep above is not vacuous",
                  orders.Length > 1, $"all of them put the {orders.FirstOrDefault()} first");
        }
        finally
        {
            L.Apply(beforeLang);
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    /// <summary>
    /// T262. The last named token in this app that guessed. <c>--capture-settings &lt;out&gt; NoSuchPage
    /// --sample</c> wrote a picture of <em>General</em> under the name the caller gave, printed
    /// <c>wrote</c> and exited 0; <c>--main NoSuchDest</c> opened Statistics. Both measured before the fix.
    ///
    /// <para>Swept over <see cref="SettingsPage.Pages"/> and <see cref="MainWindow.Destinations"/>
    /// themselves, so a page or destination added later is covered by having been added — and in both
    /// directions, since a resolver that resolved nothing would satisfy the refusal half and break every
    /// flag that names one.</para>
    ///
    /// <para>The <b>sidebar's own tags are the other end of it</b>: the page names are what
    /// <c>Nav_Click</c> reads off a clicked item, so a name here that the XAML does not carry would select
    /// nothing at all. Asserted against the markup rather than against a second list.</para>
    /// </summary>
    private static void PageNames()
    {
        foreach ((string what, string[] known, Func<string?, string?> resolve) in
                 new (string, string[], Func<string?, string?>)[]
                 {
                     ("settings page", SettingsPage.Pages, SettingsPage.Resolve),
                     ("destination", MainWindow.Destinations, MainWindow.Resolve),
                 })
        {
            Check($"every {what} this build has resolves ({known.Length})",
                  known.All(k => resolve(k) == k),
                  string.Join(", ", known.Where(k => resolve(k) != k)));

            Check($"and it resolves however it was typed — a {what} is not case-sensitive",
                  known.All(k => resolve(k.ToUpperInvariant()) == k && resolve(k.ToLowerInvariant()) == k));

            foreach (string bad in new[] { "NoSuchPage", "Genera", "Sistema", "" })
                Check($"'{bad}' is no {what}, so it resolves to nothing rather than the first one",
                      resolve(bad) is null);

            Check($"and naming no {what} at all is not a refusal — it is a request for the default",
                  resolve(null) is null);
        }

        // The names are only worth anything if the sidebar answers to them.
        Repo("every settings page name is a tag the sidebar carries", PageTags, "src/Ui/SettingsPage.xaml");
    }

    private static void PageTags(string root)
    {
        string xaml = File.ReadAllText(Path.Combine(root, "src/Ui/SettingsPage.xaml"));
        string[] tags = Regex.Matches(xaml, @"Tag=""([A-Za-z]+)""").Select(m => m.Groups[1].Value)
                             .Distinct(StringComparer.Ordinal).ToArray();

        if (!Check("the settings markup is readable", tags.Length >= 6,
                   $"{tags.Length} Tag= values — the check cannot read what it compares"))
            return;

        string[] missing = SettingsPage.Pages.Where(p => !tags.Contains(p, StringComparer.Ordinal)).ToArray();
        Check($"every page name is a sidebar tag ({SettingsPage.Pages.Length})", missing.Length == 0,
              $"{string.Join(", ", missing)} — nameable on the command line, and the sidebar selects nothing");
    }

    /// <summary>
    /// T261. A read-out that could not start says so <em>and</em> exits 1 — it used to say so and exit 0,
    /// which a script driving it reads as a scan that simply found nothing.
    ///
    /// <para>The interesting half is the boundary, and it is asserted here because it is what makes the
    /// rule safe to apply at all: a partial answer must NOT become a refusal. Measured on the types
    /// themselves — <see cref="ContextScan.Error"/> is only ever set on a scan that returned immediately,
    /// while <see cref="UsageEvidence"/> carries <c>Complete</c> beside its <c>Error</c> precisely because
    /// it can finish with holes, and <c>ContextCli</c> prints that one as a note and carries on.</para>
    ///
    /// <para>The exit code is restored afterwards: this runs inside a suite whose own exit code is the
    /// thing being reported, and a check that fails the run by demonstrating a failure is a check that
    /// cannot pass.</para>
    /// </summary>
    private static void ReadOutExit()
    {
        int before = Environment.ExitCode;
        try
        {
            Environment.ExitCode = 0;
            Check("a read-out with nothing wrong neither prints nor marks the run",
                  !ReadOut.Failed(null) && Environment.ExitCode == 0);

            Environment.ExitCode = 0;
            bool said = ReadOut.Failed("not found: D:\\nope");
            Check("and one that could not start says so and exits 1",
                  said && Environment.ExitCode == 1, $"said={said} code={Environment.ExitCode}");
        }
        finally { Environment.ExitCode = before; }

        // The boundary, on the types rather than on a promise about them. A scan carries no way to be
        // partial, so treating its Error as a refusal is total; evidence does, so its Error is a note.
        Check("a scan has no partial form, so its error is always a refusal",
              typeof(ContextScan).GetProperty("Complete") is null
              && new ContextScan { Error = "x" }.Projects.Count == 0);
        Check("and evidence does have one, which is why its error stays a note",
              typeof(UsageEvidence).GetProperty("Complete") is not null);
    }

    /// <summary>
    /// T260. <c>--lang</c> is the flag the i18n verification loop is built on, and it used to honour a
    /// code this build does not ship by quietly using the machine's own language instead: <c>--lang zz
    /// --tooltip</c> printed a tooltip in <c>PtBr</c> and exited 0. So a typo in
    /// <c>--lang fr --capture-toast extra out.png</c> wrote the card in Portuguese into the file the
    /// caller named, and nothing said so — a screenshot of the wrong thing looking exactly like a
    /// screenshot of the right one, which is what T186, T198, T200 and T231 each refuse.
    ///
    /// <para>Swept over <see cref="L.Codes"/> rather than a list written here, so a language added
    /// tomorrow is covered tomorrow — and asserted in both directions, because a refusal that refuses
    /// everything would satisfy half of this and break the flag.</para>
    /// </summary>
    private static void LangOverride()
    {
        string[] refusedShipped = L.Codes.Where(c => L.RefuseOverride(c) is not null).ToArray();
        Check($"every language this build ships is accepted ({L.Codes.Count})", refusedShipped.Length == 0,
              $"{string.Join(", ", refusedShipped)} — refused by the flag that exists to select them");

        Check("and so is auto, which asks for the OS language out loud",
              L.RefuseOverride("auto") is null);

        // The other direction. A near-miss and a wrong case are the typos that actually happen, and the
        // codes are matched exactly (`L.Resolve` is ordinal), so both must be refused rather than resolved.
        foreach (string bad in new[] { "zz", "de", "pt_BR", "EN", "en-GB", "" })
            Check($"'{bad}' is refused rather than becoming the machine's language",
                  L.RefuseOverride(bad) is not null);

        Check("the flag with no code after it is refused too — it was typed, and cannot be honoured",
              L.RefuseOverride(null) is not null);

        // A refusal nobody can act on is the reason these flags print a catalogue (T186).
        string refusal = L.RefuseOverride("zz") ?? "";
        string[] unlisted = L.Codes.Where(c => !refusal.Contains(c, StringComparison.Ordinal)).ToArray();
        Check($"and the refusal prints every code it would have accepted ({L.Codes.Count})",
              unlisted.Length == 0 && refusal.Contains("auto", StringComparison.Ordinal),
              $"missing from the catalogue: {string.Join(", ", unlisted)}");
    }

    /// <summary>
    /// T259. Every <c>.ps1</c> this repository carries is one PowerShell 5.1 will read the way it was
    /// written — because the version that ships in Windows reads a script with no byte-order mark in the
    /// <b>ANSI code page</b>, not as UTF-8.
    ///
    /// <para><b>What that does, measured on two reduced files.</b> The three UTF-8 bytes of an em dash
    /// arrive as three CP1252 characters whose last one is a right double quotation mark. Inside a
    /// double-quoted string that closes the string mid-expression and the whole script fails to parse, so
    /// <em>no</em> assertion runs; inside a comment it is merely mojibake. Both were hit in one session:
    /// the loud one adding a message to the interaction check, and the quiet one in T234, where a
    /// <c>-split</c> on a middle dot compared against a character the parser had read as something else,
    /// never matched, and asserted nothing while passing.</para>
    ///
    /// <para><b>The predicate is the property, not the fix.</b> A file that is pure ASCII has nothing to
    /// misread and needs no mark; a file carrying any character above 127 needs one. So the rule is "ASCII,
    /// or marked", which accepts both answers the task weighed and rejects the state that produced the
    /// defect — five of the six scripts, every one of them carrying prose and none of them marked.</para>
    /// </summary>
    private static void ScriptEncoding() =>
        Repo("every .ps1 is one PowerShell 5.1 reads as written", ScriptEncoding, "scripts", "build");

    private static void ScriptEncoding(string root)
    {
        string[] scripts = new[] { "scripts", "build" }
            .Select(d => Path.Combine(root, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.ps1", SearchOption.AllDirectories))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!Check("the scripts are readable", scripts.Length >= 4,
                   $"found {scripts.Length} .ps1 — the check cannot read what it compares"))
            return;

        var unreadable = new List<string>();
        foreach (string path in scripts)
        {
            byte[] bytes = File.ReadAllBytes(path);
            bool marked = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            if (marked) continue;

            // Byte-wise, deliberately: this is a question about what a *decoder* will do with the file,
            // and reading it as text first would answer with the decoder this check happens to use.
            int high = bytes.Count(b => b > 0x7F);
            if (high > 0)
                unreadable.Add($"{Path.GetFileName(path)} ({high} byte(s) over 127, and no mark)");
        }

        Check($"every .ps1 is ASCII or carries a UTF-8 mark ({scripts.Length})", unreadable.Count == 0,
              $"{string.Join(", ", unreadable)} — 5.1 reads these in the ANSI code page, so a dash inside a "
              + "string closes it and the script does not parse");
    }

    /// <summary>
    /// T284. <see cref="ApiClient.NamesRead"/> is the parser enumerating itself: the same method that
    /// parses a response is run against a lookup that records the name it was asked for and answers
    /// <c>null</c>. That is what makes the <c>--probe</c> read-out impossible to forget to update — and it
    /// is exact only while the parse has no branch in it.
    ///
    /// <para><b>The failure it cannot see.</b> A read written as <c>get("…-in-use") is "true" ?
    /// get("…-overage-reset") : null</c> is ordinary code. Under the recording lookup the condition is
    /// false, the second name is never asked for, and it is reported UNREAD — by the instrument built to
    /// say which names reach a field, with every check of the classifier green. Silent, and pointing the
    /// wrong way: a header the app reads, reported as one nothing does.</para>
    ///
    /// <para><b>So the property asserted is totality, from the source.</b> Every header name spelled in
    /// <c>ApiClient.cs</c> is a name the enumeration reports, and every name it reports is spelled there —
    /// which holds without asking anything about branches, and fails the moment a conditional read is
    /// written rather than the month somebody trusts the mark. The bare family prefix is excluded: it is
    /// what the verbatim copy filters on, not a name read into a field.</para>
    /// </summary>
    /// <summary>
    /// T285. <see cref="CodeOf"/>'s own answer, over the literal forms this repository contains — asserted
    /// against synthetic source rather than against the tree, for T248's reason: the tree happens not to
    /// hold every case today, and a check that waits for one is a check that ships broken.
    ///
    /// <para>Two directions, and the second is the one that fails quietly. A comment that survives makes a
    /// scan report something that is not there, which is loud — all three scans here were caught that way,
    /// each on its own first run. A string that does <em>not</em> survive makes a scan report less than is
    /// there, and a check asserting less still passes. Every fixture below therefore keeps a piece of code
    /// after the region being skipped, so a terminator read wrong is a fact this can see.</para>
    /// </summary>
    private static void CodeReading()
    {
        // Assembled, never written out — the fixtures are read by the very scans they are fixtures of.
        string q = "\"";
        string slashes = "//";
        string setter = "Console." + "OutputEncoding" + " =";

        (string What, string Source, bool Kept)[] cases =
        {
            ("a line comment", $"{slashes} {setter} UTF8\nint a = 1;", false),
            ("a doc comment", $"/// <c>{setter} UTF8</c>\nint a = 1;", false),
            ("a block comment", $"/* {setter} UTF8 */\nint a = 1;", false),
            ("a comment at end of line", $"int a = 1;   {slashes} {setter} UTF8", false),
            ("code itself", $"{setter} UTF8;", true),
            ("a string holding the setter", $"string s = {q}{setter} UTF8{q};", true),
        };
        foreach ((string what, string source, bool kept) in cases)
            Check($"{what}: the setter {(kept ? "survives" : "is gone")}",
                  CodeOf(source).Contains(setter) == kept, CodeOf(source).Replace("\n", "\\n"));

        // The T248 case, one level down: a `//` inside a literal is not a comment, and reading it as one
        // eats the rest of the line — silently, because what is left still parses and still asserts.
        string url = $"string site = {q}https://example.invalid/x{q}; int kept = 1;";
        Check("a // inside a string does not start a comment", CodeOf(url).Contains("int kept = 1;"),
              CodeOf(url));

        // Every terminator this file's own sources rely on. Each fixture ends in a token that only appears
        // after the literal, so a string read as unterminated loses it.
        (string What, string Source)[] terminators =
        {
            ("a doubled quote in a verbatim string", $"var r = @{q}a{q}{q}b{q}; int kept = 1;"),
            ("an escaped quote in a quoted string", $"var r = {q}a\\{q}b{q}; int kept = 1;"),
            ("a quote inside a char literal", $"char c = '{q}'; int kept = 1;"),
            ("a raw string carrying a quote", $"var r = {q}{q}{q}he said {q}hi{q}{q}{q}{q}; int kept = 1;"),
            ("a raw string carrying a comment", $"var r = {q}{q}{q}{slashes} not a comment{q}{q}{q}; int kept = 1;"),
            ("an interpolated raw string", $"var r = $${q}{q}{q}x {{{{y}}}} {slashes} z{q}{q}{q}; int kept = 1;"),
        };
        string[] lost = terminators.Where(t => !CodeOf(t.Source).Contains("int kept = 1;"))
                                   .Select(t => $"{t.What} → {CodeOf(t.Source)}").ToArray();
        Check($"the code after every literal form is still read ({terminators.Length})", lost.Length == 0,
              string.Join("; ", lost));

        // Line numbers survive a removal, or a caller counting by line answers about the wrong one.
        string across = $"/* one\ntwo */\nint a = 1;";
        Check("a removed region keeps its newlines",
              CodeOf(across).Count(ch => ch == '\n') == across.Count(ch => ch == '\n'),
              CodeOf(across).Replace("\n", "\\n"));

        // And the whole of this file, which is the largest source here and the one carrying raw strings:
        // a scanner that desynchronised on those would silently skip everything after them.
        string? self = RepoFile(Path.Combine("src", "Cli", "SelfTestCli.cs"));
        if (self is not null)
        {
            string code = CodeOf(File.ReadAllText(self));
            Check("this file's own last method survives the scan", code.Contains("private static void Remove"),
                  $"{code.Length} of {new FileInfo(self).Length} characters");
        }
    }

    private static void ParserNames() =>
        Repo("every header name the parser spells is one it reports reading", ParserNames, "src");

    /// <summary>A header name as the parse spells one: the family, and at least one character of window
    /// after it. The bare <c>anthropic-ratelimit-</c> prefix is the verbatim copy's filter, not a name.</summary>
    private static readonly Regex HeaderLiteral = new("\"(anthropic-ratelimit-[a-z0-9][a-z0-9-]*)\"",
                                                      RegexOptions.Compiled);

    private static void ParserNames(string root)
    {
        string path = Path.Combine(root, "src", "Usage", "ApiClient.cs");
        if (!Check("the parser is in the checkout", File.Exists(path), path)) return;

        // Over the code alone (T285). This scan carried two hand rules — skip a line starting with `//`,
        // and refuse a name ending in a dash — the second only because the paragraphs above the parse write
        // the family as `anthropic-ratelimit-unified-*`. Neither is needed once a comment is a region
        // rather than a shape, and the bare family prefix is excluded by requiring a character after it:
        // that literal is what the verbatim copy filters on, not a name read into a field.
        var spelled = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in HeaderLiteral.Matches(CodeOf(File.ReadAllText(path))))
            spelled.Add(m.Groups[1].Value);

        if (!Check($"the parser spells header names at all ({spelled.Count})", spelled.Count > 0,
                   "none found — the scan reads nothing, so it would pass on a parser that read nothing"))
            return;

        string[] unreported = spelled.Where(n => !ApiClient.NamesRead.Contains(n, StringComparer.OrdinalIgnoreCase))
                                     .ToArray();
        Check("every name the source spells is one the enumeration reports", unreported.Length == 0,
              $"{string.Join(", ", unreported)} — read behind a branch the recording lookup does not take, "
              + "so --probe would mark it UNREAD (T284)");

        string[] unspelled = ApiClient.NamesRead.Where(n => !spelled.Contains(n)).ToArray();
        Check("and every name it reports is spelled in the source, not assembled", unspelled.Length == 0,
              $"{string.Join(", ", unspelled)} — a name the scan cannot see is one it cannot hold");

        Check($"so the two are the same set ({ApiClient.NamesRead.Count})",
              spelled.Count == ApiClient.NamesRead.Count, $"{spelled.Count} spelled, {ApiClient.NamesRead.Count} reported");
    }

    /// <summary>
    /// T283. The console's code page is set in exactly one place — where the flags are dispatched — and
    /// this is the check that keeps it there.
    ///
    /// <para>Twelve read-outs each opened with their own <c>Console.OutputEncoding = UTF8</c>, learned one
    /// at a time as each hit the same wall: a WinExe's console starts on the OEM code page, which prints an
    /// em dash and a section sign as <c>?</c>. <c>--probe</c> never learned it, and it was the read-out
    /// densest in both — every <c>§XVIII.9</c> it sent a reader to arrived as <c>?XVIII.9</c>, which names
    /// no section.</para>
    ///
    /// <para><b>Why a source scan rather than an output assertion.</b> The defect is not what one flag
    /// prints; it is that remembering was the mechanism. A check of <c>--probe</c>'s output would have gone
    /// green the moment somebody pasted the line a thirteenth time, and said nothing about the fourteenth
    /// flag. What is asserted is the shape that made the omission impossible: one call site, in the
    /// dispatch, ahead of every verb.</para>
    /// </summary>
    private static void ConsoleCodePage() =>
        Repo("the console's code page is set once, where the flags are dispatched", ConsoleCodePage, "src");

    private static void ConsoleCodePage(string root)
    {
        // Assembled, never written out: a literal of the thing being counted would be one more of it, in
        // the file doing the counting — the same rule FlagScanning's fixtures are built by.
        string setter = "Console." + "OutputEncoding" + " =";
        string dispatch = Path.Combine("src", "Tray", "Program.cs");

        // Counted over the code alone (T285). The paragraph above names the setter in order to explain what
        // is being counted, and on this check's first run that explanation was read as a thirteenth copy —
        // so the comment is removed lexically rather than by a rule about how a line begins.
        (string File, int Count)[] setters = Directory
            .GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => (File: Path.GetRelativePath(root, f),
                          Count: CodeOf(File.ReadAllText(f)).Split(setter).Length - 1))
            .Where(x => x.Count > 0)
            .ToArray();

        if (!Check("something sets the console's code page at all", setters.Length > 0,
                   "no source sets it — a read-out full of dashes would print them as '?'"))
            return;

        string[] elsewhere = setters
            .Where(x => !string.Equals(x.File, dispatch, StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.File} ({x.Count})").ToArray();
        Check("only the dispatch sets it, so no flag can be added having forgotten to",
              elsewhere.Length == 0,
              $"{string.Join(", ", elsewhere)} — a per-file copy is a rule twelve callers kept and a "
              + "thirteenth did not (T283)");
        Check("and it sets it once, not once per verb",
              setters.Single(x => string.Equals(x.File, dispatch, StringComparison.OrdinalIgnoreCase))
                     .Count == 1);
    }

    /// <summary>
    /// T272. Every build <c>check.yml</c> runs fails on a warning, because the alternative was tolerating
    /// them and that is measurably not a stable state.
    ///
    /// <para><b>What it is asserting and why it is here rather than only in the workflow.</b> Three
    /// <c>CS8602</c> warnings stood in this project long enough that a build log reading "3 warnings" was
    /// what clean looked like, so a fourth would have arrived unread. The gate that stops that lives in a
    /// YAML file nothing reads, which is the same shape as the defect: dropping <c>-warnaserror</c> from a
    /// step is one word, it makes no test fail, and the run stays green while the protection is gone. So
    /// the flag is asserted where the assertions are.</para>
    ///
    /// <para><b>Both steps, not one.</b> <c>check.yml</c> builds twice — once for the self-check job and
    /// once for the interaction job — and a gate carried by one of them only holds when that job is the one
    /// that ran. The check counts the build steps it found and prints the count, because "all of them
    /// carry it" is not an assertion when the number could be zero.</para>
    /// </summary>
    private static void WarningGate() =>
        Repo("every build CI runs fails on a warning", WarningGate, ".github/workflows/check.yml");

    private static void WarningGate(string root)
    {
        string[] builds = File.ReadAllLines(Path.Combine(root, ".github/workflows/check.yml"))
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("run:", StringComparison.Ordinal)
                        && l.Contains("dotnet build", StringComparison.Ordinal))
            .ToArray();

        // The count is the precondition, not a nicety: a workflow this check cannot find the builds in
        // would pass an "all of them" test with nothing in hand.
        if (!Check("the workflow's builds are readable", builds.Length == 2,
                   $"found {builds.Length} `dotnet build` step(s), expected 2 — the check cannot gate what " +
                   "it cannot see"))
            return;

        string[] ungated = builds.Where(l => !l.Contains("-warnaserror", StringComparison.Ordinal)).ToArray();
        Check($"both CI builds carry -warnaserror ({builds.Length})", ungated.Length == 0,
              $"{string.Join(" | ", ungated)} — without it a real warning lands in a log nobody reads, " +
              "which is the state T272 was filed against");
    }

    /// <summary>
    /// T248. The flag scan's own two failure modes, held up by the two lines that produced them.
    ///
    /// <para>Its first run ever (T243) failed on <c>--x</c>: the paragraph above the method quotes the
    /// shape being matched, and the scan read the example as a switch the catalogue had forgotten. The fix
    /// — strip <c>//</c> to end of line — is right about comments and approximate about strings, and the
    /// approximation is the second failure: a <c>//</c> inside a literal ends the line for the scan, so a
    /// real flag sharing that line stops being seen. That one is invisible, because a check that asserts
    /// less still passes.</para>
    ///
    /// <para>Both are asserted against synthetic source rather than against the tree, which is the point:
    /// the tree happens not to contain the second case today, and a check that waits for one is a check
    /// that ships broken. The prose sample is this very file's own shape, quoted flag and all.</para>
    /// </summary>
    private static void FlagScanning()
    {
        // The samples are ASSEMBLED, never written out. A snippet containing a literal `"--sample"` is,
        // to the scan that reads this very folder, a flag the .exe accepts and the catalogue forgot — and
        // it would be right. Building the token means the fixture cannot be mistaken for the thing it is a
        // fixture of, which is the same reason the check below has to exist at all.
        static string Lit(string name) => '"' + "--" + name + '"';

        string prose = $$"""
            /// <para>The source side is every {{Lit("gone-fishing")}} literal under src\, which is
            /// what makes this derivable — one shape covers args.Contains and args[0] == alike.</para>
            // Comments first: the paragraph above quotes {{Lit("also-gone")}} to show what is matched.
            """;
        string[] fromProse = FlagsRead(prose).ToArray();
        Check("a paragraph quoting a flag is not a flag the app accepts",
              fromProse.Length == 0, string.Join(", ", fromProse));

        // The line the stripper ate. Everything after the `//` inside the literal used to vanish, and with
        // it a real switch — silently, because what is left still parses and still passes.
        string afterUrl = $"""
            string site = "https://example.invalid/x"; bool raw = flags.Contains({Lit("kept-anyway")});
            """;
        Check("a flag after a string containing // is still read",
              FlagsRead(afterUrl).SequenceEqual(new[] { "--kept-anyway" }),
              string.Join(", ", FlagsRead(afterUrl)));

        // Every comparison shape the sources actually use, so narrowing did not quietly drop one. A shape
        // missing from here would show up as a *declared* flag nothing reads — see FlagCatalogue.
        (string What, string Code)[] shapes =
        {
            ("Contains",        $"if (args.Contains({Lit("one")})) {{ }}"),
            ("== on the left",  $"if (args[0] == {Lit("two")}) {{ }}"),
            ("== on the right", $"if ({Lit("three")} == args[0]) {{ }}"),
            ("an is pattern",   $"bool x = a is {Lit("four")};"),
            ("IndexOf",         $"int at = Array.IndexOf(args, {Lit("five")});"),
        };
        string[] missed = shapes.Where(s => !FlagsRead(s.Code).Any()).Select(s => s.What).ToArray();
        Check($"every comparison shape the sources use is read ({shapes.Length})", missed.Length == 0,
              $"{string.Join(", ", missed)} — a flag read this way would read as documented-but-unused");
    }

    /// <summary>
    /// The other half of the flag surface (T243): the switches that are a bare string read out of
    /// <c>args</c> where they are used, with no table in code for T207's assertion to compare against.
    ///
    /// <para>T207 reaches exactly the flags whose variants <em>are</em> a list a build can enumerate — the
    /// toast cards, the tooltip variants, the <c>week=</c> names. Everything else — <c>--probe</c>'s three
    /// switches, <c>--activity</c>'s four, <c>--raw</c>, <c>--sample-env</c> — was documented because
    /// somebody remembered, which is the guarantee that assertion exists to replace. <c>--recorded</c>
    /// reached the catalogue that way one task ago; <c>--raw</c> did not reach it at all, and had been
    /// unlisted since <c>--live</c> shipped.</para>
    ///
    /// <para><b>Where the catalogue is read matters, and this is what §XXII.5 left to settle.</b> Taken
    /// over the whole document the second direction cannot be trusted: the prose explains other programs'
    /// flags, and <c>claude auth status --json</c> reads as this app promising a <c>--json</c> it has never
    /// had. So a flag counts as documented only where the catalogue <em>declares</em> it — inside a fenced
    /// block, left of the <c>#</c> that opens the description — which is the same position
    /// <see cref="SkillBlock"/> already keys on. Measured over the file as it stands: 45 declared, 46
    /// mentioned, and the difference is somebody else's flag.</para>
    ///
    /// <para><b>The source side is every flag literal something COMPARES against</b>, which is what makes
    /// this derivable at all and, since T248, what keeps it from reading its own prose. It used to be every
    /// <c>"--x"</c> literal with <c>//</c>-to-end-of-line stripped first — right about comments and
    /// approximate about strings, because a <c>//</c> inside a literal (a URL, a UNC path, a regex) ended
    /// the line for the scan and took any real flag after it with it, silently. Matching the comparison
    /// instead excludes prose by construction: a paragraph may quote a flag all it likes, and what counts
    /// is a literal standing beside <c>Contains</c>, <c>IndexOf</c>, <c>Equals</c>, an equality operator or
    /// an <c>is</c> pattern. (Which is why this paragraph names the operators and does not write one out —
    /// prose is excluded because it is prose about code, not because it may not mention a flag.)</para>
    ///
    /// <para><b>What happens to a flag read a way this does not know</b> — the question narrowing had to
    /// answer, since a pattern of shapes is the hardcoded list this file keeps warning about. It drops out
    /// of <c>accepted</c>, and the <em>second</em> assertion below then fails on it by name: it is declared
    /// in the catalogue and no source appears to read it. The two directions catch each other, so an
    /// unlearned shape is a red build that says which flag and not a check that quietly asserts less. The
    /// gap left is a flag both read a new way and never documented, which is one edit by one author who is
    /// looking at both.</para>
    /// </summary>
    private static void FlagCatalogue() =>
        Repo("every flag the sources accept is declared in dev-flags", FlagCatalogue, DevFlags, "src");

    /// <summary>The flag literals one source file compares against. Pure, so <see cref="FlagScanning"/> can
    /// hand it the two lines that used to be read wrong.</summary>
    /// <summary>
    /// One source file's <b>code</b>: every comment removed, every string literal kept whole (T285).
    ///
    /// <para><b>Why this exists.</b> Three checks here read this repository's own sources. <c>FlagsRead</c>
    /// (T248) matches a comparison — <c>Contains("--x")</c> — so a flag quoted in a paragraph is not a flag
    /// the app accepts. The two added since matched a bare literal and then subtracted prose by hand, and
    /// both paid for it on their first run: T283's scan read its own summary, which names the setter in
    /// order to explain what it counts, and T284's needed a second rule because the paragraph above the
    /// parse writes the family with a trailing dash. Each hand rule is right about the case that produced
    /// it and blind to the next one — and a scan whose exclusion is approximate fails by asserting
    /// <em>less</em>, which still passes.</para>
    ///
    /// <para><b>So the question is answered once, lexically, instead of by pattern.</b> A comment is not a
    /// line that looks like one; it is a region C# would not compile. Strings are kept because they are
    /// what the scans are looking for — a header name and a flag are both literals — and they are the whole
    /// reason stripping to the first <c>//</c> is wrong: <c>"https://x"</c> would end the line.</para>
    ///
    /// <para>Every literal form this repository actually contains is handled, raw strings included, because
    /// this file has them: desynchronising on <c>$$"""…"""</c> would silently skip the rest of the largest
    /// source here and answer with confidence. Newlines are preserved so a caller may still count by line.</para>
    /// </summary>
    internal static string CodeOf(string source)
    {
        var code = new StringBuilder(source.Length);
        int i = 0;

        // Emit the newlines inside a skipped region, so nothing that counts lines is thrown off by a
        // comment or a multi-line string being removed.
        void Skip(int from, int to)
        {
            for (int k = from; k < to && k < source.Length; k++)
                if (source[k] == '\n') code.Append('\n');
        }

        while (i < source.Length)
        {
            char c = source[i];

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                int end = source.IndexOf('\n', i);
                if (end < 0) break;
                Skip(i, end);
                i = end;
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                Skip(i, end);
                i = end;
                continue;
            }

            // A string may carry any of $, @ and $$ in front of it. The prefix decides how it ends, so it
            // is read before the quote rather than guessed after one.
            int p = i;
            bool verbatim = false, interpolated = false;
            while (p < source.Length && (source[p] == '@' || source[p] == '$'))
            {
                verbatim |= source[p] == '@';
                interpolated |= source[p] == '$';
                p++;
            }
            if (p < source.Length && source[p] == '"')
            {
                int quotes = 0;
                while (p + quotes < source.Length && source[p + quotes] == '"') quotes++;
                int end = quotes >= 3
                    ? RawStringEnd(source, p + quotes, quotes)
                    : verbatim ? VerbatimEnd(source, p + 1) : QuotedEnd(source, p + 1);
                code.Append(source, i, end - i);
                i = end;
                continue;
            }
            _ = interpolated;   // read for the prefix, not needed once the terminator is known

            if (c == '\'')
            {
                int end = CharEnd(source, i + 1);
                code.Append(source, i, end - i);
                i = end;
                continue;
            }

            code.Append(c);
            i++;
        }

        return code.ToString();
    }

    /// <summary>Past the closing quote of a <c>"…"</c>, where a backslash escapes the next character.</summary>
    private static int QuotedEnd(string s, int i)
    {
        while (i < s.Length)
        {
            if (s[i] == '\\') { i += 2; continue; }
            if (s[i] == '"' || s[i] == '\n') return i + 1;
            i++;
        }
        return s.Length;
    }

    /// <summary>Past the closing quote of an <c>@"…"</c>, where <c>""</c> is one quote and a backslash is
    /// an ordinary character — the form every regex in this file is written in.</summary>
    private static int VerbatimEnd(string s, int i)
    {
        while (i < s.Length)
        {
            if (s[i] != '"') { i++; continue; }
            if (i + 1 < s.Length && s[i + 1] == '"') { i += 2; continue; }
            return i + 1;
        }
        return s.Length;
    }

    /// <summary>Past the closing fence of a raw string: the first run of at least as many quotes as opened
    /// it. A shorter run is content, which is the entire point of the form.</summary>
    private static int RawStringEnd(string s, int i, int opened)
    {
        while (i < s.Length)
        {
            if (s[i] != '"') { i++; continue; }
            int run = 0;
            while (i + run < s.Length && s[i + run] == '"') run++;
            if (run >= opened) return i + run;
            i += run;
        }
        return s.Length;
    }

    /// <summary>Past the closing quote of a char literal, so <c>'"'</c> does not open a string.</summary>
    private static int CharEnd(string s, int i)
    {
        while (i < s.Length)
        {
            if (s[i] == '\\') { i += 2; continue; }
            if (s[i] == '\'' || s[i] == '\n') return i + 1;
            i++;
        }
        return s.Length;
    }

    internal static IEnumerable<string> FlagsRead(string source) =>
        FlagComparison.Matches(source).Select(m => m.Groups["flag"].Value);

    private const string FlagLiteral = @"""(?<flag>--[a-z][a-z0-9-]*)""";

    // .NET merges repeats of a named group, so one name serves every alternative.
    private static readonly Regex FlagComparison = new(
        $@"\b(?:Contains|IndexOf|Equals|StartsWith|EndsWith)\s*\([^)]*?{FlagLiteral}"
        + $@"|[=!]=\s*{FlagLiteral}"
        + $@"|{FlagLiteral}\s*[=!]="
        + $@"|\bis\s+{FlagLiteral}",
        RegexOptions.Compiled);

    private static void FlagCatalogue(string root)
    {
        string skill = Path.Combine(root, DevFlags);
        string src = Path.Combine(root, "src");
        string[] accepted = (Directory.Exists(src)
                ? Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories)
                : Array.Empty<string>())
            // Over the code, not the file (T285). Matching the comparison shape is what keeps a flag merely
            // *named* in a paragraph out of this list, and it is not enough on its own: a paragraph that
            // quotes the shape — `Contains("--x")`, written to explain what is matched — is read as a flag
            // the .exe accepts. That is this scan's version of the failure T283's and T284's each had, and
            // it turned up the moment CodeOf existed to be used here. FlagsRead's own contract is unchanged:
            // its fixtures still hand it prose directly, because what they assert is the shape rule.
            .SelectMany(f => FlagsRead(CodeOf(File.ReadAllText(f))))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal).ToArray();

        // Declaration position only: left of the `#`, inside a fence. The prose above and below names
        // flags belonging to other programs, and a check that calls one of those a broken promise is a
        // check somebody turns off.
        var declared = new HashSet<string>(StringComparer.Ordinal);
        bool fenced = false;
        foreach (string line in File.ReadAllLines(skill))
        {
            if (line.StartsWith("```", StringComparison.Ordinal)) { fenced = !fenced; continue; }
            if (!fenced) continue;
            int hash = line.IndexOf('#');
            foreach (Match m in Regex.Matches(hash < 0 ? line : line[..hash], @"--[a-z][a-z0-9-]*"))
                declared.Add(m.Value);
        }

        if (!Check("the flag catalogue and the sources are both readable",
                   accepted.Length >= 30 && declared.Count >= 30,
                   $"{accepted.Length} accepted, {declared.Count} declared — the check cannot read what it compares"))
            return;

        string[] undocumented = accepted.Where(f => !declared.Contains(f)).ToArray();
        Check($"every flag the sources accept is declared in dev-flags ({accepted.Length})",
              undocumented.Length == 0,
              $"{string.Join(", ", undocumented)} — the .exe answers to it and the catalogue does not name it");

        string[] promised = declared.Where(f => !accepted.Contains(f, StringComparer.Ordinal))
                                    .OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Check($"and every flag it declares is read by something ({declared.Count})", promised.Length == 0,
              $"{string.Join(", ", promised)} — documented, and no source compares against it");
    }

    /// <summary>A file's stem row: everything before the first dot, plus <c>.cs</c> — so
    /// <c>StatisticsPage.Throughput.cs</c> and <c>MainWindow.xaml.cs</c> both resolve to the row that names
    /// the type they are part of. One class in several files is this repository's convention (T133–T134),
    /// not an omission from the map.</summary>
    private static string Stem(string relative)
    {
        string dir = relative[..(relative.LastIndexOf('/') + 1)];
        string name = relative[dir.Length..];
        int dot = name.IndexOf('.');
        return dot < 0 ? relative : dir + name[..dot] + ".cs";
    }

    /// <summary>A heading's GitHub anchor: lowercased, everything that is not a letter, a digit, a space
    /// or a hyphen dropped, then spaces to hyphens. Verified against all thirty-six rows the ledger
    /// already carries, em dashes and apostrophes included.</summary>
    private static string Anchor(string heading)
    {
        var sb = new StringBuilder();
        foreach (char c in heading.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c is ' ' or '-') sb.Append('-');
        }
        return sb.ToString();
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
    /// <summary>
    /// T172: whose numbers the icon draws and which profile a new session would start in are two
    /// questions, and the menu marks them only when the answers differ. The states that matter — the
    /// variable naming the *other* profile, or a folder no profile covers — are precisely the ones the
    /// machine running this is not in, so they are driven through
    /// <see cref="EnvironmentProfile.SelectedIn"/> rather than waited for.
    /// </summary>
    private static void EffectiveProfile()
    {
        const string work = @"C:\Users\x\.claude-work";
        const string home = @"C:\Users\x\.claude";
        List<ClaudeInfo> profiles = new()
        {
            new ClaudeInfo { Label = "Personal", ConfigDir = home, IsDefault = true },
            new ClaudeInfo { Label = "Work", ConfigDir = work },
        };

        Check("the variable's dir names the profile that owns it",
              EnvironmentProfile.SelectedIn(profiles, work)?.Label == "Work");
        Check("and the default dir names the default profile",
              EnvironmentProfile.SelectedIn(profiles, home)?.Label == "Personal");

        // The spelling half: a variable set by hand carries a trailing slash or a relative form, and a
        // reading that split one profile into two would report a disagreement that does not exist.
        Check("a trailing separator is the same profile, not an unknown folder",
              EnvironmentProfile.SelectedIn(profiles, work + @"\")?.Label == "Work");
        Check("and so is the other case of the same path",
              EnvironmentProfile.SelectedIn(profiles, work.ToUpperInvariant())?.Label == "Work");

        // The state with no entry to hang a mark on — something other than the tray set the variable.
        Check("a dir no profile covers selects none of them",
              EnvironmentProfile.SelectedIn(profiles, @"C:\Users\x\.claude-elsewhere") is null);
        Check("and an empty profile list answers null rather than throwing",
              EnvironmentProfile.SelectedIn(new List<ClaudeInfo>(), work) is null);

        // What the menu actually decides on: the mark appears only on a real divergence.
        Check("agreement is silent, disagreement is not",
              EnvironmentProfile.SelectedIn(profiles, home)?.ConfigDir == profiles[0].ConfigDir
              && EnvironmentProfile.SelectedIn(profiles, work)?.ConfigDir != profiles[0].ConfigDir);

        // Unset is not "no profile": a bare `claude` lands in ~/.claude, so the honest reading of an
        // absent variable is the default profile — not a blank the menu would have nothing to say about.
        Check("an unset variable reads as the default config dir",
              ClaudeAccount.SamePath(EnvironmentProfile.EffectiveConfigDir(),
                                     EnvironmentProfile.Current() ?? ClaudeAccount.HomeConfigDir));

        // T173: what a queued write reports back. Driven on the outcome itself, because the states worth
        // asserting are a write that threw and a write that silently did not take, and manufacturing
        // either on this machine means rewriting the developer's own CLAUDE_CONFIG_DIR.
        Check("a value that reads back as itself landed",
              new EnvironmentProfile.WriteOutcome(work, work, null, true).Landed);
        Check("and a value that reads back as something else did not",
              !new EnvironmentProfile.WriteOutcome(work, home, null, true).Landed);
        Check("a thrown write never counts as landed, whatever the variable says",
              !new EnvironmentProfile.WriteOutcome(work, work, "access denied", true).Landed);
        // Removing the variable is the "select ~/.claude" case, where null is the intended value — and
        // an absent variable reads back as null, not as an empty string, on one route and not the other.
        Check("removing the variable lands when it reads back absent",
              new EnvironmentProfile.WriteOutcome(null, null, null, true).Landed
              && new EnvironmentProfile.WriteOutcome(null, "", null, true).Landed);
        Check("and does not land while the old value is still there",
              !new EnvironmentProfile.WriteOutcome(null, work, null, true).Landed);
        // A no-op is a landed outcome — the machine already says this — and still reports, so a caller
        // asking "did my choice reach the machine?" is never met with silence.
        Check("nothing to write is still an answer, and a landed one",
              new EnvironmentProfile.WriteOutcome(work, work, null, false) is { Wrote: false, Landed: true });
    }

    /// <summary>
    /// The sampled environment (T231), and the one promise it has to keep: a process answering off a
    /// fixture writes nothing to the machine.
    ///
    /// <para><b>Runs last, and nothing may follow it.</b> <see cref="EnvironmentProfile.Sample"/> is
    /// one-way on purpose — a fixture that could be switched back off would let a later caller write the
    /// real <c>CLAUDE_CONFIG_DIR</c> in a process that has already been lying about it — so every
    /// assertion above that reads the true variable has to have run by now.</para>
    /// </summary>
    private static void SampledEnvironment()
    {
        // Read the machine's own value BEFORE anything is sampled: it is the control the write below is
        // measured against, and after Sample() there is no way left to ask for it.
        string? real = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR", EnvironmentVariableTarget.User);

        // The catalogue is the refusal (T186's rule): an unknown mode names what is on offer rather than
        // rendering the ordinary state under a flag that asked for another.
        string? refusal = EnvironmentFixture.Apply("no-such-mode");
        Check("an unknown --sample-env mode is refused, not silently ignored", refusal is { Length: > 0 });
        Check("and the refusal names every mode there is",
              refusal is { } r && EnvironmentFixture.Modes.All(m => r.Contains(m.Name, StringComparison.Ordinal)));
        Check("a refused mode samples nothing", !EnvironmentProfile.IsSampled);

        const string fake = @"C:\Users\nobody\.claude-sampled";
        EnvironmentProfile.Sample(fake);
        Check("a sampled variable is what every reader sees",
              EnvironmentProfile.IsSampled
              && EnvironmentProfile.Current() == fake
              && EnvironmentProfile.EffectiveConfigDir() == fake);

        // The whole variable, not one caller's answer: the menu asks Selected, the read-out asks
        // EffectiveConfigDir, and a fixture that moved one without the other would certify a screen that
        // cannot occur — which is the objection T231 was filed carrying.
        var profiles = new List<ClaudeInfo>
        {
            new() { Label = "Personal", ConfigDir = ClaudeAccount.HomeConfigDir },
            new() { Label = "Work", ConfigDir = fake },
        };
        Check("and the selection follows it, so the menu and the read-out cannot disagree",
              EnvironmentProfile.SelectedIn(profiles, EnvironmentProfile.EffectiveConfigDir())?.Label == "Work");

        EnvironmentProfile.Sample(null);
        Check("a sampled-absent variable reads as the default config dir, as a real absent one does",
              ClaudeAccount.SamePath(EnvironmentProfile.EffectiveConfigDir(), ClaudeAccount.HomeConfigDir));

        // The promise. Adopt goes through the real queue, the real read-back and the real outcome — the
        // only thing it does not go through is the registry.
        var settings = new Settings();
        EnvironmentProfile.Adopt(settings, fake);
        EnvironmentProfile.Drain();
        Check("a write under the fixture moves the fixture", EnvironmentProfile.Current() == fake);
        Check("and reports itself landed, so T173's whole path is exercised off the machine",
              EnvironmentProfile.Last is { Landed: true, Wrote: true });
        Check("and the machine's own variable is untouched",
              Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR", EnvironmentVariableTarget.User) == real);
    }

    /// <summary>
    /// An observing tray adds nothing to the user's files (T239) — asserted against the real
    /// <c>%LocalAppData%\ClaudeTray</c>, because that is the thing being promised.
    ///
    /// <para>Every write entry point is driven with arguments that WOULD write, and the whole tree is
    /// compared before and after: not "the gate is present" — which is what reading the source would
    /// tell you — but "nothing landed", which is the property. A store added later without the gate
    /// fails here the moment this drives it, and a store added later that this does not drive is the
    /// hole the doc comment on <c>ProfileStore.Observing</c> names.</para>
    ///
    /// <para>Runs last for the same reason as the section above: <c>Observe()</c> is one-way.</para>
    /// </summary>
    private static void ObservingTray()
    {
        string data = Settings.DataDir;
        // Whether the tree could be READ — the one state that makes the comparison below vacuous, and so
        // the one the precondition asks about. An empty tree is not that state: a machine that has never
        // run the tray has nothing to fingerprint, and every file the calls below would create still
        // shows up in the second snapshot and is still named. Asking for a non-empty baseline instead
        // failed on exactly that machine — the runner's %LocalAppData%\ClaudeTray is created by the
        // `stores` section above and emptied again by its own cleanup, leaving a directory that exists
        // and holds nothing, which is a fine state to promise "nothing landed" about.
        bool readable = true;
        // A fingerprint of every file the app owns: path, length and write time. Taken before the switch
        // is thrown, so the baseline is the state a check run must leave behind untouched.
        Dictionary<string, (long Len, DateTime When)> Snapshot()
        {
            var map = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (Directory.Exists(data))
                    foreach (string f in Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories))
                    {
                        var fi = new FileInfo(f);
                        map[f] = (fi.Length, fi.LastWriteTimeUtc);
                    }
            }
            catch { readable = false; }
            return map;
        }

        Dictionary<string, (long Len, DateTime When)> before = Snapshot();
        Check("the store tree can be read at all, or the comparison below asserts nothing", readable);

        ProfileStore.Observe();
        Check("observing is one-way and the environment goes with it (T231's sampling, T239's promise)",
              ProfileStore.Observing && EnvironmentProfile.IsSampled);

        // Real keys and real arguments: the point is that these calls would otherwise land on the files
        // fingerprinted above. `Monitored` is this machine's own profile key, which is the one a second
        // tray polls and the one a check run would have appended to.
        string key = ProfileStore.Monitored;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        UsageHistory.Append(key, now, 0.42, now + 3600, 0.21, now + 86400, 0.1, now + 7200);
        HourlyUsage.Fold(key, new List<UsageSample>
        {
            new(now - 7200, 0.10, 0, 0.05, 0, null, 0),
            new(now - 3600, 0.20, 0, 0.10, 0, null, 0),
            new(now,        0.30, 0, 0.15, 0, null, 0),
        }, now);
        ContextNudges.Mark(key, "selftest-observing", DateTime.UtcNow);
        // The two SHARED caches, which the per-profile list above does not reach and which an end-to-end
        // run caught changing after the first five gates were in (T239). Driven through the scan and the
        // usage pass, because their writers are private: what is being asserted is that a scan an
        // observing tray runs leaves the cache alone, not that a method exists.
        ContextScanner.Scan(DateTime.UtcNow);
        ContextUsage.Compute(DateTime.UtcNow);
        // The third shared cache (T327). Driven against the real tree and the real cache path for the
        // same reason as the two above: a store this section does not drive is a store nothing here
        // promises anything about.
        SessionIndex.Load();
        new Settings().Save();
        var settings = new Settings();
        EnvironmentProfile.Adopt(settings, @"C:\Users\nobody\.claude-observing");
        EnvironmentProfile.Drain();

        Dictionary<string, (long Len, DateTime When)> after = Snapshot();
        var moved = new List<string>();
        foreach (KeyValuePair<string, (long Len, DateTime When)> kv in after)
            if (!before.TryGetValue(kv.Key, out (long Len, DateTime When) was) || was != kv.Value)
                moved.Add(Path.GetFileName(kv.Key));
        // `readable` covers the second snapshot too: a tree that became unreadable between the two reads
        // produces an empty `after`, over which both comparisons would otherwise pass saying nothing.
        Check("no file under %LocalAppData%\\ClaudeTray was created or changed"
              + (moved.Count > 0 ? " — moved: " + string.Join(", ", moved.Distinct()) : ""),
              readable && moved.Count == 0);
        Check("and nothing was deleted either", readable && before.Keys.All(after.ContainsKey));
    }

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

        InteractionIds(owners);
    }

    /// <summary>
    /// Every id <c>Check-Interaction.ps1</c> looks a control up by, because that lookup is a <em>string</em>
    /// and no compiler checks it. Renaming <c>StatusText</c> while doing T192 broke the one behind T166's
    /// "the status line must never be observed at all" — and a lookup that finds nothing makes that
    /// assertion pass by seeing nothing, which is §XX.2's defect exactly.
    ///
    /// <para><b>Derived from the script, not remembered (T203).</b> T192 wrote the fifteen ids out by hand,
    /// and a hand list is right on the day it is read off and decays silently after: by the time this was
    /// written it had already missed three (<c>BrowseButton</c>, <c>ProfileAddButton</c>,
    /// <c>ProfileRemoveButton</c>, all added with T196's row trio), and a list that is short looks exactly
    /// like a list that is complete. The uniqueness half above has never had that problem because it
    /// reflects over every <c>IComponentConnector</c>; this is the same move against a text file.</para>
    ///
    /// <para><b>Three shapes carry a literal id</b>, not one — deriving only from <c>ById</c> would have
    /// reproduced the very gap this fixes, since the three missing ones arrive as table rows:
    /// <c>ById</c>/<c>ByIdNow</c>/<c>Assert-Name</c> called with a quoted id, and <c>Id = '…'</c> in a
    /// hashtable the case then walks. A lookup whose id is a <em>variable</em> is invisible to any regex,
    /// so the ids built at runtime (<c>"Used$sfx"</c> and its two siblings) stay explicit — and the check
    /// says which kind it is asserting, because the two have different failure modes.</para>
    /// </summary>
    private static void InteractionIds(Dictionary<string, List<string>> owners) =>
        Repo("every id the interaction check drives still exists", root => InteractionIds(root, owners),
             "scripts/Check-Interaction.ps1");

    private static void InteractionIds(string root, Dictionary<string, List<string>> owners)
    {
        string script = Path.Combine(root, "scripts/Check-Interaction.ps1");
        string text = File.ReadAllText(script);
        string[] derived = System.Text.RegularExpressions.Regex
            .Matches(text, @"(?:ById|ByIdNow|Assert-Name)\s+\$\w+\s+'([A-Za-z]\w*)'|Id\s*=\s*'([A-Za-z]\w*)'")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        // The guard on the precondition, not on a weaker form of the claim: a pattern that stopped matching
        // yields an empty set, and "none of zero ids is gone" is a green tick over nothing.
        if (!Check("the interaction script's id lookups are found by pattern", derived.Length >= 12,
                   $"matched {derived.Length} in {Path.GetFileName(script)} — the lookup syntax changed " +
                   "and this check is now asserting almost nothing"))
            return;

        string[] gone = derived.Where(id => !owners.ContainsKey(id)).ToArray();
        Check($"every id the interaction check drives still exists ({derived.Length}, derived)",
              gone.Length == 0,
              gone.Length == 0 ? "" : $"{gone.Length} gone — {string.Join(", ", gone)}: renamed without " +
                                      "updating scripts\\Check-Interaction.ps1, whose lookups are strings");

        // The other kind: assembled per pane suffix, so no literal exists to match. Both halves are
        // asserted — that the control is still named that, and that the script still builds it that way,
        // since a rename of the *pattern* would leave this list pointing at ids nobody looks up any more.
        string[] composed = { "UsedS", "UsedW", "ResetS", "ResetW", "LiveHeadS", "LiveHeadW" };
        string[] lostControl = composed.Where(id => !owners.ContainsKey(id)).ToArray();
        Check($"every id it builds per pane still exists ({composed.Length}, explicit)",
              lostControl.Length == 0,
              lostControl.Length == 0 ? "" : $"{lostControl.Length} gone — {string.Join(", ", lostControl)}");

        string[] lostPattern = new[] { "Used", "Reset", "LiveHead" }
            .Where(p => !text.Contains($"\"{p}$sfx\"", StringComparison.Ordinal)).ToArray();
        Check("and the script still assembles them from $sfx", lostPattern.Length == 0,
              lostPattern.Length == 0 ? "" : $"{string.Join(", ", lostPattern)} no longer built that way — " +
                                             "the explicit list above is stale and asserts nothing");
    }

    /// <summary>
    /// The two documents a person outside this repository actually reads (T250): <c>README.md</c> and the
    /// published site. The user-facing-surface gate names three surfaces a shipped feature owes, and only
    /// <c>lang\*.json</c> was held to it — T185 fails a key that reached one language file. These two were
    /// maintained on trust, by the same hand in the same commit, which is exactly the pattern that drifts.
    ///
    /// <para><b>What is deliberately not asserted, and this is what §XXII.10 left to settle.</b> The gate
    /// asks the two to be *consistent in wording*, and a check demanding that would fail on every rewrite —
    /// the failure mode that gets a check switched off. The site's headings are marketing copy and the
    /// README's are not, on purpose. So the wording is left alone and what is checked is what has a right
    /// answer: the <b>assets</b> both files point at, and the <b>identifier</b> both files quote.</para>
    ///
    /// <para>Three ways they go wrong, all silent. A screenshot renamed leaves a broken image on a page
    /// nobody loads until a stranger does. A screenshot that neither file shows is one nobody re-takes when
    /// the window moves, so it rots into a picture of an app that no longer exists. And the winget id is a
    /// string a user types: spelled two ways it sends somebody to a package that is not there, and it is
    /// quoted in six places across the README, the site and the manifests.</para>
    /// </summary>
    private static void UserSurfaces(string root)
    {
        string readme = Path.Combine(root, "README.md");
        string site = Path.Combine(root, "docs/index.html");
        string docs = Path.Combine(root, "docs");
        string readmeText = File.ReadAllText(readme);
        string siteText = File.ReadAllText(site);

        // `src`, `href` and `content` alike: the social preview is an og:image, which a check written for
        // the first two would have declared unreferenced and been wrong about.
        static string[] Assets(string text) =>
            Regex.Matches(text, $@"(?:src|href|content)=""([^"":]+\.(?:png|gif|jpg|svg))""")
                 .Select(m => m.Groups[1].Value)
                 .Concat(Regex.Matches(text, @"!\[[^\]]*\]\(([^)]+\.(?:png|gif|jpg|svg))\)")
                              .Select(m => m.Groups[1].Value))
                 .Where(p => !p.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .OrderBy(p => p, StringComparer.Ordinal).ToArray();

        string[] fromReadme = Assets(readmeText);
        string[] fromSite = Assets(siteText);
        if (!Check("both documents still reference their screenshots",
                   fromReadme.Length >= 8 && fromSite.Length >= 8,
                   $"{fromReadme.Length} in the README, {fromSite.Length} on the page — " +
                   "the check cannot read what it compares"))
            return;

        // The README's paths are repo-relative, the page's are relative to itself.
        string[] brokenReadme = fromReadme.Where(p => !File.Exists(Path.Combine(root, p))).ToArray();
        Check($"every image the README points at exists ({fromReadme.Length})", brokenReadme.Length == 0,
              $"{string.Join(", ", brokenReadme)} — a broken image in the first thing anybody reads");

        string[] brokenSite = fromSite.Where(p => !File.Exists(Path.Combine(docs, p))).ToArray();
        Check($"every image the published page points at exists ({fromSite.Length})", brokenSite.Length == 0,
              $"{string.Join(", ", brokenSite)} — broken on a page nobody loads until a stranger does");

        var shown = new HashSet<string>(
            fromSite.Concat(fromReadme.Select(p => p.Replace("docs/", "", StringComparison.Ordinal))),
            StringComparer.OrdinalIgnoreCase);
        string[] orphans = Directory.Exists(docs)
            ? Directory.GetFiles(docs, "*.png").Select(f => Path.GetFileName(f))
                       .Where(f => !shown.Contains(f))
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        Check("and every screenshot in docs is shown by one of them", orphans.Length == 0,
              $"{string.Join(", ", orphans)} — shown nowhere, so nobody re-takes it when the window moves");

        // An identifier, not prose: a user types it, and two spellings send one of them nowhere.
        string[] ids = Regex.Matches(readmeText + siteText, @"alegauss\.[A-Za-z]+")
            .Select(m => m.Value).Distinct(StringComparer.Ordinal)
            .Where(v => !v.Equals("alegauss.github", StringComparison.Ordinal))
            .OrderBy(v => v, StringComparer.Ordinal).ToArray();
        Check("the README and the page quote one winget id", ids.Length == 1,
              $"{string.Join(", ", ids)} — one of these installs nothing");

        string manifest = Path.Combine(root, "build", "winget", "alegauss.ClaudeCodeTray.yaml");
        Check("and it is the one the manifest publishes",
              ids.Length == 1 && File.Exists(Path.Combine(root, "build", "winget", $"{ids[0]}.yaml")),
              $"{(ids.Length == 1 ? ids[0] : "several")} names no manifest beside {Path.GetFileName(manifest)}");
    }

    /// <summary>
    /// The precondition every *one claim, two places* check shares, named once (T247).
    ///
    /// <para>Seven assertions had grown the same four lines each — call <see cref="RepoFile"/>, get null on
    /// an installed copy, <c>Skip</c> with a freshly written sentence, and remember to add a matching line
    /// to <see cref="AllowedSkips"/> so the skip is not red. The duplication was not the cost. Seven
    /// sentences said one thing seven ways; the allowance was remembered in a second place, where a name
    /// typed differently is a skip that goes red for a reason nobody can act on; and the eighth check would
    /// inherit none of it. Here the allowance travels <em>with</em> the skip, so a check declares its
    /// subject and nothing else, and the family is one sentence.</para>
    ///
    /// <para>It also hands over the repository <b>root</b>, which nothing had: <c>RepoFile</c> answers about
    /// files, so the check that needed the <c>src\</c> directory derived it from wherever <c>AGENTS.md</c>
    /// happened to be found. A file the checkout is missing is now a red precondition inside the body
    /// rather than a skip — in a repository those files are always there, and pretending otherwise is how a
    /// check quietly stops asserting.</para>
    ///
    /// <para><b>The allowance is for an installed copy and nowhere else</b>, which is what §XXII.8 left to
    /// settle. <c>check.yml</c> runs the build from the checkout, so a repository skip there is
    /// <c>RepoFile</c> having broken — five checks lost in a run that stays green, which is the exact shape
    /// this file keeps naming. Under <c>CI</c> the allowance does not apply and the skip is unexpected.</para>
    ///
    /// <para><b><c>needs</c> is what the check reads (T251).</b> Naming the family's guard once left every
    /// body opening with a guard of its own — combine a path, <c>Check</c> it exists, return — which is the
    /// same shape one level down, seven times, and seven assertions that pass every run and have never
    /// been seen to fail. Declared here they are proved in one place, and the assertion says which path is
    /// missing rather than that some file was.</para>
    ///
    /// <para>§XXII.11 asked whether a governed file the checkout lacks should be red or the same skip as a
    /// missing repository. <b>Red.</b> The two are not the same fact: an installed copy has no repository
    /// and never will, while a checkout missing <c>CHANGELOG.md</c> is a repository somebody is halfway
    /// through changing — and standing a check down for that is how coverage is lost to a state nobody
    /// meant to leave behind.</para>
    /// </summary>
    private static void Repo(string claim, Action<string> assert, params string[] needs)
    {
        string? agents = RepoFile("AGENTS.md");
        if (agents is null)
        {
            RepoSkipped.Add(claim);
            Skip(claim, "no repository beside this build — a document and the thing it documents ship with " +
                        "the source, and an installed copy has neither");
            return;
        }

        string root = Path.GetDirectoryName(agents)!;
        string[] missing = needs
            .Where(n => !File.Exists(Path.Combine(root, n)) && !Directory.Exists(Path.Combine(root, n)))
            .ToArray();
        if (!Check($"{claim} — its files are in the checkout", missing.Length == 0,
                   $"{string.Join(", ", missing)} — a checkout missing one of these is not an installed " +
                   "copy, so it is red rather than a skip (T251)"))
            return;

        assert(root);
    }

    /// <summary>The claims <see cref="Repo"/> stood down this run, so the allowance is derived from the
    /// skip that happened rather than remembered beside it.</summary>
    private static readonly List<string> RepoSkipped = new();

    /// <summary>Whether this is a run that had no business skipping the repository family — GitHub Actions
    /// sets <c>CI</c>, and the checkout is the working directory there.</summary>
    private static bool OnCi => Environment.GetEnvironmentVariable("CI") is { Length: > 0 };

    /// <summary>
    /// A file in the repository this build came out of, or null when there is no repository — an installed
    /// copy has none. Walks up from the binary and from the working directory, because <c>dotnet build</c>
    /// puts the exe four levels down while CI runs it with the checkout as the current directory.
    /// </summary>
    private static string? RepoFile(string relative)
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            DirectoryInfo? dir = new(start);
            while (dir is not null)
            {
                string candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- Block F: what the note says

    /// <summary>
    /// T168: which paragraphs the method note yields, over the four inputs that decide it. The rules are
    /// Block Z's, most of them written in one week, and until this task the only verification any of them
    /// ever had was that somebody looked at two screenshots.
    ///
    /// <para>The T163 rule is pinned hardest on purpose. Getting it wrong produces a sentence that is
    /// <em>plausible</em> — "shaping it takes about 3 weeks of local history and there are 2.1 so far" —
    /// told to somebody whose projection is unshaped because they are already at the limit. Nothing on
    /// screen would look broken, which is exactly the class of defect a screenshot cannot catch.</para>
    /// </summary>
    private static void MethodNote()
    {
        static ActivityProfile Profile(double coverageWeeks, int excluded = 0,
                                       double measuredWeeks = 0, int measuredExcluded = 0, double share = 0)
            => new()
            {
                CoverageWeeks = coverageWeeks, ExcludedWeeks = excluded,
                MeasuredWeeks = measuredWeeks, MeasuredExcludedWeeks = measuredExcluded, MeasuredShare = share,
            };

        static PaceReport Report(ActivityShape? shape, ActivityProfile? activity, bool hasWindow = true)
        {
            var r = new PaceReport { Activity = activity };
            r.Weekly.HasWindow = hasWindow;
            r.Weekly.Shape = shape;
            return r;
        }

        static string[] Keys(PaceReport r, bool demoThin = false)
            => StatisticsPage.MethodNoteParts(r, demoThin).Select(p => p.Key).ToArray();

        // The two paragraphs that are not a decision: the report is always described, and the live strip's
        // blind spot is always disclosed, whatever the middle turns out to be.
        var cases = new (string What, PaceReport R, bool Thin)[]
        {
            ("nothing measured", Report(null, null, hasWindow: false), false),
            ("no shape, confident", Report(null, Profile(5)), false),
            ("no shape, thin history", Report(null, Profile(1)), false),
            ("shaped", Report(new ActivityShape { EffectiveWeeks = 4.7 }, Profile(4.7)), false),
            ("shaped and measured", Report(new ActivityShape { EffectiveWeeks = 4.7 },
                                           Profile(4.7, measuredWeeks: 4.7, share: 0.8)), false),
            ("the thin preview", Report(new ActivityShape { EffectiveWeeks = 4.7 }, Profile(4.7)), true),
        };
        string[] misframed = cases
            .Where(c => Keys(c.R, c.Thin) is not [ "stats.methodNote", .., "stats.methodNote.live" ])
            .Select(c => $"{c.What} → {string.Join(" + ", Keys(c.R, c.Thin))}").ToArray();
        Check($"the note always opens on the report and closes on the live strip ({cases.Length} shapes)",
              misframed.Length == 0, string.Join("; ", misframed));

        // Mutually exclusive by construction, and the check says so rather than the comment.
        string[] both = cases.Select(c => Keys(c.R, c.Thin))
            .Where(k => k.Contains("stats.methodNote.thin") &&
                        (k.Contains("stats.methodNote.shape") || k.Contains("stats.methodNote.shapeMeasured")))
            .Select(k => string.Join(" + ", k)).ToArray();
        Check("shaped and thin never appear together", both.Length == 0, string.Join("; ", both));

        // T163, the one that produces a plausible lie. `ActivityShape.Build` returns null for three
        // different reasons and only one of them is "not enough history": at the limit and with nothing
        // spent it also declines, and telling either of those to keep the tray running another week is
        // advice about a problem they do not have.
        Check("a confident profile with no shape is not told its history is thin",
              !Keys(Report(null, Profile(5))).Contains("stats.methodNote.thin"),
              string.Join(" + ", Keys(Report(null, Profile(5)))));
        Check("and neither is a window that has no reading at all",
              !Keys(Report(null, Profile(1), hasWindow: false)).Contains("stats.methodNote.thin"),
              string.Join(" + ", Keys(Report(null, Profile(1), hasWindow: false))));
        Check("an unconfident profile with a live window is",
              Keys(Report(null, Profile(1))).Contains("stats.methodNote.thin"),
              string.Join(" + ", Keys(Report(null, Profile(1)))));

        // Which of the two shaped paragraphs, on the half-the-grid line T93 drew.
        Check("past half the grid the note credits the measurement, not the transcripts",
              Keys(Report(new ActivityShape(), Profile(4, measuredWeeks: 4, share: 0.5)))
                  .Contains("stats.methodNote.shapeMeasured"));
        Check("and just under it, the transcripts",
              Keys(Report(new ActivityShape(), Profile(4, measuredWeeks: 4, share: 0.49)))
                  .Contains("stats.methodNote.shape"));

        // T159: the away clause is a nested fragment, so its *absence* is an empty argument rather than a
        // "(0 excluded)" nobody reads. Both halves are asserted — a clause that never appears would pass
        // half of this on its own.
        static object? Away(PaceReport r) =>
            StatisticsPage.MethodNoteParts(r, false)
                          .First(p => p.Key.StartsWith("stats.methodNote.shape", StringComparison.Ordinal))
                          .Args.LastOrDefault();

        Check("no week dropped, no away clause",
              Away(Report(new ActivityShape { EffectiveWeeks = 4.7, ExcludedWeeks = 0 }, Profile(4.7))) is "",
              $"{Away(Report(new ActivityShape { EffectiveWeeks = 4.7 }, Profile(4.7)))}");
        Check("one week dropped says so in the singular",
              Away(Report(new ActivityShape { EffectiveWeeks = 4.7, ExcludedWeeks = 1 }, Profile(4.7)))
                  is NoteFragment { Key: "stats.methodNote.away.one", Args.Length: 0 });
        Check("two says so in the plural, with the count",
              Away(Report(new ActivityShape { EffectiveWeeks = 4.7, ExcludedWeeks = 2 }, Profile(4.7)))
                  is NoteFragment { Key: "stats.methodNote.away.many", Args: ["2"] });

        // And the numbers in it are the *effective* weeks, through `Num` (T167): the span minus the weeks
        // away, because that is the figure the confidence gate acted on. A note that quotes the span
        // overstates the evidence every time a week was dropped.
        NoteFragment shapePart = StatisticsPage
            .MethodNoteParts(Report(new ActivityShape { EffectiveWeeks = 4.7, ExcludedWeeks = 2 }, Profile(6.7)), false)
            .First(p => p.Key == "stats.methodNote.shape");
        Check("the shaped paragraph quotes the effective weeks, not the span",
              shapePart.Args is ["4.7", _], string.Join(", ", shapePart.Args));

        // The thin preview poses as a machine short of the bar. It used to read "4.7 of 3" — a sentence
        // the string is never shown with — because the preview took the real machine's own figure.
        // T170: every fragment is filed under a heading, and the map has no catch-all arm — so a paragraph
        // added later is a red build here rather than one that quietly files itself under the wrong
        // surface. Derived from what the function can actually yield, never from a list kept by hand.
        string[] unfiled = cases.SelectMany(c => Keys(c.R, c.Thin)).Distinct()
                                .Where(k => StatisticsPage.HeadingFor(k) is null).ToArray();
        Check("every paragraph the note can yield is filed under a heading",
              unfiled.Length == 0, string.Join(", ", unfiled));

        // And the budget, which is the question §XV.4 left open: what the real limit is. Block Z grew this
        // note twice without anyone noticing, and a note nobody finishes reading is not more honest than a
        // shorter one — it is less. Measured over all five languages, the worst case today is 1,276
        // characters (fr, mostly-measured) in paragraphs of at most 497. The caps below sit just above
        // that: growing past one is a decision to take here, in the open, not a sentence that fits.
        const int ParagraphCap = 520, NoteCap = 1350, HeadingCap = 40;
        string beforeLang = L.Codes.First(c => L.Resolve(c) == L.Current);
        try
        {
            List<string> over = new();
            // Every shipped code, from `L.Codes` — a language added later is covered without a second
            // list being edited here, the same rule T185's translation sweep follows.
            foreach (string code in L.Codes)
            {
                L.Apply(code);
                foreach ((string what, PaceReport r, bool thin) in cases)
                {
                    IReadOnlyList<NoteLine> lines = StatisticsPage.MethodNoteLines(r, thin);
                    int total = lines.Sum(l => l.Body.Length);
                    if (total > NoteCap) over.Add($"{code}/{what}: {total} chars > {NoteCap}");
                    foreach (NoteLine l in lines)
                    {
                        if (l.Body.Length > ParagraphCap)
                            over.Add($"{code}/{what}: a paragraph of {l.Body.Length} > {ParagraphCap}");
                        if (l.Heading.Length > HeadingCap)
                            over.Add($"{code}: heading \"{l.Heading}\" is {l.Heading.Length} > {HeadingCap}");
                    }
                }
            }
            Check($"the note stays inside its budget in every language ({NoteCap} total, {ParagraphCap} a paragraph)",
                  over.Count == 0, string.Join("; ", over.Distinct().Take(4)));
        }
        finally { L.Apply(beforeLang); }

        NoteFragment thinPart = StatisticsPage
            .MethodNoteParts(Report(new ActivityShape { EffectiveWeeks = 4.7 }, Profile(4.7)), demoThin: true)
            .First(p => p.Key == "stats.methodNote.thin");
        Check("the thin preview is short of its own bar",
              thinPart.Args is ["2.1", "3"], string.Join(" of ", thinPart.Args));
    }

    // ---------------------------------------------------------------- Block AI: what --probe was asked

    /// <summary>
    /// T245. <c>--probe --recorded</c> promises to make no call and <c>--probe --live --recorded</c>
    /// promises to refuse rather than silently pick a half. Both were rules held up by whoever last ran the
    /// flag by hand — the shape T186 and T198 already made assertable for the previews, and quieter than
    /// theirs: a <c>--recorded</c> that made a call prints what it prints now plus one block, and the only
    /// evidence is a request spent against the account the flag exists to stop spending against.
    ///
    /// <para><see cref="ProbeCli.Plan"/> is what made it assertable. The four outcomes are swept over every
    /// combination of the three switches rather than listed, so the table cannot go stale, and both
    /// directions are asserted: <c>--recorded</c> never yields a reading, and its absence always does.</para>
    ///
    /// <para><b>Where this stops, stated rather than implied.</b> The plan is pure, so what is held here is
    /// the <em>decision</em>. That the run honours it rests on the single <c>if</c> in front of the one
    /// call site — which is the reason the decision was pulled out of the method at all. Driving the run
    /// itself would read a real profile's stores, which this suite does not do.</para>
    /// </summary>
    private static void ProbePlanning()
    {
        foreach (bool live in new[] { false, true })
            foreach (bool recorded in new[] { false, true })
                foreach (bool all in new[] { false, true })
                {
                    var args = new List<string>();
                    if (live) args.Add("--live");
                    if (recorded) args.Add("--recorded");
                    if (all) args.Add("--all");
                    string said = args.Count == 0 ? "(no switches)" : string.Join(" ", args);

                    ProbeCli.ProbePlan p = ProbeCli.Plan(args);

                    if (live && recorded)
                    {
                        Check($"{said}: refused, and a refusal does neither half",
                              p.Refused && !p.ReadLog && !p.TakeReading,
                              $"refused={p.Refused} readLog={p.ReadLog} takeReading={p.TakeReading}");
                        Check($"{said}: the refusal says what to pass instead",
                              (p.Refusal ?? "").Contains("--live") && (p.Refusal ?? "").Contains("--recorded"),
                              p.Refusal ?? "<none>");
                        continue;
                    }

                    Check($"{said}: reads the log unless --live, takes a reading unless --recorded",
                          !p.Refused && p.ReadLog == !live && p.TakeReading == !recorded,
                          $"refused={p.Refused} readLog={p.ReadLog} takeReading={p.TakeReading}");
                    Check($"{said}: --all is carried through untouched", p.AllProfiles == all);
                }

        // The promise itself, over the whole sweep rather than one row of it: nothing carrying --recorded
        // may come back asking for a reading, however it was spelled or ordered.
        string[][] spellings =
        {
            new[] { "--recorded" },
            new[] { "--recorded", "--all" },
            new[] { "--all", "--recorded" },
            new[] { "--RECORDED" },
            new[] { "--recorded", "--live" },
            new[] { "--live", "--recorded" },
        };
        string[] spends = spellings.Where(s => ProbeCli.Plan(s).TakeReading)
                                   .Select(s => string.Join(" ", s)).ToArray();
        Check($"no spelling of --recorded asks for a live call ({spellings.Length})", spends.Length == 0,
              $"{string.Join("; ", spends)} — the one thing this flag exists to promise");

        // And the opposite, or the check above is satisfied by a plan that never calls at all.
        Check("while the bare flag does take one",
              ProbeCli.Plan(Array.Empty<string>()) is { TakeReading: true, ReadLog: true, Refused: false });
    }

    // ---------------------------------------------------------------- Block AI: what the app reads

    /// <summary>
    /// T278. <c>--probe</c> printed fourteen names and said nothing about which of them this app reads. The
    /// read-out that now does is driven from <see cref="ApiClient.NamesRead"/> — the parser enumerating
    /// itself — so the failure it cannot have is a table drifting from the code; what it can still have is
    /// the wiring between the two coming apart, and that is what this holds up.
    ///
    /// <para><b>The defect is asymmetric, and so are the assertions.</b> A name the parser reads that the
    /// probe calls unread is a lie about the app: a field filled from a header the instrument says nothing
    /// touches. A name nothing reads is <em>permitted</em> — the whole family is recorded verbatim precisely
    /// so a header is on file before anybody knows it matters — so the property there is not that the set is
    /// empty but that it is impossible to miss: every one of them carries the loud mark, and the count is
    /// stated in front of the readings.</para>
    ///
    /// <para>Asked against the fourteen names one real account sends, so "unread" is measured over the set
    /// that actually arrives rather than over the four families a check could quietly invent.</para>
    /// </summary>
    private static void Readership()
    {
        // The reading of 2026-08-03, name for name: nine the parser reads and five it does not.
        string[] read =
        {
            "anthropic-ratelimit-unified-5h-utilization", "anthropic-ratelimit-unified-5h-reset",
            "anthropic-ratelimit-unified-5h-status", "anthropic-ratelimit-unified-7d-utilization",
            "anthropic-ratelimit-unified-7d-reset", "anthropic-ratelimit-unified-7d-status",
            "anthropic-ratelimit-unified-overage-utilization", "anthropic-ratelimit-unified-overage-reset",
            "anthropic-ratelimit-unified-overage-status",
        };
        string[] unread =
        {
            "anthropic-ratelimit-unified-status", "anthropic-ratelimit-unified-reset",
            "anthropic-ratelimit-unified-representative-claim", "anthropic-ratelimit-unified-fallback",
            "anthropic-ratelimit-unified-fallback-percentage",
        };

        // Every name the parser reads is a name the probe calls read. This is the direction that must not
        // fail: the other one is a permission.
        string[] denied = ApiClient.NamesRead.Where(n => !HeaderProbe.IsRead(n)).ToArray();
        Check($"every name the parser reads is marked read ({ApiClient.NamesRead.Count})", denied.Length == 0,
              string.Join("; ", denied));
        Check("and the parser's list is the parser's own, not an empty one",
              read.All(n => ApiClient.NamesRead.Contains(n, StringComparer.OrdinalIgnoreCase)),
              string.Join("; ", read.Where(n => !ApiClient.NamesRead.Contains(n, StringComparer.OrdinalIgnoreCase))));
        // T273's name is not in the list above and should not be: that list is the reading of 2026-08-03,
        // and this header was not sent to an account inside its quota. It is asserted separately because the
        // whole point of reading it is that it arrives on the reading nobody had yet.
        Check("and the header that says overage is happening is read, a day after the list above",
              ApiClient.NamesRead.Contains("anthropic-ratelimit-unified-overage-in-use",
                                           StringComparer.OrdinalIgnoreCase));
        Check("as is the one naming the threshold the window crossed — the first name the mark caught",
              ApiClient.NamesRead.Contains("anthropic-ratelimit-unified-5h-surpassed-threshold",
                                           StringComparer.OrdinalIgnoreCase));
        Check("a name no line of the parser asks for is not marked read",
              unread.All(n => !HeaderProbe.IsRead(n)),
              string.Join("; ", unread.Where(HeaderProbe.IsRead)));
        Check("the mark is not case-sensitive — a header name is not",
              HeaderProbe.IsRead("ANTHROPIC-RATELIMIT-UNIFIED-5H-UTILIZATION"));

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string n in read) headers[n] = "0.5";
        foreach (string n in unread) headers[n] = "five_hour";
        List<string> lines = ProbeCli.Marked(headers);

        if (Check("the dump marks every name it prints, and prints every name",
                  lines.Count == read.Length + unread.Length, $"{lines.Count} of {headers.Count}"))
        {
            Check("a read name is marked read in the dump",
                  read.All(n => lines.Any(l => l.StartsWith(ProbeCli.ReadMark) && l.Contains(n))),
                  string.Join("; ", read.Where(n => !lines.Any(l => l.StartsWith(ProbeCli.ReadMark) && l.Contains(n)))));
            Check("and an unread one carries the loud mark, which is the whole point",
                  unread.All(n => lines.Any(l => l.StartsWith(ProbeCli.UnreadMark) && l.Contains(n))),
                  string.Join("; ", unread.Where(n => !lines.Any(l => l.StartsWith(ProbeCli.UnreadMark) && l.Contains(n)))));
            Check("the value is still printed verbatim beside the mark",
                  lines.All(l => l.EndsWith(": 0.5") || l.EndsWith(": five_hour")));
        }

        // Over a log rather than one reading: the count in front of the readings is derived from the same
        // set the marks are, or the summary and the lines below it can disagree.
        var log = new List<ProbeEntry> { new(Now, headers) };
        List<string> reported = HeaderProbe.Unread(log);
        Check($"the log's unread set is exactly the names nothing reads ({unread.Length})",
              reported.Count == unread.Length && unread.All(reported.Contains),
              string.Join("; ", reported));
        Check("a log carrying only read names reports none",
              HeaderProbe.Unread(new List<ProbeEntry>
              {
                  new(Now, read.ToDictionary(n => n, _ => "0.5", StringComparer.OrdinalIgnoreCase)),
              }).Count == 0);

        Summary(log, read, unread);
    }

    /// <summary>
    /// T282. The count in front of the readings is a claim <em>about</em> the marks under it — nine of
    /// seventeen reach a field — and T278 left it as the one part of the read-out built inline and written
    /// straight to the console. Every check above it asks the classifier, which is the thing both the
    /// sentence and the marks were derived from, so a summary that recounted on its own line would have
    /// contradicted its own body with all of them passing.
    ///
    /// <para>So what is asserted here is the agreement, not the arithmetic: the numbers the sentence states
    /// are the lengths of the lists printed beneath it, and the block is a value both can be read off. The
    /// <c>--all</c> sentence is the same claim over the union of every profile's names and gets the same
    /// treatment, or the defect simply moves one screen down.</para>
    /// </summary>
    private static void Summary(List<ProbeEntry> log, string[] read, string[] unread)
    {
        HeaderReadership r = HeaderProbe.Readership(log);
        Check($"the readership counts every name on file ({read.Length + unread.Length})",
              r.Total == read.Length + unread.Length, $"{r.Total}");
        Check("read and unread account for all of them, with read derived rather than stored",
              r.Read + r.Unread.Count == r.Total && r.Read == read.Length, $"{r.Read} + {r.Unread.Count}");
        Check("and the unread list is the names nothing reads, not a count of them",
              unread.All(r.Unread.Contains) && r.Unread.Count == unread.Length,
              string.Join("; ", r.Unread));

        List<string> lines = ProbeCli.ReadershipLines(r);
        if (Check("the block leads with the sentence a reader sees first", lines.Count > 0
                  && lines[0].StartsWith("readership — "), lines.Count > 0 ? lines[0] : "<empty>"))
        {
            // The agreement itself: the sentence's own numbers, read back out of it, against the lines it
            // is a summary of. Anything that recounts on one side alone fails here and nowhere else.
            Check("its numbers are the lines beneath it, not a second count",
                  lines[0].Contains($"{r.Read} of {r.Total} ") && lines[0].Contains($"{r.Unread.Count} reach none")
                  && lines.Count(l => l.Contains(ProbeCli.UnreadMark)) == r.Unread.Count,
                  lines[0]);
            Check("every unread name is marked in the body it summarises",
                  unread.All(n => lines.Any(l => l.Contains(ProbeCli.UnreadMark) && l.Contains(n))));
            Check("and a name the parser reads that never arrived is named as the opposite fact",
                  r.NeverSent.All(n => lines.Any(l => l.Contains("read, never sent") && l.Contains(n))));
        }

        // A log carrying every name the parser reads and nothing else: no unread lines, and no prose
        // excusing them, or the block explains an absence it is not reporting.
        var allRead = new List<ProbeEntry>
        {
            new(Now, read.ToDictionary(n => n, _ => "0.5", StringComparer.OrdinalIgnoreCase)),
        };
        List<string> clean = ProbeCli.ReadershipLines(HeaderProbe.Readership(allRead));
        Check("a log with nothing unread says so and stops",
              clean.Count > 0 && clean[0].Contains("0 reach none")
              && !clean.Any(l => l.Contains(ProbeCli.UnreadMark)), string.Join(" | ", clean));
        Check("an empty log yields no block at all — not a heading over nothing",
              ProbeCli.ReadershipLines(HeaderProbe.Readership(new List<ProbeEntry>())).Count == 0);

        // The same sentence over the union of two profiles, which is what --all prints.
        List<HeaderSpread> spread = HeaderProbe.Spread(new List<(string, IReadOnlyList<ProbeEntry>)>
        {
            ("reads", allRead), ("does not", log),
        });
        string union = ProbeCli.SpreadReadership(spread);
        int unreadNames = spread.Count(h => !HeaderProbe.IsRead(h.Name));
        Check("the --all sentence counts the union of every profile's names",
              union.Contains($"{spread.Count - unreadNames} of these") && union.Contains($"{unreadNames} reach none"),
              union);
        Check("and it counts names, not readings — two profiles do not double the total",
              spread.Count == read.Length + unread.Length, $"{spread.Count}");
    }

    // ---------------------------------------------------------------- Block E: the cards' glyphs

    /// <summary>
    /// T227. Every toast draws its emoji as a flat black outline, and the task was to fix that with a font
    /// on the one <c>TextBlock</c> that carries them. <b>Measured, that fix does not exist.</b> Rendering the
    /// six glyphs the cards use under WPF's own text stack and under GDI+, in <c>Segoe UI Emoji</c> itself,
    /// produced no coloured pixel in any of the twelve — and a real <c>--capture-toast unexpected</c> with the
    /// font named came back with the same black popper. WPF draws the font's monochrome base layer because
    /// its text pipeline has no colour-font support at all, and GDI+ has none either; colour would need
    /// Direct2D interop or seven raster assets, which is a non-goal.
    ///
    /// <para><b>What is left is worth keeping, and it is what this checks.</b> Naming the family takes font
    /// <em>linking</em> out of the picture: the run used to resolve against whichever family WPF reached
    /// first from a stack containing none of these codepoints, and now comes from one that carries all of
    /// them. So the property is that every card's glyph is really in the font the card names — a card that
    /// picks a codepoint <c>Segoe UI Emoji</c> lacks would draw a tofu box, which is the same kind of defect
    /// as the black popper and the same kind a capture certifies without complaint.</para>
    ///
    /// <para>Asked of the typeface rather than of pixels, because a missing glyph and a present one both
    /// render ink. The precondition is the other half: the family the glyph used to inherit must still be
    /// missing them, or naming the emoji font is doing nothing and this would pass either way.</para>
    /// </summary>
    private static void ToastGlyphs()
    {
        // From the live notifier's own content, not from a literal here: the reset cards pick their glyph
        // per event kind, and probing one no card uses would answer about the wrong codepoint.
        string[] glyphs = new[] { "7d", "5h" }
            .SelectMany(k => Enum.GetValues<BurnTracker.ResetKind>()
                .Select(kind => TrayContext.ResetToastContent(
                    k, new BurnTracker.ResetEvent(kind, 0.8, 0.0, (long)Now, (long)Now + 3600), (long)Now).emoji))
            .Concat(new[] { "📇", "🧾", "💻", "🚫" })   // the four cards that name theirs at the call site
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (!Check($"the cards' glyphs are reachable ({glyphs.Length} distinct)", glyphs.Length >= 5,
                   string.Join(" ", glyphs)))
            return;

        string[] inherited = glyphs.Where(g => Maps(ToastWindow.BodyFont, g)).ToArray();
        if (!Check("the card's text font still carries none of them — so naming the emoji font does something",
                   inherited.Length == 0,
                   $"{string.Join(" ", inherited)} — already covered, and the fallback was never the issue"))
            return;

        string[] missing = glyphs.Where(g => !Maps(ToastWindow.EmojiFont, g)).ToArray();
        Check($"and the font the card names carries every one of them ({glyphs.Length})", missing.Length == 0,
              $"{string.Join(" ", missing)} — would draw as a tofu box, and a capture would not say so");
    }

    /// <summary>
    /// T290. The transition as a sequence, which is the part nothing could ask before: the predicates under
    /// it were asserted from the day they were written, and the defect sat in neither of them. The notifier
    /// fetched its own seed from a history the same poll had already appended to, so the first reading of a
    /// process was compared with itself — and the assertion that would have caught it is the first one here,
    /// which needs a seed, a reading, and an answer, none of which existed as an input until T290.
    ///
    /// <para>Driven with no tray and no store, because <see cref="ExtraUsageAlarm"/> takes its seed rather
    /// than reading one. That is the fix and the testability in the same move: a type that cannot look up
    /// its own previous reading cannot look up the wrong one.</para>
    /// </summary>
    private static void ExtraAlarm()
    {
        const string one = "account-one";
        const string two = "account-two";
        static UsageSample Reading(double? extra, bool? inUse) =>
            new(Now, 0.5, 0, 0.5, 0, Extra: extra, InUse: inUse);

        // The crossing on the very first poll of a process — a tray started while the account was inside
        // its quota, whose first reading is the one that goes over. Silent before T290.
        var launched = new ExtraUsageAlarm(one, Reading(0.0, false));
        Check("a crossing on the first poll after launch is announced",
              launched.Note(one, 0.0, true));
        Check("and the same spell is not announced again",
              !launched.Note(one, 0.0, true) && !launched.Note(one, 0.02, true));

        // The seed's other job, which T184 wrote it for and which must survive the fix.
        var midSpell = new ExtraUsageAlarm(one, Reading(0.0, true));
        Check("a tray started in the middle of a spell announces nothing",
              !midSpell.Note(one, 0.0, true) && !midSpell.Note(one, 0.03, true));
        Check("but it does announce the next spell, once the account has been seen back inside",
              !midSpell.Note(one, 0.0, false) && midSpell.Note(one, 0.0, true));

        // No history at all: a fresh install, or a profile whose log predates both fields. Absent is not
        // "inside the quota", so the first reading of any kind arms nothing (T179's rule, kept).
        var fresh = new ExtraUsageAlarm(one, null);
        Check("with no seed the first reading announces nothing, whatever it says",
              !fresh.Note(one, 0.42, true));
        Check("and the account still gets its next spell announced",
              !fresh.Note(one, 0.0, false) && fresh.Note(one, 0.0, true));

        // T292: two accounts' readings compare to nothing. The tray rebuilds the alarm when the icon
        // changes hands, and this is what the alarm does when something forgot to — the quiet answer, in
        // both directions, because a false "you have started paying" is the worse of the two failures.
        var quiet = new ExtraUsageAlarm(one, Reading(0.0, false));
        Check("a switch to an account already spending announces nothing — nobody saw it begin",
              !quiet.Note(two, 0.0, true));
        Check("and the incoming account's own crossing is still announced, one poll later",
              !quiet.Note(two, 0.0, false) && quiet.Note(two, 0.0, true));

        var spending = new ExtraUsageAlarm(one, Reading(0.0, true));
        Check("a switch away from a spell does not carry its latch into the new account",
              !spending.Note(two, 0.0, false) && spending.Note(two, 0.0, true));

        // The figure route, which is the only one an account whose utilization climbs would ever have had,
        // and which must not double-announce beside the boolean.
        var climbing = new ExtraUsageAlarm(one, Reading(0.0, null));
        Check("a rise in the figure alone still announces",
              climbing.Note(one, 0.02, null));
        Check("and the boolean arriving after it does not announce a second time",
              !climbing.Note(one, 0.05, true));
    }

    /// <summary>
    /// T277. The one value the complement cannot take. The card's bar renders quota <em>still available</em>,
    /// so it is drawn from <c>1 − figure</c> — and the spell of 2026-08-04 reported the figure as <c>0.0</c>
    /// on every reading, which draws it <b>full</b>: a complete allowance behind a sentence saying the quota
    /// is spent and money is being charged.
    ///
    /// <para>Asked of the arithmetic rather than of the picture, because the card is a window and this file
    /// builds none — and because a full bar is a perfectly well-formed rendering, so a capture certifies it
    /// without complaint, which is exactly how it shipped.</para>
    /// </summary>
    private static void ExtraCardBar()
    {
        Check("a figure the reading carries is drawn as the quota still available",
              TrayContext.ExtraUsageBar(0.06) is { } bar && Math.Abs(bar - 0.94) < 1e-9);
        Check("a measured zero draws no bar at all — 1 − 0 is a full one",
              TrayContext.ExtraUsageBar(0.0) is null);
        Check("and neither does a reading that carries no figure",
              TrayContext.ExtraUsageBar(null) is null);

        // The preview is the only way this card is ever seen, so it has to be the same decision: a row
        // that hand-wrote its own bar is a screenshot that can disagree with what the tray sends.
        Check("both extra-usage previews are in the catalogue, so the bar-less form is checked with the rest",
              new[] { "extra", "extra-bare" }
                  .All(n => ToastPreviews.Catalogue.Any(v => v.Name == n)));
    }

    /// <summary>Whether a family has a real glyph for every codepoint in <paramref name="text"/>. Asked of
    /// the typeface, not of a rendering: a missing glyph draws a box, which is ink like any other.</summary>
    private static bool Maps(System.Windows.Media.FontFamily family, string text)
    {
        foreach (System.Windows.Media.Typeface tf in family.GetTypefaces())
        {
            if (!tf.TryGetGlyphTypeface(out System.Windows.Media.GlyphTypeface? gt)) continue;
            bool all = true;
            for (int i = 0; i < text.Length && all;)
            {
                int cp = char.ConvertToUtf32(text, i);
                i += char.IsSurrogatePair(text, i) ? 2 : 1;
                all = gt.CharacterToGlyphMap.ContainsKey(cp);
            }
            if (all) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- Block N: what "9 projects" counts

    /// <summary>
    /// T225. The System page's project count is presented as a number of directories and was a number of
    /// keys in a file this app does not write. On a real machine the two differed by two: <c>d:/Git/x</c>
    /// and <c>D:/Git/x</c> both recorded, because Windows paths are case-insensitive and Claude Code writes
    /// whatever spelling the shell used.
    ///
    /// <para>Asserted twice over, because the interesting failure is not the arithmetic — it is the one line
    /// in <see cref="ClaudeAccount.Read(string)"/> that decides whether the fold is used at all. So the fold
    /// is checked against the exact shape that was measured, and then a synthetic <c>.claude.json</c> of that
    /// shape is read back through <c>Read</c> itself, in a throwaway tree.</para>
    /// </summary>
    private static void ProjectCount()
    {
        Check("two spellings of one drive letter are one directory",
              ClaudeAccount.CountDirectories(new[] { @"d:\Git\x", @"D:\Git\x" }) == 1);
        Check("and the whole measured shape folds to what the disk holds",
              ClaudeAccount.CountDirectories(new[]
              {
                  "d:/Git/x", "D:/Git/x", "d:/Git/y", "D:/Git/y", "C:/other",
              }) == 3);
        Check("distinct directories are still distinct — the fold is case, not a merge",
              ClaudeAccount.CountDirectories(new[] { @"D:\a", @"D:\b", @"D:\c" }) == 3);
        Check("and an empty map is zero, not one",
              ClaudeAccount.CountDirectories(Array.Empty<string>()) == 0);

        // The other end of the same claim: a file of the measured shape, read the way the page reads one.
        // Five keys, three folders — and the reading has to be the second number.
        Temp(root =>
        {
            File.WriteAllText(Path.Combine(root, ".claude.json"),
                """
                {
                  "installMethod": "native",
                  "projects": {
                    "d:/Git/x": {}, "D:/Git/x": {},
                    "d:/Git/y": {}, "D:/Git/y": {},
                    "C:/other": {}
                  }
                }
                """);
            ClaudeInfo read = ClaudeAccount.Read(root);
            Check("a config file of that shape reads as three projects, not five",
                  read.ProjectCount == 3, $"got {read.ProjectCount}");
        });
    }

    // ---------------------------------------------------------------- Blocks F and G: one number convention

    /// <summary>
    /// T167: the Statistics page states its numbers in one convention, whatever the machine's locale is.
    /// Thirteen formatters ended in <c>Fmt</c> and the method note's five interpolations did not, so on a
    /// pt-BR machine the English popup read <em>"4,7 weeks of local transcripts"</em> eight lines above
    /// <c>≈ 1,319 tok/s</c> and <c>40%</c> — in both verification screenshots of T159 and T163, unseen.
    ///
    /// <para><b>T216 pointed it at the rest of the app.</b> The sweep reached one window while four other
    /// surfaces stated the same kinds of figure, so whichever convention the app had chosen was held up on
    /// one page and nowhere else. <see cref="Nums"/> now carries the rule — numbers invariant, dates in the
    /// display language — and <see cref="Surfaces"/> lists the types that put a number in front of a reader:
    /// every static formatter on each is swept, so a <em>type</em> is what has to be remembered, not a
    /// method. Adding a formatter to a surface already here costs nothing.</para>
    ///
    /// <para>The formatters are <b>derived, never listed</b>, for the same reason the automation-id sweep
    /// derives its pages: a hardcoded list stops covering whatever is written next, which is the defect
    /// the note itself was. Every static method on those types that turns numbers into a string is run
    /// twice, once under each culture, and any that answers differently has read the OS.</para>
    ///
    /// <para>What this cannot reach is an interpolation written <em>inline</em> in a render method rather
    /// than in a formatter — the note's own shape until T167, and the shape every surface T216 converted
    /// was in. Pulling one out into a static formatter is what puts it inside this sweep; leaving it inline
    /// is what keeps it out, which is the cost §XV.2 (T168) is about.</para>
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

            static MethodInfo[] FormattersOf(Type t) => t
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public |
                            BindingFlags.DeclaredOnly)
                .Where(m => m.ReturnType == typeof(string) && !m.Name.Contains('<') &&
                            m.GetParameters().Length > 0 &&
                            m.GetParameters().All(p => Numeric(p.ParameterType) || p.HasDefaultValue))
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToArray();

            // Every formatter, with the surface it belongs to, so a failure names the file to open. Each
            // surface must still have one: a type that stops answering is a sweep that silently shrank.
            var found = Surfaces.ToDictionary(t => t, FormattersOf);
            string[] mute = found.Where(kv => kv.Value.Length == 0)
                                 .Select(kv => kv.Key.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            MethodInfo[] formatters = found.SelectMany(kv => kv.Value).ToArray();

            if (!Check($"every surface that states a number has formatters reachable by reflection " +
                       $"({Surfaces.Length} types, {formatters.Length} formatters)",
                       mute.Length == 0 && formatters.Length >= 20,
                       mute.Length > 0
                           ? $"{string.Join(", ", mute)} — no static formatter left to sweep"
                           : $"found only {formatters.Length}"))
                return;

            // Spread across every branch these have: under a k, under a M, a fraction, a whole, a unix
            // second. A single value would exercise one arm of a ternary and call the rest checked.
            double[] values = { 0, 0.5, 1.5, 4.7, 42, 99.5, 1234.5, 1_500_000, 1_700_000_000 };
            List<string> leaked = new(), threw = new();
            int compared = 0;

            foreach ((Type surface, MethodInfo[] ms) in found.Select(kv => (kv.Key, kv.Value)))
                foreach (MethodInfo m in ms)
                    foreach (double v in values)
                    {
                        object?[] args = m.GetParameters()
                            .Select(p => Numeric(p.ParameterType)
                                ? Convert.ChangeType(v, p.ParameterType)
                                : p.DefaultValue)
                            .ToArray();
                        string where = $"{surface.Name}.{m.Name}";
                        try
                        {
                            System.Globalization.CultureInfo.CurrentCulture =
                                System.Globalization.CultureInfo.InvariantCulture;
                            string a = (string)m.Invoke(null, args)!;
                            System.Globalization.CultureInfo.CurrentCulture = hostile;
                            string b = (string)m.Invoke(null, args)!;
                            compared++;
                            if (a != b) leaked.Add($"{where}({v:0.###}) → \"{a}\" / \"{b}\"");
                        }
                        catch (Exception e) { threw.Add($"{where}({v:0.###}): {(e.InnerException ?? e).Message}"); }
                    }

            System.Globalization.CultureInfo.CurrentCulture =
                System.Globalization.CultureInfo.InvariantCulture;

            // A throw is not a pass. A formatter that blew up on a probe was never compared, and counting
            // it as clean is the same silence as a Skip that hides what it guards.
            Check($"every formatter answered both cultures ({formatters.Length} × {values.Length})",
                  threw.Count == 0, string.Join("; ", threw.Take(4)));
            Check($"no formatter on any surface reads the OS number format ({compared} comparisons)",
                  leaked.Count == 0,
                  leaked.Count == 0 ? "" : $"{leaked.Count} leaked — {string.Join("; ", leaked.Take(6))}");

            // And the helper itself, by name, since everything above only says the surfaces agree with
            // themselves — they would all agree on the wrong convention just as quietly.
            System.Globalization.CultureInfo.CurrentCulture = hostile;
            Check("Nums keeps the decimal point a point", Nums.Of(4.7) == "4.7", Nums.Of(4.7));
            Check("Nums' whole form takes no group separator", Nums.Of(1234, "0") == "1234",
                  Nums.Of(1234, "0"));
            Check("Nums states a share without the space an invariant \"P0\" inserts",
                  Nums.Pct(0.271) == "27%", Nums.Pct(0.271));
            Check("Nums does not clamp a load past its window", Nums.Pct(1.4) == "140%", Nums.Pct(1.4));
            Check("the page's own helper still routes through it", StatisticsPage.Num(4.7) == "4.7",
                  StatisticsPage.Num(4.7));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }

    /// <summary>
    /// The types that put a number in front of a reader (T216). A list, unlike the formatters on them,
    /// because there is no property of a type that says "this one is read by a user" — but it is the
    /// short list, and it is the one thing a new surface has to be added to. The rule they all keep is
    /// <see cref="Nums"/>; dates are outside it and stay in the display language.
    /// </summary>
    private static readonly Type[] Surfaces =
    {
        typeof(StatisticsPage),   // the charts, the pace report, the method note
        typeof(ContextPage),      // the gauge caption, the simulation banner, the scan footer
        typeof(ContextText),      // file sizes, and the tray's context nudge
        typeof(SettingsPage),     // the poll cadence, its cost estimate, the extra-usage share
        typeof(TokenEstimate),    // "≈4.9k", on every one of the above
        typeof(SessionListRow),   // a conversation's length, in the Sessions list
        typeof(Nums),             // the rule itself
    };

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

        // `Temp` already handed this root back under the filesystem's own spelling, which is what stopped
        // the 8.3 alias skipping both of these forever (T169). Assert the resolution rather than trust it:
        // case is the same mechanism as an 8.3 alias — the name you ask by is not the name on disk — and
        // unlike an alias it can be produced on any machine, including one with 8.3 creation disabled.
        Directory.CreateDirectory(Path.Combine(root, "MixedCase"));
        string resolved = LongPath(Path.Combine(root, "mixedcase"));
        Check("a path resolves to the spelling the filesystem carries, not the one it was asked by",
              Path.GetFileName(resolved) == "MixedCase", resolved);

        // The temp path itself still has to survive the round trip, or nothing below it can be probed: a
        // character that is neither alphanumeric nor a separator — a space, a dot, an alias that resolved
        // to nothing — encodes to a `-` that matches no directory on the way down. The guard stays for
        // that genuinely unrepresentable case, because honesty about what a check covers is the point.
        // Tested on the path rather than by probing it, so a broken probe fails these checks instead of
        // skipping them.
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
    /// probe starting at the drive can rebuild it. The 8.3 alias that used to fail this on every CI run is
    /// resolved away by <see cref="LongPath"/> before the check (T169); what is left is a temp path with a
    /// space or a dot in it, which is unrepresentable however it is spelled.</summary>
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

    // ---------------------------------------------------------------- Block K: where a fan-out files

    /// <summary>
    /// T324: a tree with the two shapes a fan-out writes — <c>subagents/agent-&lt;id&gt;.jsonl</c> and
    /// <c>subagents/workflows/wf_&lt;id&gt;/agent-&lt;id&gt;.jsonl</c> — beside a plain session transcript.
    ///
    /// <para>Every tail fixture until now was one file in one folder, which is exactly the layout the
    /// old "containing directory is the project" rule was right for; that is why three months of real
    /// use never showed a workflow's tokens being drawn as a project of their own, named after the
    /// <c>wf_</c> folder. The turns carry distinct input counts so an assertion can say <em>which</em>
    /// file a sample came from.</para>
    /// </summary>
    private static void FanOut(string root)
    {
        string projects = Path.Combine(root, "projects");
        string slug = Path.Combine(projects, "d--selftest");
        string session = Path.Combine(slug, "sess-1");
        string agents = Path.Combine(session, "subagents");
        string flow = Path.Combine(agents, "workflows", "wf_475bc61a-315");
        Directory.CreateDirectory(flow);

        DateTime now = DateTime.Now;
        File.WriteAllText(Path.Combine(slug, "sess-1.jsonl"), Turn(now, "main", 1) + "\n");
        File.WriteAllText(Path.Combine(agents, "agent-aaa.jsonl"), Turn(now, "sub", 2) + "\n");
        File.WriteAllText(Path.Combine(flow, "agent-bbb.jsonl"), Turn(now, "wf", 3) + "\n");

        using var tail = new TranscriptTail(projects);
        var seen = new List<TailSample>();
        tail.Appended += batch => { lock (seen) seen.AddRange(batch); };
        tail.Start();

        if (!Check("all three transcripts of a fan-out are read", Wait(() => Count(seen) >= 3),
                   $"{Count(seen)} of 3")) return;

        TailSample[] got;
        lock (seen) got = seen.ToArray();
        TailSample Sample(int input) => got.First(s => s.Bits.Input == input);

        Check("a workflow agent's tokens are not a project of their own",
              got.All(s => s.Project == "d--selftest"),
              string.Join(", ", got.Select(s => s.Project).Distinct()));
        Check("and the display name never becomes the digits after the workflow folder's last dash",
              got.All(s => s.Name != ProjectSlug.Tail("wf_475bc61a-315")),
              ProjectSlug.Tail("wf_475bc61a-315"));
        Check("every agent files under the session that spawned it, not under its own file name",
              got.All(s => s.Session == "sess-1"),
              string.Join(", ", got.Select(s => s.Session).Distinct()));
        Check("a plain session transcript carries no workflow and no agent",
              Sample(1) is { Workflow: null, Agent: null });
        Check("a subagent carries its own id and no workflow",
              Sample(2) is { Workflow: null, Agent: "agent-aaa" },
              $"{Sample(2).Workflow ?? "-"} / {Sample(2).Agent ?? "-"}");
        Check("a workflow's agent carries both ids",
              Sample(3) is { Workflow: "wf_475bc61a-315", Agent: "agent-bbb" },
              $"{Sample(3).Workflow ?? "-"} / {Sample(3).Agent ?? "-"}");
    }

    // ---------------------------------------------------- Block AK: one row per conversation (T327)

    /// <summary>
    /// T327: the four properties a session row stands or falls on — a fan-out folds into the session
    /// that spawned it, one response is one call however many lines it wrote, the models are all of
    /// them, and the per-file cache returns what the scan did without re-reading anything.
    ///
    /// <para>The cache is driven rather than skipped, which is why <see cref="SessionIndex.Load"/>
    /// takes a cache path: a fixture that can only turn the cache off leaves the one thing that can be
    /// silently wrong — a stale row served as a fresh one — unfalsifiable.</para>
    /// </summary>
    private static void Sessions(string root)
    {
        string projects = Path.Combine(root, "projects");
        string slug = Path.Combine(projects, "d--selftest");
        string flow = Path.Combine(slug, "sess-1", "subagents", "workflows", "wf_a1b2-315");
        Directory.CreateDirectory(flow);
        string cache = Path.Combine(root, "session-index.json");

        DateTime now = DateTime.Now;
        // The conversation's own transcript: two turns, and the second written twice — one response,
        // two content blocks, the same usage on each, which is what Claude Code actually writes.
        string twice = Turn(now.AddMinutes(-30), "req-2", 500);
        File.WriteAllText(Path.Combine(slug, "sess-1.jsonl"),
                          // Three `user` lines before the first turn, in the order a real transcript
                          // has them: the IDE announcing a file, a system reminder, and only then the
                          // person. The first two are the harness writing about itself, and taking
                          // either as the prompt is what the first capture of that column did.
                          Prompt("<ide_opened_file>The user opened the file d:\\x\\y.cs") + "\n" +
                          Prompt("<system-reminder>a reminder</system-reminder>") + "\n" +
                          Prompt("  the real\n  question  ") + "\n" +
                          Prompt("a later message that is not the opening one") + "\n" +
                          Turn(now.AddHours(-1), "req-1", 100) + "\n" + twice + "\n" + twice + "\n");
        // Its fan-out, in a different file, under a different model.
        File.WriteAllText(Path.Combine(flow, "agent-aaa.jsonl"),
                          Turn(now, "req-3", 900).Replace("claude-selftest", "claude-selftest-mini") + "\n");

        IReadOnlyList<SessionRow> rows = SessionIndex.Load(out SessionScanStats cold, projectsDir: projects,
                                                           cacheFile: cache);
        if (!Check("three transcripts of one conversation produce one row", rows.Count == 1,
                   $"{rows.Count} rows from {cold.Files} files")) return;

        SessionRow r = rows[0];
        Check("a fan-out's cost is inside the session that spawned it, not beside it",
              r.Bits.Input == 100 + 500 + 900, $"{r.Bits.Input} input tokens, expected 1500");
        Check("and the row says how many transcripts the fan-out wrote", r.Agents == 1, $"{r.Agents}");
        Check("one response is one call, however many lines carried it",
              r.Calls == 3, $"{r.Calls} calls, expected 3");
        Check("the row spans first turn to last", Math.Abs(r.Seconds - 3600) <= 1, $"{r.Seconds}s");
        Check("every model that answered is named, not just the first",
              r.Models.Length == 2 && r.Models.Contains("claude-selftest") &&
              r.Models.Contains("claude-selftest-mini"), string.Join(",", r.Models));
        Check("the project is the slug, and its name comes from a cwd the transcript carried",
              r.Project == "d--selftest" && r.Name.Length > 0, $"{r.Project} / {r.Name}");
        Check("the opening prompt is the first line a person actually typed",
              r.Prompt == "the real question", r.Prompt);

        // The cache: same answer, nothing re-read. A row served from the cache that disagrees with the
        // row the scan produced is the failure this exists for.
        IReadOnlyList<SessionRow> warm = SessionIndex.Load(out SessionScanStats hot, projectsDir: projects,
                                                            cacheFile: cache);
        Check("a warm pass opens no transcript at all", hot.Read == 0 && hot.Files == cold.Files,
              $"{hot.Read} of {hot.Files} re-read");
        // Field by field, not record equality: the row carries a string[], and two arrays holding the
        // same names are not equal — a synthesized Equals would compare them by reference and this
        // would fail on a cache that is perfectly correct.
        Check("and answers exactly what the cold pass did",
              warm.Count == 1 && warm[0].Session == r.Session && warm[0].Project == r.Project &&
              warm[0].Name == r.Name && warm[0].Bits == r.Bits && warm[0].Calls == r.Calls &&
              warm[0].Agents == r.Agents && warm[0].FirstUnix == r.FirstUnix &&
              warm[0].LastUnix == r.LastUnix && warm[0].Models.SequenceEqual(r.Models));

        // Rebuild is the escape hatch that makes the two above falsifiable rather than circular.
        SessionIndex.Load(out SessionScanStats forced, projectsDir: projects,
                          mode: ActivityProfile.SweepCacheMode.Rebuild, cacheFile: cache);
        Check("--refresh re-reads every transcript past the cache",
              forced.Read == forced.Files, $"{forced.Read} of {forced.Files}");
    }

    /// <summary>One <c>user</c> line carrying one text block — the shape §I.1's amended exception reads,
    /// and the only line type in this file that is not a turn.</summary>
    private static string Prompt(string text)
        => "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":" +
           System.Text.Json.JsonSerializer.Serialize(text) + "}]}}";

    /// <summary>
    /// T334: the three rules the opening prompt stands on, none of which are about tokens — the harness's
    /// own <c>user</c> lines are declined, a slash command is reassembled from the tags that carry it,
    /// and the cap is applied before anything is stored. Pure, so it asserts the reader and not a scan.
    /// </summary>
    private static void OpeningPrompt(string root)
    {
        string projects = Path.Combine(root, "projects");
        string slug = Path.Combine(projects, "d--selftest");
        Directory.CreateDirectory(slug);
        DateTime now = DateTime.Now;

        string Only(string first)
        {
            string file = Path.Combine(slug, "sess-p.jsonl");
            File.WriteAllText(file, first + "\n" + Turn(now, "req", 10) + "\n");
            IReadOnlyList<SessionRow> rows = SessionIndex.Load(projectsDir: projects);
            return rows.Count == 1 ? rows[0].Prompt : "<" + rows.Count + " rows>";
        }

        Check("a slash command is what the person typed, name and arguments together",
              Only(Prompt("<command-message>loop</command-message>\n<command-name>/loop</command-name>\n" +
                          "<command-args>1m do the thing</command-args>")) == "/loop 1m do the thing",
              Only(Prompt("<command-message>loop</command-message>\n<command-name>/loop</command-name>\n" +
                          "<command-args>1m do the thing</command-args>")));
        Check("a command with no arguments is still the command",
              Only(Prompt("<command-name>/status</command-name>")) == "/status");
        Check("a tool result is never the opening prompt",
              Only("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":" +
                   "[{\"type\":\"tool_result\",\"content\":\"ok\"}]}}") == "");
        Check("nor is a subagent's own prompt",
              Only("{\"type\":\"user\",\"isSidechain\":true,\"message\":{\"role\":\"user\"," +
                   "\"content\":[{\"type\":\"text\",\"text\":\"agent brief\"}]}}") == "");

        // The cap is the whole safety margin, and it is applied before the value is stored — so this
        // asserts the *stored* length, not a display that could be trimming a longer string.
        string huge = Only(Prompt(new string('x', SessionIndex.PromptChars * 3)));
        Check($"a long prompt is cut to {SessionIndex.PromptChars} characters, with an ellipsis saying so",
              huge.Length == SessionIndex.PromptChars + 1 && huge.EndsWith('…'), $"{huge.Length} chars");

        // ---- the title (T336), which is the half of the exception that is preferred ----

        string Titled(params string[] lines)
        {
            File.WriteAllText(Path.Combine(slug, "sess-p.jsonl"),
                              string.Join("\n", lines) + "\n" + Turn(now, "req", 10) + "\n");
            IReadOnlyList<SessionRow> rows = SessionIndex.Load(projectsDir: projects);
            return rows.Count == 1 ? rows[0].Title : "<" + rows.Count + " rows>";
        }

        Check("a conversation is called by the title Claude Code generated for it",
              Titled(Title("Ship the sessions pane")) == "Ship the sessions pane");
        // Measured before it was assumed: 322 of the 529 titled transcripts on this machine had the
        // title rewritten mid-conversation, so keeping the first would label a third of them by what
        // they started as.
        Check("and by the LAST one, because a rewritten title is the corrected one",
              Titled(Title("What it started as"), Prompt("hello"), Title("What it turned out to be"))
              == "What it turned out to be");
        Check("a conversation with no title has none, rather than borrowing a prompt here",
              Titled(Prompt("hello")) == "");
        string bigTitle = Titled(Title(new string('y', SessionIndex.PromptChars * 2)));
        Check("and a title is capped exactly like a prompt is",
              bigTitle.Length == SessionIndex.PromptChars + 1 && bigTitle.EndsWith('…'),
              $"{bigTitle.Length} chars");
    }

    /// <summary>The line Claude Code writes when it names a conversation. Its own type, which is why
    /// T334's search for <c>"type":"summary"</c> found nothing and concluded there was no title.</summary>
    private static string Title(string text)
        => "{\"type\":\"ai-title\",\"sessionId\":\"sess-p\",\"aiTitle\":" +
           System.Text.Json.JsonSerializer.Serialize(text) + "}";

    /// <summary>
    /// T329: a session cut into the tasks that produced it, with the fan-out hanging under the right
    /// one. Four boundaries in one transcript, in the order a real one has them — turns that predate
    /// any ask, a slash command, a typed prompt, and a message queued while a turn was running, which
    /// is not a <c>user</c> line at all and is where every mid-turn ask actually lives.
    /// </summary>
    private static void Tasks(string root)
    {
        string projects = Path.Combine(root, "projects");
        string slug = Path.Combine(projects, "d--selftest");
        string flow = Path.Combine(slug, "sess-t", "subagents", "workflows", "wf_z-1");
        Directory.CreateDirectory(flow);

        DateTime t0 = DateTime.Now.AddHours(-2);
        File.WriteAllText(Path.Combine(slug, "sess-t.jsonl"), string.Join("\n", new[]
        {
            // Inherited: a turn before the first ask, which is what a resumed conversation carries.
            Turn(t0, "req-0", 7),
            Prompt("<command-message>loop</command-message>\n<command-name>/loop</command-name>\n" +
                   "<command-args>do the thing</command-args>"),
            Turn(t0.AddMinutes(10), "req-1", 100),
            Turn(t0.AddMinutes(20), "req-2", 200),
            Prompt("a second ask, typed"),
            Turn(t0.AddMinutes(40), "req-3", 400),
            Queued("a third ask, queued mid-turn"),
            Turn(t0.AddMinutes(50), "req-4", 500),
        }) + "\n");
        // The fan-out, stamped inside the first task's span, so attaching by clock has a right answer.
        File.WriteAllText(Path.Combine(flow, "agent-q.jsonl"), Turn(t0.AddMinutes(15), "req-a", 900) + "\n");

        IReadOnlyList<SessionRow> rows = SessionIndex.Load(projectsDir: projects);
        if (!Check("the session the tree is walked from is found", rows.Count == 1, $"{rows.Count} rows")) return;

        IReadOnlyList<TaskNode> tasks = SessionTasks.For(rows[0], projectsDir: projects);
        if (!Check("a transcript with three asks cuts into four tasks", tasks.Count == 4,
                   string.Join(", ", tasks.Select(t => $"{t.Kind}:{t.Calls}")))) return;

        Check("turns that predate the first ask are their own node, not the first task's",
              tasks[0].Kind == TaskKind.Continuation && tasks[0].Own.Input == 7,
              $"{tasks[0].Kind} with {tasks[0].Own.Input} input");
        Check("a slash command is a task, named by the command and not by its arguments",
              tasks[1].Kind == TaskKind.Command && tasks[1].Label == "/loop", tasks[1].Label);
        Check("and its arguments are still readable, where the amendment allows them",
              tasks[1].Prompt == "/loop do the thing", tasks[1].Prompt);
        Check("a typed prompt is named by its length and never by its text",
              tasks[2].Kind == TaskKind.Prompt && tasks[2].Prompt.Length == 0 &&
              tasks[2].Chars == "a second ask, typed".Length, $"{tasks[2].Chars} chars, \"{tasks[2].Prompt}\"");
        Check("a message queued mid-turn starts a task like any other ask",
              tasks[3].Kind == TaskKind.Prompt && tasks[3].Own.Input == 500,
              $"{tasks[3].Kind} with {tasks[3].Own.Input} input");

        // The reading the tree exists for: own beside subtree, on the task that actually spawned it.
        TaskNode command = tasks[1];
        Check("the fan-out hangs under the task that was running when it started",
              command.Children.Count == 1 && command.Children[0].Kind == TaskKind.Workflow,
              $"{command.Children.Count} children");
        Check("under its workflow, which has no turns of its own",
              command.Children[0].Own.Total == 0 && command.Children[0].Children.Count == 1);
        Check("so the task's own cost and its subtree's differ by exactly the agent",
              command.Own.Input == 300 && command.Subtree.Input == 300 + 900,
              $"own {command.Own.Input}, tree {command.Subtree.Input}");
    }

    /// <summary>A message typed while a turn was running: an <c>attachment</c>, not a <c>user</c> line.</summary>
    private static string Queued(string text)
        => "{\"type\":\"attachment\",\"attachment\":{\"type\":\"queued_command\",\"prompt\":" +
           "[{\"type\":\"text\",\"text\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}]}}";

    /// <summary>
    /// T327: the path derivation has to survive the file arriving by a route the caller did not walk.
    /// A profile whose <c>projects\</c> is one junction per repo — the dev machine's is — hands
    /// <see cref="SafeWalk"/> a reparse point, which it resolves, so every transcript comes back living
    /// under a root nobody passed. Pure, so it needs no junction on disk to assert.
    /// </summary>
    private static void Anchoring()
    {
        const string walked = @"C:\u\.claude-pessoal\projects";
        const string real = @"C:\u\.claude\projects\d--slug\sess-1\subagents\workflows\wf_x-9\agent-a.jsonl";

        TranscriptTail.Locate(walked, real, out string p, out string s, out string? wf, out string? ag);
        Check("a resolved junction target still lands in the right project",
              p == "d--slug", p);
        Check("and under the session that spawned it", s == "sess-1", s);
        Check("with its workflow and agent ids intact", wf == "wf_x-9" && ag == "agent-a",
              $"{wf ?? "-"} / {ag ?? "-"}");

        // The re-anchor is a fallback, not the rule: where the walked root does contain the file, that
        // root is the answer even if a directory of the same name appears deeper in the path.
        TranscriptTail.Locate(@"C:\u\.claude\projects", @"C:\u\.claude\projects\projects\sess.jsonl",
                              out string p2, out string s2, out _, out _);
        Check("a root that does contain the file is used as given",
              p2 == "projects" && s2 == "sess", $"{p2} / {s2}");
    }

    // ------------------------------------------------------- Block K: what "sessions active" counts

    /// <summary>
    /// T326: the three readings the headline's <c>N sessions active</c> has to get right at once — a
    /// fan-out is one conversation, a tab that has written something but produced no assistant turn is
    /// working, and a transcript nobody has touched for an hour is not.
    ///
    /// <para>The middle one is why the count is over writes rather than over reported turns, and the
    /// last one is why the window was not simply widened until the middle one looked right: that repair
    /// counts a terminal left open all week, which is what the 120 seconds exists to exclude.</para>
    /// </summary>
    private static void Active(string root)
    {
        string projects = Path.Combine(root, "projects");
        string slug = Path.Combine(projects, "d--selftest");
        string flow = Path.Combine(slug, "sess-1", "subagents", "workflows", "wf_a1b2-315");
        Directory.CreateDirectory(flow);

        DateTime now = DateTime.Now;
        File.WriteAllText(Path.Combine(slug, "sess-1.jsonl"), Turn(now, "main", 1) + "\n");
        File.WriteAllText(Path.Combine(flow, "agent-aaa.jsonl"), Turn(now, "wf-a", 2) + "\n");
        File.WriteAllText(Path.Combine(flow, "agent-bbb.jsonl"), Turn(now, "wf-b", 3) + "\n");

        // The waiting tab: a line the sample parser declines, so this session is live on the disk and
        // invisible to any reading built on reported turns. Freshly written, like the others.
        File.WriteAllText(Path.Combine(slug, "sess-2.jsonl"),
                          "{\"type\":\"user\",\"timestamp\":\"" +
                          now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ",
                              System.Globalization.CultureInfo.InvariantCulture) + "\"}\n");

        // The terminal left open: a real transcript with a real turn, untouched for an hour.
        string stale = Path.Combine(slug, "sess-3.jsonl");
        File.WriteAllText(stale, Turn(now.AddHours(-1), "old", 4) + "\n");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-1));

        using var tail = new TranscriptTail(projects);
        var rate = new LiveRate(tail);
        tail.Start();

        if (!Check("the sweep reaches every transcript in the tree",
                   Wait(() => rate.ActiveSessions >= 2), $"{rate.ActiveSessions} active")) return;

        Check("a workflow's agents count as the one conversation that spawned them",
              rate.ActiveSessions == 2, $"{rate.ActiveSessions} active, expected 2");
        Check("a session that has written but not answered still counts as working",
              tail.SessionsWrittenSince(Now - LiveRate.ActiveSeconds) == 2);
        // Not "it falls outside the 120s": a transcript past the sweep's freshness floor is never
        // opened and never remembered, so no window a caller widens to can bring it back.
        Check("and a terminal left open for an hour is not in the reading at any width",
              tail.SessionsWrittenSince(Now - 7200) == 2,
              $"{tail.SessionsWrittenSince(Now - 7200)} within two hours, expected 2");
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
    /// <param name="column">Write T287's over-quota column at all. Off by default, which is what a day
    /// folded before it existed looks like — the state the ghost must not read as "stayed inside".</param>
    /// <param name="overFrom">First hour of each day the account was past its included quota, or -1 for
    /// none. Inclusive, with <paramref name="overTo"/>, and only ever read when the column is written.</param>
    private static List<HourlyDay> FoldedWeek(double coverage, double perHour,
                                              bool column = false, int overFrom = -1, int overTo = -1)
    {
        var days = new List<HourlyDay>();
        for (int d = 8; d >= 0; d--)
        {
            DateTime date = DateTime.Today.AddDays(-d);
            var spend = new double[24];
            var count = new int[24];
            bool[]? over = column ? new bool[24] : null;
            for (int h = 0; h < 24; h++)
            {
                if (h >= 24 * coverage) continue;
                count[h] = 3;
                spend[h] = perHour;
                if (over is not null && overFrom >= 0 && h >= overFrom && h <= overTo) over[h] = true;
            }
            days.Add(new HourlyDay(date.Year * 10000 + date.Month * 100 + date.Day, spend, count, over));
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
            // Only when the fixture asked for it: a day with no `Over` is a day folded before T287's
            // column existed, and writing an all-zero one would erase the very distinction it tests.
            if (d.Over is { } over)
            {
                sb.Append("],\"x\":[");
                for (int h = 0; h < 24; h++) sb.Append($"{(h > 0 ? "," : "")}{(over[h] ? 1 : 0)}");
            }
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
        // Spelled the way the filesystem spells it rather than the way `%TEMP%` happens to (T169).
        root = LongPath(root);
        try { body(root); }
        catch (Exception e) { Fail("the section threw", e.Message); }
        finally { Remove(root); }
    }

    /// <summary>
    /// The same directory, segment by segment under the name the filesystem itself carries (T169).
    /// </summary>
    /// <remarks>
    /// A Windows CI runner's profile directory is an 8.3 alias — <c>RUNNER~1</c> — and the slug encoding
    /// maps that <c>~</c> to a <c>-</c> that matches no directory on the way down, so <see cref="Slug"/>'s
    /// two <c>TryProbe</c> assertions skipped on <em>every single run</em> there: the app's lossiest
    /// function, covered by nothing, reported as two green lines. Resolved here rather than documented,
    /// because a skip that fires every time is a check that does not exist.
    /// <para>What lets the alias through is that <see cref="Path.Combine(string, string)"/> is string
    /// concatenation: <c>%TEMP%</c> arrives already spelled <c>RUNNER~1</c> and nothing normalizes the
    /// result before it is handed to a section.</para>
    /// <para>Each segment is then looked up in its parent, because matching an 8.3 alias returns the entry
    /// under its real name. <b>Measured, against §XV.3's own premise:</b> .NET's path normalization
    /// <em>does</em> expand a short name — <c>Path.GetFullPath("C:\PROGRA~1")</c> answers
    /// <c>C:\Program Files</c> — so the design's "<c>FullName</c> does not expand 8.3" is not true on this
    /// runtime, and <c>GetFullPath</c> alone would have closed the skip. The walk stays because that
    /// expansion is an undocumented step conditional on the path containing a <c>~</c>, and because it
    /// leaves case alone; asking the filesystem depends on neither.</para>
    /// </remarks>
    private static string LongPath(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full) ?? "";
            if (root.Length == 0) return full;

            string rest = full[root.Length..].Trim('\\', '/');
            if (rest.Length == 0) return full;

            string built = root;
            foreach (string segment in rest.Split('\\', '/'))
            {
                FileSystemInfo? real = new DirectoryInfo(built).EnumerateFileSystemInfos(segment).FirstOrDefault();
                built = Path.Combine(built, real?.Name ?? segment);
            }
            return built;
        }
        catch { return path; }      // an unreadable ancestor is not this helper's problem to report
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
        Skipped.Add((name, why));
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
