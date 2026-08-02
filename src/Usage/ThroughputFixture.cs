namespace ClaudeTray;

/// <summary>Everything the Throughput tab needs to draw a reproducible three minutes: the two charts'
/// series, the raw per-second buckets the hover reports beside them, and the readings that go around
/// them. One record so a caller cannot render half a fixture.</summary>
/// <param name="PinSecondsBack">The sample the readout is held open on. A captured window has no
/// pointer in it, so the interaction has to be posed.</param>
internal readonly record struct ThroughputDemo(
    double[][] TypeRates, long[][] TypeTokens, ProjectSlice[] Projects,
    double HeadRate, long HeadTokens, int Sessions,
    double CacheReadPerSecond, int PinSecondsBack);

/// <summary>
/// The deterministic three minutes behind <c>--stats live</c> and <c>--capture-stats … live</c>.
///
/// <para>It exists for the same reason <see cref="ContextFixture"/> does: the real charts show
/// whatever happens to be generating, which cannot be captured twice the same way, and the real
/// project names are client names. Shaped by hand and with no randomness in it, so the published
/// image is stable across runs — and shaped through the <em>real</em> filter
/// (<see cref="LiveRate.RateFrom"/>), so what is published is what the app would draw, not a curve
/// drawn to look like one.</para>
///
/// <para>Split out of <c>StatisticsPage</c>'s code-behind by T108, where it had accreted next to
/// the rendering it feeds while three tasks came to depend on it. No behaviour change: the same
/// numbers, in the file where this repo keeps stand-in data for a subsystem.</para>
/// </summary>
internal static class ThroughputFixture
{
    // Four repos plus a residual, so the per-project chart is drawn with the crossings that make the
    // fixed slots (T114) worth having. The phase staggers who is generating when. The names carry
    // their parent segment because the real ones do (T154) — a fixture that publishes a shorter label
    // than the app draws would understate what the legend has to fit.
    private static readonly (string Slug, string Name, int Phase, double Weight)[] Repos =
    {
        ("d--Git-acme-web-console", "acme/web-console", 0, 1.00),
        ("d--Git-acme-billing-api", "acme/billing-api", 5, 0.62),
        ("c--Users-dev-notes",      "dev/notes",        2, 0.34),
        ("d--Git-acme-infra",       "acme/infra",       8, 0.21),
    };

    /// <summary>Long enough for both filters to be warm at the left edge: every drawn point needs the
    /// 60s of buckets behind it, and the attack lag needs its own run-up before the first drawn one
    /// (T117) — a fixture starting exactly at the left edge would ramp in from zero and then settle.</summary>
    private const int Span = LiveChart.Samples + LiveRate.WindowSeconds + LiveRate.SmoothWarmUp;

    /// <summary>The second the deliberate cache write lands in, counted back from the newest. Also
    /// where the readout is pinned: it is the second where the two numbers a hover separates differ
    /// most — 47k tokens landed, drawn as a rate that spreads them over the following minute.</summary>
    private const int SpikeSecondsBack = 41;

    /// <summary>The headline the fixture claims, in tokens/second, and the work rate the cache-read
    /// ratio is taken against.</summary>
    private const double HeadRatePerSecond = 870;

    /// <summary>What this machine's ordinary traffic really reads back per second (measured for
    /// T107) — the real order of magnitude rather than an invented one.</summary>
    private const double CacheReadPerSecond = 30_000;

    public static ThroughputDemo Build()
    {
        var perType = new long[3][];
        for (int i = 0; i < 3; i++) perType[i] = new long[Span];
        var perProject = Repos.Select(_ => new long[Span]).ToArray();
        var others = new long[Span];

        for (int i = 0; i < Span; i++)
        {
            int t = Span - 1 - i;                              // seconds ago
            bool burst = t is (< 22) or (> 40 and < 78) or (> 96 and < 128) or (> 150 and < 205);
            if (!burst) continue;
            double k = 1 - t / (double)Span;                   // newer bursts a little heavier

            for (int p = 0; p < Repos.Length; p++)
            {
                if ((i + Repos[p].Phase) % 3 != 0) continue;
                perProject[p][i] += (long)((900 * k + 120 * (i % 5) + 1400 * k) * Repos[p].Weight);
                perType[0][i] += (long)(40 * k * Repos[p].Weight);
                perType[1][i] += (long)((900 * k + 120 * (i % 5)) * Repos[p].Weight);
                perType[2][i] += (long)((1400 * k + 300 * (i % 4)) * Repos[p].Weight);
            }
            if (i % 7 == 0) others[i] = (long)(260 * k);
        }

        // One deliberate cache write, because real traffic has them: a turn that writes a large block
        // lands tens of thousands of tokens in a single second. Sized to what it *becomes* — the kernel
        // spreads it over the following minute, so a 240k second (which happens) draws a hump 25× the
        // ordinary rate and flattens everything else in the fixture to a baseline. A moderate one shows
        // the same shape and still leaves the four projects legible, which is what this image is for.
        int spike = Span - 1 - SpikeSecondsBack;
        perType[0][spike] += 2_400;
        perType[1][spike] += 600;
        perType[2][spike] += 44_000;
        perProject[0][spike] += 47_000;

        ProjectSlice[] projects = Repos
            .Select((d, p) => new ProjectSlice(d.Slug, d.Name, perProject[p],
                                               LiveRate.RateFrom(perProject[p], LiveChart.Samples),
                                               Sum(perProject[p]), HeadRatePerSecond * d.Weight, false, p))
            .Append(new ProjectSlice("+3", "+3", others, LiveRate.RateFrom(others, LiveChart.Samples),
                                     Sum(others), 41, true, -1))
            .ToArray();

        return new ThroughputDemo(
            TypeRates: perType.Select(s => LiveRate.RateFrom(s, LiveChart.Samples)).ToArray(),
            TypeTokens: perType,
            Projects: projects,
            HeadRate: HeadRatePerSecond,
            HeadTokens: 52_000,
            Sessions: 5,
            CacheReadPerSecond: CacheReadPerSecond,
            PinSecondsBack: SpikeSecondsBack);
    }

    private static long Sum(long[] a)
    {
        long s = 0;
        foreach (long v in a) s += v;
        return s;
    }
}
