using System.Globalization;
using System.Text;

namespace ClaudeTray;

/// <summary>
/// The whole context picture as one markdown document: what every project loads before its first
/// prompt, every finding with its fix, and which skills were actually used.
///
/// It is the archival companion to <see cref="ContextPrompt"/>. The prompt is an instruction aimed at
/// one project and meant to be pasted into a conversation; the report is a document — something to
/// hand to Claude wholesale, keep next to a cleanup branch, or diff against next month to see whether
/// the numbers actually moved.
///
/// Same privacy rule as everything else here, and it is restated at the top of the output so anyone
/// reading the file knows what it can and cannot contain: paths, sizes, timestamps and token
/// estimates only, never file contents (IMPROVEMENTS §I.1).
/// </summary>
internal static class ContextReport
{
    /// <param name="debt">Grades by project slug, so the table can carry them without recomputing.</param>
    public static string Build(ContextScan scan, IReadOnlyList<Finding> findings, UsageEvidence? evidence,
        IReadOnlyDictionary<string, ContextDebt> debt, int baseTokens, DateTime nowLocal)
    {
        var sb = new StringBuilder();
        Header(sb, scan, nowLocal);
        Summary(sb, scan, findings, evidence, baseTokens);
        Projects(sb, scan, findings, debt);
        Findings(sb, findings);
        Evidence(sb, scan, evidence);
        Method(sb, scan, evidence, baseTokens);
        return sb.ToString();
    }

    private static void Header(StringBuilder sb, ContextScan scan, DateTime nowLocal)
    {
        sb.AppendLine("# Context load report");
        sb.AppendLine();
        sb.Append("Generated ").Append(nowLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
          .Append(" by Claude Code Tray ").Append(Updater.CurrentVersion.ToString(3)).AppendLine(".");
        sb.AppendLine();
        sb.AppendLine("> Measured locally from `~/.claude`: **paths, sizes, timestamps and token estimates only —");
        sb.AppendLine("> no file contents**, and nothing left this machine to produce it. Token counts are");
        sb.AppendLine("> estimates and are written with a leading `≈`.");
        sb.AppendLine();
        if (scan.Truncated)
        {
            sb.AppendLine("> ⚠ The scan hit its file/directory cap, so the totals below are a floor, not a total.");
            sb.AppendLine();
        }
    }

    private static void Summary(StringBuilder sb, ContextScan scan, IReadOnlyList<Finding> findings,
        UsageEvidence? evidence, int baseTokens)
    {
        long bytes = scan.Shared.Sum(s => s.Bytes) + scan.Projects.Sum(p => p.Bytes);
        int kept = scan.Shared.Count + scan.Projects.Sum(p => p.Sources.Count);

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");
        Row(sb, "Projects", scan.Projects.Count.ToString(CultureInfo.InvariantCulture));
        // Two rows, and neither of them says "measured" (T268). This row used to read `Measured files`
        // while the *column* nine lines below reads `Measured` and means the median startup context read
        // from transcripts — one word, two labels, one document, and the prose defines only the other one.
        // Both numbers are worth keeping rather than picking one: walked-against-kept is what tells a
        // reader the shape of a capped walk, and the caveat above this table now has figures to attach to.
        Row(sb, "Files walked", scan.FilesWalked.ToString(CultureInfo.InvariantCulture));
        Row(sb, "Sources kept", kept.ToString(CultureInfo.InvariantCulture));
        Row(sb, "Footprint on disk", Kb(bytes));
        Row(sb, "Shared eager context (counted once)", TokenEstimate.Format(scan.SharedEagerTokens));
        Row(sb, "Base overhead, invisible to any file scan", TokenEstimate.Format(baseTokens));
        Row(sb, "Findings", Severities(findings));

        if (evidence is { Complete: true, Error: null })
        {
            var unused = scan.Shared.Concat(scan.Projects.SelectMany(p => p.Sources))
                .Where(s => s.Kind is ContextKind.Skill or ContextKind.Agent)
                .GroupBy(s => s.Label, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Where(s => (evidence.For(s)?.Total ?? 0) == 0)
                .ToList();
            Row(sb, $"Skills/agents never invoked in {evidence.WindowDays} days",
                $"{unused.Count} ({TokenEstimate.Format(unused.Sum(s => s.EagerTokens))} of eager index)");
        }

        sb.AppendLine();
    }

    private static void Projects(StringBuilder sb, ContextScan scan, IReadOnlyList<Finding> findings,
        IReadOnlyDictionary<string, ContextDebt> debt)
    {
        sb.AppendLine("## Projects");
        sb.AppendLine();
        sb.AppendLine("Sorted by what a session there costs before the first prompt. \"Measured\" is the median");
        sb.AppendLine("startup context of recent sessions, read from the transcripts — it includes the base");
        sb.AppendLine("overhead that the estimate cannot see, which is why it is larger.");
        sb.AppendLine();
        sb.AppendLine("| Project | Grade | Eager (est.) | Measured | Findings | Memory files | State |");
        sb.AppendLine("|---|---|---|---|---|---|---|");

        foreach (ContextProject p in scan.Projects)
        {
            int memory = p.Sources.Count(s => s.Kind is ContextKind.MemoryFile or ContextKind.MemoryIndex);
            string grade = debt.TryGetValue(p.Slug, out ContextDebt? d) ? d.Grade.ToString() : "—";
            int own = findings.Count(f => f.Scope == p.ShortPath);
            sb.Append("| `").Append(p.ShortPath).Append("` | ").Append(grade)
              .Append(" | ").Append(TokenEstimate.Format(scan.EstimatedSessionZero(p)))
              .Append(" | ").Append(p.Observed is { } o ? TokenEstimate.Format(o.Median) : "—")
              .Append(" | ").Append(own.ToString(CultureInfo.InvariantCulture))
              .Append(" | ").Append(memory.ToString(CultureInfo.InvariantCulture))
              .Append(" | ").Append(State(p)).AppendLine(" |");
        }
        sb.AppendLine();
    }

    private static void Findings(StringBuilder sb, IReadOnlyList<Finding> findings)
    {
        sb.AppendLine("## Findings");
        sb.AppendLine();
        if (findings.Count == 0)
        {
            sb.AppendLine("None.");
            sb.AppendLine();
            return;
        }

        foreach (var group in findings.GroupBy(f => f.Severity).OrderBy(g => (int)g.Key))
        {
            sb.Append("### ").Append(group.Key.ToString()).Append(" (").Append(group.Count()).AppendLine(")");
            sb.AppendLine();
            foreach (Finding f in group)
            {
                sb.Append("- **").Append(f.Scope).Append("** — ").AppendLine(f.Message);
                sb.Append("  - Fix: ").AppendLine(f.Fix);
                if (f.Path is { Length: > 0 } path) sb.Append("  - `").Append(path).AppendLine("`");
            }
            sb.AppendLine();
        }
    }

    private static void Evidence(StringBuilder sb, ContextScan scan, UsageEvidence? evidence)
    {
        sb.AppendLine("## Evidence of use");
        sb.AppendLine();
        if (evidence is null || evidence.Error != null)
        {
            sb.Append("Not available").Append(evidence?.Error is { } e ? $": {e}" : " for this run").AppendLine(".");
            sb.AppendLine();
            return;
        }

        sb.Append("Skill and agent invocations counted in ").Append(evidence.FilesRead)
          .Append(" transcripts over the last ").Append(evidence.WindowDays).AppendLine(" days.");
        if (!evidence.Complete)
            sb.AppendLine("The pass was **incomplete**, so a zero here means \"not seen\", not \"never used\".");
        sb.AppendLine();
        sb.AppendLine("Memory files are deliberately absent: a recall leaves no structured trace, and the only");
        sb.AppendLine("evidence of one would be message content, which this app never reads.");
        sb.AppendLine();
        sb.AppendLine("| Skill / agent | Used | Last 30d | Eager | Last used |");
        sb.AppendLine("|---|---|---|---|---|");

        var entries = scan.Shared.Concat(scan.Projects.SelectMany(p => p.Sources))
            .Where(s => s.Kind is ContextKind.Skill or ContextKind.Agent)
            .GroupBy(s => s.Label, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(s => (source: s, stat: evidence.For(s)))
            .OrderBy(x => x.stat?.Total ?? 0)
            .ThenByDescending(x => x.source.EagerTokens)
            .ToList();

        foreach (var (source, stat) in entries)
        {
            string used = stat is { Total: > 0 }
                ? stat.Total.ToString(CultureInfo.InvariantCulture)
                : evidence.Complete ? "never" : "—";
            sb.Append("| `").Append(source.Label).Append("` | ").Append(used)
              .Append(" | ").Append((stat?.Recent ?? 0).ToString(CultureInfo.InvariantCulture))
              .Append(" | ").Append(TokenEstimate.Format(source.EagerTokens))
              .Append(" | ").Append(stat is { Total: > 0 }
                  ? stat.LastUsedUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                  : "—")
              .AppendLine(" |");
        }
        sb.AppendLine();
    }

    // Everything a reader needs to argue with the numbers, rather than having to trust them.
    private static void Method(StringBuilder sb, ContextScan scan, UsageEvidence? evidence, int baseTokens)
    {
        sb.AppendLine("## How these numbers were produced");
        sb.AppendLine();
        sb.AppendLine("- **Eager vs lazy.** Instruction files, the memory index and every skill/agent");
        sb.AppendLine("  *description* are in the prompt from the first request on, so they are paid on every");
        sb.AppendLine("  request of the session. Memory *bodies*, skill *bodies* and nested instruction files are");
        sb.AppendLine("  read only when used. `settings.json` never enters the prompt at all.");
        sb.Append("- **Token counts are estimates**, from characters classified per markdown line (prose ")
          .Append(TokenEstimate.ProseCharsPerToken.ToString("0.00", CultureInfo.InvariantCulture))
          .Append(" / code ").Append(TokenEstimate.CodeCharsPerToken.ToString("0.00", CultureInfo.InvariantCulture))
          .Append(" / table ").Append(TokenEstimate.TableCharsPerToken.ToString("0.00", CultureInfo.InvariantCulture))
          .AppendLine(" chars per token). There is no tokenizer on this machine by design.");
        sb.Append("- **Base overhead ").Append(TokenEstimate.Format(baseTokens))
          .AppendLine("** is Claude Code's own system prompt, tool definitions and MCP");
        sb.AppendLine("  schemas. No file scan can see it; it is fitted from the transcripts, and it is never folded");
        sb.AppendLine("  into a per-project number.");
        sb.AppendLine("- **Measured session zero** sums `input_tokens + cache_creation_input_tokens +");
        sb.AppendLine("  cache_read_input_tokens` of a session's first request. All three are needed: the stable");
        sb.AppendLine("  prefix is shared between sessions, so most of a warm start arrives as a cache *read*.");
        sb.Append("- **Grades** come from eager tokens plus a penalty per open finding, and are a summary")
          .AppendLine(" rather");
        sb.AppendLine("  than a verdict.");
        sb.Append("- Scan: ").Append(scan.FilesWalked).Append(" files in ")
          .Append(scan.ElapsedMs.ToString("0", CultureInfo.InvariantCulture)).Append(" ms")
          .Append(scan.FromCache ? " (cached)" : " (fresh)");
        if (evidence is { Error: null })
            sb.Append("; evidence: ").Append(ContextUsage.Summary(evidence));
        sb.AppendLine(".");
    }

    private static void Row(StringBuilder sb, string label, string value)
        => sb.Append("| ").Append(label).Append(" | ").Append(value).AppendLine(" |");

    private static string Severities(IReadOnlyList<Finding> findings)
    {
        if (findings.Count == 0) return "none";
        string parts = string.Join(", ", Enum.GetValues<RuleSeverity>()
            .Select(s => (s, n: findings.Count(f => f.Severity == s)))
            .Where(x => x.n > 0)
            .Select(x => $"{x.n} {x.s.ToString().ToLowerInvariant()}"));
        return $"{findings.Count} ({parts})";
    }

    private static string State(ContextProject p) => p.State switch
    {
        PathState.Resolved => "ok",
        PathState.Missing => "**path gone**",
        _ => "not a path",
    };

    private static string Kb(long bytes) => bytes < 1024 * 1024
        ? (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB"
        : (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
}
