using System.Text;

namespace ClaudeTray;

/// <summary>
/// Builds a throwaway <c>~/.claude</c> lookalike with known contents, so every part of the Context
/// Load Inspector can be exercised and screenshotted without depending on whatever the developer's
/// real machine happens to contain.
///
/// It exists for three reasons:
/// <list type="bullet">
/// <item><b>Determinism.</b> A fixture makes every rule fire on demand — an oversized index, a dead
/// pointer, an unindexed memory, a duplicated memory directory, a dead project directory, a 90-day-old
/// file — instead of hoping the dev machine has one of each today.</item>
/// <item><b>Publishable screenshots.</b> The real machine's project names are client names. A
/// screenshot for the README has to come from here.</item>
/// <item><b>The zero state.</b> A project with no memory at all is a case the UI has to render, and
/// this repo happens to be one — but the fixture carries it too, so it is never accidental.</item>
/// </list>
///
/// The seam it plugs into already existed: <see cref="ContextScanner.Options.ClaudeRoot"/>, added in
/// Phase 1 for exactly this. Rebuilt from scratch on every run, and never cached, so a fixture scan
/// can't poison the real cache.
/// </summary>
internal static class ContextFixture
{
    /// <summary>Where the fixture is built. Wiped and rebuilt on every call.</summary>
    public static string Root => Path.Combine(Path.GetTempPath(), "ClaudeTray-sample", "dot-claude");

    private static string ReposRoot => Path.Combine(Path.GetTempPath(), "ClaudeTray-sample", "repos");

    /// <summary>Create the fixture and return the root to scan. Throws only if the temp dir is unusable.</summary>
    public static string Build(DateTime nowUtc)
    {
        string sampleBase = Path.Combine(Path.GetTempPath(), "ClaudeTray-sample");
        if (Directory.Exists(sampleBase)) Directory.Delete(sampleBase, recursive: true);

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ReposRoot);

        // --- machine-wide: the user instruction file, a couple of skills, a fat settings.json -------
        Write(Path.Combine(Root, "CLAUDE.md"), UserInstructions(), nowUtc.AddDays(-3));
        Skill(Path.Combine(Root, "skills", "release-notes"), "release-notes",
            "Write release notes from a git range. Use when asked to draft a changelog, summarise commits "
            + "since a tag, or prepare release notes for a version.", 2_400, nowUtc.AddDays(-20));
        // A description long enough to trip the 500-char rule, which is a real finding on real machines.
        Skill(Path.Combine(Root, "skills", "everything-helper"), "everything-helper",
            Repeat("This skill helps with a great many things across the whole repository, including but "
                   + "not limited to builds, tests, docs, refactors and deployments. ", 8), 9_000,
            nowUtc.AddDays(-120));
        // A skill with no description: the index entry it produces has nothing to match on.
        Skill(Path.Combine(Root, "skills", "nameless-helper"), "nameless-helper", "", 1_200,
            nowUtc.AddDays(-60));

        // Settings past the 40 KB mark: never in the prompt, but a symptom worth naming.
        Write(Path.Combine(Root, "settings.json"), FatSettings(), nowUtc.AddDays(-1));

        // --- healthy: small instructions, a tidy index, three small memories ----------------------
        string healthy = Repo("healthy", nowUtc);
        Write(Path.Combine(healthy, "CLAUDE.md"), HealthyInstructions(), nowUtc.AddDays(-9));
        string healthyMem = ProjectDir(healthy, nowUtc, out _);
        Memory(healthyMem, "MEMORY.md", HealthyIndex(), nowUtc.AddDays(-2));
        Memory(healthyMem, "prefers-dotnet-cli.md",
            Fact("prefers-dotnet-cli", "The user builds from the CLI, never from an IDE", "user"), nowUtc.AddDays(-5));
        Memory(healthyMem, "release-runs-on-tag.md",
            Fact("release-runs-on-tag", "Releases are cut by pushing a version tag", "project"), nowUtc.AddDays(-8));
        Memory(healthyMem, "review-before-merge.md",
            Fact("review-before-merge", "Every change gets a review pass before merge", "feedback"), nowUtc.AddDays(-11));
        Transcript(healthy, nowUtc.AddDays(-1), 38_400, new[] { "release-notes", "release-notes" });

        // --- bloated: everything eager and everything large --------------------------------------
        string bloated = Repo("bloated", nowUtc);
        Write(Path.Combine(bloated, "AGENTS.md"), Repeat(BloatedParagraph(), 46), nowUtc.AddDays(-4));
        // Two full instruction files in one root, which is its own finding.
        Write(Path.Combine(bloated, "CLAUDE.md"), Repeat(BloatedParagraph(), 6), nowUtc.AddDays(-4));
        string bloatedMem = ProjectDir(bloated, nowUtc, out _);
        Memory(bloatedMem, "MEMORY.md", BloatedIndex(), nowUtc.AddDays(-1));
        for (int i = 1; i <= 6; i++)
            Memory(bloatedMem, $"project_subsystem_{i}.md",
                Fact($"project-subsystem-{i}", $"How subsystem {i} is wired together", "project")
                + Repeat("It also records a long tail of detail that would be better off in a skill. ", 60),
                nowUtc.AddDays(-3 - i));
        // A memory nobody indexed, and one with no description at all.
        Memory(bloatedMem, "project_orphan_note.md",
            Fact("project-orphan-note", "Never linked from the index", "project"), nowUtc.AddDays(-6));
        Memory(bloatedMem, "project_wrong_type.md",
            "---\nname: project-wrong-type\ndescription: Filed under a type that does not exist\n"
            + "metadata:\n  type: notes\n---\n\nThe four valid types are user/feedback/project/reference.\n",
            nowUtc.AddDays(-12));
        Memory(bloatedMem, "project_no_description.md",
            "---\nname: project-no-description\nmetadata:\n  type: project\n---\n\nNo description line at all.\n",
            nowUtc.AddDays(-100));
        Transcript(bloated, nowUtc.AddHours(-5), 61_200, new[] { "release-notes" });

        // --- duplicated: a byte-for-byte copy of the healthy memory directory --------------------
        // This is the worktree-sibling case: two project directories, one memory directory copied.
        string twin = Repo("healthy-worktree", nowUtc);
        Write(Path.Combine(twin, "CLAUDE.md"), HealthyInstructions(), nowUtc.AddDays(-9));
        string twinMem = ProjectDir(twin, nowUtc, out _);
        foreach (string file in Directory.GetFiles(healthyMem))
            File.Copy(file, Path.Combine(twinMem, Path.GetFileName(file)), overwrite: true);
        Touch(twinMem, nowUtc.AddDays(-2));

        // --- orphaned: a project directory whose repository is gone ------------------------------
        string gonePath = Path.Combine(ReposRoot, "deleted");
        string goneMem = ProjectDir(gonePath, nowUtc, out _);   // the ~/.claude side stays…
        Memory(goneMem, "MEMORY.md", OrphanIndex(), nowUtc.AddDays(-95));
        Memory(goneMem, "project_stale_note.md",
            Fact("project-stale-note", "Something that was true a quarter ago", "project"), nowUtc.AddDays(-140));
        // …while the repository it belongs to never exists, which is what makes it orphaned.

        // A dead project directory that holds nothing at all.
        string emptyGone = Path.Combine(ReposRoot, "abandoned");
        Directory.CreateDirectory(Path.Combine(Root, "projects", Slug(emptyGone)));

        // --- zero: a project that loads instructions and nothing else ----------------------------
        string zero = Repo("zero-memory", nowUtc);
        Write(Path.Combine(zero, "AGENTS.md"), HealthyInstructions(), nowUtc.AddDays(-1));
        ProjectDir(zero, nowUtc, out _);
        Transcript(zero, nowUtc.AddHours(-2), 34_100, Array.Empty<string>());

        // --- a shared/virtual project directory that is not a path at all ------------------------
        string shared = Path.Combine(Root, "projects", "_sample-shared");
        Directory.CreateDirectory(shared);
        Memory(Path.Combine(shared, "memory"), "MEMORY.md",
            "# Shared notes\n\n- [Conventions](conventions.md) — how we name things\n", nowUtc.AddDays(-30));
        Memory(Path.Combine(shared, "memory"), "conventions.md",
            Fact("conventions", "Naming conventions shared across projects", "reference"), nowUtc.AddDays(-30));

        return Root;
    }

    // ---------------------------------------------------------------- pieces

    // A repository directory, which is what makes the matching project slug resolve.
    private static string Repo(string name, DateTime nowUtc)
    {
        string path = Path.Combine(ReposRoot, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// The <c>~/.claude/projects/&lt;slug&gt;</c> side for a repository path, named with the same
    /// encoding Claude Code uses (drive colon and every separator become '-'), so the scanner resolves
    /// it back by probing exactly as it does for real projects.
    /// </summary>
    private static string ProjectDir(string repoPath, DateTime nowUtc, out string slug)
    {
        slug = Slug(repoPath);
        string dir = Path.Combine(Root, "projects", slug);
        string memory = Path.Combine(dir, "memory");
        Directory.CreateDirectory(memory);
        return memory;
    }

    /// <summary>The same encoding Claude Code uses for a project directory name: the drive colon and
    /// every separator become '-'. Factored out because three call sites have to agree, or a fixture
    /// project silently fails to resolve.</summary>
    private static string Slug(string repoPath)
        => repoPath.Replace(":", "-").Replace(Path.DirectorySeparatorChar, '-')
                   .Replace(Path.AltDirectorySeparatorChar, '-');

    private static void Skill(string dir, string name, string description, int bodyBytes, DateTime when)
    {
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        sb.Append("---\nname: ").Append(name);
        // An empty description is a fixture case in its own right: the frontmatter omits it.
        if (description.Length > 0) sb.Append("\ndescription: ").Append(description);
        sb.Append("\n---\n\n");
        sb.Append("# ").Append(name).Append("\n\n");
        sb.Append(Repeat("The body of a skill is lazy: it is only read once the skill is invoked. ",
            Math.Max(1, bodyBytes / 70)));
        Write(Path.Combine(dir, "SKILL.md"), sb.ToString(), when);
    }

    private static void Memory(string dir, string name, string body, DateTime when)
    {
        Directory.CreateDirectory(dir);
        Write(Path.Combine(dir, name), body, when);
    }

    private static string Fact(string name, string description, string type) =>
        $"---\nname: {name}\ndescription: {description}\nmetadata:\n  type: {type}\n---\n\n{description}.\n";

    /// <summary>
    /// A transcript with a first request that reports a real startup context, plus optional skill
    /// invocations — enough for the measured session-zero tick and the evidence column to have
    /// something to show.
    /// </summary>
    private static void Transcript(string repoPath, DateTime when, int startupTokens, string[] skillsUsed)
    {
        string slug = Slug(repoPath);
        string dir = Path.Combine(Root, "projects", slug);
        Directory.CreateDirectory(dir);

        string cwd = System.Text.Json.JsonSerializer.Serialize(repoPath);
        string stamp = when.ToString("yyyy-MM-ddTHH:mm:ss.000Z");
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"user\",\"cwd\":").Append(cwd).Append(",\"timestamp\":\"").Append(stamp)
          .Append("\",\"message\":{\"role\":\"user\"}}\n");
        sb.Append("{\"type\":\"assistant\",\"cwd\":").Append(cwd).Append(",\"timestamp\":\"").Append(stamp)
          .Append("\",\"message\":{\"model\":\"claude-opus-5\",\"usage\":{\"input_tokens\":6,")
          .Append("\"cache_creation_input_tokens\":").Append(startupTokens)
          .Append(",\"cache_read_input_tokens\":0,\"output_tokens\":120}}}\n");

        foreach (string skill in skillsUsed)
            sb.Append("{\"type\":\"assistant\",\"cwd\":").Append(cwd).Append(",\"timestamp\":\"").Append(stamp)
              .Append("\",\"message\":{\"model\":\"claude-opus-5\",\"content\":[{\"type\":\"tool_use\",")
              .Append("\"name\":\"Skill\",\"input\":{\"skill\":\"").Append(skill)
              .Append("\"}}],\"usage\":{\"input_tokens\":8,\"cache_read_input_tokens\":")
              .Append(startupTokens).Append(",\"output_tokens\":40}}}\n");

        Write(Path.Combine(dir, "session-sample.jsonl"), sb.ToString(), when);
    }

    private static void Write(string path, string content, DateTime whenUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        // Timestamps are part of the fixture: the stale-memory and never-used rules are about age.
        File.SetLastWriteTimeUtc(path, whenUtc);
    }

    private static void Touch(string dir, DateTime whenUtc)
    {
        foreach (string f in Directory.GetFiles(dir)) File.SetLastWriteTimeUtc(f, whenUtc);
    }

    private static string Repeat(string text, int times)
    {
        var sb = new StringBuilder(text.Length * times);
        for (int i = 0; i < times; i++) sb.Append(text);
        return sb.ToString();
    }

    // ---------------------------------------------------------------- contents

    private static string UserInstructions() =>
        "# Global instructions\n\nKeep answers short. Prefer the CLI over IDE workflows.\n\n"
        + "## Commits\n\nOne commit per finished task, written in the imperative.\n";

    private static string HealthyInstructions() =>
        "# Project\n\nA small service. Build with `dotnet build`, test with `dotnet test`.\n\n"
        + "## Conventions\n\n- One class per file\n- Tests next to the code they cover\n";

    private static string HealthyIndex() =>
        "# Memory index\n\n"
        + "- [Prefers the dotnet CLI](prefers-dotnet-cli.md) — builds from a terminal\n"
        + "- [Release runs on tag](release-runs-on-tag.md) — push a tag to release\n"
        + "- [Review before merge](review-before-merge.md) — always a review pass\n";

    // Oversized, and two of its pointers resolve to nothing.
    private static string BloatedIndex()
    {
        var sb = new StringBuilder("# Memory index\n\n");
        for (int i = 1; i <= 6; i++)
            sb.Append("- [Subsystem ").Append(i).Append("](project_subsystem_").Append(i)
              .Append(".md) — how subsystem ").Append(i).Append(" is wired\n");
        sb.Append("- [Deleted note](project_deleted_note.md) — this file is gone\n");
        sb.Append("- [Renamed note](project_old_name.md) — renamed months ago\n");
        sb.Append(Repeat("- [Filler](project_subsystem_1.md) — kept for historical reasons\n", 220));
        return sb.ToString();
    }

    private static string OrphanIndex() =>
        "# Memory index\n\n- [Stale note](project_stale_note.md) — from a quarter ago\n"
        + "- [Also gone](project_vanished.md) — no such file\n";

    private static string BloatedParagraph() =>
        "Every request of every session pays for this paragraph, because an instruction file is loaded "
        + "in full before the first prompt. That is fine for a page of conventions and expensive for a "
        + "manual. Detail that is only sometimes relevant belongs in a skill, whose body is lazy.\n\n";

    private static string FatSettings()
    {
        var sb = new StringBuilder("{\n  \"permissions\": {\n    \"allow\": [\n");
        for (int i = 0; i < 1_300; i++)
            sb.Append("      \"Bash(sample-command-").Append(i).Append(":*)\",\n");
        sb.Append("      \"Bash(echo:*)\"\n    ]\n  }\n}\n");
        return sb.ToString();
    }
}
