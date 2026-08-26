using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeTray;

/// <summary>
/// Part of <see cref="SelfTestCli"/> — the arithmetic, the stores and the transcripts they read.
///
/// <para>One class in several files, on this repository's own rule for a type with many
/// independent surfaces (T133–T134, applied here by T381): the suite reached 9,330 lines and
/// 76 sections in one file, and adding a section meant scrolling past the whole of it to find
/// whether the helper you wanted already existed. <c>Run</c> keeps the ordered list of
/// sections — it is the table of contents, and its order is load-bearing — along with
/// <c>Check</c>, <c>Skip</c>, <c>Temp</c>, <c>CodeOf</c>, <c>Repo</c> and the counters, which
/// are the suite's own vocabulary.</para>
/// </summary>
internal static partial class SelfTestCli
{
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
        rate.Add(new[] { new TailSample(t0, new TokenBits(60_000, 0, 0, 0, 0, 0), "slug", "session", "slug") });
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
        // T301: except the one variant built to disagree. A preview is worth exactly what it actually
        // renders, so the state `--stats ghost-pieces` announces is asserted here rather than trusted —
        // the shape T186 is about, one level up: a row that quietly stopped producing its own state would
        // still print its description and still capture a picture of something else.
        HourlyUsage.GhostWeek pieces =
            HourlyUsage.Demo(DateTime.Today.AddDays(-7), 7 * 86400, 0.5, 0.80, inPieces: true);
        Check("the ghost-pieces preview really does draw a week seen in pieces", pieces.ShadedAboveCurve);
        Check("with hours marked over", pieces.OverSpans.Count > 0, $"{pieces.OverSpans.Count} spans");
        Check("and a line that stops short of the ceiling, or there is nothing to disagree with",
              pieces.Total < 1 - 0.005, $"total {pieces.Total:0.###}");

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
            new() { (start + 0.1 * window, new TokenBits(1000, 0, 0, 0, 0, 0)),
                    (start + 0.3 * window, new TokenBits(3000, 0, 0, 0, 0, 0)) },
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

        // T297, the same question about a different single-writer rule, and asked the same way. The folded
        // store's line format had a second writer inside `--selftest` itself, kept in step by hand — and it
        // was not: T287 added the `x` column to the real one and the fixture went on writing three, so three
        // checks quietly described days folded before the column existed and none of them failed. Counting
        // the composer rather than parsing it: the `"d":` key opens every line, and exactly one place in the
        // repository may write it.
        Repo("the folded store's line is composed in one place and nowhere else", root =>
        {
            var writers = new List<string>();
            foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs",
                                                             SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string t = lines[i].TrimStart();
                    if (t.StartsWith("//") || t.StartsWith("///")) continue;
                    // Appending it is writing it. Reading a line back to assert what is in it — which the
                    // checks above do, by `Contains` — is not, and is the distinction this looks for.
                    if (t.Contains("\\\"d\\\":", StringComparison.Ordinal)
                        && t.Contains("Append", StringComparison.Ordinal))
                        writers.Add($"{Path.GetFileName(path)}:{i + 1}");
                }
            }
            Check("exactly one place appends the day key",
                  writers.Count == 1 && writers[0].StartsWith("HourlyUsage.cs", StringComparison.Ordinal),
                  $"{(writers.Count == 0 ? "(none)" : string.Join(", ", writers))} — a second writer of this " +
                  "format is a fixture free to describe a store the fold cannot produce, and it drifts one " +
                  "way: the copy loses the column the real one gains (T287, T297)");
        }, "src/Usage/HourlyUsage.cs");

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

    /// <summary>The states an entry of one verdict can be found in. A verdict that links has four; the two
    /// that refuse have only what their own reading can say.</summary>
    private static string[] States(ProfileLink.Verdict v) => v switch
    {
        ProfileLink.Verdict.Merge or ProfileLink.Verdict.Adopt =>
            new[] { "acting", "already-linked", "absent-primary", "link-only" },
        ProfileLink.Verdict.Withheld => new[] { "same", "granting", "unreadable" },
        _ => new[] { "explained" },
    };

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
}
