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
/// <param name="Prompt">The text that opened the conversation, truncated to
/// <see cref="SessionIndex.PromptChars"/> — §I.1's amended exception (T334), and the only field here a
/// person wrote. Empty when the transcript carries no readable opening prompt.</param>
/// <param name="Title">The title Claude Code generated for the conversation, same cap (T336). Derived
/// from content rather than being content, which makes it the narrower half of that exception and the
/// one a reader would rather have. Empty for the transcripts that carry none — 135 of 664 here.</param>
/// <param name="Efforts">Calls at each effort level the transcript named, unrendered — the mix rather
/// than a winner, because the dearer level is usually the minority and a vote would drop it (T331).
/// Empty for a session whose lines named none.</param>
/// <param name="Minutes">Tokens per unix minute, so a sweep can find the heaviest five hours across
/// exactly the sessions on screen rather than across whatever the index happens to hold (T332). A
/// session's own peak is not a reading — most conversations are shorter than the window — which is
/// why this is the series and not a figure.</param>
internal readonly record struct SessionRow(
    string Session, string Project, string Name,
    double FirstUnix, double LastUnix, int Calls, TokenBits Bits, string[] Models, int Agents,
    string Prompt = "", string Title = "",
    IReadOnlyDictionary<string, int>? Efforts = null,
    IReadOnlyDictionary<long, long>? Minutes = null)
{
    /// <summary>Wall-clock seconds from the first answered turn to the last. Zero for a session with a
    /// single turn, which is a real answer and not a missing one.</summary>
    public double Seconds => Math.Max(0, LastUnix - FirstUnix);
}

/// <summary>
/// One line a person actually wrote, told apart from the many <c>user</c> lines that are the harness
/// writing about itself. The unit both readers of §I.1's amended exception share: the Sessions list
/// takes the first one of these in a transcript, and <see cref="SessionTasks"/> cuts a conversation at
/// every one of them.
/// </summary>
/// <param name="Text">Flattened and cut to <see cref="SessionIndex.PromptChars"/> — never longer, and
/// never stored longer.</param>
/// <param name="Chars">How long it was <em>before</em> the cut, which is what a drill-down node may
/// say about a prompt it is not allowed to quote.</param>
/// <param name="Command">It was a slash command, so <paramref name="Text"/> is its name and arguments
/// rather than free text — and a person starting work, which is what makes it a task boundary.</param>
internal readonly record struct PersonAsk(string Text, int Chars, bool Command);

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

    /// <summary>
    /// How much of a conversation's opening prompt may be kept — §I.1's one amended exception (T334).
    ///
    /// <para>A named constant rather than a literal at the call site because it <b>is</b> the safety
    /// margin: it bounds what the store holds, not merely what the list draws, so the truncation
    /// happens before anything is written and what is not stored cannot leak from the store. Changing
    /// this number changes the promise, which is the reason it is spelled once.</para>
    /// </summary>
    public const int PromptChars = 200;

    /// <summary>
    /// Cache schema generation. Bumped whenever a cached entry gains a field <em>or an existing field
    /// changes meaning</em> — T334 added the prompt at 2 and taught it to skip the harness's own
    /// <c>user</c> lines at 3, and gained the title at 4 — and the second of those is the instructive one: the files had not
    /// changed, so a size+mtime key kept serving the old answer and the fix was invisible on every row
    /// that had not been touched since. 5 splits the cache write by TTL (T330), which is the same trap:
    /// the totals were already right, so nothing on an untouched row would have looked wrong.
    /// 6 keeps the effort each call ran at (T331), which no earlier entry recorded at all, and 7 the
    /// per-minute token series the heaviest-window sweep runs over (T332).
    /// See <see cref="FileEntry.V"/>.
    /// </summary>
    private const int Schema = 7;

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
            V = Schema,
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
                // The one field a person wrote, taken once, from the conversation's own transcript
                // and never from an agent's (§I.1's exception is about the prompt *you* typed).
                if (!entry.Agent && entry.Prompt.Length == 0 && TryReadPersonAsk(line, out PersonAsk ask))
                    entry.Prompt = ask.Text;

                // The title, taken *last* rather than once: Claude Code rewrites it as a conversation
                // turns out to be about something else, and it did so in 322 of the 529 transcripts
                // here that carry one. Keeping the first would label a third of them by what they
                // started as (T336).
                if (!entry.Agent && TryReadTitle(line, out string title))
                    entry.Title = title;

                if (!UsageReport.LooksLikeSample(line)) continue;
                if (!UsageReport.TryParseSample(line, 0, double.MaxValue, out double t, out TokenBits bits,
                                                out string? id, out string? cwd, out string? model,
                                                out string? effort))
                    continue;
                // One response, several content blocks, the same usage repeated on each: keyed on the
                // response, a thinking-plus-tool-use turn is one call and one set of tokens.
                if (id != null && !seen.Add(id)) continue;

                entry.Calls++;
                entry.In += bits.Input;
                entry.Out += bits.Output;
                entry.Cc += bits.CacheCreate;
                entry.Cr += bits.CacheRead;
                entry.C1h += bits.CacheCreate1h;
                entry.C5m += bits.CacheCreate5m;
                if (entry.First == 0 || t < entry.First) entry.First = t;
                if (t > entry.Last) entry.Last = t;
                if (entry.Cwd.Length == 0 && cwd is { Length: > 0 }) entry.Cwd = cwd;
                if (model is { Length: > 0 } && !models.Contains(model, StringComparer.Ordinal))
                    models.Add(model);
                // Counted per call, not per session: the mix is the reading, and a session that ran
                // ten of fifty calls at the dear level is not an instance of that level (T331).
                if (effort is { Length: > 0 })
                    entry.Efforts[effort] = entry.Efforts.TryGetValue(effort, out int had) ? had + 1 : 1;
                // The minute this turn landed in, against the work it carried — the series the
                // heaviest-window sweep runs over (T332). Cache reads are out here for the same
                // reason they are out of every token figure shown: they are two orders of magnitude
                // larger than the work and would be the only thing the peak ever measured.
                long spend = bits.Input + bits.Output + bits.CacheCreate;
                if (spend > 0)
                {
                    string minute = ((long)(t / 60)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    entry.Minutes[minute] = entry.Minutes.TryGetValue(minute, out long was) ? was + spend : spend;
                }
            }
        }
        catch { /* a transcript being written, or an unreadable file — keep what we got */ }

        entry.Models = models.ToArray();
        entry.Read = read;
        return entry;
    }

    /// <summary>
    /// The text that opened a conversation, truncated to <see cref="PromptChars"/>.
    ///
    /// <para><b>This is the only content reader in the app, and it is deliberately not in the
    /// parser.</b> <see cref="UsageReport.TryParseSample(string, double, double, out double, out TokenBits, out string?, out string?, out string?)"/>
    /// is the promise every other transcript reader stands on and it still returns no content; putting
    /// this beside it would quietly widen what all of them may see. It lives here, named for exactly
    /// what it does, so an audit of §I.1 has one place to look.</para>
    ///
    /// <para>Refused, not merely skipped: a <c>tool_result</c> (a user-typed line is not the only kind
    /// of <c>user</c> line), a sidechain — a subagent's prompt is the app's own fan-out and not
    /// something the person typed — and a meta line, which is Claude Code talking to itself. The first
    /// line that survives all three is the opening prompt, and nothing after it is read.</para>
    ///
    /// <para>Truncation happens <em>here</em>, before the value reaches an entry, so the cache holds
    /// no more than the list may show. Newlines and runs of whitespace collapse to single spaces
    /// because the destination is one row of a table, and a pasted block would otherwise arrive as a
    /// hundred blank characters.</para>
    /// </summary>
    internal static bool TryReadPersonAsk(string line, out PersonAsk ask)
    {
        ask = default;
        // A message typed while a turn was already running is not a `user` line at all: Claude Code
        // files it as an `attachment` of type `queued_command`, carrying the prompt in its own array.
        // Measured, that is where every mid-turn ask in this repository's own transcripts lives, and a
        // reader that only knows `user` lines reports a conversation of five asks as one (T329).
        if (line.Contains("\"queued_command\"", StringComparison.Ordinal))
            return TryReadQueued(line, out ask);

        // Ordinal rejects first: this runs on every line of every transcript, and all but a handful
        // of them can be dismissed without JsonDocument.
        if (!line.Contains("\"type\":\"user\"", StringComparison.Ordinal)) return false;
        if (line.Contains("\"tool_result\"", StringComparison.Ordinal)) return false;
        if (line.Contains("\"isSidechain\":true", StringComparison.Ordinal)) return false;
        if (line.Contains("\"isMeta\":true", StringComparison.Ordinal)) return false;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(line);
            System.Text.Json.JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("type", out var ty) || ty.GetString() != "user") return false;
            if (!root.TryGetProperty("message", out var msg) ||
                msg.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
            if (!msg.TryGetProperty("content", out var content)) return false;

            string? text = null;
            if (content.ValueKind == System.Text.Json.JsonValueKind.String)
                text = content.GetString();
            else if (content.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (System.Text.Json.JsonElement block in content.EnumerateArray())
                {
                    // An image block is a person asking, but not in words this may read — and a
                    // content array that holds one is not a text prompt.
                    if (block.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    if (!block.TryGetProperty("type", out var bt)) continue;
                    if (bt.GetString() == "image") return false;
                    if (bt.GetString() != "text") continue;
                    if (block.TryGetProperty("text", out var tx)) text = tx.GetString();
                    break;   // the first text block is the prompt; the rest is not a title
                }

            if (text is not { Length: > 0 }) return false;

            bool command = Tag(text, "command-name") is { Length: > 0 };
            string flat = Typed(text);
            if (flat.Length == 0) return false;
            ask = new PersonAsk(Cut(flat), flat.Length, command);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// The title Claude Code generated for a conversation, capped like everything else here.
    ///
    /// <para>Its own line — <c>{"type":"ai-title","aiTitle":"…","sessionId":"…"}</c> — and the reason
    /// T334 said no title existed is that the search that looked for one looked for
    /// <c>"type":"summary"</c>, which appears nowhere. Measured after the correction: 529 of this
    /// machine's 664 transcripts carry a title.</para>
    ///
    /// <para>It is <b>derived from</b> content rather than <b>being</b> content, which makes it the
    /// narrower half of §I.1's amended exception and the better half to show — a label about a
    /// conversation instead of the words a person typed. It is still inside the exception, and it is
    /// still capped: a generated string is short in practice, and a bound that holds only for
    /// well-behaved input is the kind the first badly-behaved one finds.</para>
    /// </summary>
    private static bool TryReadTitle(string line, out string title)
    {
        title = "";
        if (!line.Contains("\"ai-title\"", StringComparison.Ordinal)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(line);
            System.Text.Json.JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("type", out var ty) || ty.GetString() != "ai-title") return false;
            if (!root.TryGetProperty("aiTitle", out var t) || t.GetString() is not { Length: > 0 } s)
                return false;
            title = Cut(Flatten(s));
            return title.Length > 0;
        }
        catch { return false; }
    }

    /// <summary>A queued command: the same question — what did a person type — asked of the other
    /// shape it is stored in. Same classification and same cap, so the two routes cannot answer
    /// differently.</summary>
    private static bool TryReadQueued(string line, out PersonAsk ask)
    {
        ask = default;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("attachment", out var att) ||
                att.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
            if (!att.TryGetProperty("type", out var at) || at.GetString() != "queued_command") return false;
            if (!att.TryGetProperty("prompt", out var arr) ||
                arr.ValueKind != System.Text.Json.JsonValueKind.Array) return false;

            foreach (System.Text.Json.JsonElement block in arr.EnumerateArray())
            {
                if (block.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                if (!block.TryGetProperty("type", out var bt) || bt.GetString() != "text") continue;
                if (block.TryGetProperty("text", out var tx) && tx.GetString() is { Length: > 0 } text)
                {
                    bool command = Tag(text, "command-name") is { Length: > 0 };
                    string flat = Typed(text);
                    if (flat.Length == 0) return false;
                    ask = new PersonAsk(Cut(flat), flat.Length, command);
                    return true;
                }
                break;
            }
            return false;
        }
        catch { return false; }
    }

    /// <summary>
    /// What the <em>person</em> typed, out of a <c>user</c> line that is often not that at all.
    ///
    /// <para>Measured on the first capture of this column, where nine rows in ten read
    /// <c>&lt;command-message&gt;loop&lt;/command-message&gt; &lt;command-name&gt;/l…</c> or
    /// <c>&lt;ide_opened_file&gt;The user opened the file d:\…</c>. Both are the harness writing a
    /// <c>user</c> line about itself, and showing them made the column worse than empty: it looked
    /// like content and carried none.</para>
    ///
    /// <para>Two rules. A <b>slash command</b> is real input, so it is reassembled from the two tags
    /// that hold it — the name and its arguments — and reads as the person typed it. Anything else
    /// that <em>opens</em> with a tag is the harness announcing something (an IDE file, a system
    /// reminder, a local command's stdout), and the honest answer is to decline this line entirely so
    /// the scan keeps looking for the first one a person wrote.</para>
    /// </summary>
    private static string Typed(string text)
    {
        if (Tag(text, "command-name") is { Length: > 0 } name)
            return Flatten(Tag(text, "command-args") is { Length: > 0 } args ? name + " " + args : name);
        string head = text.TrimStart();
        if (head.StartsWith('<')) return "";
        // Two the tag rule cannot catch, because Claude Code writes them as plain prose: the caveat
        // it prepends to a resumed conversation, and the note an interrupt leaves behind.
        if (head.StartsWith("Caveat:", StringComparison.Ordinal)) return "";
        if (head.StartsWith("[Request interrupted", StringComparison.Ordinal)) return "";
        return Flatten(text);
    }

    private static string? Tag(string text, string name)
    {
        int open = text.IndexOf('<' + name + '>', StringComparison.Ordinal);
        if (open < 0) return null;
        open += name.Length + 2;
        int close = text.IndexOf("</" + name + '>', open, StringComparison.Ordinal);
        return close < 0 ? null : text[open..close];
    }

    /// <summary>One row's worth of whitespace: newlines and runs collapse to single spaces, because the
    /// destination is one line of a table and a pasted block would otherwise arrive as a hundred blank
    /// characters. Length-preserving in the sense that matters — this is what <see cref="PersonAsk.Chars"/>
    /// counts, so "85 characters" describes what a person typed and not how they laid it out.</summary>
    private static string Flatten(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        bool space = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>The cap, applied in one place. An ellipsis so a truncated prompt is visibly truncated
    /// rather than quietly ending mid-word.</summary>
    private static string Cut(string flat)
        => flat.Length <= PromptChars ? flat : flat[..PromptChars].TrimEnd() + "…";

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
            long input = 0, output = 0, create = 0, cached = 0, hour = 0, five = 0;
            int calls = 0, agents = 0;
            double first = 0, last = 0;
            string cwd = "", prompt = "", title = "";
            var models = new List<string>();
            var efforts = new Dictionary<string, int>(StringComparer.Ordinal);
            var minutes = new Dictionary<long, long>();
            foreach (FileEntry e in group)
            {
                calls += e.Calls;
                input += e.In; output += e.Out; create += e.Cc; cached += e.Cr;
                hour += e.C1h; five += e.C5m;
                if (first == 0 || (e.First > 0 && e.First < first)) first = e.First;
                if (e.Last > last) last = e.Last;
                if (e.Agent) agents++;
                if (cwd.Length == 0) cwd = e.Cwd;
                if (prompt.Length == 0) prompt = e.Prompt;
                if (title.Length == 0) title = e.Title;
                foreach (string m in e.Models)
                    if (!models.Contains(m, StringComparer.Ordinal)) models.Add(m);
                EffortMix.Merge(efforts, e.Efforts);
                foreach (KeyValuePair<string, long> kv in e.Minutes)
                    if (long.TryParse(kv.Key, System.Globalization.NumberStyles.Integer,
                                      System.Globalization.CultureInfo.InvariantCulture, out long min))
                        minutes[min] = minutes.TryGetValue(min, out long had) ? had + kv.Value : kv.Value;
            }

            FileEntry any = group[0];
            rows.Add(new SessionRow(
                any.Session, any.Project,
                cwd.Length > 0 ? ProjectSlug.NameFor(any.Project, cwd) : ProjectSlug.Tail(any.Project),
                first, last, calls, new TokenBits(input, output, create, cached, hour, five),
                models.ToArray(), agents, prompt, title, efforts, minutes));
        }

        rows.Sort((a, b) => b.LastUnix.CompareTo(a.LastUnix));
        return rows;
    }

    // ---------------------------------------------------------------- the per-file cache

    /// <summary>One transcript's contribution, valid while its size and mtime are unchanged — the
    /// file-identity key T92 established and T92's cache has used ever since.</summary>
    private sealed class FileEntry
    {
        /// <summary>Schema generation. An entry written before a field existed deserializes with that
        /// field empty and its size+mtime still matching, so it would be served as fresh forever — the
        /// one way this cache can be silently wrong. Bumping this retires the old entries instead.</summary>
        public int V { get; set; }

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
        /// <summary>Already truncated to <see cref="PromptChars"/> — see <see cref="TryReadPersonAsk"/>.</summary>
        public string Prompt { get; set; } = "";
        /// <summary>The last title the transcript carried, already truncated — see <see cref="TryReadTitle"/>.</summary>
        public string Title { get; set; } = "";
        public double First { get; set; }
        public double Last { get; set; }
        public int Calls { get; set; }
        public long In { get; set; }
        public long Out { get; set; }
        public long Cc { get; set; }
        public long Cr { get; set; }
        /// <summary>Of <see cref="Cc"/>, what the transcript attributed to each cache TTL (T330).</summary>
        public long C1h { get; set; }
        /// <inheritdoc cref="C1h"/>
        public long C5m { get; set; }
        public string[] Models { get; set; } = Array.Empty<string>();
        /// <summary>Calls at each effort level this file recorded — see <see cref="EffortMix"/> (T331).</summary>
        public Dictionary<string, int> Efforts { get; set; } = new(StringComparer.Ordinal);
        /// <summary>Tokens per unix minute, keyed as text because that is what a JSON object key is.
        /// Twelve thousand buckets across this machine's whole history, against eighty-three thousand
        /// turns — the resolution T332 settled on. Excludes cache reads.</summary>
        public Dictionary<string, long> Minutes { get; set; } = new(StringComparer.Ordinal);

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
                if (e.V == Schema && e.Path.Length > 0 && e.Session.Length > 0) map[e.Path] = e;
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
