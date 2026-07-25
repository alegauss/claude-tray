namespace ClaudeTray;

/// <summary>How much a finding matters. Ordered so the report can group by it.</summary>
internal enum RuleSeverity
{
    /// <summary>It is eager and large, or it is duplicated wholesale. Fix this one.</summary>
    High,
    /// <summary>Wrong or rotting rather than expensive — a broken pointer, a dead directory.</summary>
    Medium,
    /// <summary>Worth tidying, costs little today.</summary>
    Low,
    /// <summary>Not a problem, just something to look at when convenient.</summary>
    Info,
}

/// <summary>
/// One thing worth doing something about, and what to do. <see cref="Fix"/> is not optional by
/// design: a finding without a fix is nagging, which is the failure mode this whole feature is
/// trying to avoid.
/// </summary>
/// <param name="RuleId">Stable identifier (<c>index-too-big</c>) — the key a localized UI will use.</param>
/// <param name="Scope">Which project (or <c>~/.claude</c>) the finding belongs to.</param>
/// <param name="Message">One plain sentence: what is true, with the number that makes it true.</param>
/// <param name="Fix">One plain sentence: what to do instead.</param>
/// <param name="Path">The file or directory it is about, when there is exactly one.</param>
internal sealed record Finding(
    string RuleId, RuleSeverity Severity, string Scope, string Message, string Fix, string? Path = null);

/// <summary>
/// The advisor. Every rule here fires on the baseline measured from a real machine
/// (IMPROVEMENTS §III), so none of them are hypothetical, and each one carries the concrete fix.
///
/// Two standing constraints shape what is and isn't a rule:
/// <list type="bullet">
/// <item>Only what the scan already knows — sizes, names, frontmatter, timestamps, index link
/// targets and memory-file hashes. No new IO, and never file contents.</item>
/// <item>No false positives on a real machine. Where the honest signal is fuzzy (does this skill
/// description contain "trigger words"?) the rule is narrowed to the part that is objectively
/// measurable (is there a description at all; is it longer than the index can afford) rather than
/// guessing and being wrong at the user.</item>
/// </list>
/// This is deliberately independent of the window: it is exercised headlessly by
/// <c>--context --check</c> first, so the rules can be judged against a whole machine before any of
/// them reach a UI.
/// </summary>
internal static class ContextRules
{
    // Thresholds. Each is the point where the measured baseline stops looking like normal use.
    /// <summary>The index is eager. Past this it is a per-request tax rather than a table of contents.</summary>
    public const int IndexMaxBytes = 8 * 1024;
    /// <summary>"One memory = one fact"; past this a file is several facts wearing one name.</summary>
    public const int MemoryFileMaxBytes = 4 * 1024;
    /// <summary>An instruction file is eager in full — this is where it starts to dominate.</summary>
    public const int InstructionsMaxBytes = 12 * 1024;
    /// <summary>Both instruction files loading in full, rather than one importing the other.</summary>
    public const int DualInstructionsMinBytes = 1024;
    /// <summary>Never in the prompt, so this is a tidiness signal: accumulated permissions.</summary>
    public const int SettingsMaxBytes = 40 * 1024;
    /// <summary>A skill description is eager. Past this the index pays for prose.</summary>
    public const int DescriptionMaxChars = 500;
    /// <summary>Untouched this long is a review-queue candidate, nothing worse.</summary>
    public const int StaleDays = 90;

    /// <summary>The valid <c>type:</c> values for a memory file, per the memory format.</summary>
    private static readonly string[] MemoryTypes = { "user", "feedback", "project", "reference" };

    /// <summary>Every finding in the scan, most severe first.</summary>
    public static List<Finding> Evaluate(ContextScan scan, DateTime nowUtc)
    {
        var findings = new List<Finding>();

        // Machine-wide sources: the user instruction file, the user/plugin skill index, settings.
        CheckInstructions(findings, "~/.claude", scan.Shared);
        CheckDescriptions(findings, "~/.claude", scan.Shared);
        CheckSettings(findings, "~/.claude", scan.Shared);

        foreach (ContextProject p in scan.Projects)
        {
            string scope = p.ShortPath;
            CheckDeadProjectDir(findings, p);
            CheckIndexSize(findings, scope, p);
            CheckIndexPointers(findings, scope, p);
            CheckMemoryFileSizes(findings, scope, p);
            CheckMemoryFrontmatter(findings, scope, p);
            CheckStaleMemory(findings, scope, p, nowUtc);
            CheckInstructions(findings, scope, p.Sources);
            CheckDualInstructions(findings, scope, p);
            CheckDescriptions(findings, scope, p.Sources);
            CheckSettings(findings, scope, p.Sources);
        }

        CheckDuplicateMemoryDirs(findings, scan);

        return findings
            .OrderBy(f => (int)f.Severity)
            .ThenBy(f => f.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .ToList();
    }

    // ---------------------------------------------------------------- memory

    // The index loads on every request of every session in the project, so its size is the one memory
    // number that is unambiguously a cost rather than a symptom.
    private static void CheckIndexSize(List<Finding> into, string scope, ContextProject p)
    {
        foreach (ContextSource s in Of(p.Sources, ContextKind.MemoryIndex))
        {
            if (s.Bytes <= IndexMaxBytes) continue;
            into.Add(new Finding("index-too-big", RuleSeverity.High, scope,
                $"The memory index is {Kb(s.Bytes)} ({TokenEstimate.Format(s.EagerTokens)} tokens) and every request of every session pays for it.",
                "Split it by area, or prune the pointers you no longer recall by.", s.Path));
        }
    }

    // A pointer to a memory that is gone, and a memory nobody points at, are the same bookkeeping
    // slip seen from two sides — and both are cheap to fix once named.
    private static void CheckIndexPointers(List<Finding> into, string scope, ContextProject p)
    {
        ContextSource? index = Of(p.Sources, ContextKind.MemoryIndex).FirstOrDefault();
        if (index?.IndexLinks is not { } links) return;

        var files = Of(p.Sources, ContextKind.MemoryFile)
            .Select(s => System.IO.Path.GetFileName(s.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dangling = links.Where(l => !files.Contains(l)).ToList();
        if (dangling.Count > 0)
            into.Add(new Finding("index-pointer-dead", RuleSeverity.Medium, scope,
                $"{dangling.Count} index {Plural(dangling.Count, "pointer", "pointers")} " +
                $"{Plural(dangling.Count, "resolves", "resolve")} to no file ({Examples(dangling)}).",
                "Drop those lines — a pointer to a deleted memory costs tokens and finds nothing.",
                index.Path));

        var indexed = links.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphans = files.Where(f => !indexed.Contains(f)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        if (orphans.Count > 0)
            into.Add(new Finding("memory-not-indexed", RuleSeverity.Medium, scope,
                $"{orphans.Count} memory {Plural(orphans.Count, "file is", "files are")} not listed in the index ({Examples(orphans)}).",
                "Add a pointer for each, or delete them — an unindexed memory is unlikely to be recalled.",
                index.Path));
    }

    private static void CheckMemoryFileSizes(List<Finding> into, string scope, ContextProject p)
    {
        var big = Of(p.Sources, ContextKind.MemoryFile).Where(s => s.Bytes > MemoryFileMaxBytes).ToList();
        if (big.Count == 0) return;

        ContextSource worst = big.MaxBy(s => s.Bytes)!;
        into.Add(new Finding("memory-file-too-big", RuleSeverity.Low, scope,
            $"{big.Count} memory {Plural(big.Count, "file is", "files are")} over {Kb(MemoryFileMaxBytes)} " +
            $"(largest {Kb(worst.Bytes)}, {System.IO.Path.GetFileName(worst.Path)}).",
            "One memory, one fact — split them so recall can bring back just the relevant part.",
            worst.Path));
    }

    // The description is what recall matches on, so a memory without one is a memory that will not be
    // found; an invalid `type:` breaks the same contract more quietly.
    private static void CheckMemoryFrontmatter(List<Finding> into, string scope, ContextProject p)
    {
        var files = Of(p.Sources, ContextKind.MemoryFile).ToList();
        if (files.Count == 0) return;

        var noDescription = files.Where(s => string.IsNullOrWhiteSpace(s.Description)).ToList();
        if (noDescription.Count > 0)
            into.Add(new Finding("memory-no-description", RuleSeverity.Medium, scope,
                $"{noDescription.Count} memory {Plural(noDescription.Count, "file has", "files have")} no " +
                $"frontmatter description ({Examples(noDescription.Select(s => System.IO.Path.GetFileName(s.Path)))}).",
                "Add one — the description is what relevance matching reads when deciding to recall the file.",
                noDescription[0].Path));

        var badType = files
            .Where(s => s.Type is { Length: > 0 } t && !MemoryTypes.Contains(t.Trim().ToLowerInvariant()))
            .ToList();
        if (badType.Count > 0)
            into.Add(new Finding("memory-bad-type", RuleSeverity.Medium, scope,
                $"{badType.Count} memory {Plural(badType.Count, "file has", "files have")} a `type:` outside " +
                $"user/feedback/project/reference ({Examples(badType.Select(s => s.Type!))}).",
                "Use one of the four types, so the memory is filed where it will be looked for.",
                badType[0].Path));
    }

    private static void CheckStaleMemory(List<Finding> into, string scope, ContextProject p, DateTime nowUtc)
    {
        var stale = Of(p.Sources, ContextKind.MemoryFile)
            .Where(s => (nowUtc - s.ModifiedUtc).TotalDays >= StaleDays)
            .ToList();
        if (stale.Count == 0) return;

        ContextSource oldest = stale.MinBy(s => s.ModifiedUtc)!;
        into.Add(new Finding("memory-stale", RuleSeverity.Info, scope,
            $"{stale.Count} memory {Plural(stale.Count, "file has", "files have")} not been touched in " +
            $"{StaleDays}+ days (oldest {(int)(nowUtc - oldest.ModifiedUtc).TotalDays} days).",
            "Worth a review pass — keep what is still true, drop what the code now says better.",
            oldest.Path));
    }

    /// <summary>
    /// A set of projects whose memory directories are copies of each other, and what one copy holds.
    /// Public because the cross-project view renders these too: detection lives here once, and each
    /// surface formats its own sentence — the CLI in English, the window localized.
    /// </summary>
    internal sealed record DuplicateCluster(List<ContextProject> Projects, int Files, long Bytes);

    // Two projects whose memory hashes match file-for-file are copies. On a machine that uses git
    // worktrees this happens by construction, and the copies then drift apart independently.
    public static List<DuplicateCluster> DuplicateMemoryDirs(ContextScan scan)
    {
        var bySignature = new Dictionary<string, List<ContextProject>>(StringComparer.Ordinal);
        foreach (ContextProject p in scan.Projects)
        {
            if (Signature(p) is not { Length: > 0 } sig) continue;
            if (!bySignature.TryGetValue(sig, out List<ContextProject>? group))
                bySignature[sig] = group = new List<ContextProject>();
            group.Add(p);
        }

        return bySignature.Values
            .Where(g => g.Count > 1)
            .Select(g => new DuplicateCluster(g,
                g[0].Sources.Count(IsMemory),
                g[0].Sources.Where(IsMemory).Sum(s => s.Bytes)))
            .OrderByDescending(c => c.Bytes)
            .ToList();

        static bool IsMemory(ContextSource s) => s.Kind is ContextKind.MemoryFile or ContextKind.MemoryIndex;
    }

    private static void CheckDuplicateMemoryDirs(List<Finding> into, ContextScan scan)
    {
        foreach (DuplicateCluster c in DuplicateMemoryDirs(scan))
            into.Add(new Finding("memory-duplicated", RuleSeverity.High,
                string.Join(", ", c.Projects.Select(p => p.ShortPath)),
                $"{c.Projects.Count} projects hold the same {c.Files} memory files ({Kb(c.Bytes)}) byte for byte.",
                "Keep one directory and point the others at it with a junction, so they can't drift apart."));
    }

    /// <summary>
    /// Identity of a project's memory directory: every file's name and content hash, ordered. Empty
    /// when the project has no memory (or the scan came from a cache old enough to lack hashes), which
    /// is what keeps "they're both empty" from reading as duplication.
    /// </summary>
    private static string? Signature(ContextProject p)
    {
        var parts = p.Sources
            .Where(s => s.Kind is ContextKind.MemoryFile or ContextKind.MemoryIndex)
            .Where(s => s.ContentHash is { Length: > 0 })
            .Select(s => System.IO.Path.GetFileName(s.Path).ToLowerInvariant() + ":" + s.ContentHash)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        return parts.Count == 0 ? null : string.Join("|", parts);
    }

    // ---------------------------------------------------------------- projects, instructions, skills

    // A project directory in ~/.claude whose real path is gone still carries its memory, and still
    // shows up in every cross-project total. Deliberately only fires for Missing — a slug that was
    // never a path (a shared or virtual project dir) is legitimate and must not be reported.
    private static void CheckDeadProjectDir(List<Finding> into, ContextProject p)
    {
        if (p.State != PathState.Missing) return;

        long bytes = p.Bytes;
        // An empty leftover directory is tidiness, not debt — saying "0 B of context is still stored"
        // would overstate it, and an overstated finding is how an advisor loses its credibility.
        into.Add(bytes > 0
            ? new Finding("project-dir-dead", RuleSeverity.Medium, p.ShortPath,
                $"The project directory this belongs to no longer exists, but {Kb(bytes)} of context is still stored for it.",
                "Archive the whole ~/.claude/projects directory for it — nothing will recall from it again.")
            : new Finding("project-dir-dead-empty", RuleSeverity.Low, p.ShortPath,
                "The project directory this belongs to no longer exists, and it holds no context — just a leftover directory.",
                "Delete it whenever convenient; it costs nothing but noise in every cross-project total."));
    }

    private static void CheckInstructions(List<Finding> into, string scope, List<ContextSource> sources)
    {
        foreach (ContextSource s in sources.Where(s =>
                     s.Mode == LoadMode.Eager &&
                     s.Kind is ContextKind.UserInstructions or ContextKind.ProjectInstructions or ContextKind.Import))
        {
            if (s.Bytes <= InstructionsMaxBytes) continue;
            into.Add(new Finding("instructions-too-big", RuleSeverity.High, scope,
                $"{s.Label} is {Kb(s.Bytes)} ({TokenEstimate.Format(s.EagerTokens)} tokens) and loads in full on every request.",
                "Trim it, or move the parts that are only sometimes relevant into a skill — a skill body is lazy.",
                s.Path));
        }
    }

    // CLAUDE.md and AGENTS.md in the same project root are both eager, so keeping two full files
    // means paying for both every session. The good pattern is already on this machine: a one-line
    // CLAUDE.md that says `@AGENTS.md`, which the scanner counts once.
    private static void CheckDualInstructions(List<Finding> into, string scope, ContextProject p)
    {
        if (p.State != PathState.Resolved || p.Path.Length == 0) return;

        var roots = Of(p.Sources, ContextKind.ProjectInstructions)
            .Where(s => string.Equals(System.IO.Path.GetDirectoryName(s.Path), p.Path,
                StringComparison.OrdinalIgnoreCase))
            .Where(s => s.Bytes > DualInstructionsMinBytes)
            .ToList();
        if (roots.Count < 2) return;

        into.Add(new Finding("instructions-duplicated", RuleSeverity.Medium, scope,
            $"{roots.Count} full instruction files load from the project root " +
            $"({string.Join(", ", roots.Select(s => s.Label))}, {TokenEstimate.Format(roots.Sum(s => s.EagerTokens))} tokens together).",
            "Make one of them a single `@` import line pointing at the other, so the shared text is counted once.",
            roots[0].Path));
    }

    // A skill or agent body is lazy, but its description is in the always-loaded index — so the
    // description is the part with a per-session price, and the part recall matches on.
    private static void CheckDescriptions(List<Finding> into, string scope, List<ContextSource> sources)
    {
        var entries = sources
            .Where(s => s.Kind is ContextKind.Skill or ContextKind.Agent)
            .ToList();
        if (entries.Count == 0) return;

        var missing = entries.Where(s => string.IsNullOrWhiteSpace(s.Description)).ToList();
        if (missing.Count > 0)
            into.Add(new Finding("description-missing", RuleSeverity.Medium, scope,
                $"{missing.Count} {Plural(missing.Count, "skill/agent has", "skills/agents have")} no description " +
                $"({Examples(missing.Select(s => s.Label))}).",
                "Add one that says when to use it — the description is the whole index entry, and it is what gets matched.",
                missing[0].Path));

        var verbose = entries
            .Where(s => (s.Description?.Length ?? 0) > DescriptionMaxChars)
            .OrderByDescending(s => s.Description!.Length)
            .ToList();
        if (verbose.Count > 0)
            into.Add(new Finding("description-too-long", RuleSeverity.Medium, scope,
                $"{verbose.Count} {Plural(verbose.Count, "description is", "descriptions are")} over " +
                $"{DescriptionMaxChars} characters (longest {verbose[0].Description!.Length}, {verbose[0].Label}).",
                "Keep the trigger words and move the rest into the body — every character of a description is eager.",
                verbose[0].Path));
    }

    private static void CheckSettings(List<Finding> into, string scope, List<ContextSource> sources)
    {
        foreach (ContextSource s in Of(sources, ContextKind.Settings).Where(s => s.Bytes > SettingsMaxBytes))
            into.Add(new Finding("settings-too-big", RuleSeverity.Low, scope,
                $"{s.Label} is {Kb(s.Bytes)} — it never enters the prompt, but that size is usually accumulated permissions.",
                "Consolidate the permission list; broad rules replace dozens of one-off entries.", s.Path));
    }

    // ---------------------------------------------------------------- helpers

    private static IEnumerable<ContextSource> Of(List<ContextSource> sources, ContextKind kind)
        => sources.Where(s => s.Kind == kind);

    private static string Plural(int n, string one, string many) => n == 1 ? one : many;

    /// <summary>Up to three names, so a finding is actionable without becoming a file listing.</summary>
    private static string Examples(IEnumerable<string> names)
    {
        var list = names.Take(4).ToList();
        return list.Count > 3
            ? string.Join(", ", list.Take(3)) + ", …"
            : string.Join(", ", list);
    }

    private static string Kb(long bytes) => bytes < 1024
        ? $"{bytes} B"
        : (bytes / 1024.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " KB";
}
