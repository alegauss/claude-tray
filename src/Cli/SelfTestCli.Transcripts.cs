using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeTray;

/// <summary>
/// Part of <see cref="SelfTestCli"/> — the transcripts, and every reading swept out of them: the byte
/// cursor, the activity grid, one row per conversation, the tasks under one, and what a conversation was
/// worth.
///
/// <para>Split from the quota arithmetic next door because the two share nothing but a folder: that one is
/// pure over synthetic readings, this one builds a <c>projects/</c> tree and walks it. One class in several
/// files, on this repository's own rule (T133–T134, applied to the suite by T381).</para>
/// </summary>
internal static partial class SelfTestCli
{

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

    /// <summary>
    /// T330. The cache TTL split the transcript states and <see cref="TokenBits"/> now carries.
    ///
    /// <para><c>cache_creation_input_tokens</c> was the whole of what this app knew about a cache write,
    /// and the two are not priced alike: against a model's base input rate a five-minute write is 1.25×
    /// and a one-hour write 2×, so one blended number misses the write component by nearly two. Measured
    /// over this machine's transcripts when the split was first read: <b>804.5M</b> one-hour tokens
    /// against <b>37.7M</b> five-minute ones, across 146,428 lines carrying a write — Claude Code writes
    /// almost entirely at the one-hour TTL, which is the expensive one.</para>
    ///
    /// <para><b>The total stays authoritative and the split describes it.</b> Every one of those 146,428
    /// lines carried the object and summed to the total exactly, so the case this fixes is not one the
    /// tree can currently produce — which is precisely T248's argument for a synthetic fixture. A line
    /// with the total and no object yields <c>0/0</c>, and the remainder is reported by
    /// <see cref="TokenBits.CacheCreateUnattributed"/> rather than folded into either rate: guessing
    /// five-minute would price the most expensive component at the cheapest number, and guessing
    /// one-hour would do the reverse, both silently.</para>
    /// </summary>
    private static void CacheTtl()
    {
        // Assembled the way every fixture here is: the reader under test is handed the shape, not a file.
        static string Line(string usage) =>
            "{\"type\":\"assistant\",\"timestamp\":\"2026-08-11T12:00:00.000Z\",\"requestId\":\"ttl\"," +
            "\"message\":{\"model\":\"claude-selftest\",\"usage\":{" + usage + "}}}";

        const string Both = "\"input_tokens\":10,\"output_tokens\":20,\"cache_read_input_tokens\":30," +
                            "\"cache_creation_input_tokens\":100," +
                            "\"cache_creation\":{\"ephemeral_1h_input_tokens\":90,\"ephemeral_5m_input_tokens\":10}";
        const string NoObject = "\"input_tokens\":10,\"output_tokens\":20,\"cache_read_input_tokens\":30," +
                                "\"cache_creation_input_tokens\":100";
        const string Short = "\"input_tokens\":10,\"output_tokens\":20,\"cache_read_input_tokens\":30," +
                             "\"cache_creation_input_tokens\":100," +
                             "\"cache_creation\":{\"ephemeral_1h_input_tokens\":40}";

        (string What, string Usage, long Hour, long Five, long Unattributed)[] cases =
        {
            ("a line stating both TTLs", Both, 90, 10, 0),
            ("a line carrying only the total", NoObject, 0, 0, 100),
            ("a line whose split falls short of its total", Short, 40, 0, 60),
        };

        var wrong = new List<string>();
        foreach ((string what, string usage, long hour, long five, long unattributed) in cases)
        {
            if (!UsageReport.TryParseSample(Line(usage), 0, double.MaxValue, out _, out TokenBits b,
                                            out _, out _))
            {
                wrong.Add($"{what} → not parsed at all");
                continue;
            }
            if (b.CacheCreate1h != hour || b.CacheCreate5m != five || b.CacheCreateUnattributed != unattributed)
                wrong.Add($"{what} → 1h={b.CacheCreate1h} 5m={b.CacheCreate5m} " +
                          $"unattributed={b.CacheCreateUnattributed}, expected {hour}/{five}/{unattributed}");
            // The split describes the total; counting it again would inflate every token figure in the app.
            if (b.Total != 160)
                wrong.Add($"{what} → Total {b.Total}, expected 160 — the split is being double-counted");
        }
        Check($"the cache write is read at the TTL the transcript states ({cases.Length} shapes)",
              wrong.Count == 0, string.Join("; ", wrong));

        // Folding is where a split is quietly lost: the totals stay right, so nothing downstream looks
        // wrong. Two adds and the subtree has to carry both halves.
        var node = new TaskNode(TaskKind.Prompt, "s", "", 0, 0, 0, 1,
                                new TokenBits(0, 0, 100, 0, 90, 10), new List<TaskNode>
                                {
                                    new(TaskKind.Command, "a", "", 0, 0, 0, 1,
                                        new TokenBits(0, 0, 50, 0, 20, 30), new List<TaskNode>()),
                                });
        TokenBits sum = node.Subtree;
        Check("and it survives being folded into a subtree",
              sum.CacheCreate == 150 && sum.CacheCreate1h == 110 && sum.CacheCreate5m == 40,
              $"{sum.CacheCreate} written, {sum.CacheCreate1h} at 1h, {sum.CacheCreate5m} at 5m — " +
              "expected 150/110/40");
    }

    /// <summary>
    /// T346. What a conversation was worth at API list prices.
    ///
    /// <para><b>The arithmetic is asserted against a hand-computed figure</b>, because every part of it is
    /// a multiplier somebody could plausibly get wrong in the same direction: input and output at each
    /// model's own rate, a cache read at 0.1× the input rate, a five-minute write at 1.25× and a one-hour
    /// write at 2×. A single blended cache rate passes a "does it produce a number" check and is wrong by
    /// nearly two on the component this machine writes almost all of (T330).</para>
    ///
    /// <para><b>Per model, or the figure is meaningless.</b> Two models in one conversation is the case the
    /// split exists for, so the fixture is exactly that — and the check that the cheap model's tokens are
    /// not priced at the dear model's rate is the whole of why <c>SessionRow.PerModel</c> is persisted at
    /// all.</para>
    ///
    /// <para><b>An unpriced model is reported, never folded in.</b> A model id the table has no row for is
    /// the shape that arrives on its own — the next release names one — and a pricer that silently drops it
    /// reads as a cheaper week rather than as a partial answer.</para>
    /// </summary>
    private static void ListPricing()
    {
        // One model, one of each kind of token, against a rate card read off the table by hand.
        // claude-opus-5 is $5/MTok in, $25/MTok out.
        var opusOnly = new Dictionary<string, TokenBits>(StringComparer.Ordinal)
        {
            // 1M in, 1M out, 1M cache read, 1M written (600k at 1h, 400k at 5m)
            ["claude-opus-5"] = new TokenBits(1_000_000, 1_000_000, 1_000_000, 1_000_000, 600_000, 400_000),
        };
        // 5 + 25 + (1M read × 0.1 × 5) + (0.6M × 2 × 5) + (0.4M × 1.25 × 5) = 5 + 25 + 0.5 + 6 + 2.5
        Near("a conversation is priced per token kind, each at its own multiplier",
             ListPrices.Of(opusOnly).Dollars, 39.0, 1e-9);

        // The same tokens on Haiku 4.5 ($1/$5) — a fifth of the input rate, so every component scales.
        var haikuOnly = new Dictionary<string, TokenBits>(StringComparer.Ordinal)
        {
            ["claude-haiku-4-5"] = new TokenBits(1_000_000, 1_000_000, 1_000_000, 1_000_000, 600_000, 400_000),
        };
        Near("and a cheaper model prices the same tokens lower",
             ListPrices.Of(haikuOnly).Dollars, 7.8, 1e-9);

        // Both in one conversation: the sum, which is the figure a row shows and the reason the split is
        // persisted. A pricer that took one model for the whole row would read 39 or 7.8, never 46.8.
        var mixed = new Dictionary<string, TokenBits>(StringComparer.Ordinal)
        {
            ["claude-opus-5"] = opusOnly["claude-opus-5"],
            ["claude-haiku-4-5"] = haikuOnly["claude-haiku-4-5"],
        };
        ListPrices.Equivalent both = ListPrices.Of(mixed);
        Near("and two models in one conversation are priced separately and summed",
             both.Dollars, 46.8, 1e-9);
        Check("a fully-priced conversation says so", both.Complete && both.UnpricedTokens == 0,
              $"{both.UnpricedTokens} unpriced");

        // A dated id resolves through its family, and through the *longest* matching prefix: `claude-opus-4`
        // is a different generation at three times the rate, so a naive first-match would price 4.5 at it.
        Near("a dated model id is priced through its family",
             ListPrices.Of(new Dictionary<string, TokenBits>(StringComparer.Ordinal)
             { ["claude-opus-4-5-20251101"] = new TokenBits(1_000_000, 0, 0, 0, 0, 0) }).Dollars,
             5.0, 1e-9);

        // The case that must not be silent.
        ListPrices.Equivalent partial = ListPrices.Of(new Dictionary<string, TokenBits>(StringComparer.Ordinal)
        {
            ["claude-opus-5"] = new TokenBits(1_000_000, 0, 0, 0, 0, 0),
            ["claude-not-a-model-yet"] = new TokenBits(9_000_000, 0, 0, 0, 0, 0),
        });
        Check("an unpriced model leaves the figure visibly partial rather than cheap",
              !partial.Complete && partial.UnpricedTokens == 9_000_000 && Math.Abs(partial.Dollars - 5.0) < 1e-9,
              $"complete={partial.Complete} unpriced={partial.UnpricedTokens} dollars={partial.Dollars}");
        Check("and a conversation of only unknown models is priced at nothing, not at zero dollars",
              ListPrices.Of(new Dictionary<string, TokenBits>(StringComparer.Ordinal)
              { ["nope"] = new TokenBits(1_000_000, 0, 0, 0, 0, 0) }).PricedTokens == 0);

        // A cache write with no TTL stated is priced at the DEARER rate (T330's `Unattributed`). Guessing
        // five-minute here would price the component this machine writes almost all of at the cheap rate.
        Near("a cache write with no TTL stated is priced at the one-hour rate",
             ListPrices.Of(new Dictionary<string, TokenBits>(StringComparer.Ordinal)
             { ["claude-opus-5"] = new TokenBits(0, 0, 1_000_000, 0, 0, 0) }).Dollars,
             10.0, 1e-9);
    }

    /// <summary>
    /// T331. The effort a turn ran at, and the mix that is shown instead of a winner.
    ///
    /// <para>Effort is the largest lever on what a task costs and it does not work the way it sounds:
    /// it buys <em>more calls</em>, not longer answers. So a session's mix is the reading, and the
    /// dear level is usually the minority — 127,292 calls at <c>high</c> against 6,254 at
    /// <c>xhigh</c> over this machine's transcripts. A majority vote would round the expensive part
    /// away, which is why <see cref="EffortMix"/> keeps every level that ran.</para>
    ///
    /// <para><b>Two things a scan of the tree cannot hold up.</b> The field sits at the line's
    /// <em>root</em>, not inside <c>message</c> beside the model id — the one detail about reading it
    /// that is not guessable, and a fixture is the only way to assert it. And three of the five
    /// levels have never run here, so the ladder's order is asserted against synthetic input or not
    /// at all: <c>xhigh</c> between <c>high</c> and <c>max</c> is exactly the order no alphabetical
    /// sort produces.</para>
    /// </summary>
    private static void Effort()
    {
        static string Line(string body) =>
            "{\"type\":\"assistant\",\"timestamp\":\"2026-08-11T12:00:00.000Z\"," + body +
            "\"message\":{\"model\":\"claude-selftest\",\"usage\":{\"input_tokens\":10," +
            "\"output_tokens\":20,\"cache_read_input_tokens\":0,\"cache_creation_input_tokens\":0}}}";

        (string What, string Body, string? Expected)[] lines =
        {
            ("an effort at the line's root", "\"effort\":\"xhigh\",", "xhigh"),
            ("a line naming none", "", null),
            // The shape that would pass a reader looking in the wrong object: the model id lives in
            // `message`, and effort does not.
            ("an effort nested where the model id lives", "\"cwd\":\"D:\\\\x\",", null),
        };

        var wrong = new List<string>();
        foreach ((string what, string body, string? expected) in lines)
        {
            if (!UsageReport.TryParseSample(Line(body), 0, double.MaxValue, out _, out _, out _, out _,
                                            out _, out string? got))
                wrong.Add($"{what} → not parsed");
            else if (got != expected)
                wrong.Add($"{what} → '{got ?? "null"}', expected '{expected ?? "null"}'");
        }
        Check($"the effort a turn ran at is read from the line's root ({lines.Length} shapes)",
              wrong.Count == 0, string.Join("; ", wrong));

        // The ladder is an order, and it is not the alphabet: `xhigh` sits between `high` and `max`.
        var mix = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["max"] = 1, ["high"] = 40, ["xhigh"] = 10, ["low"] = 2,
        };
        string[] order = EffortMix.Ordered(mix).Select(p => p.Name).ToArray();
        Check("and a mix is ordered by the ladder, not alphabetically",
              order.SequenceEqual(new[] { "low", "high", "xhigh", "max" }), string.Join(" ", order));

        // A level this app has never seen is news: it is kept and shown as the transcript spelled it,
        // rather than dropped for not being on the ladder.
        var unknown = new Dictionary<string, int>(StringComparer.Ordinal) { ["high"] = 1, ["ludicrous"] = 1 };
        Check("an effort the ladder does not name is still counted and still shown",
              EffortMix.Ordered(unknown).Select(p => p.Name).SequenceEqual(new[] { "high", "ludicrous" }) &&
              EffortMix.Line(unknown).Contains("ludicrous", StringComparison.Ordinal),
              EffortMix.Line(unknown));

        // The whole point: the minority survives. One level renders as its own name, several render
        // as shares, and the dear 20% must appear in the line.
        string one = EffortMix.Line(new Dictionary<string, int>(StringComparer.Ordinal) { ["high"] = 9 });
        string many = EffortMix.Line(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["high"] = 40, ["xhigh"] = 10,
        });
        Check("a single level is its own name and a mix keeps the expensive minority",
              !one.Contains('%') && many.Contains("80%", StringComparison.Ordinal) &&
              many.Contains("20%", StringComparison.Ordinal),
              $"one → '{one}', mixed → '{many}'");

        Check("and a session that named no effort renders nothing rather than a level",
              EffortMix.Line(new Dictionary<string, int>()).Length == 0 && EffortMix.Line(null).Length == 0,
              $"'{EffortMix.Line(null)}'");
    }

    /// <summary>
    /// T332. The sliding five-hour sweep, against inputs whose answer is known by construction.
    ///
    /// <para>The sweep is the first reading here that looks <em>backwards</em> across days, and its
    /// two failure modes are quiet ones. A frame that is off by one bucket reports a peak that is
    /// nearly right — 0.018% is the whole difference between minute buckets and exact turns on this
    /// machine, so no eyeballing of a real number would ever catch a fencepost. And a median taken
    /// over calendar days rather than <em>active</em> ones halves itself on a machine that rests at
    /// weekends, which reads as a peak twice as dramatic as it is.</para>
    ///
    /// <para>So the fixtures are arithmetic: a block whose sum is known, an empty minute the frame
    /// has to span, a bucket exactly five hours old that has to have left it, and days whose peaks
    /// are 10/20/30 so the median can only be 20 if active days are what was counted.</para>
    /// </summary>
    private static void Heaviest()
    {
        // Minute 0 is a real instant (1970), so the fixtures sit at a round modern one to make the
        // local-day grouping below mean what it says.
        long day0 = (long)(new DateTimeOffset(new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Local))
                           .ToUnixTimeSeconds() / 60);

        // A quiet hour, then a burst, then a quiet hour: the frame must find the burst and start on
        // its first minute, not on the first minute of the series.
        var series = new Dictionary<long, long>
        {
            [day0] = 1,                 // an hour before the burst
            [day0 + 120] = 100,
            [day0 + 121] = 200,
            [day0 + 400] = 300,         // 4h40 after the burst's start: still inside a 5h frame
            [day0 + 700] = 50,          // a later, lighter stretch of its own
        };
        PeakWindow peak = HeaviestWindow.Of(series);
        // 600 and not 601: the frame ending at +400 has already slid past minute 0, which is the
        // fencepost this fixture exists for. Written the other way round first, it reported 1000 —
        // +400 and a bucket at +421 are a heavier frame than the one intended, and the check said so.
        Check("the sweep finds the heaviest frame and starts it on the first minute that carried work",
              peak.Tokens == 600 && peak.StartMinute == day0 + 120,
              $"{peak.Tokens} from minute +{peak.StartMinute - day0}, expected 600 from +120");

        // The half-open edge, on its own: two buckets exactly a span apart never share a frame.
        var edge = new Dictionary<long, long> { [day0] = 5, [day0 + HeaviestWindow.Span] = 6 };
        Check("a bucket exactly five hours older has left the frame",
              HeaviestWindow.Of(edge).Tokens == 6, $"{HeaviestWindow.Of(edge).Tokens}, expected 6");

        // Nothing in range is a state, not a zero to draw.
        Check("an empty series is reported as nothing found, not as a peak of zero",
              !HeaviestWindow.Of(new Dictionary<long, long>()).Found && !HeaviestWindow.Of(null).Found,
              "a zero peak was reported as found");

        // Three worked days at 10/20/30 and two idle ones. The median is 20 only if the idle days
        // were left out; counting all five calendar days would answer 10.
        var week = new Dictionary<long, long>
        {
            [day0] = 10,
            [day0 + 1440] = 20,
            [day0 + 2880] = 30,
            // days 4 and 5 carry nothing at all
        };
        Check("the median day is the median of the days that carried work",
              HeaviestWindow.MedianDayPeak(week) == 20 && HeaviestWindow.ActiveDays(week) == 3,
              $"median {HeaviestWindow.MedianDayPeak(week)} over {HeaviestWindow.ActiveDays(week)} days, " +
              "expected 20 over 3");

        // A zero bucket is not a day. An index that wrote one would otherwise invent an active day
        // and drag the median down.
        var withZero = new Dictionary<long, long> { [day0] = 10, [day0 + 1440] = 0 };
        Check("and a minute that carried nothing makes no day active",
              HeaviestWindow.ActiveDays(withZero) == 1 && HeaviestWindow.MedianDayPeak(withZero) == 10,
              $"{HeaviestWindow.ActiveDays(withZero)} active days");
    }

    /// <summary>
    /// T333. The by-kind table, and the three ways it could quietly say something false.
    ///
    /// <para><b>A mean instead of a median.</b> The reading is what a <em>usual</em> task of that kind
    /// costs, and one overnight run among short ones moves a mean by an order of magnitude. The
    /// fixture makes them disagree on purpose.</para>
    ///
    /// <para><b>Commands rolled together.</b> The finding that produced this block was about one
    /// command carrying 57% of a range, not about automation in general — lumping every command into a
    /// single row would report the true total under a label that names nothing to act on.</para>
    ///
    /// <para><b>A share of the wrong denominator.</b> Shares are taken against what the table itself
    /// sums to, so they add to 100% by construction rather than against a token figure gathered
    /// somewhere else, which would drift the moment either side changed what it counts.</para>
    /// </summary>
    private static void Kinds()
    {
        static TaskRow T(string kind, string name, long tokens) => new(kind, name, 0, 0, tokens);

        var rows = new[]
        {
            // One command that is heavy and rare, another that is light and frequent: rolled together
            // they would be one row of 1,300 and neither number would be true of either command.
            T(nameof(TaskKind.Command), "/loop", 1000),
            T(nameof(TaskKind.Command), "/loop", 200),
            T(nameof(TaskKind.Command), "/loop", 100),
            T(nameof(TaskKind.Command), "/review", 50),
            T(nameof(TaskKind.Prompt), "", 30),
            T(nameof(TaskKind.Prompt), "", 10),
            T(nameof(TaskKind.Continuation), "", 5),
            // A task that got no answer is not a size, and counting it would drag every median down.
            T(nameof(TaskKind.Prompt), "", 0),
        };

        IReadOnlyList<WorkGroup> table = WorkKinds.Of(rows);

        // Looked up rather than asserted into existence: rolling every command into one row is one of
        // the defects under test, and a First() that throws on it takes the whole run down with it —
        // every check after this one would never run, which is worse than the defect (seen doing it).
        WorkGroup loop = table.FirstOrDefault(g => g.Name == "/loop");
        WorkGroup prompts = table.FirstOrDefault(g => g.Kind == nameof(TaskKind.Prompt));
        if (!Check("the table has a row per named command to read",
                   loop.Tasks > 0 && prompts.Tasks > 0,
                   $"rows: {string.Join(", ", table.Select(g => $"{g.Kind}/{g.Name}"))} — a command " +
                   "rolled in with the rest leaves nothing here to assert against"))
            return;
        Check("a command's row is its own median, not the mean its heaviest task drags",
              loop.Tasks == 3 && loop.Median == 200 && loop.Total == 1300,
              $"{loop.Tasks} tasks, median {loop.Median}, total {loop.Total} — expected 3/200/1300");

        Check("and two commands are two rows, not one lump called 'commands'",
              table.Count(g => g.IsCommand) == 2 &&
              table.Any(g => g.Name == "/review" && g.Total == 50),
              string.Join(", ", table.Select(g => $"{g.Kind}:{g.Name}={g.Total}")));

        Check("a task that got no answer is left out rather than counted as a zero-token task",
              prompts.Tasks == 2, $"{prompts.Tasks} prompt tasks, expected 2");

        Check("the table is ordered heaviest first",
              table.Select(g => g.Total).SequenceEqual(table.Select(g => g.Total).OrderByDescending(v => v)),
              string.Join(" ", table.Select(g => g.Total)));

        // The denominator is the table's own sum, so the shares are of what is on screen.
        long total = WorkKinds.Total(table);
        Check("and every share is taken against what the table itself sums to",
              total == 1395 && Math.Abs(table.Sum(g => (double)g.Total / total) - 1.0) < 1e-9,
              $"{total}, expected 1395");

        // The kinds that are not commands are named through the string table; a command is named as
        // the person typed it and never translated.
        Check("a command is labelled by its own name and a kind by the string table",
              WorkKinds.Label(loop) == "/loop" &&
              WorkKinds.Label(prompts) != "stats.kind.prompt", WorkKinds.Label(prompts));
    }

    /// <summary>
    /// T337. Which conversation a spike on the live strip belongs to.
    ///
    /// <para>The rule is one line and its whole value is the <em>window</em>. Without it, a click on a
    /// strip that is drawing nothing would still open a conversation — the newest one, which may have
    /// ended yesterday — and confidently name the wrong thing for a spike that is not there. So the
    /// fixture puts a stale conversation and a live one side by side, and then asks the same question
    /// of a list where everything is stale.</para>
    ///
    /// <para>The direction is the finding, not the mechanism: the strip keeps
    /// <see cref="LiveRate.HistorySeconds"/> seconds and the list opens on seven days, so the inverse
    /// join T329 sketched would be blank for every row but one.</para>
    /// </summary>
    private static void RunningSession()
    {
        const double Now = 1_800_000_000;
        static SessionRow At(string id, double last) =>
            new(id, "proj", id, last - 60, last, 1, default, Array.Empty<string>(), 0);

        var rows = new[]
        {
            At("yesterday", Now - 86_400),
            At("an-hour-ago", Now - 3_600),
            At("running", Now - 20),
            At("also-recent", Now - 200),
        };

        SessionRow? live = SessionIndex.Running(rows, Now, LiveRate.HistorySeconds);
        Check("the newest conversation inside the strip's window is the running one",
              live?.Session == "running", live?.Session ?? "none");

        // The whole point of the window: a strip with nothing on it names nothing.
        SessionRow? none = SessionIndex.Running(
            new[] { At("yesterday", Now - 86_400), At("an-hour-ago", Now - 3_600) },
            Now, LiveRate.HistorySeconds);
        Check("and a list with nothing recent names no conversation at all",
              none is null, none?.Session ?? "null");

        Check("an empty list is no conversation, not a crash",
              SessionIndex.Running(Array.Empty<SessionRow>(), Now, LiveRate.HistorySeconds) is null, "");

        // The edge the window is: a conversation exactly at the far end of the ring is still in it.
        Check($"a conversation exactly {LiveRate.HistorySeconds}s old is still inside the ring",
              SessionIndex.Running(new[] { At("edge", Now - LiveRate.HistorySeconds) }, Now,
                                   LiveRate.HistorySeconds)?.Session == "edge",
              "the far end of the ring was treated as outside it");
    }

    /// <summary>
    /// T335. The fixture a published shot of the Sessions pane comes from, and the two things about it
    /// that a check can hold.
    ///
    /// <para><b>What it cannot hold</b>, said first because it decides what the rest is worth: nothing
    /// here can tell whether somebody committed a PNG taken from their own profile. That is why the
    /// fixture has to be the <em>easy</em> path — a named preview both flags already read — rather than
    /// a careful one. What is checkable is that the easy path exists, reads its invented tree, and
    /// produces a pane worth photographing.</para>
    ///
    /// <para>So: the preview exists and is capturable, and the tree it builds exercises the pane rather
    /// than merely populating it — several conversations, a fan-out folded into its parent row, both
    /// task kinds so the by-kind table has more than one row, and a prompt past the cap so the
    /// truncation is visible in the picture instead of taken on trust.</para>
    /// </summary>
    private static void SessionsFixture()
    {
        // The variant a published capture is supposed to use. A published shot cannot come from a
        // fixture that --capture-stats refuses to render.
        if (!Check("the Sessions pane has a preview a capture can use",
                   StatsPreviews.Resolve(new[] { "sessions" }, capturing: true) is { } c && c.Variant.Sessions,
                   "no capturable 'sessions' preview — a published shot would have to come from the real pane"))
            return;

        string root = SessionFixture.Build(new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc));
        IReadOnlyList<SessionRow> rows = SessionIndex.Load(null, root);

        if (!Check($"it reads as a list of conversations ({rows.Count})", rows.Count >= 3,
                   "fewer than three rows is not a list worth a screenshot"))
            return;

        // A fan-out folded into the row that spawned it is the claim the pane makes on screen, so the
        // picture has to contain one.
        Check("one conversation folds a fan-out into its own row",
              rows.Any(r => r.Agents > 0), string.Join(", ", rows.Select(r => $"{r.Name}:{r.Agents}")));

        // Both kinds, or the by-kind table under the list is one row and shows nothing.
        IReadOnlyList<WorkGroup> kinds = WorkKinds.Of(rows.SelectMany(r => r.Tasks ?? Array.Empty<TaskRow>()));
        Check("and the by-kind table has a command and a typed prompt to compare",
              kinds.Any(g => g.IsCommand) && kinds.Any(g => g.Kind == nameof(TaskKind.Prompt)),
              string.Join(", ", kinds.Select(g => $"{g.Kind}/{g.Name}")));

        // The truncation is a claim the README makes about this pane; the shot should show it working.
        // At the cap, not exactly it: the stored form carries the mark that says it was cut.
        Check($"a prompt long enough to be truncated is on screen ({SessionIndex.PromptChars})",
              rows.Any(r => r.Prompt.Length >= SessionIndex.PromptChars),
              string.Join(", ", rows.Select(r => r.Prompt.Length)));

        // And the whole point: nothing in the picture came from a real profile.
        Check("every row in it is invented, not this machine's",
              rows.All(r => r.Session.StartsWith("sample-", StringComparison.Ordinal)),
              string.Join(", ", rows.Select(r => r.Session).Where(s => !s.StartsWith("sample-"))));
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
}
