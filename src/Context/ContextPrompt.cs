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
/// <item><b>It says when it is partial.</b> A capped walk means findings that were never reached, and
/// this text leaves the app — clipboard, then Claude Code, then an agent that edits files — so the
/// caveat has to travel inside it. <see cref="ContextReport"/> already carries the same fact inside the
/// document it writes; this used to print it in the scan header, above the <c>#</c>, which is outside
/// what a person copies (T267).</item>
/// </list>
///
/// <para><b>English, not localized</b>, like every sentence already here: the audience is Claude Code,
/// not the window. The button that copies this is localized; what it copies is not.</para>
/// </summary>
internal static class ContextPrompt
{
    /// <summary>
    /// A cleanup prompt for one scope: the findings, grouped by severity, each with its fix and the
    /// file it is about. <paramref name="candidates"/> are the paths the user ticked in the what-if
    /// simulator, if any — they become an explicit "these are the ones I am considering" section, so
    /// the simulation the user just ran carries over into the conversation.
    /// </summary>
    /// <param name="truncated">The scan hit its file/directory cap, so the findings are a floor. Said in
    /// the text rather than left to the caller's console (T267): this is the artifact that travels.</param>
    public static string Build(string title, IReadOnlyList<Finding> findings,
        IReadOnlyList<ContextSource> candidates, int candidateTokens, bool truncated = false)
    {
        var sb = new StringBuilder();
        sb.Append("# Context cleanup — ").AppendLine(title);
        sb.AppendLine();
        sb.AppendLine("Claude Code loads instruction files, the memory index and every skill's description");
        sb.AppendLine("before the first prompt, so that part of the context is paid on every request of the");
        sb.AppendLine("session. The findings below were measured locally by Claude Code Tray — file sizes,");
        sb.AppendLine("names, timestamps and token estimates only, never file contents.");
        sb.AppendLine();
        if (truncated)
        {
            sb.AppendLine("> ⚠ The scan that produced this hit its file/directory cap, so **the list below is a");
            sb.AppendLine("> floor, not a total** — there may be findings it never reached.");
            sb.AppendLine();
        }

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
        if (truncated)
        {
            // An instruction and not a second note. The rest of this section instructs, the reader is
            // something that will act, and the failure being avoided is a partial list treated as whole —
            // which a note does not prevent and being told to say it out loud does.
            sb.AppendLine();
            sb.AppendLine("Because the scan was capped, **say that this list is partial before you start** — I");
            sb.AppendLine("may want to widen it or re-scan rather than act on a floor.");
        }
        return sb.ToString();
    }
}
