using System.Text;
using System.Text.Json;

namespace ClaudeTray;

/// <summary>How often one skill or agent was actually invoked, and when it last was.</summary>
internal sealed class UsageStat
{
    /// <summary>Invocations in the last 30 days.</summary>
    public int Recent { get; set; }
    /// <summary>Invocations in the whole window (90 days) — the number a prune decision uses.</summary>
    public int Total { get; set; }
    public DateTime LastUsedUtc { get; set; }
}

/// <summary>
/// Evidence of use, mined from the transcripts: which skills and agents were actually invoked over
/// the last 30 / 90 days.
///
/// This is what turns advice into a decision. "Trim your skills" is nagging; *"this skill has not
/// been invoked once in 90 days, and its description costs you ≈180 tokens in every session"* is a
/// fact someone can act on.
///
/// <para><b>What it does not claim.</b> Memory files are deliberately left out. A memory is recalled
/// by the harness injecting it into the conversation, which leaves no structured record — the only
/// trace is in message content, which this app never reads (IMPROVEMENTS §I.1). Counting merely the
/// memories somebody happened to open with a tool would under-count real recalls and could label a
/// working memory "never used", which is the one error an advisor must not make. So memories get no
/// usage annotation at all, rather than a misleading one.</para>
///
/// <para>That was the reasoning; T85 <b>measured</b> it, and the measurement is what keeps this
/// paragraph from being an assumption nobody rechecks. Over the whole local history — 530
/// transcripts, 195,451 lines — every top-level <c>type</c>, every <c>attachment.type</c> and every
/// JSON field name was enumerated. The harness keeps four kinds of structured provenance
/// (<c>attributionSkill</c> ×40,573, <c>attributionAgent</c> ×4,450, <c>attributionMcpServer</c> and
/// <c>attributionMcpTool</c> ×230 each) and sixteen attachment kinds including <c>skill_listing</c>
/// and <c>agent_listing_delta</c>. <b>Not one of them concerns a memory.</b> No field matching
/// <c>memor*</c> or <c>recall*</c> exists anywhere; the only memory paths in a transcript sit under
/// <c>file-history-snapshot.snapshot.trackedFileBackups</c>, which records files Claude <i>wrote</i>,
/// not ones it recalled — the opposite signal, and it would mark the memories being maintained as the
/// ones in use.</para>
///
/// <para><b>To re-check</b> when Claude Code changes: enumerate distinct <c>attachment.type</c> values
/// and field names across <c>~/.claude/projects/**/*.jsonl</c> and look for a memory analogue of
/// <c>attributionSkill</c>. Until one exists, this exclusion stands — it is a binding non-goal in
/// ROADMAP.md, not an open task.</para>
///
/// <para><b>Privacy.</b> Two fields are read out of a tool call and nothing else: <c>input.skill</c>
/// for the <c>Skill</c> tool and <c>input.subagent_type</c> for the agent tool — both plain
/// identifiers. No other argument, and no message content, is parsed or retained.</para>
/// </summary>
internal sealed class UsageEvidence
{
    /// <summary>By skill name, lowercased.</summary>
    public Dictionary<string, UsageStat> Skills { get; set; } = new();
    /// <summary>By agent / subagent type, lowercased.</summary>
    public Dictionary<string, UsageStat> Agents { get; set; } = new();

    /// <summary>
    /// True when every transcript in the window was read to the end. When false, a zero count means
    /// "not seen in what was read", not "never used" — and <b>nothing may report "never used"</b>,
    /// because a truncated pass is exactly how an advisor talks somebody into deleting a skill they
    /// use every day.
    /// </summary>
    public bool Complete { get; set; } = true;

    public int WindowDays { get; set; } = 90;
    public int RecentDays { get; set; } = 30;
    public int FilesRead { get; set; }
    public long BytesRead { get; set; }
    public double ElapsedMs { get; set; }
    public DateTime ComputedUtc { get; set; }
    public string? Error { get; set; }

    /// <summary>Usage for a scanned skill/agent source, matched on its frontmatter name and then on
    /// the trailing part of its label ("plugin:math-olympiad" → "math-olympiad").</summary>
    public UsageStat? For(ContextSource s)
    {
        Dictionary<string, UsageStat> table = s.Kind == ContextKind.Agent ? Agents : Skills;
        foreach (string candidate in Candidates(s))
            if (table.TryGetValue(candidate, out UsageStat? stat)) return stat;
        return null;
    }

    /// <summary>
    /// Whether a zero may be reported as a zero for this source. It requires only that the pass was
    /// complete — because the claim being made is a fact about the window ("not invoked in the last
    /// 90 days of transcripts"), not an inference about the file ("never used", which would also have
    /// to know when the skill first existed).
    ///
    /// An earlier version additionally required the file to be older than the window, using its
    /// mtime as a stand-in for age. That suppressed every claim on a real machine — a plugin
    /// marketplace refresh restamps every <c>SKILL.md</c> — which is a worse failure than the one it
    /// was guarding against, since the fact was true all along.
    /// </summary>
    public bool CanReportZero(ContextSource s)
        => Complete && Error == null && s.Kind is ContextKind.Skill or ContextKind.Agent;

    private static IEnumerable<string> Candidates(ContextSource s)
    {
        if (s.Name is { Length: > 0 } n) yield return n.Trim().ToLowerInvariant();
        string label = s.Label;
        int colon = label.LastIndexOf(':');
        yield return (colon >= 0 ? label[(colon + 1)..] : label).Trim().ToLowerInvariant();
    }
}

/// <summary>
/// The miner behind <see cref="UsageEvidence"/>. Reads the transcripts Claude Code already keeps and
/// counts <c>Skill</c> / agent invocations by name.
///
/// The volume is the whole design problem: a real machine has hundreds of megabytes of recent
/// transcripts, so this cannot run inside <see cref="ContextScanner.Scan"/> without ruining the
/// scan's latency. Instead it is a separate pass with its own per-file cache — a file whose size and
/// mtime are unchanged is never read twice — which makes the steady-state cost just the session
/// currently being written.
/// </summary>
internal static class ContextUsage
{
    internal sealed class Options
    {
        public string ClaudeRoot { get; set; } = ContextScanner.DefaultClaudeRoot;
        /// <summary>The decision window. 0 means every transcript on disk.</summary>
        public int WindowDays { get; set; } = 90;
        public int RecentDays { get; set; } = 30;
        /// <summary>Give up past this and report the pass as incomplete rather than block the UI.</summary>
        public int TimeBudgetMs { get; set; } = 20_000;
        public bool UseCache { get; set; } = true;
    }

    private static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClaudeTray", "context-usage.json");

    /// <summary>Mine the transcripts. Safe on a background thread; never throws.</summary>
    public static UsageEvidence Compute(DateTime nowUtc, Options? options = null)
    {
        var opt = options ?? new Options();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var evidence = new UsageEvidence
        {
            ComputedUtc = nowUtc,
            WindowDays = opt.WindowDays,
            RecentDays = opt.RecentDays,
        };

        try
        {
            string projects = Path.Combine(opt.ClaudeRoot, "projects");
            if (!Directory.Exists(projects))
            {
                evidence.Error = $"not found: {projects}";
                return evidence;
            }

            DateTime cutoff = opt.WindowDays > 0 ? nowUtc.AddDays(-opt.WindowDays) : DateTime.MinValue;
            DateTime recentCutoff = nowUtc.AddDays(-opt.RecentDays);

            List<FileInfo> files;
            try
            {
                files = SafeWalk.Files(projects, "*.jsonl")
                    .Where(f => f.LastWriteTimeUtc >= cutoff)
                    .ToList();
            }
            catch (Exception e)
            {
                evidence.Error = e.Message;
                return evidence;
            }

            Dictionary<string, CacheEntry> cache = opt.UseCache ? LoadCache() : new();
            var fresh = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
            var perFile = new FileCounts[files.Count];
            bool timedOut = false;

            // Counting is per file and independent, so it fans out the same way the scan does. The
            // shared state is the cache dictionaries, guarded below when results are merged.
            Parallel.For(0, files.Count,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) },
                i =>
                {
                    FileInfo f = files[i];
                    // A file already counted at this exact size and mtime cannot have new events in
                    // it — this is what keeps a rescan cheap after the first pass.
                    if (cache.TryGetValue(f.FullName, out CacheEntry? hit) &&
                        hit.Bytes == f.Length && hit.Ticks == f.LastWriteTimeUtc.Ticks)
                    {
                        perFile[i] = new FileCounts(hit.Skills, hit.Agents, f.LastWriteTimeUtc, 0);
                        lock (fresh) fresh[f.FullName] = hit;
                        return;
                    }

                    if (sw.ElapsedMilliseconds > opt.TimeBudgetMs) { timedOut = true; return; }

                    FileCounts counts = CountFile(f);
                    perFile[i] = counts;
                    lock (fresh)
                        fresh[f.FullName] = new CacheEntry
                        {
                            Path = f.FullName,
                            Bytes = f.Length,
                            Ticks = f.LastWriteTimeUtc.Ticks,
                            Skills = counts.Skills,
                            Agents = counts.Agents,
                        };
                });

            foreach (FileCounts? counts in perFile)
            {
                if (counts is null) continue;
                evidence.FilesRead++;
                evidence.BytesRead += counts.BytesRead;
                bool recent = counts.WhenUtc >= recentCutoff;
                Merge(evidence.Skills, counts.Skills, counts.WhenUtc, recent);
                Merge(evidence.Agents, counts.Agents, counts.WhenUtc, recent);
            }

            evidence.Complete = !timedOut && evidence.FilesRead == files.Count;
            if (opt.UseCache) SaveCache(fresh.Values.ToList());
        }
        catch (Exception e)
        {
            evidence.Error = e.Message;
            evidence.Complete = false;
        }

        evidence.ElapsedMs = sw.Elapsed.TotalMilliseconds;
        return evidence;
    }

    private static void Merge(Dictionary<string, UsageStat> into, Dictionary<string, int> counts,
        DateTime whenUtc, bool recent)
    {
        foreach ((string name, int n) in counts)
        {
            if (!into.TryGetValue(name, out UsageStat? stat))
                into[name] = stat = new UsageStat();
            stat.Total += n;
            if (recent) stat.Recent += n;
            if (whenUtc > stat.LastUsedUtc) stat.LastUsedUtc = whenUtc;
        }
    }

    private sealed record FileCounts(
        Dictionary<string, int> Skills, Dictionary<string, int> Agents, DateTime WhenUtc, long BytesRead);

    // The names of the tools whose invocation is the evidence. The agent tool has been called both
    // Task and Agent across versions, so both are counted.
    private const string SkillTool = "Skill";
    private static readonly string[] AgentTools = { "Agent", "Task" };

    /// <summary>
    /// Count invocations in one transcript. The whole file has to be read — a tool call can be
    /// anywhere in a session — so the hot path is a pair of ordinal substring tests that reject the
    /// overwhelming majority of lines before any JSON is parsed.
    /// </summary>
    private static FileCounts CountFile(FileInfo file)
    {
        var skills = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var agents = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;

        try
        {
            foreach (string line in File.ReadLines(file.FullName))
            {
                bytes += line.Length;
                if (!line.Contains("\"tool_use\"", StringComparison.Ordinal)) continue;
                if (!line.Contains("\"skill\"", StringComparison.OrdinalIgnoreCase) &&
                    !line.Contains("\"subagent_type\"", StringComparison.Ordinal)) continue;

                CountLine(line, skills, agents);
            }
        }
        catch { /* a transcript being written, or an unreadable file — count what we got */ }

        return new FileCounts(skills, agents, file.LastWriteTimeUtc, bytes);
    }

    private static void CountLine(string line, Dictionary<string, int> skills, Dictionary<string, int> agents)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("message", out JsonElement msg) ||
                msg.ValueKind != JsonValueKind.Object) return;
            if (!msg.TryGetProperty("content", out JsonElement content) ||
                content.ValueKind != JsonValueKind.Array) return;

            foreach (JsonElement block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) continue;
                if (!block.TryGetProperty("type", out JsonElement type) || type.GetString() != "tool_use") continue;
                if (!block.TryGetProperty("name", out JsonElement toolName)) continue;
                string tool = toolName.GetString() ?? "";
                if (!block.TryGetProperty("input", out JsonElement input) ||
                    input.ValueKind != JsonValueKind.Object) continue;

                // Exactly one field is read per tool, and it is an identifier. Nothing else in
                // `input` is touched — see the privacy note on UsageEvidence.
                if (tool == SkillTool)
                {
                    if (Named(input, "skill") is { Length: > 0 } skill) Bump(skills, skill);
                }
                else if (AgentTools.Contains(tool))
                {
                    if (Named(input, "subagent_type") is { Length: > 0 } agent) Bump(agents, agent);
                }
            }
        }
        catch { /* a truncated line — no evidence from it */ }
    }

    private static string? Named(JsonElement input, string property)
        => input.TryGetProperty(property, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()?.Trim().ToLowerInvariant()
            : null;

    private static void Bump(Dictionary<string, int> counts, string name)
        => counts[name] = counts.TryGetValue(name, out int n) ? n + 1 : 1;

    // ---------------------------------------------------------------- cache

    /// <summary>One transcript's counts, valid while its size and mtime are unchanged.</summary>
    private sealed class CacheEntry
    {
        public string Path { get; set; } = "";
        public long Bytes { get; set; }
        public long Ticks { get; set; }
        public Dictionary<string, int> Skills { get; set; } = new();
        public Dictionary<string, int> Agents { get; set; } = new();
    }

    private static Dictionary<string, CacheEntry> LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return new(StringComparer.OrdinalIgnoreCase);
            var list = JsonSerializer.Deserialize<List<CacheEntry>>(File.ReadAllText(CachePath));
            var map = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (CacheEntry e in list ?? new()) map[e.Path] = e;
            return map;
        }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private static void SaveCache(List<CacheEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(entries));
        }
        catch { /* the cache is an optimization; failing to write it costs one slow pass */ }
    }

    /// <summary>Compact display form: "12×", the localized "none" when a zero can be reported, and
    /// "—" when there is no evidence to speak from.</summary>
    public static string Format(UsageStat? stat, bool canReportZero)
    {
        if (stat is { Total: > 0 })
            return stat.Total.ToString(System.Globalization.CultureInfo.InvariantCulture) + "×";
        return canReportZero ? L.T("context.usage.never") : "—";
    }

    /// <summary>The one-line summary the reports print, e.g. "606 transcripts, 737 MB, 1.9s".</summary>
    public static string Summary(UsageEvidence e)
    {
        var sb = new StringBuilder();
        // Bytes are what this pass actually read: the per-file cache means most of them are usually
        // skipped, and saying "735 MB" when 18 MB was read would misreport the cost.
        sb.Append(e.FilesRead).Append(" transcripts, ")
          .Append((e.BytesRead / 1048576.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
          .Append(" MB read, ").Append((e.ElapsedMs / 1000).ToString("0.0",
              System.Globalization.CultureInfo.InvariantCulture)).Append('s');
        if (!e.Complete) sb.Append(" (incomplete — zero counts suppressed)");
        return sb.ToString();
    }
}
