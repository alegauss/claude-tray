namespace ClaudeTray;

/// <summary>One project's share of the live rate: its per-second history over the strip, the same
/// history as a <b>rolling rate</b>, its total over the trailing window, and its rate right now.
/// <paramref name="Slug"/> is the <c>~/.claude/projects/&lt;slug&gt;</c> directory name and the grouping
/// key; <paramref name="Display"/> is the folder name resolved from the session's <c>cwd</c> — never the
/// full path. <paramref name="IsOthers"/> marks the residual bucket that everything past the top few
/// folds into, and <paramref name="Slot"/> is its <b>stable</b> position: assigned on first appearance
/// and held until the project leaves the window, so neither its colour nor its place in the legend
/// moves while it is on screen (residual slices carry −1).</summary>
internal readonly record struct ProjectSlice(
    string Slug, string Display, long[] PerSecond, double[] RatePerSecond,
    long WindowTokens, double TokensPerSecond, bool IsOthers, int Slot);

/// <summary>
/// The one number in this app that should move: tokens/second over a trailing minute, fed by
/// <see cref="TranscriptTail"/> and recomputed on a clock rather than on an API poll.
///
/// <para>It exists <b>beside</b> <see cref="WindowPace.TokensPerSecond"/>, never instead of it. That
/// one divides a window's tokens by the seconds since the window opened — on the weekly tab the
/// denominator reaches 604,800, so a 200k-token burst moves the third decimal. It is immobile by
/// construction, and it is the right answer to "what did this week cost". This is the answer to
/// "what is happening now", and a user shown only the fast number loses the pacing story the slow one
/// carries.</para>
///
/// <para><b>Neither number is quota.</b> Both are token throughput measured from local transcripts;
/// the rate limit is a separate, opaque weighting the app only ever learns from the API. A big moving
/// tok/s next to a burn-up chart will be read as quota unless the copy says otherwise, which is why
/// the callers label it explicitly.</para>
///
/// <para>Two filters in series, for two different reasons. The window is <b>age-weighted</b>: a turn
/// counts fully in the second it lands and linearly less as it ages out of
/// <see cref="WindowSeconds"/>. A plain rectangular window was tried first and is wrong in exactly
/// the way this metric must not be — one 60k-token turn pinned the display at 1,000 tok/s for a full
/// minute after the work had stopped, then dropped to zero in a single step. Weighting by age makes
/// a pause decay from the moment it starts, and the triangular kernel still reports sustained work at
/// its true rate (the weights sum to <c>W/2</c>, which is what it is divided by). An <b>EWMA</b> on
/// top only takes the corners off the arrival of a large turn.</para>
///
/// <para>Cache reads are excluded from the headline, exactly as <see cref="WindowPace"/> excludes
/// them: they are the whole context re-read every turn, they dwarf real work by an order of
/// magnitude, and they barely weigh on the limit. They are still counted, and exposed separately, so
/// "nothing is happening" and "a huge context is being re-read" stay distinguishable.</para>
///
/// <para>The caller drives <see cref="Tick"/>; there is no timer in here. That is what lets T99's
/// strip stop dead when the window is hidden, and it keeps the decay honest — a rate that is only
/// recomputed when a turn arrives would hold its last value forever through a pause.</para>
/// </summary>
internal sealed class LiveRate
{
    /// <summary>The trailing window the rate is measured over.</summary>
    public const int WindowSeconds = 60;

    /// <summary>How much per-second history is kept — T99's strip draws ~3 minutes of it, and it is
    /// the same span <see cref="TranscriptTail.MaxSampleAgeSeconds"/> reports over.</summary>
    public const int HistorySeconds = 300;

    /// <summary>EWMA time constant, in seconds. Short enough that the number still visibly responds
    /// within a turn or two, long enough to take the stair-steps off the box filter.</summary>
    public const double SmoothingTau = 8;

    /// <summary>A session counts as active if a turn landed in this many seconds — presence of a file
    /// is not activity, or every terminal left open all week would inflate the count.</summary>
    public const double ActiveSeconds = 120;

    /// <summary>Categorical series the chart will draw before folding the rest into "others". Four is
    /// the palette's validated ceiling; a fifth invented hue is never the answer.</summary>
    public const int MaxProjects = 4;

    /// <summary>Seconds of rolling-rate history that can be reported: every point needs the
    /// <see cref="WindowSeconds"/> of buckets behind it, so the oldest point a
    /// <see cref="HistorySeconds"/> ring can answer for is that much newer than its far end.</summary>
    public const int MaxRateHistory = HistorySeconds - WindowSeconds;

    /// <summary>
    /// Seconds of series computed and smoothed <b>before</b> the first reported point, so the attack lag
    /// is already warm when that point is reached (T117).
    /// </summary>
    /// <remarks>The kernel is boundary-independent — a point depends only on the 60s behind it — but the
    /// EWMA carries state, and starting it at the oldest reported point made a given second's value
    /// depend on <em>where the window began</em>. Drawn as a chart that scrolls, that is visible: the
    /// left-hand end of the line kept settling toward its neighbours and re-adjusting as it moved
    /// leftwards, because it was being recomputed with a colder filter each second. Three time constants
    /// leaves ~5% of the starting error at the first reported point and none measurable after it.</remarks>
    public const int SmoothWarmUp = (int)(3 * SmoothingTau);

    private readonly object _gate = new();
    private readonly TokenBits[] _ring = new TokenBits[HistorySeconds];
    // Per-project rings, sharing _head with the total. Only projects with something in the strip are
    // kept, so this stays a handful of entries however long the window is open.
    private readonly Dictionary<string, long[]> _byProject = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _projectNames = new(StringComparer.OrdinalIgnoreCase);
    // Slug → its fixed slot (colour + legend position) for as long as it has anything in the window.
    // A chart of lines needs this: with the slot recomputed from the current ranking, a project's line
    // would change colour mid-flight every time the heaviest repo changed, which is unreadable.
    private readonly Dictionary<string, int> _slots = new(StringComparer.OrdinalIgnoreCase);
    // Session id → when it last produced a turn. Pruned to ActiveSeconds on every tick.
    private readonly Dictionary<string, double> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private long _head;              // unix second the newest bucket represents
    private bool _started;

    private double _smoothed;
    private double _instant;
    private TokenBits _window;
    private double _lastTick;
    private long _turns;
    private double _newestSample;

    /// <param name="tail">The feed. Subscribing here rather than polling it keeps the arrival path
    /// event-driven; <see cref="Tick"/> only ever reads what has already landed.</param>
    public LiveRate(TranscriptTail tail) => tail.Appended += Add;

    /// <summary>Smoothed non-cache-read tokens/second over the trailing window. This is the headline.</summary>
    public double TokensPerSecond { get { lock (_gate) return _smoothed; } }

    /// <summary>The same rate before the EWMA — the age-weighted window on its own. Useful for
    /// verifying that the smoothing is cosmetic and not inventing a number.</summary>
    public double Instant { get { lock (_gate) return _instant; } }

    /// <summary>Totals over the trailing window, split by type, cache reads included so they can be
    /// reported separately rather than silently.</summary>
    public TokenBits Window { get { lock (_gate) return _window; } }

    /// <summary>Cache-read tokens/second over the trailing window, kept out of the headline.</summary>
    public double CacheReadPerSecond { get { lock (_gate) return (double)_window.CacheRead / WindowSeconds; } }

    /// <summary>Responses seen since construction.</summary>
    public long Turns { get { lock (_gate) return _turns; } }

    /// <summary>The unix second the newest bucket represents — the instant every series returned from
    /// here ends at. A consumer that keeps its own history uses it to tell "one second later" from "a gap"
    /// (see <see cref="LiveChart"/>: a chart may append, but must not silently rewrite).</summary>
    public long HeadSecond { get { lock (_gate) return _head; } }

    /// <summary>Nothing in the trailing window. The honest reading is "no local turn landed", not
    /// "the user is idle" — usage from another machine or from claude.ai leaves no transcript here.</summary>
    public bool Quiet { get { lock (_gate) return _window.Total == 0; } }

    /// <summary>Seconds since the newest turn, or +∞ if none has been seen.</summary>
    public double SecondsSinceLastTurn
    {
        get { lock (_gate) return _newestSample > 0 ? Math.Max(0, _lastTick - _newestSample) : double.PositiveInfinity; }
    }

    /// <summary>
    /// The headline rate <em>as a series</em>: for each of the last <paramref name="seconds"/> seconds,
    /// the age-weighted rate as it stood at that second, oldest first. Split by token type, in the
    /// stacking order input / output / cache-create; cache reads stay out, as in the headline.
    /// </summary>
    /// <remarks>
    /// This is the same kernel <see cref="TokensPerSecond"/> reports, evaluated at 180 different
    /// instants instead of one — which is the whole point of T114. The last point therefore <b>is</b>
    /// the number shown above the chart (before its attack smoothing), so a line drawn from this can
    /// never contradict it, and because the kernel is linear the type series and the per-project series
    /// both sum to it.
    ///
    /// <para>It is also what makes a <em>line</em> honest here. The raw per-second buckets are events —
    /// a turn's whole usage lands in the second it finished (see §V.11) — so joining them with a line
    /// would draw a continuous rate that never existed. A trailing rate is continuous by construction:
    /// it decays through a pause because the work really is ageing out of the window, and rises when
    /// new work lands.</para>
    /// </remarks>
    public double[][] TypeRates(int seconds)
    {
        lock (_gate)
        {
            seconds = Math.Clamp(seconds, 1, MaxRateHistory);
            return new[]
            {
                Series(seconds, s => _ring[Index(s)].Input),
                Series(seconds, s => _ring[Index(s)].Output),
                Series(seconds, s => _ring[Index(s)].CacheCreate),
            };
        }
    }

    /// <summary>The same series, not split — total non-cache-read tokens/second per second.</summary>
    public double[] RateHistory(int seconds)
    {
        lock (_gate)
        {
            seconds = Math.Clamp(seconds, 1, MaxRateHistory);
            return Series(seconds, s =>
            {
                TokenBits b = _ring[Index(s)];
                return b.Input + b.Output + b.CacheCreate;
            });
        }
    }

    /// <summary>The same kernel over a plain oldest-first array of per-second tokens, returning the last
    /// <paramref name="seconds"/> points. The seam the <c>--stats live</c> fixture draws through, so a
    /// published chart is shaped by the real filter rather than by a hand-drawn curve. Points whose
    /// window would reach past the start of the array see only what is there, so a series that begins at
    /// its first bucket ramps in — which is what a feed that just started really looks like.</summary>
    public static double[] RateFrom(long[] perSecond, int seconds)
    {
        int warm = Math.Clamp(perSecond.Length - WindowSeconds - seconds, 0, SmoothWarmUp);
        var raw = new double[seconds + warm];
        for (int k = 0; k < raw.Length; k++)
        {
            int end = perSecond.Length - raw.Length + k;
            double weighted = 0;
            for (int i = 0; i < WindowSeconds; i++)
            {
                int idx = end - i;
                if (idx < 0) break;
                if (idx < perSecond.Length) weighted += perSecond[idx] * (1.0 - (double)i / WindowSeconds);
            }
            raw[k] = weighted / (WindowSeconds / 2.0);
        }
        Smooth(raw);
        return warm == 0 ? raw : raw[warm..];
    }

    // The kernel, evaluated once per output second, with SmoothWarmUp seconds computed ahead of the
    // first reported one so the lag is warm by the time the series starts. Caller holds the lock.
    private double[] Series(int seconds, Func<long, long> bucketAt)
    {
        // Whatever headroom the ring has left after the kernel's own reach: every point needs
        // WindowSeconds of buckets behind it, and the oldest bucket must still be in the ring.
        int warm = Math.Clamp(HistorySeconds - WindowSeconds + 1 - seconds, 0, SmoothWarmUp);
        var raw = new double[seconds + warm];
        for (int k = 0; k < raw.Length; k++)
        {
            long t = _head - (raw.Length - 1 - k);
            double weighted = 0;
            for (int i = 0; i < WindowSeconds; i++)
                weighted += bucketAt(t - i) * (1.0 - (double)i / WindowSeconds);
            raw[k] = weighted / (WindowSeconds / 2.0);
        }
        Smooth(raw);
        return warm == 0 ? raw : raw[warm..];
    }

    /// <summary>
    /// The headline's attack-only lag, applied along a series: rises are eased in over
    /// <see cref="SmoothingTau"/>, falls follow the window exactly.
    /// </summary>
    /// <remarks>Without this the right-hand end of a drawn line would sit visibly above the number
    /// printed over it during any rise — same data, one of them filtered — and of two numbers that
    /// disagree on screen, both lose. Smoothing the parts and smoothing the whole are not quite the same
    /// operation (the attack-only branch is per series), so per-project lines no longer sum to the total
    /// *during* an attack; they are drawn as separate lines rather than stacked, so nothing in the chart
    /// asks them to.</remarks>
    private static double[] Smooth(double[] series)
    {
        double k = 1 - Math.Exp(-1.0 / SmoothingTau);       // one second per step
        double smoothed = series.Length > 0 ? series[0] : 0;
        for (int i = 0; i < series.Length; i++)
        {
            smoothed = series[i] <= smoothed ? series[i] : smoothed + k * (series[i] - smoothed);
            series[i] = smoothed;
        }
        return series;
    }

    /// <summary>Per-second history, oldest first, exactly <see cref="HistorySeconds"/> long — the raw
    /// buckets, in tokens. The last entry is the current second and is still filling.</summary>
    public TokenBits[] Strip()
    {
        lock (_gate)
        {
            var outp = new TokenBits[HistorySeconds];
            for (int i = 0; i < HistorySeconds; i++)
                outp[i] = _ring[Index(_head - (HistorySeconds - 1 - i))];
            return outp;
        }
    }

    /// <summary>Advance to <paramref name="nowUnix"/> and recompute. Call about once a second; calling
    /// it faster is harmless (the buckets are per-second) and calling it slower just coarsens the
    /// decay. Skipped time is zero-filled, so a paused caller resumes with a truthful gap rather than
    /// a stale plateau.</summary>
    public void Tick(double nowUnix)
    {
        lock (_gate)
        {
            long sec = (long)Math.Floor(nowUnix);
            if (!_started)
            {
                _head = sec;
                _started = true;
            }
            else if (sec > _head)
            {
                // Zero every second that went by, capped at the ring size: a caller that stopped
                // ticking for an hour must come back to an empty strip, not to whatever it left.
                long steps = Math.Min(sec - _head, HistorySeconds);
                for (long s = _head + 1; s <= _head + steps; s++)
                {
                    _ring[Index(s)] = default;
                    foreach (long[] ring in _byProject.Values) ring[Index(s)] = 0;
                }
                _head = sec;
            }

            // Drop projects that have nothing left in the strip, and sessions that have gone quiet.
            // Both are bounded by activity rather than by uptime.
            foreach (string slug in _byProject.Where(kv => Sum(kv.Value) == 0).Select(kv => kv.Key).ToList())
            {
                _byProject.Remove(slug);
                _projectNames.Remove(slug);
                _slots.Remove(slug);            // the slot is free again only once the line is gone
            }
            foreach (string id in _sessions.Where(kv => nowUnix - kv.Value > ActiveSeconds).Select(kv => kv.Key).ToList())
                _sessions.Remove(id);

            long input = 0, output = 0, create = 0, read = 0;
            double weighted = 0;
            for (int i = 0; i < WindowSeconds; i++)
            {
                TokenBits b = _ring[Index(_head - i)];
                input += b.Input; output += b.Output; create += b.CacheCreate; read += b.CacheRead;
                // Full weight at the current second, zero at the far edge of the window.
                weighted += (b.Input + b.Output + b.CacheCreate) * (1.0 - (double)i / WindowSeconds);
            }
            _window = new TokenBits(input, output, create, read);

            // Σ weights over a sustained rate R is R·W/2, so dividing by W/2 reports R — the kernel
            // changes how a burst ages, not what steady work reads as.
            _instant = weighted / (WindowSeconds / 2.0);

            // Smoothing applies to the *attack* only. A large turn arriving in one second is the one
            // thing that would read as a spike, so it is eased in with a first-order lag discretised
            // at the real elapsed time (an irregular caller gets the same curve as a punctual one).
            // Downwards there is nothing to smooth — the age-weighted window already decays linearly
            // — so the rate follows it exactly, which is what makes it reach a true zero a minute
            // after the last turn instead of being snapped there from whatever the lag left behind.
            double dt = _lastTick > 0 ? Math.Clamp(nowUnix - _lastTick, 0, HistorySeconds) : 0;
            if (dt <= 0 || _instant <= _smoothed) _smoothed = _instant;
            else _smoothed += (1 - Math.Exp(-dt / SmoothingTau)) * (_instant - _smoothed);

            _lastTick = nowUnix;
        }
    }

    // Called on the tail's sweep thread.
    private void Add(IReadOnlyList<TailSample> batch)
    {
        lock (_gate)
        {
            if (!_started) { _head = (long)Math.Floor(batch[^1].Unix); _started = true; }

            foreach (TailSample s in batch)
            {
                long sec = (long)Math.Floor(s.Unix);
                // A turn stamped ahead of our clock (skew between writer and reader) belongs to now,
                // not to the future — dropping it would lose the very turn that is running.
                if (sec > _head) sec = _head;
                if (sec <= _head - HistorySeconds) continue;   // older than the strip; already gone

                TokenBits b = _ring[Index(sec)];
                _ring[Index(sec)] = new TokenBits(
                    b.Input + s.Bits.Input,
                    b.Output + s.Bits.Output,
                    b.CacheCreate + s.Bits.CacheCreate,
                    b.CacheRead + s.Bits.CacheRead);

                // Attribution is free: the transcript's path already names the project directory and
                // the session file. Same non-cache-read definition as the headline rate.
                if (s.Project.Length > 0)
                {
                    if (!_byProject.TryGetValue(s.Project, out long[]? ring))
                        _byProject[s.Project] = ring = new long[HistorySeconds];
                    ring[Index(sec)] += s.Bits.Input + s.Bits.Output + s.Bits.CacheCreate;
                    if (s.Name.Length > 0) _projectNames[s.Project] = s.Name;
                }
                if (s.Session.Length > 0 && s.Unix > _sessions.GetValueOrDefault(s.Session))
                    _sessions[s.Session] = s.Unix;

                if (s.Unix > _newestSample) _newestSample = s.Unix;
                _turns++;
            }
        }
    }

    /// <summary>Sessions that produced a turn in the last <see cref="ActiveSeconds"/>. A count, never
    /// a list — the useful fact is "three things are running", and session ids are not for display.</summary>
    public int ActiveSessions { get { lock (_gate) return _sessions.Count; } }

    /// <summary>
    /// The history split by project, in <b>slot order</b>, capped at <see cref="MaxProjects"/> with the
    /// rest folded into a single "others" slice. For one project this is a curiosity; across five repos
    /// it is the question actually being asked — <em>which of these is eating the week?</em>
    /// </summary>
    /// <remarks>
    /// <para>Each project carries both readings: <c>PerSecond</c>, the raw tokens that landed in each
    /// second, and <c>RatePerSecond</c>, the same history through the headline's kernel — the series a
    /// line is drawn from.</para>
    /// <para><b>Slots are sticky (T114).</b> A project claims the lowest free slot the first time it is
    /// ranked and keeps it until it has nothing left in the window; the slices come back ordered by slot
    /// rather than by rate. Ranking by rate is right for a snapshot and wrong for a chart: it would swap
    /// two lines' colours mid-flight whenever the busier repo changed, which is exactly the moment the
    /// chart is being read. Who *fills* the four slots is still decided by rate, so a newcomer that
    /// arrives while all four are taken folds into the residual until one frees.</para>
    /// <para><paramref name="seconds"/> bounds the rate series (clamped to
    /// <see cref="MaxRateHistory"/>); the raw <c>PerSecond</c> array is always the full ring.</para>
    /// </remarks>
    public ProjectSlice[] Projects(int seconds = MaxRateHistory)
    {
        lock (_gate)
        {
            if (_byProject.Count == 0) return Array.Empty<ProjectSlice>();
            seconds = Math.Clamp(seconds, 1, MaxRateHistory);

            // On the chart while it has anything the chart could still draw — not merely while it has
            // something in the last minute (T115). The trailing-window test looks equivalent and is not:
            // a project that pauses for longer than the rate window vanished *entirely*, history and all,
            // and its whole series came back the moment one turn landed. Watched over a few minutes, a
            // paused repo therefore blinked in and out instead of decaying and ageing off the left edge.
            // Ranking still uses the rate — who is busiest now is what decides who gets a slot.
            var ranked = _byProject
                .Select(kv => (slug: kv.Key, ring: kv.Value, win: WindowSum(kv.Value),
                               rate: WeightedRate(kv.Value), drawn: DrawnSum(kv.Value, seconds)))
                .Where(p => p.drawn > 0)
                .OrderByDescending(p => p.rate)
                .ThenBy(p => p.slug, StringComparer.OrdinalIgnoreCase)   // stable when tied
                .ToList();
            if (ranked.Count == 0) return Array.Empty<ProjectSlice>();

            // Hand out slots by rate, but only ones nobody holds — and only to projects that don't
            // already have one, so an existing line never moves.
            foreach (var p in ranked)
            {
                if (_slots.ContainsKey(p.slug)) continue;
                for (int slot = 0; slot < MaxProjects; slot++)
                    if (!_slots.ContainsValue(slot)) { _slots[p.slug] = slot; break; }
            }

            var slices = new List<ProjectSlice>();
            foreach (var p in ranked.Where(p => _slots.ContainsKey(p.slug)).OrderBy(p => _slots[p.slug]))
                slices.Add(new ProjectSlice(p.slug, _projectNames.GetValueOrDefault(p.slug, p.slug),
                                            Unroll(p.ring), Series(seconds, s => p.ring[Index(s)]),
                                            p.win, p.rate, false, _slots[p.slug]));

            // Everything without a slot becomes one residual series. Never a generated hue.
            var rest = ranked.Where(p => !_slots.ContainsKey(p.slug)).ToList();
            if (rest.Count > 0)
            {
                var merged = new long[HistorySeconds];
                var mergedRate = new double[seconds];
                long win = 0;
                double rate = 0;
                foreach (var p in rest)
                {
                    long[] u = Unroll(p.ring);
                    for (int i = 0; i < merged.Length; i++) merged[i] += u[i];
                    double[] r = Series(seconds, s => p.ring[Index(s)]);
                    for (int i = 0; i < mergedRate.Length; i++) mergedRate[i] += r[i];
                    win += p.win;
                    rate += p.rate;      // the kernel is linear, so the parts add up to the whole
                }
                slices.Add(new ProjectSlice($"+{rest.Count}", $"+{rest.Count}", merged, mergedRate,
                                            win, rate, true, -1));
            }
            return slices.ToArray();
        }
    }

    // The same age-weighted kernel the headline uses, so the per-project rates are comparable with it
    // — and, because the kernel is linear, so that they sum to it.
    private double WeightedRate(long[] ring)
    {
        double weighted = 0;
        for (int i = 0; i < WindowSeconds; i++)
            weighted += ring[Index(_head - i)] * (1.0 - (double)i / WindowSeconds);
        return weighted / (WindowSeconds / 2.0);
    }

    private long[] Unroll(long[] ring)
    {
        var outp = new long[HistorySeconds];
        for (int i = 0; i < HistorySeconds; i++) outp[i] = ring[Index(_head - (HistorySeconds - 1 - i))];
        return outp;
    }

    private long WindowSum(long[] ring)
    {
        long sum = 0;
        for (int i = 0; i < WindowSeconds; i++) sum += ring[Index(_head - i)];
        return sum;
    }

    // Everything a chart of `seconds` seconds can still show of this project: the drawn span plus the
    // kernel's reach behind its oldest point, since a bucket that old still moves that point.
    private long DrawnSum(long[] ring, int seconds)
    {
        long sum = 0;
        for (int i = 0; i < seconds + WindowSeconds && i < HistorySeconds; i++) sum += ring[Index(_head - i)];
        return sum;
    }

    private static long Sum(long[] ring)
    {
        long s = 0;
        foreach (long v in ring) s += v;
        return s;
    }

    private static int Index(long sec)
    {
        int i = (int)(sec % HistorySeconds);
        return i < 0 ? i + HistorySeconds : i;
    }
}
