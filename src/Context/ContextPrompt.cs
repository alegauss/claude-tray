using System.Text;

namespace ClaudeTray;

/// <summary>
/// Turns findings into a ready-made prompt for Claude Code.
///
/// This is the app's answer to "so fix it": it does not edit the developer's instruction files,
/// memories or skills — it writes down exactly what it measured and hands that to the tool that is
/// already good at editing them (IMPROVEMENTS §I.4). Measuring is a read; rewriting somebody's memory
/// on a heuristic is a different risk class entirely.
///
/// Two properties the generated text must keep:
/// <list type="bullet">
/// <item><b>Paths and numbers only.</b> Every sentence comes from a <see cref="Finding"/>, which is
/// built from sizes, names, timestamps and token estimates. No file contents, ever.</item>
/// <item><b>It asks before deleting.</b> The prompt explicitly tells Claude to show its plan first and
/// not to delete anything unprompted — the same restraint the window itself observes.</item>
/// </list>
/// </summary>
internal static class ContextPrompt
{
    /// <summary>
    /// A cleanup prompt for one scope: the findings, grouped by severity, each with its fix and the
    /// file it is about. <paramref name="candidates"/> are the paths the user ticked in the what-if
    /// simulator, if any — they become an explicit "these are the ones I am considering" section, so
    /// the simulation the user just ran carries over into the conversation.
    /// </summary>
    public static string Build(string title, IReadOnlyList<Finding> findings,
        IReadOnlyList<ContextSource> candidates, int candidateTokens)
    {
        var sb = new StringBuilder();
        sb.Append("# Context cleanup — ").AppendLine(title);
        sb.AppendLine();
        sb.AppendLine("Claude Code loads instruction files, the memory index and every skill's description");
        sb.AppendLine("before the first prompt, so that part of the context is paid on every request of the");
        sb.AppendLine("session. The findings below were measured locally by Claude Code Tray — file sizes,");
        sb.AppendLine("names, timestamps and token estimates only, never file contents.");
        sb.AppendLine();

        if (findings.Count == 0)
        {
            sb.AppendLine("No findings: nothing here needs attention.");
        }
        else
        {
            foreach (var group in findings.GroupBy(f => f.Severity).OrderBy(g => (int)g.Key))
            {
                sb.Append("## ").AppendLine(group.Key.ToString().ToUpperInvariant());
                sb.AppendLine();
                foreach (Finding f in group)
                {
                    sb.Append("- **").Append(f.Scope).Append("** — ").AppendLine(f.Message);
                    sb.Append("  - Fix: ").AppendLine(f.Fix);
                    if (f.Path is { Length: > 0 } path)
                        sb.Append("  - File: `").Append(path).AppendLine("`");
                }
                sb.AppendLine();
            }
        }

        if (candidates.Count > 0)
        {
            sb.AppendLine("## Candidates I am considering removing");
            sb.AppendLine();
            sb.Append("Together these account for ").Append(TokenEstimate.Format(candidateTokens))
              .AppendLine(" tokens of eager context per session:");
            sb.AppendLine();
            foreach (ContextSource s in candidates.OrderByDescending(s => s.EagerTokens))
                sb.Append("- `").Append(s.Path).Append("` — ")
                  .Append(TokenEstimate.Format(s.EagerTokens)).AppendLine(" eager tokens");
            sb.AppendLine();
        }

        sb.AppendLine("## What I would like you to do");
        sb.AppendLine();
        sb.AppendLine("Work through the findings above, starting with the most severe. For each one, tell me");
        sb.AppendLine("what you plan to change before you change it. **Do not delete or move any file without");
        sb.AppendLine("showing me the plan first** — some of these files may matter more than their size");
        sb.AppendLine("suggests, and you can read them; the measurements above cannot.");
        return sb.ToString();
    }
}
