using System.Diagnostics;
using System.Text;

namespace ClaudeTray;

/// <summary>One assistant turn seen by the tail reader: when it landed, what it cost, the
/// <c>~/.claude/projects/&lt;slug&gt;</c> directory it landed in, and which session file carried it.</summary>
/// <remarks><paramref name="Project"/> is the encoded slug — unique, and the right grouping key, since
/// two worktrees of the same repo share a folder name but never a slug. <paramref name="Name"/> is
/// that project's folder name resolved from the session's <c>cwd</c>, which is the only way to get it
/// right: the slug encodes path separators and literal dashes identically, so
/// <c>d--Git-acme-claude-tray</c> cannot be split back into "claude-tray" without guessing (and
/// guessing turns <c>...-shio-2026-3</c> into "3"). <paramref name="Session"/> is the transcript's
/// file name. All three are identifiers the path or the header already carries — no content.</remarks>
internal readonly record struct TailSample(
    double Unix, TokenBits Bits, string Project, string Session, string Name);

/// <summary>What the last sweep cost, so "only the bytes appended" is a number and not a claim.
/// <paramref name="Files"/> is what the last <em>full</em> sweep enumerated; <paramref name="Sweeps"/>
/// counts every sweep and <paramref name="FullSweeps"/> only the reconciling ones, so the ratio says
/// how much of the tree the tail is actually walking (T103).</summary>
internal readonly record struct TailStats(
    int Files, int Tracked, long BytesRead, long Samples, int Sweeps, double LastSweepMs, bool Watching,
    int FullSweeps, double LastFullSweepMs);

/// <summary>
/// A byte-level tail over <c>~/.claude/projects/**/*.jsonl</c>: notices an appended turn within a
/// second or two and parses only the bytes that were appended.
///
/// <para>The transcripts are append-only and written as turns land, so the freshest usage signal on
/// the machine is local and needs no API call. What was missing is a cheap way to notice an append:
/// <see cref="UsageReport"/>'s sweep re-reads every in-window <c>*.jsonl</c> on every refresh, which
/// is the right shape for a weekly total and far too expensive to run once a second. This keeps a
/// per-file byte cursor instead, so an append costs the appended bytes.</para>
///
/// <para>A <see cref="FileSystemWatcher"/> is the fast path and a <see cref="SweepFloorMs"/> timer is
/// the floor — the watcher is a hint, not a guarantee (its buffer can overflow, and it can die
/// outright), and a missed event must not freeze the display. Losing the watcher degrades the latency
/// to the floor and nothing else.</para>
///
/// <para>The event also says <em>which</em> file changed, and that is the sweep's work list (T103):
/// walking the whole tree every <see cref="SweepFloorMs"/> is O(all history) and grows forever, while
/// an append is one path. Every <see cref="ReconcileMs"/> the pass is whole anyway — that is the
/// bound on what a dropped event costs, the only way a file nobody announced is discovered, and when
/// cursors for quiet files are retired. No watcher means every pass is whole.</para>
///
/// <para>Two failure modes are designed for rather than discovered. A file that <b>shrank</b>
/// (rotation, a truncated write) has its cursor reset to zero instead of seeking past the end, and the
/// re-read is filtered against the newest timestamp already emitted for that file so the turns don't
/// arrive twice. A <b>partial last line</b> is held back: the cursor only ever advances past a
/// newline, so a half-written turn is read on the next sweep, whole, instead of being dropped as
/// unparseable.</para>
///
/// <para>Privacy (§I.1) is by construction: parsing is
/// <see cref="UsageReport.TryParseSample"/>, the same code the sweep uses, which reads
/// <c>type</c>, <c>timestamp</c>, <c>message.model</c> and <c>message.usage</c> and nothing else.</para>
///
/// <para>Sibling of, not duplicate of, T92: that caches per-file <em>hourly buckets</em> to make the
/// daily profile rebuild cheap; this needs <em>byte</em> resumption to see a single turn inside a
/// second. They share the idea of a file-identity key and nothing else.</para>
/// </summary>
internal sealed class TranscriptTail : IDisposable
{
    /// <summary>Turns older than this are never reported: this is a "what is running now" feed, and
    /// T99's strip only draws ~3 minutes. Also bounds what a fresh start reads back.</summary>
    public const double MaxSampleAgeSeconds = 300;

    /// <summary>Sweep floor — the cadence when the watcher says nothing. Short enough that a lost
    /// watcher is a slower feed rather than a dead one.</summary>
    public const int SweepFloorMs = 3000;

    /// <summary>How often the sweep walks the <b>whole</b> tree rather than only what the watcher
    /// named (T103). Every sweep used to be whole, which is O(all history) — cheap per pass and
    /// growing forever — while a watcher event already carries the one path that changed. The full
    /// pass stays as reconciliation: it is what a dropped event costs, and what prunes cursors for
    /// files that have gone quiet. A watcher that is not running forces every sweep to be full.</summary>
    public const int ReconcileMs = 30_000;

    /// <summary>Coalesces the burst of events one appended turn produces into a single sweep.</summary>
    public const int DebounceMs = 250;

    /// <summary>How far back into an already-existing file the first sweep reads, so opening the
    /// window mid-burst shows the current rate instead of waiting for the next turn. Bounded because
    /// this runs against every transcript touched recently.</summary>
    public const long PrimeBytes = 256 * 1024;

    /// <summary>Raised on a background thread with the turns one sweep found, newest last. Never
    /// raised with an empty batch. Use this or <see cref="Drain"/>, not both — the batch is reported
    /// once.</summary>
    public event Action<IReadOnlyList<TailSample>>? Appended;

    private sealed class Cursor
    {
        public long Offset;          // always sits immediately after a newline once aligned
        public bool NeedsAlign;      // primed mid-line: drop bytes up to the first newline
        public double LastUnix;      // newest turn already reported from this file
        public double SkipUpTo;      // set for one re-read after a shrink, to not report twice
    }

    private readonly string _dir;
    private readonly Dictionary<string, Cursor> _cursors = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TailSample> _pending = new();
    // Response id → when it was reported. Claude Code writes one line per content block of a single
    // API response, each repeating that response's usage verbatim, so counting lines counts a
    // thinking-plus-tool-use turn twice. Pruned to the freshness window, so it stays a few hundred
    // entries even on a busy machine.
    private readonly Dictionary<string, double> _seen = new(StringComparer.Ordinal);
    // Slug → the project's real folder name, learned from any line's cwd. One entry per project.
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    // Paths the watcher named since the last sweep — the cheap sweep's whole work list (T103).
    private readonly HashSet<string> _hot = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _timer;
    private int _sweeping;
    private bool _disposed;

    private int _files, _sweeps, _fullSweeps;
    private long _bytesRead, _samples;
    private double _lastSweepMs, _lastFullSweepMs;
    private long _lastFullTicks;   // Stopwatch timestamp of the last whole-tree pass; 0 = never
    private bool _watching;

    /// <param name="projectsDir">Stand-in for <c>~/.claude/projects</c>; defaults to the real one.</param>
    public TranscriptTail(string? projectsDir = null)
        => _dir = projectsDir ?? Path.Combine(ContextScanner.DefaultClaudeRoot, "projects");

    /// <summary>The directory being tailed — <c>~/.claude/projects</c> unless overridden.</summary>
    public string Root => _dir;

    public TailStats Stats
    {
        get
        {
            lock (_gate)
                return new TailStats(_files, _cursors.Count, _bytesRead, _samples, _sweeps, _lastSweepMs,
                                     _watching, _fullSweeps, _lastFullSweepMs);
        }
    }

    /// <summary>Begin tailing. Safe to call on a machine with no transcripts at all: the sweep simply
    /// finds nothing and the watcher is never armed.</summary>
    public void Start()
    {
        if (_disposed) return;
        StartWatching();
        _timer = new System.Threading.Timer(_ => Sweep(), null, 0, SweepFloorMs);
    }

    /// <summary>Take everything seen since the last call. Empty when nothing is running — which is the
    /// answer, not a failure.</summary>
    public IReadOnlyList<TailSample> Drain()
    {
        lock (_gate)
        {
            if (_pending.Count == 0) return Array.Empty<TailSample>();
            var batch = _pending.ToArray();
            _pending.Clear();
            return batch;
        }
    }

    // The watcher is the low-latency path: an event pulls the next sweep forward past the debounce
    // and names the file to look at. Trusting that path alone would make a dropped event a
    // permanently missed file, which is why it is a *work list* and not the only way in — the
    // reconciling sweep (ReconcileMs) still walks everything, so a lost event costs latency.
    private void StartWatching()
    {
        try
        {
            if (!System.IO.Directory.Exists(_dir)) return;

            _watcher = new FileSystemWatcher(_dir, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,   // several sessions writing at once shouldn't overflow
            };

            void Bump(params string?[] paths)
            {
                lock (_gate)
                    foreach (string? p in paths)
                        if (p is { Length: > 0 }) _hot.Add(p);
                try { _timer?.Change(DebounceMs, SweepFloorMs); } catch { /* disposed mid-event */ }
            }

            _watcher.Changed += (_, e) => Bump(e.FullPath);
            _watcher.Created += (_, e) => Bump(e.FullPath);
            // Both ends: the new name is where the turns are now, and the old one may still carry a
            // cursor that has to be retired rather than left pointing at a path nobody will visit.
            _watcher.Renamed += (_, e) => Bump(e.FullPath, e.OldFullPath);
            // A watcher that has errored is finished. Drop it and keep the floor timer: the feed gets
            // slower, never wrong. Same ruling as ContextWindow's T82 watcher.
            _watcher.Error += (_, _) => StopWatching();

            _watcher.EnableRaisingEvents = true;
            _watching = true;
        }
        catch
        {
            StopWatching();   // no watcher is a supported state
        }
    }

    private void StopWatching()
    {
        try
        {
            if (_watcher is { } w) { w.EnableRaisingEvents = false; w.Dispose(); }
        }
        catch { /* disposal is best-effort */ }
        finally { _watcher = null; _watching = false; }
    }

    // One pass: look at the transcripts that may have grown, read whatever each grew by, report the
    // turns. Re-entrancy matters — the watcher can pull a sweep forward while one is still running.
    private void Sweep()
    {
        if (Interlocked.CompareExchange(ref _sweeping, 1, 0) != 0) return;
        long t0 = Stopwatch.GetTimestamp();
        List<TailSample>? found = null;

        try
        {
            double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            double floor = now - MaxSampleAgeSeconds;
            // Clock skew between the writer's timestamps and ours must not silently drop a fresh turn.
            double ceiling = now + 60;
            DateTime cutoffUtc = DateTimeOffset.FromUnixTimeSeconds((long)floor).UtcDateTime.AddMinutes(-1);

            // Whole tree, or only the paths the watcher named? Full when the watcher is not running
            // (there is no work list to trust), on the first pass, and every ReconcileMs regardless —
            // so a dropped event costs latency and never a stranded file.
            bool full;
            string[] hot;
            lock (_gate)
            {
                full = !_watching || _lastFullTicks == 0 ||
                       Stopwatch.GetElapsedTime(_lastFullTicks).TotalMilliseconds >= ReconcileMs;
                hot = full ? Array.Empty<string>() : _hot.ToArray();
                _hot.Clear();   // taken; anything arriving from here on bumps the next sweep
            }

            int files = 0;
            foreach (FileInfo fi in full ? Enumerate() : Named(hot))
            {
                files++;
                // Metadata only — a transcript nobody has written to since the freshness floor cannot
                // hold a turn worth reporting, so it is never opened.
                if (fi.LastWriteTimeUtc < cutoffUtc) continue;
                ReadAppended(fi, floor, ceiling, ref found);
            }

            if (full) PruneCursors(cutoffUtc);

            lock (_gate)
            {
                // Nothing older than the freshness floor can be reported again, so it need not be
                // remembered. Keeps the dedup set bounded by activity, not by uptime.
                if (_seen.Count > 0)
                    foreach (string old in _seen.Where(kv => kv.Value < floor).Select(kv => kv.Key).ToList())
                        _seen.Remove(old);

                _sweeps++;
                _lastSweepMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                if (full)
                {
                    // `Files` stays the size of the tree, not of the last work list — a cheap sweep
                    // that looked at one file has not discovered that the other 601 are gone.
                    _files = files;
                    _fullSweeps++;
                    _lastFullSweepMs = _lastSweepMs;
                    _lastFullTicks = t0;
                }
                if (found != null)
                {
                    found.Sort((a, b) => a.Unix.CompareTo(b.Unix));
                    _pending.AddRange(found);
                    _samples += found.Count;
                }
            }
        }
        catch { /* a sweep that throws is a skipped sweep, never a crashed tray */ }
        finally { Interlocked.Exchange(ref _sweeping, 0); }

        if (found is { Count: > 0 })
        {
            try { Appended?.Invoke(found); } catch { /* a bad handler must not kill the feed */ }
        }
    }

    private IEnumerable<FileInfo> Enumerate() => SafeWalk.Files(_dir, "*.jsonl");

    // The cheap sweep's work list: the watcher's own paths. A path that has since vanished (renamed
    // away, deleted) is not an error — it is the event that says its cursor can go.
    private IEnumerable<FileInfo> Named(string[] paths)
    {
        foreach (string p in paths)
        {
            FileInfo? fi = null;
            try
            {
                var candidate = new FileInfo(p);
                if (candidate.Exists) fi = candidate;
                else lock (_gate) _cursors.Remove(p);
            }
            catch { /* an unreadable path is skipped, like any other in a sweep */ }
            if (fi != null) yield return fi;
        }
    }

    // Cursors are created per file that recently held a turn, and until T103 they were never removed —
    // a long-lived window accumulated one per transcript it ever saw. A file untouched since the
    // cutoff cannot report anything again (every turn in it is already past the freshness floor), so
    // its cursor is dead weight. Re-priming it later is safe for the same reason: the floor drops what
    // the prime reads back, and `_seen` covers the clock-skew edge.
    private void PruneCursors(DateTime cutoffUtc)
    {
        string[] tracked;
        lock (_gate)
        {
            if (_cursors.Count == 0) return;
            tracked = _cursors.Keys.ToArray();
        }

        List<string>? dead = null;
        foreach (string path in tracked)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.LastWriteTimeUtc < cutoffUtc) (dead ??= new()).Add(path);
            }
            catch { (dead ??= new()).Add(path); }
        }

        if (dead == null) return;
        // Sweeps are serialized, so nothing else creates a cursor while this runs.
        lock (_gate)
            foreach (string path in dead) _cursors.Remove(path);
    }

    private void ReadAppended(FileInfo fi, double floor, double ceiling, ref List<TailSample>? found)
    {
        Cursor cur;
        lock (_gate)
        {
            if (!_cursors.TryGetValue(fi.FullName, out Cursor? existing))
            {
                // First sight of this file. Read back a bounded tail rather than starting at EOF, so a
                // window opened mid-burst has a rate immediately; the freshness floor throws away
                // whatever of that tail is too old to matter. A file smaller than the prime window is
                // read whole, which is also the new-file case.
                long from = Math.Max(0, fi.Length - PrimeBytes);
                existing = new Cursor { Offset = from, NeedsAlign = from > 0 };
                _cursors[fi.FullName] = existing;
            }
            cur = existing;
        }

        if (fi.Length < cur.Offset)
        {
            // Shrank: rotated or truncated mid-write. Seeking past the end would silently stop the
            // file forever, so restart it — and filter the re-read against what was already reported.
            cur.Offset = 0;
            cur.NeedsAlign = false;
            cur.SkipUpTo = cur.LastUnix;
        }
        if (fi.Length == cur.Offset) return;

        byte[] buf;
        int read;
        try
        {
            using var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            long len = Math.Min(fi.Length, fs.Length);
            if (len <= cur.Offset) return;

            int want = (int)Math.Min(len - cur.Offset, int.MaxValue / 2);
            fs.Seek(cur.Offset, SeekOrigin.Begin);
            buf = new byte[want];
            read = fs.ReadAtLeast(buf, want, throwOnEndOfStream: false);
        }
        catch { return; }   // locked, deleted mid-sweep — the next sweep picks it up
        if (read <= 0) return;

        int start = 0;
        if (cur.NeedsAlign)
        {
            // Primed mid-line: everything before the first newline is a fragment of a turn we are not
            // resuming, and dropping it also keeps the UTF-8 decode on a character boundary.
            int nl = Array.IndexOf(buf, (byte)'\n', 0, read);
            if (nl < 0) { cur.Offset += read; return; }
            start = nl + 1;
            cur.NeedsAlign = false;
        }

        // Advance only past the last complete line, so a half-written turn waits for its newline
        // instead of being parsed as garbage and lost.
        int lastNl = read > start ? Array.LastIndexOf(buf, (byte)'\n', read - 1, read - start) : -1;
        if (lastNl < 0)
        {
            cur.Offset += start;   // nothing complete yet; the fragment is re-read next sweep
            return;
        }

        string text = Encoding.UTF8.GetString(buf, start, lastNl + 1 - start);
        cur.Offset += lastNl + 1;

        lock (_gate) _bytesRead += read;

        string project = ProjectOf(fi);
        string session = System.IO.Path.GetFileNameWithoutExtension(fi.Name);
        double skip = cur.SkipUpTo;
        cur.SkipUpTo = 0;

        foreach (string line in text.Split('\n'))
        {
            if (line.Length == 0) continue;
            if (!UsageReport.TryParseSample(line, floor, ceiling, out double t, out TokenBits bits,
                                            out string? id, out string? cwd))
                continue;
            if (cwd is { Length: > 0 })
                lock (_gate)
                {
                    if (!_names.ContainsKey(project) && ResolveName(project, cwd) is { Length: > 0 } n)
                        _names[project] = n;
                }
            if (t > cur.LastUnix) cur.LastUnix = t;
            if (t <= skip) continue;   // already reported before the file shrank
            if (id != null)
            {
                lock (_gate)
                {
                    if (!_seen.TryAdd(id, t)) continue;   // another content block of the same response
                }
            }
            string name;
            lock (_gate) name = _names.TryGetValue(project, out string? n) ? n : ProjectSlug.Tail(project);
            (found ??= new()).Add(new TailSample(t, bits, project, session, name));
        }
    }

    // The transcript path already encodes the project directory — the same key ContextScanner groups
    // by — so attribution is free and needs no content.
    private static string ProjectOf(FileInfo fi)
        => fi.Directory?.Name ?? "?";

    /// <summary>
    /// The project's folder name, resolved by matching the slug against the recorded <c>cwd</c> —
    /// <see cref="ProjectSlug.RootFor"/>, which since T105 is the app's only reader of that encoding.
    ///
    /// <para>Naïvely taking <c>GetFileName(cwd)</c> is wrong, and wrong in a way that only shows up in
    /// use: <c>cwd</c> is the working directory <em>of that turn</em>, so any <c>cd</c> inside the
    /// session renames the project — a session that ran one command in <c>docs/_preview</c> started
    /// reporting itself as "_preview". The slug, however, encodes the session's <b>root</b>, so
    /// walking the cwd up its parents until one encodes to the slug recovers the root exactly.</para>
    /// </summary>
    private static string? ResolveName(string slug, string cwd)
    {
        if (ProjectSlug.RootFor(slug, cwd) is not { Length: > 0 } root) return null;
        string name = System.IO.Path.GetFileName(root.TrimEnd('\\', '/'));
        return name.Length > 0 ? name : root;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWatching();
        try { _timer?.Dispose(); } catch { /* best-effort */ }
        _timer = null;
    }
}
