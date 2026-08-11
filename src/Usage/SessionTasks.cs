namespace ClaudeTray;

/// <summary>What a node of a session's call tree is, which is also what it may be labelled with.</summary>
internal enum TaskKind
{
    /// <summary>A slash command — a person starting work, and measurably the expensive kind.</summary>
    Command,
    /// <summary>A typed prompt. Named by its length, not by its text (§I.1).</summary>
    Prompt,
    /// <summary>Turns that predate the transcript's first person-ask: a resumed conversation's
    /// inheritance. Its own node, or a resumed session's spend disappears.</summary>
    Continuation,
    /// <summary>A <c>wf_&lt;id&gt;</c> folder — one workflow, holding its agents.</summary>
    Workflow,
    /// <summary>One <c>agent-&lt;id&gt;.jsonl</c>.</summary>
    Agent,
}

/// <summary>
/// One node of the call tree under a session: a task, a workflow, or an agent.
/// </summary>
/// <param name="Label">What identifies it — a slash command's <em>name</em>, an agent's or workflow's
/// id, or the empty string for a prompt, which is named by <paramref name="Chars"/> instead.</param>
/// <param name="Prompt">Text only where §I.1's amendment already permits it: the conversation's
/// opening ask, and a slash command's own line, which is a name and its arguments. Empty
/// everywhere else, including every later typed prompt.</param>
/// <param name="Chars">How long the prompt was, for a node that may not quote it. Zero where there is
/// no prompt to measure.</param>
/// <param name="Own">What this node itself spent — for a task, its own turns; for a workflow, nothing,
/// because a workflow has no turns of its own.</param>
internal sealed record TaskNode(
    TaskKind Kind, string Label, string Prompt, int Chars,
    double FirstUnix, double LastUnix, int Calls, TokenBits Own, List<TaskNode> Children)
{
    /// <summary>This node plus everything under it. The pair <see cref="Own"/>/<see cref="Subtree"/> is
    /// the reading the tree exists for: whether the coordinator or the fleet was the expensive one.</summary>
    public TokenBits Subtree
    {
        get
        {
            long i = Own.Input, o = Own.Output, c = Own.CacheCreate, r = Own.CacheRead;
            long h = Own.CacheCreate1h, m = Own.CacheCreate5m;
            foreach (TaskNode child in Children)
            {
                TokenBits s = child.Subtree;
                i += s.Input; o += s.Output; c += s.CacheCreate; r += s.CacheRead;
                h += s.CacheCreate1h; m += s.CacheCreate5m;
            }
            return new TokenBits(i, o, c, r, h, m);
        }
    }

    /// <summary>Calls in this node and everything under it.</summary>
    public int SubtreeCalls => Calls + Children.Sum(c => c.SubtreeCalls);

    public double Seconds => Math.Max(0, LastUnix - FirstUnix);
}

/// <summary>
/// A session cut into the tasks that produced it, with the fan-out each one caused hanging under it.
///
/// <para>The request this answers was for a <b>stack trace of a session</b>, and the shape is right:
/// the provenance is already on disk. A task begins when a person asks for something and ends when
/// the next person-ask arrives; a fan-out writes its agents under the session's own folder, so
/// task → workflow → agent is a path, not an inference.</para>
///
/// <para><b>Telling a person-ask from the rest is the whole trick</b>, and it is
/// <see cref="SessionIndex.TryReadPersonAsk"/>'s — the same reader the Sessions list uses, so the two
/// cannot disagree about what a person wrote. Sidechains, meta lines, tool results, images, the
/// harness's own tagged lines and the two prose ones (a resume caveat, an interrupt note) are all
/// declined; a slash command is <em>not</em>, because it is a person starting work.</para>
///
/// <para><b>On demand, and not cached.</b> A session is a handful of files and this is what a click
/// costs, against <see cref="SessionIndex"/>'s whole-tree scan which is what a page costs. Caching it
/// would key a second store on the same file identity for a hundredth of the work.</para>
/// </summary>
internal static class SessionTasks
{
    /// <summary>
    /// The call tree for one session, newest task last.
    /// </summary>
    /// <param name="row">The session, as the index produced it.</param>
    /// <param name="profile">Whose transcripts. Defaults to the monitored profile.</param>
    /// <param name="projectsDir">A stand-in tree, for fixtures.</param>
    public static IReadOnlyList<TaskNode> For(SessionRow row, ProfileRef? profile = null,
                                              string? projectsDir = null)
    {
        string root = projectsDir ?? (profile ?? ProfileStore.MonitoredRef).ProjectsDir;
        if (!Directory.Exists(root)) return Array.Empty<TaskNode>();

        // The session's own transcript, and the fan-out files written under it. Found by walking
        // rather than remembered, because the index stores rows and not paths — and metadata-only
        // enumeration of a tree this reader never opens is the cheap half of a sweep.
        string? own = null;
        var agents = new List<(string Path, string? Workflow, string Agent)>();
        foreach (FileInfo f in SafeWalk.Files(root, "*.jsonl"))
        {
            TranscriptTail.Locate(root, f.FullName, out string project, out string session,
                                  out string? workflow, out string? agent);
            if (session != row.Session || project != row.Project) continue;
            if (agent == null) own = f.FullName;
            else agents.Add((f.FullName, workflow, agent));
        }

        List<TaskNode> tasks = own == null ? new() : Cut(own);
        if (tasks.Count == 0 && agents.Count == 0) return Array.Empty<TaskNode>();

        // A session whose own transcript could not be read still has its fan-out, and dropping that
        // would report the expensive half as absent rather than as unattributed.
        if (tasks.Count == 0)
            tasks.Add(new TaskNode(TaskKind.Continuation, "", "", 0, 0, 0, 0, default, new()));

        Attach(tasks, agents);
        return tasks;
    }

    // ---------------------------------------------------------------- cutting

    /// <summary>
    /// One transcript into tasks. Turns land on the task whose person-ask most recently preceded
    /// them; turns that precede the first ask at all become a single <see cref="TaskKind.Continuation"/>
    /// node, because a resumed conversation inherits its parent's turns and dropping them would make a
    /// resumed session's spend disappear.
    /// </summary>
    private static List<TaskNode> Cut(string path)
    {
        var tasks = new List<Builder>();
        Builder? current = null;
        bool first = true;

        try
        {
            foreach (string line in File.ReadLines(path))
            {
                if (SessionIndex.TryReadPersonAsk(line, out PersonAsk ask))
                {
                    // The opening ask is the one §I.1's amendment already permits in words; every
                    // later typed prompt is a length. A slash command is a name either way.
                    bool quotable = first || ask.Command;
                    current = new Builder
                    {
                        Kind = ask.Command ? TaskKind.Command : TaskKind.Prompt,
                        Label = ask.Command ? CommandName(ask.Text) : "",
                        Prompt = quotable ? ask.Text : "",
                        Chars = ask.Chars,
                    };
                    tasks.Add(current);
                    first = false;
                    continue;
                }

                if (!UsageReport.LooksLikeSample(line)) continue;
                if (!UsageReport.TryParseSample(line, 0, double.MaxValue, out double t, out TokenBits bits,
                                                out string? id, out _, out _))
                    continue;

                current ??= Continuation(tasks);
                if (id != null && !current.Seen.Add(id)) continue;
                current.Add(t, bits);
            }
        }
        catch { /* a transcript being written, or an unreadable file — keep what we cut */ }

        return tasks.Where(b => b.Calls > 0 || b.Kind != TaskKind.Continuation)
                    .Select(b => b.Build()).ToList();
    }

    private static Builder Continuation(List<Builder> tasks)
    {
        var b = new Builder { Kind = TaskKind.Continuation };
        tasks.Insert(0, b);
        return b;
    }

    /// <summary>Just the command, without its arguments: the node is a name, and the arguments are
    /// already in <c>Prompt</c> where the amendment allows them.</summary>
    private static string CommandName(string text)
    {
        int space = text.IndexOf(' ');
        return space < 0 ? text : text[..space];
    }

    // ---------------------------------------------------------------- hanging the fan-out

    /// <summary>
    /// Each agent transcript under the task that was running when it started, grouped by its workflow
    /// where the path names one.
    ///
    /// <para>By clock, because that is what the transcripts carry: an agent file records no parent
    /// task, and its first turn falls inside exactly one task's span. An agent that matches no task —
    /// a clock that disagrees, a task whose own turns were unreadable — goes on the last task rather
    /// than nowhere, since "unattributed" is a worse answer than "roughly here" for a total.</para>
    /// </summary>
    private static void Attach(List<TaskNode> tasks, List<(string Path, string? Workflow, string Agent)> agents)
    {
        // Each task's effective start. A task that got no answer has no turns and therefore no clock
        // of its own, and leaving it at zero made it match *every* agent — it sorted last among the
        // matches and collected the whole fan-out of a task an hour earlier. Inheriting the previous
        // task's last turn puts it back in the sequence it is actually in.
        var starts = new double[tasks.Count];
        double running = 0;
        for (int i = 0; i < tasks.Count; i++)
        {
            starts[i] = tasks[i].FirstUnix > 0 ? tasks[i].FirstUnix : running;
            if (tasks[i].LastUnix > running) running = tasks[i].LastUnix;
        }

        foreach ((string path, string? workflow, string agent) in agents)
        {
            TaskNode node = Summarize(path, TaskKind.Agent, agent);
            if (node.Calls == 0) continue;

            int at = tasks.Count - 1;
            while (at > 0 && starts[at] > node.FirstUnix) at--;
            TaskNode host = tasks[at];

            if (workflow == null) { host.Children.Add(node); continue; }

            TaskNode? flow = host.Children.FirstOrDefault(c => c.Kind == TaskKind.Workflow &&
                                                               c.Label == workflow);
            if (flow == null)
            {
                // A workflow has no turns of its own — it is a folder — so its Own stays zero and its
                // whole cost is its subtree's. That is the honest shape and not a missing number.
                flow = new TaskNode(TaskKind.Workflow, workflow, "", 0,
                                    node.FirstUnix, node.LastUnix, 0, default, new());
                host.Children.Add(flow);
            }
            flow.Children.Add(node);
        }
    }

    private static TaskNode Summarize(string path, TaskKind kind, string label)
    {
        var b = new Builder { Kind = kind, Label = label };
        try
        {
            foreach (string line in File.ReadLines(path))
            {
                if (!UsageReport.LooksLikeSample(line)) continue;
                if (!UsageReport.TryParseSample(line, 0, double.MaxValue, out double t, out TokenBits bits,
                                                out string? id, out _, out _))
                    continue;
                if (id != null && !b.Seen.Add(id)) continue;
                b.Add(t, bits);
            }
        }
        catch { /* same rule as everywhere: keep what was read */ }
        return b.Build();
    }

    // ---------------------------------------------------------------- accumulation

    private sealed class Builder
    {
        public TaskKind Kind;
        public string Label = "";
        public string Prompt = "";
        public int Chars;
        public readonly HashSet<string> Seen = new(StringComparer.Ordinal);
        public int Calls;
        private long _in, _out, _cc, _cr, _c1h, _c5m;
        private double _first, _last;

        public void Add(double t, TokenBits bits)
        {
            Calls++;
            _in += bits.Input; _out += bits.Output; _cc += bits.CacheCreate; _cr += bits.CacheRead;
            _c1h += bits.CacheCreate1h; _c5m += bits.CacheCreate5m;
            if (_first == 0 || t < _first) _first = t;
            if (t > _last) _last = t;
        }

        public TaskNode Build() => new(Kind, Label, Prompt, Chars, _first, _last, Calls,
                                       new TokenBits(_in, _out, _cc, _cr, _c1h, _c5m), new List<TaskNode>());
    }
}
