using System.Text.Json;

namespace ClaudeTray;

/// <summary>One conversation, summed. The unit a person actually remembers working in — "the session
/// this morning that took an hour and felt expensive" — which until T327 was the one unit no reader
/// here could produce.</summary>
/// <param name="Session">The session id: the transcript's own file stem, or the folder a fan-out's
/// agents were written under. Not for display — an identifier, like <paramref name="Project"/>.</param>
/// <param name="Project">The <c>projects/&lt;slug&gt;</c> directory, the same grouping key every other
/// reader uses. <paramref name="Name"/> is its display form, resolved through
/// <see cref="ProjectSlug.NameFor"/> from a <c>cwd</c> the transcript carried.</param>
/// <param name="Calls">Distinct responses, not lines: Claude Code writes one line per content block,
/// each repeating the same <c>usage</c>, and counting those counts a thinking-plus-tool-use turn
/// twice.</param>
/// <param name="Models">Every model that answered in this session, in first-seen order. Usually one;
/// a session that changed model mid-way is exactly the case a single value would hide.</param>
/// <param name="Agents">How many separate transcripts the fan-out wrote — 0 for a session that never
/// fanned out. Their cost is already inside <paramref name="Bits"/>; this says how much of the
/// conversation is not in its own file.</param>
internal readonly record struct SessionRow(
    string Session, string Project, string Name,
    double FirstUnix, double LastUnix, int Calls, TokenBits Bits, string[] Models, int Agents)
{
    /// <summary>Wall-clock seconds from the first answered turn to the last. Zero for a session with a
    /// single turn, which is a real answer and not a missing one.</summary>
    public double Seconds => Math.Max(0, LastUnix - FirstUnix);
}

/// <summary>What one <see cref="SessionIndex.Load"/> cost, so "the cache is doing something" is a
/// number rather than a claim — the same reason <see cref="ActivityProfile"/> reports its own.</summary>
internal readonly record struct SessionScanStats(
    string Root, int Files, int Read, long BytesRead, int Lines, int Sessions, double Ms);

/// <summary>
/// One pass over a profile's <c>projects/**/*.jsonl</c>, producing one row per conversation.
///
/// <para>Every other reader in this app aggregates over a <b>window</b> (<see cref="UsageReport"/>,
/// five hours and seven days), a <b>project</b> (<see cref="LiveRate"/>'s strip) or an <b>hour</b>
/// (<see cref="ActivityProfile"/>). None of them aggregates over the conversation, which is the unit a
/// person remembers and the one the transcripts are actually organised by.</para>
///
/// <para><b>A scan, not a tail.</b> <see cref="TranscriptTail"/> answers "what is happening now" from
/// appended bytes; this answers "what did that session cost" from the whole file, and the two share
/// only <see cref="TranscriptTail.Locate"/> — the path derivation that says which conversation a file
/// belongs to.</para>
///
/// <para><b>Privacy (§I.1) by construction.</b> Parsing is
/// <see cref="UsageReport.TryParseSample(string, double, double, out double, out TokenBits, out string?, out string?, out string?)"/>,
/// the same parser the sweep and the tail use, which reads <c>type</c>, <c>timestamp</c>,
/// <c>requestId</c>, <c>message.id</c>, <c>message.model</c>, <c>message.usage</c> and <c>cwd</c> and
/// nothing else. The parser is the promise — this class adds no field to it.</para>
///
/// <para><b>Three things it has to get right</b>, all of them learned from getting them wrong:
/// subagents fold into the session that spawned them, or a coordinator that fanned out to eleven
/// agents reports as cheap; one response is several lines, so totals are keyed on the response and not
/// on the line; and it reads <em>a profile's</em> transcripts, because <see cref="ProfileRef"/> has
/// travelled into every other reader since T128.</para>
/// </summary>
internal static class SessionIndex
{
    /// <summary>The same ceiling the hour sweep uses, and for the same reason: a tree far larger than
    /// any real one is a bug somewhere else, and a scan that never returns is worse than a short one.</summary>
    public const int MaxFiles = 20_000;

    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Every conversation in this profile's transcripts, newest last turn first.
    /// </summary>
    /// <param name="profile">Whose transcripts. Defaults to the monitored profile, as everywhere.</param>
    /// <param name="projectsDir">A stand-in tree, for fixtures. Supplying one without a
    /// <paramref name="cacheFile"/> forces <see cref="ActivityProfile.SweepCacheMode.Off"/>, so a
    /// fixture can never write the real cache by forgetting to say so.</param>
    /// <param name="mode">How the per-file cache may be used. <c>Rebuild</c> is what makes a cache bug
    /// falsifiable: without a way to re-read everything, a wrong cached row is indistinguishable from a
    /// right one.</param>
    /// <param name="cacheFile">Where the per-file cache lives, for a fixture that needs to exercise it
    /// rather than merely skip it. A cache nothing can drive is a cache nothing can catch.</param>
    public static IReadOnlyList<SessionRow> Load(ProfileRef? profile = null, string? projectsDir = null,
                                                 ActivityProfile.SweepCacheMode mode = ActivityProfile.SweepCacheMode.Use,
                                                 string? cacheFile = null)
        => Load(out _, profile, projectsDir, mode, cacheFile);

    /// <inheritdoc cref="Load(ProfileRef?, string?, ActivityProfile.SweepCacheMode, string?)"/>
    /// <param name="stats">What the pass cost.</param>
    public static IReadOnlyList<SessionRow> Load(out SessionScanStats stats, ProfileRef? profile = null,
                                                 string? projectsDir = null,
                                                 ActivityProfile.SweepCacheMode mode = ActivityProfile.SweepCacheMode.Use,
                                                 string? cacheFile = null)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (projectsDir != null && cacheFile == null) mode = ActivityProfile.SweepCacheMode.Off;
        string root = projectsDir ?? (profile ?? ProfileStore.MonitoredRef).ProjectsDir;
        string cache = cacheFile ?? CachePath;

        Dictionary<string, FileEntry> known = mode == ActivityProfile.SweepCacheMode.Use ? ReadCache(cache) : new(PathComparer);
        var swept = new Dictionary<string, FileEntry>(PathComparer);
        int seen = 0, read = 0, lines = 0;
        long bytes = 0;

        if (Directory.Exists(root))
            foreach (FileInfo f in SafeWalk.Files(root, "*.jsonl"))
            {
                if (++seen > MaxFiles) break;
                if (known.TryGetValue(f.FullName, out FileEntry? hit) &&
                    hit.Bytes == SafeLength(f) && hit.Ticks == SafeTicks(f))
                {
                    swept[f.FullName] = hit;
                    continue;
                }
                FileEntry entry = ScanFile(root, f);
                swept[f.FullName] = entry;
                read++;
                bytes += entry.Read;
                lines += entry.Calls;
            }

        if (mode != ActivityProfile.SweepCacheMode.Off) WriteCache(cache, swept);

        List<SessionRow> rows = Merge(swept.Values);
        stats = new SessionScanStats(root, seen, read, bytes, lines, rows.Count,
                                     System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
        return rows;
    }

    // ---------------------------------------------------------------- one file

    /// <summary>
    /// One transcript reduced to what a session row needs. Like <see cref="ActivityProfile"/>'s hour
    /// sweep this deliberately applies <b>no</b> time window to individual lines, so the entry
    /// describes the file rather than the moment it was scanned and stays valid until the file changes.
    ///
    /// <para>The de-duplication is per file, and that is not an approximation: measured over this
    /// machine's 664 transcripts, 144,930 assistant lines carried 80,836 distinct responses — 44%
    /// repeats, every one of them inside the file that wrote it, and not one response appeared in two
    /// files. So a per-file total can be summed without a second pass, which is what lets the cache
    /// hold totals instead of an id per turn.</para>
    /// </summary>
    private static FileEntry ScanFile(string root, FileInfo file)
    {
        TranscriptTail.Locate(root, file.FullName, out string project, out string session,
                              out _, out string? agent);

        var entry = new FileEntry
        {
            Path = file.FullName,
            Bytes = SafeLength(file),
            Ticks = SafeTicks(file),
            Project = project,
            Session = session,
            Agent = agent != null,
        };

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var models = new List<string>();
        long read = 0;
        try
        {
            foreach (string line in File.ReadLines(file.FullName))
            {
                read += line.Length;
                if (!UsageReport.LooksLikeSample(line)) continue;
                if (!UsageReport.TryParseSample(line, 0, double.MaxValue, out double t, out TokenBits bits,
                                                out string? id, out string? cwd, out string? model))
                    continue;
                // One response, several content blocks, the same usage repeated on each: keyed on the
                // response, a thinking-plus-tool-use turn is one call and one set of tokens.
                if (id != null && !seen.Add(id)) continue;

                entry.Calls++;
                entry.In += bits.Input;
                entry.Out += bits.Output;
                entry.Cc += bits.CacheCreate;
                entry.Cr += bits.CacheRead;
                if (entry.First == 0 || t < entry.First) entry.First = t;
                if (t > entry.Last) entry.Last = t;
                if (entry.Cwd.Length == 0 && cwd is { Length: > 0 }) entry.Cwd = cwd;
                if (model is { Length: > 0 } && !models.Contains(model, StringComparer.Ordinal))
                    models.Add(model);
            }
        }
        catch { /* a transcript being written, or an unreadable file — keep what we got */ }

        entry.Models = models.ToArray();
        entry.Read = read;
        return entry;
    }

    // ---------------------------------------------------------------- many files → one row each

    private static List<SessionRow> Merge(IEnumerable<FileEntry> entries)
    {
        // Project *and* session: a session id is a uuid and collisions are not the worry, but a row
        // that names one project has to be built from files that all name that project.
        var byKey = new Dictionary<string, List<FileEntry>>(StringComparer.Ordinal);
        foreach (FileEntry e in entries)
        {
            if (e.Calls == 0) continue;   // a transcript with no answered turn is not a conversation
            string key = e.Project + " " + e.Session;
            if (!byKey.TryGetValue(key, out List<FileEntry>? group)) byKey[key] = group = new();
            group.Add(e);
        }

        var rows = new List<SessionRow>(byKey.Count);
        foreach (List<FileEntry> group in byKey.Values)
        {
            long input = 0, output = 0, create = 0, cached = 0;
            int calls = 0, agents = 0;
            double first = 0, last = 0;
            string cwd = "";
            var models = new List<string>();
            foreach (FileEntry e in group)
            {
                calls += e.Calls;
                input += e.In; output += e.Out; create += e.Cc; cached += e.Cr;
                if (first == 0 || (e.First > 0 && e.First < first)) first = e.First;
                if (e.Last > last) last = e.Last;
                if (e.Agent) agents++;
                if (cwd.Length == 0) cwd = e.Cwd;
                foreach (string m in e.Models)
                    if (!models.Contains(m, StringComparer.Ordinal)) models.Add(m);
            }

            FileEntry any = group[0];
            rows.Add(new SessionRow(
                any.Session, any.Project,
                cwd.Length > 0 ? ProjectSlug.NameFor(any.Project, cwd) : ProjectSlug.Tail(any.Project),
                first, last, calls, new TokenBits(input, output, create, cached),
                models.ToArray(), agents));
        }

        rows.Sort((a, b) => b.LastUnix.CompareTo(a.LastUnix));
        return rows;
    }

    // ---------------------------------------------------------------- the per-file cache

    /// <summary>One transcript's contribution, valid while its size and mtime are unchanged — the
    /// file-identity key T92 established and T92's cache has used ever since.</summary>
    private sealed class FileEntry
    {
        public string Path { get; set; } = "";
        public long Bytes { get; set; }
        public long Ticks { get; set; }
        public string Project { get; set; } = "";
        public string Session { get; set; } = "";
        /// <summary>A <c>cwd</c> this file carried, kept because it is the only honest way to name the
        /// project — the slug cannot be split back into a folder name without guessing.</summary>
        public string Cwd { get; set; } = "";
        /// <summary>This transcript is a fan-out's, not the conversation's own file.</summary>
        public bool Agent { get; set; }
        public double First { get; set; }
        public double Last { get; set; }
        public int Calls { get; set; }
        public long In { get; set; }
        public long Out { get; set; }
        public long Cc { get; set; }
        public long Cr { get; set; }
        public string[] Models { get; set; } = Array.Empty<string>();

        /// <summary>Bytes this pass read for the entry — 0 when it came from the cache. Not persisted.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public long Read { get; set; }
    }

    /// <summary>
    /// Shared by every profile rather than stored per profile, for the same reason
    /// <see cref="ActivityProfile.SweepCachePath"/> is: an entry is keyed by absolute transcript path
    /// plus a size+mtime fingerprint, so one profile's entry cannot be mistaken for another's, and two
    /// profiles pointing at one tree share the work instead of repeating it. What is per profile is the
    /// <em>question</em> — which tree gets walked — and that is the argument, not the store.
    /// </summary>
    public static string CachePath => Path.Combine(Settings.DataDir, "session-index.json");

    private static Dictionary<string, FileEntry> ReadCache(string path)
    {
        try
        {
            if (!File.Exists(path)) return new(PathComparer);
            var list = JsonSerializer.Deserialize<List<FileEntry>>(File.ReadAllText(path));
            var map = new Dictionary<string, FileEntry>(PathComparer);
            foreach (FileEntry e in list ?? new())
                if (e.Path.Length > 0 && e.Session.Length > 0) map[e.Path] = e;
            return map;
        }
        catch { return new(PathComparer); }
    }

    private static void WriteCache(string path, Dictionary<string, FileEntry> entries)
    {
        // T239: an observing tray reads this store and adds nothing to it.
        if (ProfileStore.Observing) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(entries.Values));
            File.Move(tmp, path, overwrite: true);
        }
        catch { /* the cache is an optimization; failing to write it costs one slow scan */ }
    }

    private static long SafeLength(FileInfo f) { try { return f.Length; } catch { return -1; } }
    private static long SafeTicks(FileInfo f) { try { return f.LastWriteTimeUtc.Ticks; } catch { return -1; } }
}
