namespace ClaudeTray;

/// <summary>The `--activity` family: the weekly activity profile as a 24x7 grid. Split out of `Program.cs` by T132 —
/// moved verbatim.</summary>
internal static class ActivityCli
{
    // Headless view of ActivityProfile: the 168-bucket week the projection will be shaped by (T87),
    // printed as a shade grid so a wrong grid is visible at a glance rather than only as a wrong
    // marker on a chart. Flags: `--refresh` to force a rescan past the daily cache *and* past T92's
    // per-file sweep cache (every transcript re-read, which is the only way to falsify that cache),
    // `--numbers` for raw percentages instead of shades, `--root <dir>` to read a stand-in for
    // ~/.claude.
    internal static void PrintActivity(string[] flags)
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
            List<UsageSample> raw = UsageHistory.Load(ProfileStore.Monitored, 0);
            int before = HourlyUsage.Load(ProfileStore.Monitored).Count;
            HourlyUsage.Fold(ProfileStore.Monitored, raw, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            int after = HourlyUsage.Load(ProfileStore.Monitored).Count;
            Console.WriteLine($"folded {raw.Count:N0} readings — {after} days in the store (+{after - before})");
            Console.WriteLine();
        }

        ActivityProfile prof = ActivityProfile.Load(nowUtc, flags.Contains("--refresh"), root);
        if (prof.Error != null) { Console.WriteLine("error: " + prof.Error); return; }

        string source = prof.FromCache
            ? $"cached, built {prof.ComputedUtc.ToLocalTime():yyyy-MM-dd HH:mm}"
            : $"fresh scan, {prof.ElapsedMs:0}ms";
        Console.WriteLine($"Activity profile — {prof.CoverageWeeks:0.0} weeks of coverage, " +
                          $"{prof.Samples:N0} assistant lines ({source})");
        if (root == null) Console.WriteLine("cache: " + ActivityProfile.CachePath(ProfileStore.Monitored));
        // What the sweep actually cost. Printing "read" beside "in window" is what makes the per-file
        // cache falsifiable: warm it should be a handful of files, and `--refresh` must read them all.
        if (!prof.FromCache)
            Console.WriteLine($"sweep: {prof.FilesSeen:N0} transcripts in window, {prof.FilesRead:N0} read " +
                              $"({prof.BytesRead / 1048576.0:0.#} MB), {prof.ElapsedMs:0}ms" +
                              (root == null ? "" : " (fixture root — sweep cache bypassed)"));
        Console.WriteLine(prof.Confident
            ? $"confidence: usable — {prof.CoverageWeeks:0.0} weeks ≥ {ActivityProfile.ConfidentWeeks:0.0}, " +
              "the projection may follow this shape"
            : $"confidence: thin — {prof.CoverageWeeks:0.0} weeks < {ActivityProfile.ConfidentWeeks:0.0}, " +
              "the projection stays a straight line");
        Console.WriteLine();

        if (prof.Samples == 0) { Console.WriteLine("no activity in the last 12 weeks — nothing to shape"); return; }

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
        var (measured, weeks, days) = HourlyUsage.MeasuredProfile(ProfileStore.Monitored, nowLocal);
        Console.WriteLine($"Measured profile — {days} folded days, {weeks:0.0} weeks");
        Console.WriteLine("store: " + HourlyUsage.FilePath(ProfileStore.Monitored));

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
}
