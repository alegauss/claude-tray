using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeTray;

/// <summary>What kind of context file a source is. Determines how it is grouped and priced.</summary>
internal enum ContextKind
{
    UserInstructions,      // ~/.claude/CLAUDE.md — every session, every project
    ProjectInstructions,   // <project>/CLAUDE.md | AGENTS.md | CLAUDE.local.md, and the parent chain
    NestedInstructions,    // CLAUDE.md/AGENTS.md in a subdirectory — only loaded if that subtree is touched
    Import,                // a file pulled in by an `@path` line inside an instruction file
    MemoryIndex,           // memory/MEMORY.md — the index that is always loaded
    MemoryFile,            // memory/<slug>.md — a body, recalled only when relevant
    Skill,                 // SKILL.md — the body is lazy, the name+description is not
    Agent,                 // .claude/agents/<name>.md — same eager-description/lazy-body split
    Settings,              // settings.json / settings.local.json — measured, never in the prompt
}

/// <summary>
/// Whether a source is paid on every request of the session. This distinction is the whole point
/// of the feature: total bytes on disk is the wrong number.
/// </summary>
internal enum LoadMode
{
    /// <summary>In the cached prefix from the first request on. Multiplies by every request.</summary>
    Eager,
    /// <summary>Read only when used (a memory recalled, a skill invoked, a subtree opened).</summary>
    Lazy,
    /// <summary>Never enters the prompt at all — measured only because its size is a symptom.</summary>
    NotLoaded,
}

/// <summary>How well a <c>~/.claude/projects/&lt;slug&gt;</c> directory maps back to a real path.</summary>
internal enum PathState
{
    /// <summary>The slug resolved to a directory that exists.</summary>
    Resolved,
    /// <summary>It looks like a path, but that directory is gone — a dead project dir.</summary>
    Missing,
    /// <summary>Not a filesystem path at all (e.g. a shared/virtual project dir).</summary>
    NotAPath,
}

/// <summary>One measured context file.</summary>
internal sealed class ContextSource
{
    public ContextKind Kind { get; set; }
    public LoadMode Mode { get; set; }
    public string Path { get; set; } = "";
    /// <summary>Short display name — the file name, or "skills/preview-ui" style for skills.</summary>
    public string Label { get; set; } = "";
    public long Bytes { get; set; }
    /// <summary>Characters as the estimator saw them; 0 when the file was sized but not read.</summary>
    public int Chars { get; set; }
    /// <summary>Estimated tokens for the whole file.</summary>
    public int Tokens { get; set; }
    /// <summary>
    /// Estimated tokens actually paid at every request. Equals <see cref="Tokens"/> for eager
    /// sources, 0 for lazy bodies, and just the name+description for a skill or agent — whose
    /// entry in the prompt's index is eager even though the body is not.
    /// </summary>
    public int EagerTokens { get; set; }
    public DateTime ModifiedUtc { get; set; }
    /// <summary>Machine-wide rather than per-project (user instructions, user/plugin skills, user settings).</summary>
    public bool Shared { get; set; }

    // Frontmatter, for memory files / skills / agents. Read here because the file is being read
    // anyway; the rules in Phase 3 (missing description, wrong type) are all decided from these.
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Type { get; set; }

    /// <summary>Anything the scan wants to flag: "@import", "import cycle", "unreadable".</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Markdown link targets found in a memory index (<c>MEMORY.md</c>) — bare file names, no
    /// directory parts, no URLs. This is what lets the rules tell a pointer to a deleted memory from
    /// a memory nobody indexed. File names only: nothing from the body is retained.
    /// </summary>
    public List<string>? IndexLinks { get; set; }

    /// <summary>
    /// Content hash, set for memory files only. Two projects whose memory directories hash the same
    /// file-for-file are copies of each other, which is a real finding — one shared directory plus a
    /// junction, not two copies drifting apart.
    /// </summary>
    public string? ContentHash { get; set; }
}

/// <summary>
/// The observed cost of starting a session, measured from transcripts rather than estimated: the
/// full context of the <em>first</em> assistant request of a session — system prompt + tool
/// definitions + instructions + memory index — as
/// <c>input_tokens + cache_creation_input_tokens + cache_read_input_tokens</c>.
///
/// All three terms are needed, which is worth spelling out because it is easy to get wrong: the
/// stable prefix is shared between sessions, so a session started while a previous one's cache is
/// still alive reports most of its startup context as <c>cache_read</c>, not <c>cache_creation</c>.
/// Counting only the creation side made two thirds of the projects on the dev machine look like
/// they had no measurable startup at all. A cached read is still context loaded — just billed at
/// the cheaper rate.
///
/// Usage counts and model ids only — never message content.
/// </summary>
internal sealed class SessionZero
{
    public int Median { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }
    public int Samples { get; set; }
    /// <summary>Model of the median sample — priced with the cache-write rate.</summary>
    public string Model { get; set; } = "";
    public DateTime NewestUtc { get; set; }

    /// <summary>
    /// ≈USD to load this startup context on a cold cache, at the median sample's model price. The
    /// cache-write rate is deliberate: it is what a first session of the day actually costs, and the
    /// point of the number is the toll you pay for opening the project, not its best case.
    /// </summary>
    [JsonIgnore]
    public double Cost => Median * UsageInsights.Price(Model).cw / 1_000_000.0;
}

/// <summary>One <c>~/.claude/projects/&lt;slug&gt;</c> directory and everything it loads.</summary>
internal sealed class ContextProject
{
    public string Slug { get; set; } = "";
    /// <summary>Resolved filesystem path, or the best-effort literal reading when unresolved.</summary>
    public string Path { get; set; } = "";
    public PathState State { get; set; }
    /// <summary>Set when the path came from a transcript's <c>cwd</c> instead of slug probing.</summary>
    public bool FromTranscript { get; set; }
    public List<ContextSource> Sources { get; set; } = new();
    public SessionZero? Observed { get; set; }
    /// <summary>Transcript files considered for <see cref="Observed"/> (after the age/count caps).</summary>
    public int TranscriptsRead { get; set; }
    /// <summary>The nested-instructions walk hit its cap here — this project's lazy list is partial.</summary>
    public bool Truncated { get; set; }

    [JsonIgnore]
    public string Name => State == PathState.NotAPath || Path.Length == 0
        ? Slug
        : System.IO.Path.GetFileName(Path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : Path;

    /// <summary>
    /// Display name: the last two path segments ("viglet/cloud", "turing/2026.3"). Leaf-only would
    /// be ambiguous on this machine — three different worktree projects are all called "2026.3".
    /// The rule itself lives in <see cref="ProjectSlug.ShortName"/> since T154, so the Statistics
    /// legend names the same directory the same way this list does.
    /// </summary>
    [JsonIgnore]
    public string ShortPath => State == PathState.NotAPath || Path.Length == 0
        ? Slug
        : ProjectSlug.ShortName(Path);

    /// <summary>Eager tokens from this project's own sources (excludes the shared ones).</summary>
    [JsonIgnore]
    public int EagerTokens => Sources.Sum(s => s.EagerTokens);
    [JsonIgnore]
    public long Bytes => Sources.Sum(s => s.Bytes);
}

/// <summary>The whole scan: machine-wide sources once, then one entry per project.</summary>
internal sealed class ContextScan
{
    /// <summary>Sources that load for every project — counted once in a cross-project total.</summary>
    public List<ContextSource> Shared { get; set; } = new();
    public List<ContextProject> Projects { get; set; } = new();

    public int FilesWalked { get; set; }
    /// <summary>True when the file cap cut the walk short — the numbers below are a floor, not a total.</summary>
    public bool Truncated { get; set; }
    public DateTime ScannedUtc { get; set; }
    /// <summary>True when this result came back from the on-disk cache rather than a fresh walk.</summary>
    public bool FromCache { get; set; }
    public double ElapsedMs { get; set; }
    public string? Error { get; set; }

    /// <summary>Eager tokens every session pays regardless of project (user CLAUDE.md, skill index).</summary>
    [JsonIgnore]
    public int SharedEagerTokens => Shared.Sum(s => s.EagerTokens);

    /// <summary>
    /// What opening a session in this project costs before the first prompt, as far as the
    /// filesystem can tell: shared eager + the project's eager sources. It deliberately excludes
    /// Claude Code's own system prompt and tool definitions, which no scan can see — see
    /// <see cref="ContextScanner.Calibrate"/> for that constant, measured from transcripts.
    /// </summary>
    public int EstimatedSessionZero(ContextProject p) => SharedEagerTokens + p.EagerTokens;
}

/// <summary>
/// Reads every file Claude Code loads into context before the first prompt — the user and project
/// instruction chain, the memory index and its files, skill and agent frontmatter — and measures
/// what each one costs, split into what is paid <em>every session</em> (eager) and what is paid
/// only when used (lazy).
///
/// Two phases, so the result can be cached honestly:
/// <list type="number">
/// <item><b>Plan</b> — enumerate and stat, no file contents. Produces a fingerprint over every
/// candidate file's path+size+mtime, which is what invalidates the cache. Directory mtimes alone
/// would miss an in-place edit of <c>MEMORY.md</c>, which is exactly the change that matters.</item>
/// <item><b>Execute</b> — read and estimate only what the plan found, then persist the result
/// under <c>%LocalAppData%\ClaudeTray</c> keyed by that fingerprint.</item>
/// </list>
///
/// Privacy, non-negotiable: sizes, names, frontmatter, timestamps and token counts. From
/// transcripts, only <c>usage</c> numbers, the model id and the session's <c>cwd</c> — never
/// message content.
/// </summary>
internal static class ContextScanner
{
    /// <summary>Tunables — also the seam the fixture/sample mode in Phase 4 will use.</summary>
    internal sealed class Options
    {
        /// <summary>Root to scan; defaults to <c>~/.claude</c>.</summary>
        public string ClaudeRoot { get; set; } = DefaultClaudeRoot;
        /// <summary>Ignore transcripts untouched for longer than this (0 = no age filter).</summary>
        public int TranscriptDays { get; set; } = 30;
        /// <summary>Most recent transcripts per project to sample for session zero.</summary>
        public int TranscriptsPerProject { get; set; } = 8;
        /// <summary>Hard cap on files the walk may consider, across every project.</summary>
        public int FileCap { get; set; } = 20_000;
        /// <summary>How deep to look for nested CLAUDE.md/AGENTS.md below a project root.</summary>
        public int NestedDepth { get; set; } = 4;
        /// <summary>Directories the nested walk may visit per project, before it gives up.</summary>
        public int NestedDirCap { get; set; } = 600;
        /// <summary>Use (and refresh) the on-disk cache.</summary>
        public bool UseCache { get; set; } = true;
    }

    internal static string DefaultClaudeRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    /// <summary>
    /// Claude Code's own system prompt, tool definitions and MCP schemas — the part of session zero
    /// no filesystem scan can see. <see cref="Calibrate"/> fits it from transcripts when at least
    /// three projects have an observed session; this is the measured median from the dev machine's
    /// 27 projects (p25 30k, p75 34k), used when there isn't enough to fit. It is a real cost, so it
    /// is shown as its own segment rather than folded into a project's number.
    /// </summary>
    internal const int FallbackBaseTokens = 32_000;

    /// <summary>
    /// The context window session zero is measured against. 200k is the standard; the 1M-context
    /// models report it in their id, which is the only signal available here — so the window is read
    /// off the model of the observed session rather than assumed.
    /// </summary>
    internal static int ContextPageFor(string? model)
        => model is { Length: > 0 } m && m.Contains("[1m]", StringComparison.OrdinalIgnoreCase)
            ? 1_000_000
            : 200_000;

    // Through Settings.DataDir, not by composing the store root again (T354): this cache is
    // deliberately shared across profiles, but where the store lives is one fact and this file
    // is not the place it is decided.
    private static string CachePath => Path.Combine(Settings.DataDir, "context-cache.json");

    // Directories that never hold context but can hold a hundred thousand files. Skipped by name
    // at every level of the nested-instructions walk.
    private static readonly string[] SkipDirs =
    {
        ".git", "node_modules", "bin", "obj", "target", "dist", "build", "out", ".next", ".nuxt",
        "vendor", "venv", ".venv", "__pycache__", ".gradle", ".idea", ".vs", "packages", "coverage",
    };

    private static readonly string[] InstructionNames = { "CLAUDE.md", "CLAUDE.local.md", "AGENTS.md" };

    /// <summary>Scan everything. Safe to call on a background thread; never throws.</summary>
    public static ContextScan Scan(DateTime nowUtc, Options? options = null)
    {
        var opt = options ?? new Options();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (!Directory.Exists(opt.ClaudeRoot))
                return new ContextScan { ScannedUtc = nowUtc, Error = $"not found: {opt.ClaudeRoot}" };

            Plan plan = BuildPlan(nowUtc, opt);

            if (opt.UseCache && LoadCache() is { } cached && cached.Fingerprint == plan.Fingerprint &&
                ImportsUnchanged(cached.Imports))
            {
                cached.Scan.FromCache = true;
                cached.Scan.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                return cached.Scan;
            }

            ContextScan scan = Execute(plan, nowUtc, opt);
            scan.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            if (opt.UseCache) SaveCache(plan, scan);
            return scan;
        }
        catch (Exception e)
        {
            return new ContextScan
            {
                ScannedUtc = nowUtc,
                Error = e.Message,
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
            };
        }
    }

    // ---------------------------------------------------------------- plan (enumerate + stat only)

    private sealed record PlanFile(
        string Slug, ContextKind Kind, LoadMode Mode, string Path, string Label,
        long Bytes, DateTime ModifiedUtc, bool Shared);

    private sealed class PlanProject
    {
        public string Slug = "";
        /// <summary>The <c>~/.claude/projects/&lt;slug&gt;</c> directory itself.</summary>
        public string Dir = "";
        public string Path = "";
        public PathState State;
        public bool FromTranscript;
        public bool Truncated;
        public List<PlanFile> Files = new();
        public List<FileInfo> Transcripts = new();
    }

    private sealed class Plan
    {
        public List<PlanFile> Shared = new();
        public List<PlanProject> Projects = new();
        public string Fingerprint = "";

        // Projects are planned in parallel (the walk is IO-bound), so the global counters that back
        // the file cap are interlocked. Which files get dropped once the cap is reached stops being
        // deterministic, but that only happens in the pathological case the cap exists to survive —
        // and it is reported either way.
        private int _walked;
        private volatile bool _truncated;
        public int Walked => Volatile.Read(ref _walked);
        public bool Truncated { get => _truncated; set => _truncated = value; }
        /// <summary>Claim a slot under the cap; false once it is exhausted.</summary>
        public bool Claim(int cap)
        {
            if (Interlocked.Increment(ref _walked) <= cap) return true;
            Interlocked.Decrement(ref _walked);
            _truncated = true;
            return false;
        }
        public void Count(int n) => Interlocked.Add(ref _walked, n);
    }

    private static Plan BuildPlan(DateTime nowUtc, Options opt)
    {
        var plan = new Plan();
        string root = opt.ClaudeRoot;

        // --- machine-wide sources, measured once ------------------------------------------------
        AddFile(plan, plan.Shared, "", ContextKind.UserInstructions, LoadMode.Eager,
            Path.Combine(root, "CLAUDE.md"), "~/.claude/CLAUDE.md", shared: true, opt);
        foreach (string name in new[] { "settings.json", "settings.local.json" })
            AddFile(plan, plan.Shared, "", ContextKind.Settings, LoadMode.NotLoaded,
                Path.Combine(root, name), "~/.claude/" + name, shared: true, opt);

        AddSkills(plan, plan.Shared, "", Path.Combine(root, "skills"), "user", shared: true, opt);
        AddAgents(plan, plan.Shared, "", Path.Combine(root, "agents"), "user", shared: true, opt);

        // Plugin skills: every marketplace/plugin under ~/.claude/plugins contributes a skills dir,
        // and each of those descriptions is in the eager index exactly like a user skill.
        string plugins = Path.Combine(root, "plugins");
        if (Directory.Exists(plugins))
            foreach (string skillsDir in SafeDirs(plugins, "skills", SearchOption.AllDirectories))
                AddSkills(plan, plan.Shared, "", skillsDir, "plugin", shared: true, opt);

        // --- per project ------------------------------------------------------------------------
        string projectsDir = Path.Combine(root, "projects");
        if (!Directory.Exists(projectsDir)) { plan.Fingerprint = Fingerprint(plan); return plan; }

        DateTime transcriptCutoff = opt.TranscriptDays > 0
            ? nowUtc.AddDays(-opt.TranscriptDays)
            : DateTime.MinValue;

        // The project list is built (and ordered) up front so that planning them in parallel below
        // can't make the fingerprint depend on thread scheduling.
        plan.Projects = SafeEnumerateDirs(projectsDir)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .Select(d => new PlanProject { Slug = Path.GetFileName(d), Dir = d })
            .ToList();

        // Walking 33 project trees is almost entirely waiting on the filesystem, so it parallelizes
        // nearly linearly — which is what keeps a warm rescan comfortably inside the tray's poll
        // budget rather than merely at it. Each project writes only into its own PlanProject; the
        // one piece of shared state is the interlocked file counter behind the cap.
        Parallel.ForEach(plan.Projects,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) },
            pp => PlanProjectFiles(plan, pp, transcriptCutoff, opt));

        plan.Fingerprint = Fingerprint(plan);
        return plan;
    }

    private static void PlanProjectFiles(Plan plan, PlanProject pp, DateTime transcriptCutoff, Options opt)
    {
        string dir = pp.Dir;

        // Transcripts: newest first, age-filtered, capped. Also the fallback source of truth
        // for the project's real path (each record carries the session's cwd).
        pp.Transcripts = SafeFiles(dir, "*.jsonl")
            .Where(f => f.LastWriteTimeUtc >= transcriptCutoff)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(Math.Max(1, opt.TranscriptsPerProject))
            .ToList();
        plan.Count(pp.Transcripts.Count);

        // Slug → path, best answer first (T105 — one resolver for the whole app, `ProjectSlug`).
        // The recorded cwd walked up to the ancestor that *encodes to this slug* is exact: it
        // verifies instead of guessing, so it is preferred over probing the filesystem, which can
        // only guess (both `claude\tray` and `claude-tray` encode identically, and if both exist the
        // probe returns whichever it reaches first). It also answers for a directory that is gone.
        string? cwd = CwdFromTranscripts(pp.Transcripts);
        if (cwd is { Length: > 0 } && ProjectSlug.RootFor(pp.Slug, cwd) is { Length: > 0 } root)
        {
            pp.Path = root;
            pp.FromTranscript = true;
            pp.State = Directory.Exists(root) ? PathState.Resolved : PathState.Missing;
        }
        else if (ProjectSlug.TryProbe(pp.Slug, out string probed))
        {
            pp.Path = probed;
            pp.State = PathState.Resolved;
        }
        else if (cwd is { Length: > 0 })
        {
            // Nothing verified and nothing on disk, but the session did record where it ran. Kept as
            // a fallback rather than an answer: a `cd` inside the session moves it off the root.
            pp.Path = cwd;
            pp.FromTranscript = true;
            pp.State = Directory.Exists(cwd) ? PathState.Resolved : PathState.Missing;
        }
        else
        {
            pp.Path = ProjectSlug.Literal(pp.Slug);
            pp.State = pp.Path.Length > 0 ? PathState.Missing : PathState.NotAPath;
            if (pp.State == PathState.NotAPath) pp.Path = "";
        }

        // Memory lives under the project dir in ~/.claude, so it is measurable even for an
        // orphaned project — which is precisely the dead weight worth reporting.
        string memory = Path.Combine(dir, "memory");
        if (Directory.Exists(memory))
            foreach (FileInfo f in SafeFiles(memory, "*.md").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                bool index = f.Name.Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase);
                AddFile(plan, pp.Files, pp.Slug,
                    index ? ContextKind.MemoryIndex : ContextKind.MemoryFile,
                    index ? LoadMode.Eager : LoadMode.Lazy,
                    f, "memory/" + f.Name, shared: false, opt);
            }

        if (pp.State != PathState.Resolved) return;

        // The instruction chain: the project root's own files plus every ancestor's, all eager
        // (Claude Code walks up to the drive root), then nested ones, which are lazy — they
        // only load when a file in that subtree is read.
        foreach (string name in InstructionNames)
            AddFile(plan, pp.Files, pp.Slug, ContextKind.ProjectInstructions, LoadMode.Eager,
                Path.Combine(pp.Path, name), name, shared: false, opt);

        for (string? up = SafeParent(pp.Path); up != null; up = SafeParent(up))
            foreach (string name in InstructionNames)
                AddFile(plan, pp.Files, pp.Slug, ContextKind.ProjectInstructions, LoadMode.Eager,
                    Path.Combine(up, name), Rel(pp.Path, Path.Combine(up, name)), shared: false, opt);

        AddNested(plan, pp, opt);

        string dotClaude = Path.Combine(pp.Path, ".claude");
        AddSkills(plan, pp.Files, pp.Slug, Path.Combine(dotClaude, "skills"), "project", shared: false, opt);
        AddAgents(plan, pp.Files, pp.Slug, Path.Combine(dotClaude, "agents"), "project", shared: false, opt);
        foreach (string name in new[] { "settings.json", "settings.local.json" })
            AddFile(plan, pp.Files, pp.Slug, ContextKind.Settings, LoadMode.NotLoaded,
                Path.Combine(dotClaude, name), ".claude/" + name, shared: false, opt);
    }

    // Nested CLAUDE.md / AGENTS.md below the project root: real context, but lazy — Claude Code
    // pulls one in when it reads a file in that directory. Depth- and skip-list-bounded, because
    // a repo root can hide a quarter of a million files under node_modules.
    private static void AddNested(Plan plan, PlanProject pp, Options opt)
    {
        int dirs = 0;
        // One pruning enumeration of the subtree rather than three stat calls per directory: the
        // enumerator hands back name, size and mtime from the directory entry it already read, and
        // ShouldRecursePredicate lets us refuse node_modules *before* descending into it — which is
        // the difference between a fast scan and one that walks a quarter-million files.
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = opt.NestedDepth,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,   // don't follow junctions into loops
        };

        var found = new List<(string path, long len, DateTime mtime)>();
        try
        {
            var walk = new System.IO.Enumeration.FileSystemEnumerable<(string, long, DateTime)>(
                pp.Path,
                (ref System.IO.Enumeration.FileSystemEntry e) =>
                    (e.ToFullPath(), e.Length, e.LastWriteTimeUtc.UtcDateTime),
                options)
            {
                ShouldIncludePredicate = (ref System.IO.Enumeration.FileSystemEntry e) =>
                    !e.IsDirectory && IsInstructionName(e.FileName),
                ShouldRecursePredicate = (ref System.IO.Enumeration.FileSystemEntry e) =>
                {
                    // A monorepo can carry thousands of directories inside the depth limit. Count
                    // them, and once past the cap stop descending and say so — a silent cap that
                    // reads as "all clear" is worse than no feature.
                    if (++dirs > opt.NestedDirCap) { plan.Truncated = true; pp.Truncated = true; return false; }
                    return !e.FileName.StartsWith('.') && !IsSkippedDir(e.FileName);
                },
            };
            found.AddRange(walk);
        }
        catch { /* an unreadable subtree just yields fewer nested files */ }

        foreach (var (path, len, mtime) in found)
        {
            // The project root's own CLAUDE.md/AGENTS.md were already added as eager sources.
            if (string.Equals(Path.GetDirectoryName(path), pp.Path, StringComparison.OrdinalIgnoreCase))
                continue;
            AddFile(plan, pp.Files, pp.Slug, ContextKind.NestedInstructions, LoadMode.Lazy,
                path, Rel(pp.Path, path), len, mtime, false, opt);
        }
    }

    private static bool IsInstructionName(ReadOnlySpan<char> name)
    {
        foreach (string n in InstructionNames)
            if (name.Equals(n, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool IsSkippedDir(ReadOnlySpan<char> name)
    {
        foreach (string d in SkipDirs)
            if (name.Equals(d, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // Skills: <root>/<name>/SKILL.md, possibly a level deeper for namespaced ones.
    private static void AddSkills(Plan plan, List<PlanFile> into, string slug, string dir,
        string origin, bool shared, Options opt)
    {
        if (!Directory.Exists(dir)) return;
        foreach (FileInfo f in SafeFiles(dir, "SKILL.md", SearchOption.AllDirectories))
            AddFile(plan, into, slug, ContextKind.Skill, LoadMode.Lazy, f,
                $"{origin}:{Path.GetFileName(Path.GetDirectoryName(f.FullName)!)}", shared, opt);
    }

    private static void AddAgents(Plan plan, List<PlanFile> into, string slug, string dir,
        string origin, bool shared, Options opt)
    {
        if (!Directory.Exists(dir)) return;
        foreach (FileInfo f in SafeFiles(dir, "*.md"))
            AddFile(plan, into, slug, ContextKind.Agent, LoadMode.Lazy, f,
                $"{origin}:{Path.GetFileNameWithoutExtension(f.Name)}", shared, opt);
    }

    private static void AddFile(Plan plan, List<PlanFile> into, string slug, ContextKind kind,
        LoadMode mode, string path, string label, bool shared, Options opt)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists) return;
        AddFile(plan, into, slug, kind, mode, fi, label, shared, opt);
    }

    private static void AddFile(Plan plan, List<PlanFile> into, string slug, ContextKind kind,
        LoadMode mode, FileInfo fi, string label, bool shared, Options opt)
        => AddFile(plan, into, slug, kind, mode, fi.FullName, label, fi.Length, fi.LastWriteTimeUtc, shared, opt);

    // The size/mtime overload: used when a directory enumeration already handed those back, so we
    // don't pay a second stat call per file.
    private static void AddFile(Plan plan, List<PlanFile> into, string slug, ContextKind kind,
        LoadMode mode, string path, string label, long bytes, DateTime modifiedUtc, bool shared, Options opt)
    {
        if (!plan.Claim(opt.FileCap)) return;
        into.Add(new PlanFile(slug, kind, mode, path, label, bytes, modifiedUtc, shared));
    }

    // ---------------------------------------------------------------- execute (read + estimate)

    private static ContextScan Execute(Plan plan, DateTime nowUtc, Options opt)
    {
        var scan = new ContextScan
        {
            ScannedUtc = nowUtc,
            FilesWalked = plan.Walked,
            Truncated = plan.Truncated,
        };

        var imports = new ImportSink(plan.Shared);
        scan.Shared = plan.Shared.Select(f => Measure(f, imports)).ToList();
        // `@path` imports are eager continuations of the file that names them, so they belong in
        // the same list — appended after measuring, since only reading the parent reveals them.
        scan.Shared.AddRange(imports.Found.Select(f => Measure(f, null)));

        // Reading ~800 small files and the head of every recent transcript is again IO-bound, and
        // each project is independent — so it fans out the same way the plan does, into a pre-sized
        // array so the result order stays the plan's order regardless of who finishes first.
        var measured = new ContextProject[plan.Projects.Count];
        Parallel.For(0, plan.Projects.Count,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) },
            i =>
            {
                PlanProject pp = plan.Projects[i];
                var project = new ContextProject
                {
                    Slug = pp.Slug,
                    Path = pp.Path,
                    State = pp.State,
                    FromTranscript = pp.FromTranscript,
                    TranscriptsRead = pp.Transcripts.Count,
                    Truncated = pp.Truncated,
                };
                var projectImports = new ImportSink(pp.Files, plan.Shared);
                project.Sources = pp.Files.Select(f => Measure(f, projectImports)).ToList();
                project.Sources.AddRange(projectImports.Found.Select(f => Measure(f, null)));
                project.Observed = MeasureSessionZero(pp.Transcripts);
                measured[i] = project;
            });

        scan.Projects = measured
            .OrderByDescending(p => p.EagerTokens)
            .ThenBy(p => p.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return scan;
    }

    /// <summary>
    /// Collector for `@path` imports, and the guard against counting one twice. On Windows the
    /// classic case is a one-line <c>CLAUDE.md</c> that says <c>@agents.md</c> — case-insensitively
    /// the very <c>AGENTS.md</c> already measured, which without this check reports a 43 KB file as
    /// 26k eager tokens instead of 13k.
    /// </summary>
    private sealed class ImportSink
    {
        public readonly List<PlanFile> Found = new();
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

        public ImportSink(params IEnumerable<PlanFile>[] already)
        {
            foreach (var list in already)
                foreach (PlanFile f in list) _seen.Add(f.Path);
        }

        public bool TryAdd(PlanFile f)
        {
            if (!_seen.Add(f.Path)) return false;
            Found.Add(f);
            return true;
        }
    }

    // Read one file and turn it into a measured source. `imports` collects `@path` lines found in
    // eager instruction files (one level deep, as Claude Code documents); null disables the follow,
    // which is how the recursion is stopped after that one level.
    private static ContextSource Measure(PlanFile f, ImportSink? imports)
    {
        var s = new ContextSource
        {
            Kind = f.Kind,
            Mode = f.Mode,
            Path = f.Path,
            Label = f.Label,
            Bytes = f.Bytes,
            ModifiedUtc = f.ModifiedUtc,
            Shared = f.Shared,
        };

        // settings.json is never in the prompt — its size is the only interesting thing about it,
        // and it is the one file here that can be 90 KB of permissions. Don't read it.
        if (f.Kind == ContextKind.Settings) return s;

        string? text = ReadTextSafe(f.Path);
        if (text == null) { s.Note = "unreadable"; return s; }

        s.Chars = text.Length;
        s.Tokens = TokenEstimate.Of(text);

        var (name, description, type) = Frontmatter(text);
        s.Name = name;
        s.Description = description;
        s.Type = type;

        // Extras the rule engine needs, gathered here because the file is already in hand — a
        // second pass would mean reading every memory file twice.
        if (f.Kind is ContextKind.MemoryIndex or ContextKind.MemoryFile)
            s.ContentHash = Hash(text);
        if (f.Kind == ContextKind.MemoryIndex)
            s.IndexLinks = MarkdownLinks(text);

        s.EagerTokens = f.Mode switch
        {
            LoadMode.Eager => s.Tokens,
            // A skill or agent body is lazy, but its name + description sits in the always-loaded
            // index. That entry is the honest eager cost of *having* the skill available.
            LoadMode.Lazy when f.Kind is ContextKind.Skill or ContextKind.Agent => IndexEntryTokens(s, f),
            _ => 0,
        };

        if (imports != null && f.Mode == LoadMode.Eager)
            CollectImports(f, text, imports);

        return s;
    }

    // The prompt lists each skill/agent as roughly "- name: description (Tools: …)". Estimate that
    // line, plus a few tokens of scaffolding. Falls back to the directory name when the frontmatter
    // has no description at all — which is itself a finding for Phase 3.
    private static int IndexEntryTokens(ContextSource s, PlanFile f)
    {
        const int Scaffolding = 6;
        string name = s.Name ?? f.Label;
        return Scaffolding + TokenEstimate.Of(name + ": " + (s.Description ?? ""));
    }

    /// <summary>
    /// The <c>](target)</c> half of every markdown link, reduced to bare file names: the index format
    /// is <c>- [Title](file.md) — hook</c>. Anything with a directory part or a scheme is dropped, so
    /// a link to <c>../ROADMAP.md</c> or an URL can never be reported as a broken memory pointer.
    /// </summary>
    private static List<string> MarkdownLinks(string text)
    {
        var links = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        while ((i = text.IndexOf("](", i, StringComparison.Ordinal)) >= 0)
        {
            i += 2;
            int end = text.IndexOf(')', i);
            if (end < 0) break;
            string target = text[i..end].Trim();
            i = end + 1;

            if (target.Length == 0 || target.Contains('/') || target.Contains('\\') ||
                target.Contains(':') || target.StartsWith('#')) continue;
            if (!target.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Add(target)) links.Add(target);
        }
        return links;
    }

    // FNV-1a 64 over the text — the same non-cryptographic hash the cache fingerprint uses. Enough to
    // decide two files are the same content; nothing here is a security boundary.
    private static string Hash(string text)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            h ^= b;
            h *= 1099511628211UL;
        }
        return h.ToString("x16");
    }

    // Minimal YAML frontmatter reader: `name`, `description`, `type`, supporting the single-line
    // form and the folded/literal block forms (`>-`, `|`) that long skill descriptions use.
    private static (string? name, string? description, string? type) Frontmatter(string text)
    {
        if (!text.StartsWith("---")) return (null, null, null);

        string? name = null, description = null, type = null;
        using var reader = new StringReader(text);
        reader.ReadLine();                       // the opening ---
        string? current = null;                  // key whose folded block we are inside
        var block = new StringBuilder();

        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("---")) break;   // end of frontmatter

            // Continuation of a folded block: indented, and we are inside one.
            if (current != null && (line.StartsWith(' ') || line.StartsWith('\t')))
            {
                block.Append(line.Trim()).Append(' ');
                continue;
            }
            if (current != null) { Assign(current, Folded(block)); current = null; block.Clear(); }

            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string key = line[..colon].Trim().ToLowerInvariant();
            string value = line[(colon + 1)..].Trim();
            if (key is not ("name" or "description" or "type")) continue;

            // A block indicator, or nothing at all after the colon: both mean the value is the
            // indented lines that follow. The bare form is what a plain multi-line YAML scalar looks
            // like, and treating it as an empty value made real skill descriptions read as missing —
            // which both understated their (eager) index cost and produced a bogus finding.
            if (value is ">" or ">-" or "|" or "|-" or "") { current = key; continue; }
            Assign(key, value.Trim('"', '\''));
        }
        if (current != null) Assign(current, Folded(block));

        void Assign(string key, string value)
        {
            if (value.Length == 0) return;
            switch (key)
            {
                case "name": name = value; break;
                case "description": description = value; break;
                case "type": type = value; break;
            }
        }

        return (name, description, type);

        // A collected block, with the quotes a multi-line scalar may be wrapped in removed.
        static string Folded(StringBuilder sb) => sb.ToString().Trim().Trim('"', '\'');
    }

    // `@path` on its own line pulls another file into the instructions. Follow one level and flag
    // a self/parent cycle rather than recursing into it.
    private static void CollectImports(PlanFile parent, string text, ImportSink into)
    {
        string baseDir = Path.GetDirectoryName(parent.Path) ?? "";
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length < 2 || line[0] != '@' || line.Contains(' ')) continue;

            string spec = line[1..].Trim();
            if (spec.Length == 0) continue;
            string path = spec.StartsWith('~')
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), spec[1..].TrimStart('/', '\\'))
                : Path.GetFullPath(Path.Combine(baseDir, spec));

            var fi = new FileInfo(path);
            if (!fi.Exists) continue;

            // TryAdd refuses a path already measured — a self-import, a cycle, and the
            // `CLAUDE.md`-imports-`AGENTS.md` case all fall out of that one check.
            into.TryAdd(new PlanFile(parent.Slug, ContextKind.Import, LoadMode.Eager, fi.FullName,
                "@" + spec, fi.Length, fi.LastWriteTimeUtc, parent.Shared));
        }
    }

    // ---------------------------------------------------------------- session zero (observed)

    /// <summary>
    /// The measured startup context of recent sessions: for each transcript, the total context of
    /// its first non-sidechain assistant request — which <em>is</em> everything Claude Code loaded
    /// before the model saw the first prompt. Reported as a median over the sampled sessions, since
    /// an individual session's first turn can carry IDE attachments or a pasted prompt on top.
    /// </summary>
    private static SessionZero? MeasureSessionZero(List<FileInfo> transcripts)
    {
        var samples = new List<(int tokens, string model, DateTime when)>();
        foreach (FileInfo f in transcripts)
            if (FirstRequest(f) is { } s) samples.Add(s);

        if (samples.Count == 0) return null;
        samples.Sort((a, b) => a.tokens.CompareTo(b.tokens));
        var median = samples[samples.Count / 2];

        return new SessionZero
        {
            Median = median.tokens,
            Min = samples[0].tokens,
            Max = samples[^1].tokens,
            Samples = samples.Count,
            Model = median.model,
            NewestUtc = samples.Max(s => s.when),
        };
    }

    // Only the head of the file is read: session zero is by definition at the top, and a transcript
    // can be tens of megabytes.
    private const int HeadLines = 60;

    private static (int tokens, string model, DateTime when)? FirstRequest(FileInfo f)
    {
        try
        {
            int seen = 0;
            foreach (string line in File.ReadLines(f.FullName))
            {
                if (++seen > HeadLines) return null;
                if (line.Length == 0) continue;

                // A session continued after a compaction opens with the summary of the one before
                // it: its first request carries replayed history, not a startup context. Skip the
                // whole file rather than record a number that means something else.
                if (line.Contains("\"isCompactSummary\":true") || line.Contains("\"type\":\"summary\""))
                    return null;
                if (!line.Contains("\"assistant\"")) continue;

                using var doc = JsonDocument.Parse(line);
                JsonElement root = doc.RootElement;
                if (!root.TryGetProperty("type", out var t) || t.GetString() != "assistant") continue;
                // A subagent's first request carries its own (different) prompt — not session zero.
                if (root.TryGetProperty("isSidechain", out var sc) && sc.ValueKind == JsonValueKind.True) continue;
                if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) continue;
                if (!msg.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) continue;

                string model = msg.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "";
                if (model == "<synthetic>") continue;   // CLI-generated, no real API usage

                int tokens = (int)(Num(u, "input_tokens")
                                   + Num(u, "cache_creation_input_tokens")
                                   + Num(u, "cache_read_input_tokens"));
                if (tokens <= 0) continue;

                DateTime when = root.TryGetProperty("timestamp", out var ts) &&
                                DateTime.TryParse(ts.GetString(), null,
                                    System.Globalization.DateTimeStyles.AdjustToUniversal |
                                    System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime parsed)
                    ? parsed : f.LastWriteTimeUtc;

                return (tokens, model, when);
            }
        }
        catch { /* a session being written, or a truncated line — just no sample from this file */ }
        return null;
    }

    // The session's working directory, recorded on every record. Authoritative when slug probing
    // is ambiguous or the directory is gone.
    private static string? CwdFromTranscripts(List<FileInfo> transcripts)
    {
        foreach (FileInfo f in transcripts)
        {
            try
            {
                int seen = 0;
                foreach (string line in File.ReadLines(f.FullName))
                {
                    if (++seen > HeadLines) break;
                    if (!line.Contains("\"cwd\"")) continue;
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("cwd", out var c) && c.GetString() is { Length: > 0 } cwd)
                        return cwd;
                }
            }
            catch { /* try the next transcript */ }
        }
        return null;
    }

    private static double Num(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    // ---------------------------------------------------------------- calibration

    /// <summary>
    /// The correction that turns a filesystem estimate into a real session-zero number, fitted from
    /// transcripts: <c>observed ≈ Base + Chars / CharsPerToken</c> over every project that has both
    /// an observed median and eager context on disk.
    ///
    /// <see cref="Base"/> is what no scan can see — Claude Code's system prompt and tool
    /// definitions, identical across projects — and <see cref="CharsPerToken"/> is the measured
    /// density of the instruction files themselves, which is what validates
    /// <see cref="TokenEstimate"/> instead of trusting its constants.
    /// </summary>
    internal sealed record Calibration(
        double Base, double BaseP25, double BaseP75, double CharsPerToken, int SlopePairs,
        int Points, int WithinBand, double MedianErrorPct, double WorstErrorPct,
        List<CalibrationPoint> Samples)
    {
        /// <summary>The ±band the corrected estimate is judged against (task 1.2's ±15%).</summary>
        public const double BandPct = 15;
    }

    /// <summary>One project in the fit: what the scanner sees, and what the transcripts measured.</summary>
    internal sealed record CalibrationPoint(string Slug, double Chars, int Estimated, int Observed)
    {
        /// <summary>The scan's estimate plus the base overhead no scan can see.</summary>
        public int Corrected(double fittedBase) => Estimated + (int)Math.Round(fittedBase);

        /// <summary>How far the corrected estimate lands from the measurement, in percent.</summary>
        public double ErrorPct(double fittedBase)
            => Observed > 0 ? Math.Abs(Corrected(fittedBase) - Observed) / (double)Observed * 100 : 0;
    }

    public static Calibration? Calibrate(ContextScan scan)
    {
        // One point per project: eager characters and eager token estimate on disk, against the
        // observed startup tokens. The shared sources load for every project, so they are part of
        // every point — they shift the whole line, not its slope.
        double sharedChars = EagerChars(scan.Shared);
        int sharedTokens = scan.SharedEagerTokens;

        var points = new List<CalibrationPoint>();
        foreach (ContextProject p in scan.Projects)
        {
            if (p.Observed is not { Samples: > 0 } o) continue;
            points.Add(new CalibrationPoint(p.Slug,
                sharedChars + EagerChars(p.Sources), sharedTokens + p.EagerTokens, o.Median));
        }
        if (points.Count < 3) return null;

        // The base overhead — Claude Code's system prompt, tool definitions and any connected MCP
        // servers' schemas — is what no filesystem scan can see. It is the same order of magnitude
        // in every project, so the *median* residual estimates it. Deliberately not least squares:
        // a couple of sessions opened with a big pasted prompt or a pile of IDE attachments drag a
        // mean fit badly, and the quartiles below say how much spread is left.
        var residuals = points.Select(p => (double)(p.Observed - p.Estimated)).OrderBy(r => r).ToList();
        double bas = Percentile(residuals, 0.50);

        // Chars per token, measured the same robust way: the median slope over every pair of
        // projects far enough apart in size to carry signal (Theil–Sen). Pairwise differences cancel
        // the base overhead entirely, which is what makes this a measurement of the *files* rather
        // than of the harness around them.
        // Slopes are collected as tokens-per-char and inverted at the end, not filtered to the
        // pairs that came out positive — dropping the ones noise pushed the wrong way would bias
        // the median upward, which is exactly the kind of flattery this number exists to avoid.
        var slopes = new List<double>();
        for (int i = 0; i < points.Count; i++)
            for (int j = i + 1; j < points.Count; j++)
            {
                double dc = points[i].Chars - points[j].Chars;
                if (Math.Abs(dc) < MinPairChars) continue;
                slopes.Add((points[i].Observed - points[j].Observed) / dc);
            }
        slopes.Sort();
        double tokensPerChar = slopes.Count > 0 ? Percentile(slopes, 0.50) : 0;

        var errors = points.Select(p => p.ErrorPct(bas)).OrderBy(e => e).ToList();

        return new Calibration(
            Base: bas,
            BaseP25: Percentile(residuals, 0.25),
            BaseP75: Percentile(residuals, 0.75),
            CharsPerToken: tokensPerChar > 0 ? 1 / tokensPerChar : 0,
            SlopePairs: slopes.Count,
            Points: points.Count,
            WithinBand: errors.Count(e => e <= Calibration.BandPct),
            MedianErrorPct: Percentile(errors.ToList(), 0.50),
            WorstErrorPct: errors[^1],
            Samples: points.OrderByDescending(p => p.Observed).ToList());
    }

    // Pairs closer than this in characters carry more noise than signal — the token difference they
    // imply is smaller than the per-session variation from attachments and the first prompt.
    private const int MinPairChars = 4_000;

    private static double Percentile(List<double> sorted, double q)
    {
        if (sorted.Count == 0) return 0;
        double pos = q * (sorted.Count - 1);
        int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
        return lo == hi ? sorted[lo] : sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }

    /// <summary>Characters that are actually in the prompt: whole eager files, plus just the
    /// name+description of each lazy skill/agent (its entry in the always-loaded index).</summary>
    private static double EagerChars(IEnumerable<ContextSource> sources)
        => sources.Sum(s => s.Mode == LoadMode.Eager
            ? s.Chars
            : s.Kind is ContextKind.Skill or ContextKind.Agent
                ? (s.Name?.Length ?? 0) + (s.Description?.Length ?? 0) + 2
                : 0);

    // Slug resolution moved to `ProjectSlug` (T105): three call sites had grown their own reading of
    // one lossy encoding, and one of them encoded a different set of characters than Claude Code does.

    // ---------------------------------------------------------------- cache

    private sealed class CacheFile
    {
        public string Fingerprint { get; set; } = "";
        /// <summary>`@import` targets, fingerprinted separately: they are only discovered by
        /// reading a parent file, so the plan phase can't see them.</summary>
        public List<ImportStamp> Imports { get; set; } = new();
        public ContextScan Scan { get; set; } = new();
    }

    internal sealed class ImportStamp
    {
        public string Path { get; set; } = "";
        public long Bytes { get; set; }
        public long Ticks { get; set; }
    }

    private static readonly JsonSerializerOptions CacheJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static CacheFile? LoadCache()
    {
        try
        {
            return File.Exists(CachePath)
                ? JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(CachePath), CacheJson)
                : null;
        }
        catch { return null; }   // corrupt or older shape — just rescan
    }

    private static void SaveCache(Plan plan, ContextScan scan)
    {
        // T239: an observing tray writes NOTHING - caches included. They are shared and rewritten
        // whole, so two processes doing it at once can tear one; and "writes nothing" is a promise
        // that can be checked, while "nothing except caches" is a list that grows a hole.
        if (ProfileStore.Observing) return;
        try
        {
            var cache = new CacheFile
            {
                Fingerprint = plan.Fingerprint,
                Imports = scan.Shared.Concat(scan.Projects.SelectMany(p => p.Sources))
                    .Where(s => s.Kind == ContextKind.Import)
                    .Select(s => new ImportStamp { Path = s.Path, Bytes = s.Bytes, Ticks = s.ModifiedUtc.Ticks })
                    .ToList(),
                Scan = scan,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(cache, CacheJson));
        }
        catch { /* the cache is an optimization; a failure to write it costs a rescan, nothing more */ }
    }

    private static bool ImportsUnchanged(List<ImportStamp> imports)
    {
        try
        {
            foreach (ImportStamp i in imports)
            {
                var fi = new FileInfo(i.Path);
                if (!fi.Exists || fi.Length != i.Bytes || fi.LastWriteTimeUtc.Ticks != i.Ticks) return false;
            }
            return true;
        }
        catch { return false; }   // can't verify => rescan
    }

    // Identity of everything the plan found: path + size + mtime of every context file and every
    // transcript in the sample window, plus the project set and its resolution. A directory-mtime
    // fingerprint would be cheaper but wrong — editing MEMORY.md in place leaves its directory
    // untouched, and that is the single most important change to notice.
    /// <summary>
    /// Bumped whenever a field is added to what a scan produces. The fingerprint is otherwise purely
    /// about the files on disk, so without this an older cache would deserialize cleanly with the new
    /// fields empty — and the rules would quietly see no index links and no content hashes.
    /// </summary>
    private const string CacheSchema = "v2";

    private static string Fingerprint(Plan plan)
    {
        var sb = new StringBuilder(CacheSchema + "\n");
        void Stamp(PlanFile f) => sb.Append(f.Path).Append('|').Append(f.Bytes).Append('|')
                                   .Append(f.ModifiedUtc.Ticks).Append('\n');

        foreach (PlanFile f in plan.Shared) Stamp(f);
        foreach (PlanProject p in plan.Projects)
        {
            sb.Append('#').Append(p.Slug).Append('|').Append(p.Path).Append('|').Append((int)p.State).Append('\n');
            foreach (PlanFile f in p.Files) Stamp(f);
            foreach (FileInfo t in p.Transcripts)
                sb.Append('t').Append(t.FullName).Append('|').Append(t.Length).Append('|')
                  .Append(t.LastWriteTimeUtc.Ticks).Append('\n');
        }
        sb.Append(plan.Truncated ? "!trunc" : "");

        // FNV-1a 64: no allocation, no crypto dependency, and collision-safe enough for a cache key.
        ulong hash = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(sb.ToString()))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash.ToString("x16");
    }

    // ---------------------------------------------------------------- filesystem helpers
    // Every one of these swallows the access/IO errors that a walk over a developer's whole drive
    // will hit (permissions, junction loops, files vanishing mid-scan) — a scan must never fail
    // wholesale because one directory was unreadable. Two rules make that actually true: results are
    // materialized, because a try around a lazy enumerable catches nothing (the IO happens inside the
    // caller's foreach), and anything recursive goes through SafeWalk, which scopes a failure to the
    // subtree that caused it instead of aborting the sweep.

    private static string? ReadTextSafe(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return null; }
    }

    private static IEnumerable<string> SafeEnumerateDirs(string dir)
    {
        try { return Directory.GetDirectories(dir); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeDirs(string dir, string pattern, SearchOption option)
    {
        if (option == SearchOption.AllDirectories) return SafeWalk.Dirs(dir, pattern);
        try { return Directory.GetDirectories(dir, pattern); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<FileInfo> SafeFiles(string dir, string pattern,
        SearchOption option = SearchOption.TopDirectoryOnly)
    {
        if (option == SearchOption.AllDirectories) return SafeWalk.Files(dir, pattern);
        try { return new DirectoryInfo(dir).GetFiles(pattern); }
        catch { return Array.Empty<FileInfo>(); }
    }

    private static string? SafeParent(string dir)
    {
        try { return Directory.GetParent(dir)?.FullName; }
        catch { return null; }
    }

    private static string Rel(string from, string path)
    {
        try { return Path.GetRelativePath(from, path).Replace('\\', '/'); }
        catch { return path; }
    }
}
