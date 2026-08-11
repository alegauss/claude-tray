namespace ClaudeTray;

/// <summary>The `--sessions` read-out: what one conversation cost (T327). The headless companion to the
/// page T328 builds, and the only way to falsify the index's cache before there is a window.</summary>
internal static class SessionsCli
{
    /// <summary>Column width for a project name — the two segments the app names a directory by since
    /// T154 ("alegauss/claude-tray"), so the disambiguating half is not what gets cut.</summary>
    private const int NameWidth = 24;

    // Flags: `--refresh` to re-read every transcript past the per-file cache (the only way a wrong
    // cached row is falsifiable), `--root <dir>` to scan a stand-in tree, `--all` to list every session
    // instead of the newest few, `--project <slug-or-name>` to narrow to one repo.
    internal static void PrintSessions(string[] flags)
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        int rootAt = Array.IndexOf(flags, "--root");
        string? root = rootAt >= 0 && rootAt + 1 < flags.Length ? flags[rootAt + 1] : null;
        int projAt = Array.IndexOf(flags, "--project");
        string? project = projAt >= 0 && projAt + 1 < flags.Length ? flags[projAt + 1] : null;

        var mode = flags.Contains("--refresh")
            ? ActivityProfile.SweepCacheMode.Rebuild
            : ActivityProfile.SweepCacheMode.Use;

        IReadOnlyList<SessionRow> rows = SessionIndex.Load(out SessionScanStats st, projectsDir: root, mode: mode);

        // A bare argument is a session id (or a prefix of one): print that conversation's call tree
        // instead of the list. The read-out equivalent of clicking a row (T329).
        string? pick = flags.FirstOrDefault(f => !f.StartsWith("--") && f != root && f != project);
        if (pick is { Length: > 0 })
        {
            PrintTree(rows, pick, root);
            return;
        }

        Console.WriteLine($"Sessions — {st.Sessions:N0} conversations in {st.Files:N0} transcripts");
        // Which tree, always: this reads a *profile's* transcripts, and "whose numbers are these"
        // is the question T128 exists to keep answerable.
        Console.WriteLine("tree: " + st.Root);
        // Read beside seen is what makes the cache falsifiable: warm it should be a handful of files,
        // and --refresh must read every one of them.
        Console.WriteLine($"scan: {st.Read:N0} read ({st.BytesRead / 1048576.0:0.#} MB, {st.Lines:N0} responses), " +
                          $"{st.Ms:0}ms" + (root == null ? "" : " (fixture root — cache bypassed)"));
        if (root == null) Console.WriteLine("cache: " + SessionIndex.CachePath);
        Console.WriteLine();

        if (project is { Length: > 0 })
            rows = rows.Where(r => r.Project.Contains(project, StringComparison.OrdinalIgnoreCase) ||
                                   r.Name.Contains(project, StringComparison.OrdinalIgnoreCase)).ToList();

        if (rows.Count == 0) { Console.WriteLine("no conversations found."); return; }

        int show = flags.Contains("--all") ? rows.Count : Math.Min(rows.Count, 20);
        Console.WriteLine($"{"last turn",-16} {"project",-NameWidth}  {"dur",6}  " +
                          $"{"calls",6}  {"tokens",12}  {"cache read",12}  {"agents",6}  models");

        foreach (SessionRow r in rows.Take(show))
        {
            DateTime last = DateTimeOffset.FromUnixTimeSeconds((long)r.LastUnix).LocalDateTime;
            long billed = r.Bits.Input + r.Bits.Output + r.Bits.CacheCreate;
            Console.WriteLine(
                $"{last:MM-dd HH:mm}     {Trim(r.Name, NameWidth),-NameWidth}  {Dur(r.Seconds),6}  " +
                $"{r.Calls,6}  {billed,12:N0}  {r.Bits.CacheRead,12:N0}  {(r.Agents > 0 ? r.Agents.ToString() : "-"),6}  " +
                $"{string.Join(",", r.Models.Select(Short))}");
            // What the conversation is called, on its own line: the generated title, or the opening
            // prompt where there is none. Indented under the row, never in a column, because it is as
            // long as somebody made it (T336).
            if ((r.Title.Length > 0 ? r.Title : r.Prompt) is { Length: > 0 } label)
                Console.WriteLine($"{"",16}{Trim(label, 96)}");
        }

        if (show < rows.Count)
            Console.WriteLine($"… and {rows.Count - show:N0} more — --all lists them.");
    }

    /// <summary>
    /// One conversation as its call tree: task → workflow → agent, each with its own cost beside its
    /// subtree's — the reading that says whether the coordinator or the fleet was the expensive one.
    /// </summary>
    private static void PrintTree(IReadOnlyList<SessionRow> rows, string pick, string? root)
    {
        SessionRow row = rows.FirstOrDefault(r => r.Session.StartsWith(pick, StringComparison.OrdinalIgnoreCase));
        if (row.Session is not { Length: > 0 })
        {
            Console.WriteLine($"no conversation whose id starts with \"{pick}\".");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"{row.Name}  ·  {row.Session}");
        Console.WriteLine($"{DateTimeOffset.FromUnixTimeSeconds((long)row.FirstUnix).LocalDateTime:MM-dd HH:mm}" +
                          $" → {DateTimeOffset.FromUnixTimeSeconds((long)row.LastUnix).LocalDateTime:HH:mm}" +
                          $"  ·  {Dur(row.Seconds)}  ·  {row.Calls:N0} calls");
        Console.WriteLine();

        IReadOnlyList<TaskNode> tasks = SessionTasks.For(row, projectsDir: root);
        if (tasks.Count == 0) { Console.WriteLine("nothing to walk — its transcript could not be read."); return; }

        // Own beside subtree on every line, always: printing only the total is what makes a fan-out
        // look like the coordinator's own spend.
        Console.WriteLine($"{"task",-46}  {"calls",5}         {"own",10}       {"tree",10}");
        foreach (TaskNode t in tasks) Walk(t, 0);

        static void Walk(TaskNode n, int depth)
        {
            string indent = new string(' ', depth * 2);
            long own = n.Own.Input + n.Own.Output + n.Own.CacheCreate;
            TokenBits sub = n.Subtree;
            long all = sub.Input + sub.Output + sub.CacheCreate;
            Console.WriteLine($"{indent}{Trim(Name(n), 46 - depth * 2),-46}  " +
                              $"{n.Calls,5} calls  own {own,10:N0}  tree {all,10:N0}" +
                              (n.Prompt.Length > 0 ? "" : ""));
            if (n.Prompt.Length > 0) Console.WriteLine($"{indent}  \"{n.Prompt}\"");
            foreach (TaskNode c in n.Children) Walk(c, depth + 1);
        }
    }

    /// <summary>What a node is called. A prompt is named by its <em>length</em>, which is the line
    /// §I.1 has always drawn and is drawn here explicitly because a drill-down is the feature most
    /// likely to argue for crossing it.</summary>
    private static string Name(TaskNode n) => n.Kind switch
    {
        TaskKind.Command => n.Label,
        TaskKind.Prompt => n.Prompt.Length > 0 ? "prompt" : $"prompt ({n.Chars:N0} chars)",
        TaskKind.Continuation => "(inherited turns)",
        TaskKind.Workflow => "workflow " + n.Label,
        _ => n.Label,
    };

    /// <summary>Hours and minutes, or minutes: a session is normally minutes and occasionally a whole
    /// afternoon, and one unit that reads well at both ends does not exist.</summary>
    private static string Dur(double seconds)
        => seconds >= 3600 ? $"{seconds / 3600:0.0}h" : $"{Math.Max(1, seconds / 60):0}m";

    /// <summary>The part of a model id that varies. Printing the full id four times a row says nothing
    /// the first one did not.</summary>
    private static string Short(string model)
    {
        string m = model.StartsWith("claude-", StringComparison.Ordinal) ? model[7..] : model;
        int dash = m.LastIndexOf('-');
        return dash > 0 && m[(dash + 1)..].All(char.IsAsciiDigit) ? m[..dash] : m;
    }

    // Elided in the middle, not from the front: a name is "parent/leaf" since T154 and the parent is
    // the half that tells two checkouts of one release folder apart.
    private static string Trim(string s, int width)
    {
        if (s.Length <= width) return s;
        int head = (width - 1) / 2;
        return s[..head] + "…" + s[^(width - 1 - head)..];
    }
}
