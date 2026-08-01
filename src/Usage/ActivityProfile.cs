using System.Text;
using System.Text.Json;

namespace ClaudeTray;

/// <summary>
/// A weekly <em>shape</em> of when this machine is actually used: 168 buckets (day-of-week × local
/// hour), each holding <c>p(active)</c> — the recency-weighted fraction of observed weeks in which
/// that hour contained at least one request.
///
/// It exists because the weekly projection currently extrapolates the average pace since the window
/// opened, a straight line that spends quota at 03:00 Sunday exactly as fast as at 15:00 Tuesday.
/// That line can land the "you run out here" marker in the middle of a stretch where nobody is
/// working, and it misreads any window whose <em>remaining</em> active/idle mix differs from its
/// elapsed one. Projecting along this shape instead is <c>T87</c>; this type only measures the shape.
///
/// <para><b>Source: the transcripts, not <see cref="UsageHistory"/>.</b> The usage log is pruned at 8
/// days, so it structurally cannot see other weeks. <c>~/.claude/projects</c> keeps months. Only
/// timestamps are read — the same privacy line <see cref="UsageReport"/> holds: never message
/// content, and here not even token counts.</para>
///
/// <para>Two guards against overfitting, both in <see cref="Compute"/>: exponential decay across
/// weeks, so a habit from two months ago cannot outvote last week; and a blend toward a flat prior
/// weighted by how many weeks were actually observed, so one holiday week cannot convince the app
/// that nobody works on Wednesdays.</para>
///
/// <para>A bucket is a probability, never a boolean. "Usually quiet but sometimes worked" has to
/// degrade smoothly, or a single unusual evening flips a whole projection.</para>
///
/// <para><b>The sweep is incremental (T92).</b> A rebuild used to re-read every transcript in the
/// window — 15s over 93k requests on the dev machine — although a session untouched since the last
/// sweep produces exactly the counts it produced then. Each file is now reduced once to the local
/// hours it touched (<c>SweepEntry</c>, cached on path+size+mtime), so a rebuild costs only the
/// sessions that changed. The week index is deliberately *not* cached: it is relative to now, so it
/// would rot by one every seven days — see <c>SweepEntry</c>.</para>
/// </summary>
internal sealed class ActivityProfile
{
    /// <summary>7 days × 24 local hours.</summary>
    public const int Buckets = 7 * 24;

    /// <summary>How far back the scan looks. Beyond ~3 months a habit is a different job.</summary>
    public const int MaxWeeks = 12;

    /// <summary>Per-week decay: last week weighs 1, the week before 0.8, … (half-life ≈ 3 weeks).</summary>
    public const double WeekDecay = 0.8;

    /// <summary>Strength of the flat prior, expressed in equivalent weeks of evidence.</summary>
    public const double PriorWeeks = 1.5;

    /// <summary>Below this much coverage the shape is a guess; T87 falls back to the straight line.</summary>
    public const double ConfidentWeeks = 3.0;

    /// <summary>Effective weeks of *measured* evidence at which a bucket is taken entirely from the
    /// folded aggregate rather than from transcripts. With <see cref="WeekDecay"/> this is reached
    /// after roughly four weeks of continuous coverage, and the ramp to it is linear — a hard cutover
    /// would visibly jump the projection on an arbitrary day.</summary>
    public const double MeasuredTrustWeeks = 3.0;

    /// <summary>The cache is recomputed about once a day — a habit does not move faster than that.</summary>
    public const double RefreshHours = 20;

    private const int CacheVersion = 1;

    /// <summary>Walk cap, mirroring <see cref="ContextScanner"/>: a pathological tree must not hang.</summary>
    private const int MaxFiles = 20_000;

    /// <summary>p(active) per bucket, indexed by <see cref="Index(DayOfWeek,int)"/>.</summary>
    public double[] P = new double[Buckets];

    /// <summary>Span of transcript coverage in weeks (capped at <see cref="MaxWeeks"/>).</summary>
    public double CoverageWeeks;

    /// <summary>Requests that fed the grid. Only their timestamps were read.</summary>
    public long Samples;

    /// <summary>When this grid was computed (not when it was loaded).</summary>
    public DateTime ComputedUtc;

    public bool FromCache;
    public double ElapsedMs;
    public string? Error;

    /// <summary>Transcripts in the 12-week window — the work the sweep had to consider.</summary>
    public int FilesSeen;

    /// <summary>Transcripts actually opened. With a warm sweep cache this is only the ones that
    /// changed, which is the whole point of T92 — and printing both is what makes a cache bug
    /// visible rather than merely fast.</summary>
    public int FilesRead;

    /// <summary>Bytes read by this sweep, not bytes on disk.</summary>
    public long BytesRead;

    // ---------------------------------------------------------------- the measured blend (T93)

    /// <summary>The transcript-only grid, kept as it was before the measured blend so the two sources
    /// can still be compared (`--activity --measured`) rather than one silently becoming the other.</summary>
    public double[]? Inferred;

    /// <summary>The measured grid this was blended with, when there was one.</summary>
    public double[]? Measured;

    /// <summary>Per bucket, how much of <see cref="P"/> came from the measured grid (0–1).</summary>
    public double[]? MeasuredWeight;

    /// <summary>Span of folded coverage behind <see cref="Measured"/>, in weeks.</summary>
    public double MeasuredWeeks;

    /// <summary>Folded days behind <see cref="Measured"/>.</summary>
    public int MeasuredDays;

    /// <summary>Mean of <see cref="MeasuredWeight"/> — the share of the whole grid now taken from what
    /// the rate limit recorded. The UI's method note is worded off this: the local-transcript blind
    /// spot stops being true to the degree this dominates, and a stale disclaimer is its own kind of
    /// wrong.</summary>
    public double MeasuredShare;

    /// <summary>
    /// Fold the measured grid into this one, bucket by bucket.
    ///
    /// <para>The transcript grid has a structural blind spot: usage from another machine or from
    /// claude.ai counts against the same limit and leaves no transcript here. The folded aggregate
    /// does not — it is built from the rate-limit utilization itself, which counts every request
    /// whoever made it and wherever. So once there is enough of it, the measured grid is not a second
    /// opinion to validate against (its role in T88) but the better source.</para>
    ///
    /// <para>Blended per bucket rather than switched wholesale, weighted by how many effective weeks
    /// the store has actually observed <em>that hour</em>. Hours it knows well come from the store;
    /// hours it barely saw — and every hour, on a machine that has just started folding — stay with
    /// the transcripts. That keeps the transcript grid's two remaining jobs: bootstrapping the first
    /// weeks, and covering hours the app was not running to observe.</para>
    /// </summary>
    public void BlendMeasured(string profileKey, DateTime nowLocal)
    {
        try
        {
            HourlyUsage.MeasuredWeek m = HourlyUsage.MeasuredProfile(profileKey, nowLocal);
            MeasuredWeeks = m.Weeks;
            MeasuredDays = m.Days;
            if (m.Days == 0) return;

            var inferred = (double[])P.Clone();
            var weight = new double[Buckets];
            double share = 0;

            for (int b = 0; b < Buckets; b++)
            {
                double w = Math.Clamp(m.Observed[b] / MeasuredTrustWeeks, 0, 1);
                weight[b] = w;
                share += w;
                P[b] = w * m.P[b] + (1 - w) * inferred[b];
            }

            Inferred = inferred;
            Measured = m.P;
            MeasuredWeight = weight;
            MeasuredShare = share / Buckets;
        }
        catch { /* the store is an improvement on the grid, never a precondition for having one */ }
    }

    /// <summary>Weeks of evidence behind the shape, from whichever source has more of it. A machine
    /// with a thin transcript history but weeks of folded readings knows its own week perfectly
    /// well.</summary>
    public double EffectiveWeeks => Math.Max(CoverageWeeks, MeasuredWeeks);

    /// <summary>Enough weeks behind the shape to project along it rather than along a slope.</summary>
    public bool Confident => EffectiveWeeks >= ConfidentWeeks;

    /// <summary>Overall share of hours that are active — the flat prior the grid is blended toward.</summary>
    public double Mean
    {
        get
        {
            double sum = 0;
            foreach (double p in P) sum += p;
            return sum / Buckets;
        }
    }

    public static int Index(DayOfWeek day, int hour) => (int)day * 24 + hour;

    public double At(DayOfWeek day, int hour) => P[Index(day, hour)];

    public double At(DateTime local) => At(local.DayOfWeek, local.Hour);

    /// <summary>
    /// Expected active hours in a local time span: Σ p over the hours it covers, pro-rating the
    /// partial hours at each end. This is the quantity the staircase projection spends quota against
    /// — "three active hours left today", not "four hours left today".
    ///
    /// Walks wall-clock hours, so a DST day is 24 buckets long even though it is 23 or 25 real hours.
    /// That is the intended reading: the grid is a habit expressed in clock time.
    /// </summary>
    public double ExpectedActiveHours(DateTime fromLocal, DateTime toLocal)
    {
        if (toLocal <= fromLocal) return 0;

        double total = 0;
        DateTime cursor = fromLocal;
        // A week is 168 iterations; the cap only stops a caller passing an absurd span.
        for (int guard = 0; guard < MaxWeeks * Buckets && cursor < toLocal; guard++)
        {
            DateTime nextHour = cursor.Date.AddHours(cursor.Hour + 1);
            DateTime end = nextHour < toLocal ? nextHour : toLocal;
            total += At(cursor) * (end - cursor).TotalHours;
            cursor = end;
        }
        return total;
    }

    // ---------------------------------------------------------------- loading

    // Set while a background refresh is in flight, so a report refreshed every poll can't stack up
    // scans of the same hundreds of megabytes.
    private static int _refreshing;

    /// <summary>
    /// The profile for now: the cached grid when there is one, recomputed in the background once it
    /// passes <see cref="RefreshHours"/>. Only the very first call on a machine pays for the scan —
    /// a day-old grid is still a good grid, and blocking a chart on a 15-second sweep to sharpen a
    /// habit by one day is a bad trade. Best-effort throughout: a failure yields an empty,
    /// non-confident profile with <see cref="Error"/> set, never an exception.
    /// </summary>
    /// <param name="refresh">Force a synchronous rescan, ignoring the cache.</param>
    /// <param name="claudeRoot">Stand-in for <c>~/.claude</c> (fixtures). Never reads or writes the cache.</param>
    /// <param name="profile">Which profile's transcripts to mine and whose cache to use. Defaults to the
    /// monitored one. A fixture root wins over it and stays cacheless — a synthetic tree must never
    /// overwrite a real profile's grid (T128).</param>
    public static ActivityProfile Load(DateTime nowUtc, bool refresh = false, string? claudeRoot = null,
                                       ProfileRef? profile = null)
    {
        ProfileRef p = profile ?? ProfileStore.MonitoredRef;
        bool real = claudeRoot == null;
        if (real && !refresh && ReadCache(p.Key) is { } cached)
        {
            cached.FromCache = true;
            if ((nowUtc - cached.ComputedUtc).TotalHours >= RefreshHours &&
                Interlocked.CompareExchange(ref _refreshing, 1, 0) == 0)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        ActivityProfile next = Compute(DateTimeOffset.UtcNow.UtcDateTime, p.ProjectsDir);
                        if (next.Error == null) WriteCache(p.Key, next);
                    }
                    catch { /* the stale grid stays; another open will try again */ }
                    finally { Interlocked.Exchange(ref _refreshing, 0); }
                });
            }
            cached.BlendMeasured(p.Key, nowUtc.ToLocalTime());
            return cached;
        }

        // A fixture root must neither read nor write the shared sweep cache (the same rule T128 set
        // for the grid cache); `--refresh` re-reads every transcript but still leaves the cache warm,
        // or forcing a rescan would make the next one slow too.
        SweepCacheMode mode = !real ? SweepCacheMode.Off
                            : refresh ? SweepCacheMode.Rebuild
                            : SweepCacheMode.Use;
        ActivityProfile fresh = Compute(nowUtc, claudeRoot is { } root ? ProjectsDir(root) : p.ProjectsDir, mode);

        // Cache first, blend second, and never the other way round: what is cached is the transcript
        // grid, which only changes when the transcripts do. The measured store changes every day the
        // tray runs, so the blend is applied on every read instead of being frozen into the cache.
        if (real && fresh.Error == null) WriteCache(p.Key, fresh);
        if (real) fresh.BlendMeasured(p.Key, nowUtc.ToLocalTime());
        return fresh;
    }

    private static string ProjectsDir(string? claudeRoot) => Path.Combine(
        claudeRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"),
        "projects");

    /// <summary>Per profile (T125): the grid is mined from a config dir's transcripts, so it describes
    /// that profile's week and nobody else's.</summary>
    public static string CachePath(string profileKey) =>
        ProfileStore.PathFor(profileKey, "activity-profile.json");

    // ---------------------------------------------------------------- computing

    /// <summary>
    /// Build the grid from every request timestamp in the transcripts, newest 12 weeks only.
    ///
    /// Weeks are the rolling windows <c>[now − 7(w+1)d, now − 7w d)</c>. Each such window contains
    /// exactly one occurrence of every (day, hour) bucket, whatever the time of day now happens to
    /// be — which is why no partial-week correction is needed: week 0 is as complete as week 5.
    /// </summary>
    public static ActivityProfile Compute(DateTime nowUtc, string projectsDir,
                                          SweepCacheMode cache = SweepCacheMode.Use)
    {
        var prof = new ActivityProfile { ComputedUtc = nowUtc };
        long startedTicks = DateTime.UtcNow.Ticks;
        try
        {
            if (!Directory.Exists(projectsDir))
            {
                prof.Error = "no transcripts directory";
                return prof;
            }

            DateTime nowLocal = nowUtc.ToLocalTime();
            DateTime cutoffUtc = nowUtc.AddDays(-7 * MaxWeeks);

            // A transcript last written before the cutoff cannot hold an in-range timestamp, so it is
            // never opened and never cached — which also bounds the cache to the current window.
            var files = new List<FileInfo>();
            foreach (FileInfo f in SafeWalk.Files(projectsDir, "*.jsonl"))
            {
                try { if (f.LastWriteTimeUtc < cutoffUtc) continue; } catch { continue; }
                files.Add(f);
                if (files.Count >= MaxFiles) break;
            }
            prof.FilesSeen = files.Count;

            Dictionary<string, SweepEntry> known = cache == SweepCacheMode.Use ? ReadSweep() : new(PathComparer);
            var swept = new Dictionary<string, SweepEntry>(files.Count, PathComparer);

            foreach (FileInfo f in files)
            {
                // A file already swept at this exact size and mtime cannot have gained a timestamp —
                // this is what turns the daily rebuild into the cost of the sessions that changed.
                if (known.TryGetValue(f.FullName, out SweepEntry? hit) &&
                    hit.Bytes == f.Length && hit.Ticks == f.LastWriteTimeUtc.Ticks)
                {
                    swept[f.FullName] = hit;
                    continue;
                }

                SweepEntry entry = SweepFile(f);
                swept[f.FullName] = entry;
                prof.FilesRead++;
                prof.BytesRead += entry.Read;
            }

            if (cache != SweepCacheMode.Off) WriteSweep(swept);

            // active[week][bucket] — "this hour saw at least one request that week". A count would let
            // one frantic afternoon outvote five ordinary ones; presence is the honest signal.
            var active = new bool[MaxWeeks, Buckets];
            DateTime? earliestLocal = null;
            long samples = 0;

            // The one place a week index is assigned, so the cached and full-sweep paths cannot drift:
            // both aggregate the same absolute hour buckets here. The week is *not* stored — it is
            // relative to now, so a cached week number would rot by one every seven days.
            foreach (SweepEntry entry in swept.Values)
            {
                for (int i = 0; i < entry.H.Length; i++)
                {
                    DateTime local = new(entry.H[i] * TimeSpan.TicksPerHour);
                    // A clock-skewed future timestamp is not evidence of anything. Checked against the
                    // hour rather than relying on a negative week: `(int)` truncates toward zero, so a
                    // few hours into the future would still land in week 0.
                    if (local > nowLocal) continue;

                    int week = (int)((nowLocal - local).TotalDays / 7);
                    if (week < 0 || week >= MaxWeeks) continue;

                    if (earliestLocal == null || local < earliestLocal) earliestLocal = local;
                    active[week, Index(local.DayOfWeek, local.Hour)] = true;
                    samples += entry.N[i];
                }
            }

            prof.Samples = samples;
            double coverageDays = earliestLocal is { } first ? (nowLocal - first).TotalDays : 0;
            prof.CoverageWeeks = Math.Min(coverageDays / 7.0, MaxWeeks);

            // Whole weeks only vote; the oldest partial one would report every bucket it happens to
            // miss as idle. With less than a week of data, week 0 votes alone.
            int weeks = Math.Clamp((int)Math.Floor(coverageDays / 7.0), 1, MaxWeeks);
            if (samples == 0) { prof.ElapsedMs = Elapsed(startedTicks); return prof; }

            double weightSum = 0;
            var raw = new double[Buckets];
            for (int w = 0; w < weeks; w++)
            {
                double weight = Math.Pow(WeekDecay, w);
                weightSum += weight;
                for (int b = 0; b < Buckets; b++)
                    if (active[w, b]) raw[b] += weight;
            }
            if (weightSum > 0)
                for (int b = 0; b < Buckets; b++) raw[b] /= weightSum;

            // Shrink toward the user's own overall active rate, hard when there is little evidence and
            // barely at all once there are months of it. A flat prior rather than a zero prior: the
            // question a thin grid should answer is "how busy is this person generally", not "are they
            // asleep".
            double prior = 0;
            foreach (double r in raw) prior += r;
            prior /= Buckets;

            for (int b = 0; b < Buckets; b++)
                prof.P[b] = (weeks * raw[b] + PriorWeeks * prior) / (weeks + PriorWeeks);

            prof.ElapsedMs = Elapsed(startedTicks);
            return prof;
        }
        catch (Exception e)
        {
            prof.Error = e.Message;
            prof.ElapsedMs = Elapsed(startedTicks);
            return prof;
        }
    }

    private static double Elapsed(long startedTicks)
        => (DateTime.UtcNow.Ticks - startedTicks) / (double)TimeSpan.TicksPerMillisecond;

    // ---------------------------------------------------------------- the per-file sweep (T92)

    /// <summary>How <see cref="Compute"/> may use the per-file sweep cache.</summary>
    internal enum SweepCacheMode
    {
        /// <summary>Read what is cached, open only what changed, write the result back.</summary>
        Use,
        /// <summary>Open every transcript, then write the result back — what `--activity --refresh`
        /// does. Without it a cache bug would be unfalsifiable.</summary>
        Rebuild,
        /// <summary>Neither read nor write: a fixture tree must never touch the real cache.</summary>
        Off,
    }

    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// One transcript reduced to what the grid needs: the local hours it touched, and how many request
    /// lines landed in each. Valid while the file's size and mtime are unchanged.
    ///
    /// <para><b>Absolute hours, never week indices.</b> A week here is the rolling window
    /// <c>[now−7(w+1)d, now−7w d)</c>, so the week a timestamp belongs to depends on when the sweep
    /// runs. Caching a week number would therefore be caching an answer that expires; caching the hour
    /// keeps the entry true forever and costs the same bytes.</para>
    ///
    /// <para>The key is <c>local.Ticks / TicksPerHour</c> — the local hour the request happened in,
    /// converted once at scan time so the historical UTC offset (and DST) is the one that applied
    /// then. Reversing it recovers the day-of-week and hour exactly, which is all a bucket is.</para>
    /// </summary>
    private sealed class SweepEntry
    {
        public string Path { get; set; } = "";
        public long Bytes { get; set; }
        public long Ticks { get; set; }
        /// <summary>Distinct local-hour keys, ascending.</summary>
        public long[] H { get; set; } = Array.Empty<long>();
        /// <summary>Request lines per hour, parallel to <see cref="H"/>.</summary>
        public int[] N { get; set; } = Array.Empty<int>();

        /// <summary>Bytes this sweep read for the entry — 0 when it came from the cache. Not persisted.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public long Read { get; set; }
    }

    /// <summary>
    /// Reduce one transcript to its hour histogram. Deliberately does <b>not</b> apply the 12-week
    /// cutoff to individual lines: the entry then describes the file rather than the moment it was
    /// scanned, so it stays valid as the window slides and <see cref="Compute"/> can filter at
    /// aggregation time. The file-level mtime check already keeps out-of-window files from being read
    /// at all, which is what bounds this.
    /// </summary>
    private static SweepEntry SweepFile(FileInfo file)
    {
        var hours = new Dictionary<long, int>();
        long read = 0;
        try
        {
            foreach (string line in File.ReadLines(file.FullName))
            {
                read += line.Length;
                if (!IsRequestLine(line)) continue;
                if (!TryReadTimestamp(line, out DateTime whenUtc)) continue;

                long key = whenUtc.ToLocalTime().Ticks / TimeSpan.TicksPerHour;
                hours[key] = hours.TryGetValue(key, out int n) ? n + 1 : 1;
            }
        }
        catch { /* a transcript being written, or an unreadable file — keep what we got */ }

        long[] keys = hours.Keys.ToArray();
        Array.Sort(keys);
        var counts = new int[keys.Length];
        for (int i = 0; i < keys.Length; i++) counts[i] = hours[keys[i]];

        return new SweepEntry
        {
            Path = file.FullName,
            Bytes = SafeLength(file),
            Ticks = SafeTicks(file),
            H = keys,
            N = counts,
            Read = read,
        };
    }

    private static long SafeLength(FileInfo f) { try { return f.Length; } catch { return -1; } }
    private static long SafeTicks(FileInfo f) { try { return f.LastWriteTimeUtc.Ticks; } catch { return -1; } }

    /// <summary>
    /// Shared by every profile rather than stored per profile, for the same reason
    /// <c>context-usage.json</c> is (see <see cref="ProfileStore"/>): entries are keyed by absolute
    /// transcript path plus a size+mtime fingerprint, so one profile's entry cannot be mistaken for
    /// another's, and two profiles pointing at the same tree share the work instead of repeating it.
    /// </summary>
    public static string SweepCachePath => Path.Combine(Settings.DataDir, "activity-sweep.json");

    private static Dictionary<string, SweepEntry> ReadSweep()
    {
        try
        {
            if (!File.Exists(SweepCachePath)) return new(PathComparer);
            var list = JsonSerializer.Deserialize<List<SweepEntry>>(File.ReadAllText(SweepCachePath));
            var map = new Dictionary<string, SweepEntry>(PathComparer);
            foreach (SweepEntry e in list ?? new())
                if (e.Path.Length > 0 && e.H.Length == e.N.Length) map[e.Path] = e;
            return map;
        }
        catch { return new(PathComparer); }
    }

    private static void WriteSweep(Dictionary<string, SweepEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SweepCachePath)!);
            File.WriteAllText(SweepCachePath, JsonSerializer.Serialize(entries.Values));
        }
        catch { /* the cache is an optimization; failing to write it costs one slow sweep */ }
    }

    // Substring tests rather than JsonDocument.Parse: this walks months of transcripts — hundreds of
    // megabytes — and parsing every line to reach one field costs seconds, for a grid whose buckets
    // are an hour wide. `input_tokens` is what distinguishes a real request from a synthetic or
    // tool-result line, and `<synthetic>` turns (interrupts, replays) are not user presence.
    //
    // This is the one reader that does NOT need T102's de-duplication: the grid records *presence*
    // ("this hour saw work"), so the extra lines one response writes set a bit that is already set.
    // Only `Samples` counts them, which is why it is reported as lines and not as requests.
    private static bool IsRequestLine(string line)
        => line.Length > 0
           && line.Contains("\"type\":\"assistant\"", StringComparison.Ordinal)
           && line.Contains("\"input_tokens\"", StringComparison.Ordinal)
           && !line.Contains("\"<synthetic>\"", StringComparison.Ordinal);

    private const string TimestampKey = "\"timestamp\":\"";

    private static bool TryReadTimestamp(string line, out DateTime whenUtc)
    {
        whenUtc = default;
        int at = line.IndexOf(TimestampKey, StringComparison.Ordinal);
        if (at < 0) return false;
        int from = at + TimestampKey.Length;
        int end = line.IndexOf('"', from);
        if (end <= from) return false;

        return DateTime.TryParse(line.AsSpan(from, end - from), null,
            System.Globalization.DateTimeStyles.AdjustToUniversal |
            System.Globalization.DateTimeStyles.AssumeUniversal, out whenUtc);
    }

    // ---------------------------------------------------------------- cache

    // ~1.5 KB of JSON: a version, when it was built, what it was built from, and 168 rounded floats.
    // Rounded to three decimals because the third decimal of a habit is noise, and the file is read
    // on a UI path.
    private static void WriteCache(string profileKey, ActivityProfile p)
    {
        try
        {
            var sb = new StringBuilder(2048);
            long computed = new DateTimeOffset(p.ComputedUtc, TimeSpan.Zero).ToUnixTimeSeconds();
            sb.Append(FormattableString.Invariant(
                $"{{\"v\":{CacheVersion},\"t\":{computed},\"weeks\":{p.CoverageWeeks:0.###},\"n\":{p.Samples},\"p\":["));
            for (int b = 0; b < Buckets; b++)
            {
                if (b > 0) sb.Append(',');
                sb.Append(FormattableString.Invariant($"{p.P[b]:0.###}"));
            }
            sb.Append("]}");

            string path = CachePath(profileKey);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, sb.ToString());
        }
        catch { /* the cache is an optimization; a failed write just means another scan tomorrow */ }
    }

    private static ActivityProfile? ReadCache(string profileKey)
    {
        try
        {
            string path = CachePath(profileKey);
            if (!File.Exists(path)) return null;

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement r = doc.RootElement;
            if (!r.TryGetProperty("v", out JsonElement v) || v.GetInt32() != CacheVersion) return null;
            if (!r.TryGetProperty("p", out JsonElement arr) || arr.ValueKind != JsonValueKind.Array ||
                arr.GetArrayLength() != Buckets) return null;

            var prof = new ActivityProfile
            {
                ComputedUtc = DateTimeOffset.FromUnixTimeSeconds(
                    r.TryGetProperty("t", out JsonElement t) ? t.GetInt64() : 0).UtcDateTime,
                CoverageWeeks = r.TryGetProperty("weeks", out JsonElement w) ? w.GetDouble() : 0,
                Samples = r.TryGetProperty("n", out JsonElement n) ? n.GetInt64() : 0,
            };
            int i = 0;
            foreach (JsonElement e in arr.EnumerateArray())
                prof.P[i++] = Math.Clamp(e.GetDouble(), 0, 1);
            return prof;
        }
        catch { return null; }   // a corrupt cache is one extra scan, not an error
    }
}
