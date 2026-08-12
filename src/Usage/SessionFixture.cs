namespace ClaudeTray;

/// <summary>
/// A synthetic <c>projects/</c> tree for the Sessions pane, so a published screenshot of it is a
/// picture of invented work and never of somebody's own (T335).
///
/// <para><b>Why this exists at all.</b> <c>--capture-stats</c> renders that pane from the monitored
/// profile, and since T334 the pane carries the opening prompt of every conversation. So the one
/// command that produces a screenshot for <c>README.md</c> and the published page produced one holding
/// this machine's real prompts, into a public repository. §I.1's amendment permits the prompt <em>on
/// the user's own screen</em>; it says nothing about a picture of that screen in a git history, which
/// is a different promise and a worse one to break — a screenshot cannot be un-published.</para>
///
/// <para>The precedent is <see cref="AccountFixture"/>, written for exactly this shape of problem: a
/// published shot of the System page would carry this machine's login, so it is taken over a fixture
/// instead. The rule both follow is that the fixture must be the <b>easy</b> path, not the careful
/// one — nothing can check whether somebody committed a PNG anyway.</para>
///
/// <para>What it puts on screen is chosen to exercise the pane rather than to look tidy: several
/// projects so the list is worth sorting, a fan-out under one conversation so a row folds its agents
/// in, both task kinds so the by-kind table has more than one row, and one prompt longer than
/// <see cref="SessionIndex.PromptChars"/> so the truncation is visible rather than assumed.</para>
/// </summary>
internal static class SessionFixture
{
    /// <summary>Somewhere the user's name is not in the path, because these screenshots get published —
    /// the same question <see cref="ContextFixture"/> and <see cref="AccountFixture"/> ask.</summary>
    public static string Root => Path.Combine(SampleRoot.For("ClaudeTray-sample"), "sessions", "projects");

    /// <summary>Obviously invented, and obviously not a real ask: a reader of the screenshot should never
    /// wonder whether they are looking at somebody's actual work.</summary>
    private const string Long =
        "Sample conversation for the published screenshot — this prompt is deliberately longer than the " +
        "two hundred characters the index keeps, so the row it produces shows the truncation working " +
        "rather than leaving anyone to take it on trust from the documentation alone.";

    /// <summary>Build the tree and return the <c>projects/</c> directory to read. Rebuilt on every call,
    /// so a stale fixture can never be the thing that got photographed.</summary>
    public static string Build(DateTime nowUtc)
    {
        if (Directory.Exists(Root))
            try { Directory.Delete(Root, recursive: true); } catch { /* rebuilt below anyway */ }
        Directory.CreateDirectory(Root);

        // Deterministic clocks, counted back from "now" so the pane's relative times ("Yesterday
        // 14:05") render the way they will for a reader rather than as a fixed date in the past.
        DateTime t0 = nowUtc.AddHours(-3);

        Session("D--Sample-web-shop", "sample-web-shop", t0, new[]
        {
            Ask("/loop", "15m keep the checkout tests green"),
            Ask("", "Add a currency formatter to the cart summary"),
        }, fanOut: true);

        Session("D--Sample-api-gateway", "sample-api-gateway", nowUtc.AddHours(-9), new[]
        {
            Ask("", Long),
        });

        Session("D--Sample-notes-app", "sample-notes-app", nowUtc.AddDays(-1).AddHours(-2), new[]
        {
            Ask("/review", "the retry policy"),
            Ask("", "Why does the sync run twice on resume?"),
        });

        return Root;
    }

    private static (string Command, string Text) Ask(string command, string text) => (command, text);

    /// <summary>One conversation: a person-ask, an answer, and — where asked for — a fan-out written
    /// under the session's own folder, which is the layout <see cref="TranscriptTail.Locate"/> reads.</summary>
    private static void Session(string slug, string name, DateTime start,
                                (string Command, string Text)[] asks, bool fanOut = false)
    {
        string dir = Path.Combine(Root, slug);
        Directory.CreateDirectory(dir);
        string session = $"sample-{name}";
        var lines = new List<string>();
        DateTime at = start;

        foreach ((string command, string text) in asks)
        {
            // A slash command is not "text that starts with /": the harness writes its own
            // <command-name> markup and that tag is what the ask reader keys on, so a fixture that
            // typed the slash would produce a table with no command row in it — which is exactly what
            // the check reported before this line was right.
            lines.Add(User(at, command.Length > 0
                ? $"<command-name>{command}</command-name><command-args>{text}</command-args>"
                : text));
            at = at.AddMinutes(1);
            for (int i = 0; i < 6; i++)
            {
                lines.Add(Assistant(at, $"{session}-{lines.Count}", 3_500 + i * 900, name));
                at = at.AddMinutes(2);
            }
        }
        File.WriteAllLines(Path.Combine(dir, session + ".jsonl"), lines);

        if (!fanOut) return;

        // Agents file under <slug>/<session>/wf_.../<agent>.jsonl — their spend folds into the row,
        // which is the behaviour a one-row-per-conversation list is claiming on screen.
        string flow = Path.Combine(dir, session, "wf_sample");
        Directory.CreateDirectory(flow);
        for (int a = 0; a < 3; a++)
        {
            var agent = new List<string>();
            DateTime t = start.AddMinutes(4 + a);
            for (int i = 0; i < 4; i++)
            {
                agent.Add(Assistant(t, $"{session}-agent{a}-{i}", 2_200 + i * 400, name));
                t = t.AddMinutes(1);
            }
            File.WriteAllLines(Path.Combine(flow, $"agent-{a}.jsonl"), agent);
        }
    }

    private static string Stamp(DateTime t) =>
        t.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ",
                                     System.Globalization.CultureInfo.InvariantCulture);

    private static string User(DateTime t, string text) =>
        $"{{\"type\":\"user\",\"timestamp\":\"{Stamp(t)}\",\"message\":{{\"role\":\"user\"," +
        $"\"content\":{System.Text.Json.JsonSerializer.Serialize(text)}}}}}";

    private static string Assistant(DateTime t, string id, int output, string name) =>
        $"{{\"type\":\"assistant\",\"timestamp\":\"{Stamp(t)}\",\"requestId\":\"{id}\"," +
        $"\"effort\":\"high\",\"cwd\":\"D:\\\\Sample\\\\{name}\"," +
        // A real model id, not an invented one (T346). It used to say `claude-sample-5`, which the rate
        // card correctly refuses to price — so the published Sessions shot showed an empty money column
        // and the feature could not be screenshotted at all. A model is not an account: naming one here
        // reveals nothing about this machine, and the pane a reader sees is the pane the app draws.
        $"\"message\":{{\"id\":\"msg-{id}\",\"model\":\"claude-opus-4-5\",\"usage\":{{" +
        $"\"input_tokens\":{output / 4},\"output_tokens\":{output}," +
        $"\"cache_creation_input_tokens\":{output * 3}," +
        $"\"cache_creation\":{{\"ephemeral_1h_input_tokens\":{output * 3},\"ephemeral_5m_input_tokens\":0}}," +
        $"\"cache_read_input_tokens\":{output * 12}}}}}}}";
}
