using System.Globalization;
using System.Text.Json;

namespace ClaudeTray.Cases;

/// <summary>
/// WW315. The machine these cases are about, fabricated for the run and taken down after it.
/// <para>
/// The first adoption run in the guest reached it, restored the published engine, built and ran
/// eleven cases. Five answered. Six had nothing to answer with, and said so precisely:
/// <c>--profiles reports 0, and the profile card is Collapsed below two</c>, and <c>no *.jsonl
/// under C:\Users\oobe\.claude\projects, so no report can render</c>. That is the engine behaving —
/// not one of the six went green on absent data. What it blocks is the rest of the migration: WW83
/// moves a profile switch and WW85 a submenu walk, and both need two profiles to exist before there
/// is a switch to make or a submenu to walk.
/// </para>
/// <para>
/// <b>Nothing is written outside the tree.</b> That was the question the task was opened with, and
/// the answer is that no profile is repointed: the application's own discovery is given somewhere
/// else to look. <c>CLAUDE_CONFIG_DIR</c> names a directory in this repository, and that directory's
/// <c>settings.json</c> names the second one through the same <c>env.CLAUDE_CONFIG_DIR</c> key the
/// application already follows. Two profiles, both under <c>bench/</c>, found by the rules the
/// product ships rather than by a list this file hands it.
/// </para>
/// <para>
/// Which is also what makes it disposable. <c>bench/</c> is gitignored, so it never travels to the
/// guest and is built wherever the cases run; it is deleted and rebuilt at the start of a run rather
/// than merely overwritten, because a bench left by a previous run is the shape of fixture that goes
/// on satisfying a precondition after the thing that wrote it has changed.
/// </para>
/// <para>
/// <b>It adds and cannot subtract, and that is measured rather than assumed.</b> Discovery also
/// sweeps <c>~/.claude</c> and the <c>~/.claude-*</c> convention, and the home those resolve against
/// is <c>GetFolderPath(UserProfile)</c> — which reads the process token and not the environment. A
/// run with <c>USERPROFILE</c> pointed at this bench still found this developer's own two accounts
/// beside the fabricated pair. So the guarantee is a floor of two and never an isolation: on the
/// guest, which has none, the floor is the whole answer and the cases run against exactly what they
/// were written for; on a machine with real profiles the run says how many through
/// <see cref="Beside" /> rather than mixing them in silently. A count and never the labels, for the
/// reason this whole file fabricates rather than borrows: those are somebody's accounts.
/// </para>
/// </summary>
internal sealed class Bench : IDisposable
{
    /// <summary>What the variable was before this run set it, so the desk is left as it was found.</summary>
    private readonly string? before;

    private Bench(string root, string configDir, string second, string? before)
    {
        Root = root;
        ConfigDir = configDir;
        Second = second;
        this.before = before;
    }

    /// <summary>Everything this fabricated, and the one directory taking it down has to remove.</summary>
    public string Root { get; }

    /// <summary>The profile the run is in: what <c>CLAUDE_CONFIG_DIR</c> names, and the default.</summary>
    public string ConfigDir { get; }

    /// <summary>The other one, reached through the first's settings file rather than registered.</summary>
    public string Second { get; }

    /// <summary>Where the transcripts this profile reports on are, which is per config dir (T125).</summary>
    public string Projects => Path.Combine(ConfigDir, "projects");

    /// <summary>
    /// What this machine has that the bench did not put there — the labels a discovery sweep finds
    /// outside <see cref="Root" />.
    /// <para>
    /// Empty is the state the cases were written for and the state of the guest. Anything else is a
    /// developer's own desk, and it is named rather than hidden: a run there is reading real account
    /// labels into a trace, and a case that switches profiles is one assertion away from a real
    /// setting. Neither is a reason to refuse; both are a reason to say so out loud.
    /// </para>
    /// </summary>
    /// <param name="reported">How many profiles the application discovered in total.</param>
    /// <returns>How many of them this bench did not fabricate.</returns>
    public int Beside(int reported) => Math.Max(0, reported - Fabricated);

    /// <summary>How many profiles this bench put on the machine.</summary>
    /// <remarks>
    /// Counted off the disk rather than written down as two, so the number cannot disagree with what
    /// <see cref="Under" /> built.
    /// </remarks>
    public int Fabricated => Directory.Exists(Root) ? Directory.EnumerateDirectories(Root).Count() : 0;

    /// <summary>
    /// Build it under <paramref name="repository"/> and point the environment at it.
    /// <para>
    /// The variable is set on this process rather than declared on each fixture, for the reason a
    /// case carries no path at all: the launch inherits this process's environment, so one line here
    /// reaches every fixture and no case file has to name a directory that differs per checkout.
    /// </para>
    /// </summary>
    /// <param name="repository">This repository's root.</param>
    public static Bench Under(string repository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);

        var root = Path.Combine(repository, "bench");
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);

        var one = Path.Combine(root, "one");
        var two = Path.Combine(root, "two");
        Directory.CreateDirectory(one);
        Directory.CreateDirectory(two);

        // Distinct accounts, because discovery keeps the first of two paths reaching one account —
        // a bench whose halves shared a uuid would fabricate two directories and report one profile.
        File.WriteAllText(Path.Combine(one, ".claude.json"), Account(
            "3f8a1c52-0000-4000-8000-000000000001", "bench-one@example.invalid", "Winwright Bench"));
        File.WriteAllText(Path.Combine(two, ".claude.json"), Account(
            "3f8a1c52-0000-4000-8000-000000000002", "bench-two@example.invalid", null));

        // The second profile, named the way the application already looks for one. A registered list
        // would work too and would prove less: registration is a clause that admits a directory
        // without asking whether it looks like a profile, so a bench built on it could pass while
        // discovery proper was broken.
        File.WriteAllText(
            Path.Combine(one, "settings.json"),
            JsonSerializer.Serialize(
                new { env = new Dictionary<string, string> { ["CLAUDE_CONFIG_DIR"] = two } }));

        Transcript(Path.Combine(one, "projects", "D--bench-winwright"));

        var before = Environment.GetEnvironmentVariable(Variable);
        Environment.SetEnvironmentVariable(Variable, one);

        return new Bench(root, one, two, before);
    }

    /// <summary>The variable the application reads a config dir out of, and this run writes.</summary>
    private const string Variable = "CLAUDE_CONFIG_DIR";

    /// <summary>
    /// One <c>.claude.json</c> naming an account. A directory counts as a profile when it holds a
    /// credentials file or a config naming an account, and this is the second: no credential is
    /// fabricated anywhere here, because a file called <c>.credentials.json</c> on a developer's
    /// machine is a thing nobody should have to look twice at.
    /// </summary>
    private static string Account(string uuid, string email, string? organization)
    {
        var account = new Dictionary<string, object>
        {
            ["accountUuid"] = uuid,
            ["emailAddress"] = email,
            ["displayName"] = "Winwright bench",
            ["userRateLimitTier"] = "default",
        };

        // An organization on one and none on the other, so the two labels differ the way the product
        // derives them rather than because this file typed two strings: the organization where there
        // is one, "Personal" where there is not.
        if (organization is not null)
            account["organizationName"] = organization;

        return JsonSerializer.Serialize(new { oauthAccount = account });
    }

    /// <summary>
    /// A session with enough in it for a report to render numbers rather than a page of zeroes.
    /// <para>
    /// The line's shape is written here and also in <c>src/Cli/SelfTestCli.Transcripts.cs</c>, which
    /// is a duplication and a deliberate one: the alternative is this project reaching into a private
    /// method of the application it drives. It is the safe direction to drift in — a line the reader
    /// stops understanding renders no report, and the cases that need one then go <em>unchecked</em>
    /// naming this file, which is the answer they gave before the bench existed.
    /// </para>
    /// </summary>
    /// <param name="slug">The per-project directory a transcript lives in.</param>
    private static void Transcript(string slug)
    {
        Directory.CreateDirectory(slug);

        // Recent, because the grid is twelve rolling weeks back from now and a transcript older than
        // that is a file the report can see and has nothing to say about.
        var now = DateTime.Now;
        var lines = Enumerable.Range(0, 8).Select(at => Turn(now.AddHours(-at * 3), at));

        File.WriteAllLines(Path.Combine(slug, "session.jsonl"), lines);
    }

    /// <summary>One assistant turn: a timestamp, a request id and a usage block. No content, ever.</summary>
    /// <param name="local">When the turn happened, in this machine's own time.</param>
    /// <param name="at">Which turn, so two lines are never one request counted twice.</param>
    private static string Turn(DateTime local, int at)
    {
        var stamp = local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        return JsonSerializer.Serialize(new
        {
            type = "assistant",
            timestamp = stamp,
            requestId = $"bench-{at}-{stamp}",
            cwd = @"D:\bench",
            message = new
            {
                id = $"msg-bench-{at}",
                model = "claude-bench",

                // Distinct per line, so a check that reports which line a sample came from has two
                // answers to tell apart rather than eight copies of one.
                usage = new
                {
                    input_tokens = 120 + at,
                    output_tokens = 340,
                    cache_creation_input_tokens = 0,
                    cache_read_input_tokens = 0,
                },
            },
        });
    }

    /// <summary>Take it down, and put the variable back to whatever it was.</summary>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Variable, before);

        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (Exception left) when (left is IOException or UnauthorizedAccessException)
        {
            // A bench a run could not remove is worth saying and not worth failing over: the next
            // run deletes it before it builds, so what is left here is disk and never a stale answer.
            Console.WriteLine($"the bench under {Root} could not be removed: {left.Message}");
        }
    }
}
