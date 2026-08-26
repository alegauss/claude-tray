using System.Text;

namespace ClaudeTray;

/// <summary>
/// The script that makes two profiles one setup, composed and handed over — never run (T367).
///
/// <para><b>Why a script and not a button.</b> Two profiles are two config dirs, and a user who wants to
/// keep working through a change of subscription has to link them by hand: a dozen commands in the right
/// order, where the wrong order loses a tree. The app cannot do it for them, because it has no write path
/// into <c>~\.claude</c> and is not going to grow one (§I.4). <see cref="ContextPrompt"/> already composes
/// work for somebody else to carry out, and this is the same shape — the app knows the profiles, the
/// person owns the filesystem.</para>
///
/// <para><b>The engineering is the merge, not the link.</b> A bare <c>rmdir</c> plus <c>mklink</c> drops
/// whichever side went second, and on the machine this was designed against the two sides were not
/// remotely equal: <c>file-history</c> held 4,553 files on one and 338 session directories on the other,
/// and <c>history.jsonl</c> held two disjoint prompt histories. So every entry in
/// <see cref="Catalogue"/> declares what has to happen <em>before</em> a link is safe, and the three
/// answers are not interchangeable:</para>
/// <list type="bullet">
/// <item><b>Merge, then link.</b> A union over the entries — session uuids, project slugs, skill folders —
/// or over the lines of a <c>.jsonl</c> by timestamp. The original is moved aside and kept, never
/// deleted.</item>
/// <item><b>Adopt whole.</b> <c>plugins</c> records an absolute <c>installPath</c> per entry, so a
/// file-level merge leaves orphans pointing at the other profile's tree; <c>CLAUDE.md</c> is prose, and a
/// union of two sets of instructions is not instructions. One side wins entire.</item>
/// <item><b>Withheld, and offered as a comment.</b> <c>settings.json</c> <em>can</em> be unioned, and that
/// is exactly why it is not: the union widens the other account's permission allowlist, which is a
/// decision and not a default.</item>
/// <item><b>Never.</b> <c>.claude.json</c> carries <c>oauthAccount</c> — the field that makes a profile a
/// different profile — and <c>.credentials.json</c> is a token. Linking either is not "one setup", it is
/// one account, and §I.6 already settled that no file is swapped to switch accounts.</item>
/// </list>
///
/// <para><b>Two properties the emitted text must keep.</b> It must not act: printing the plan is what a
/// bare run does, and <c>-Apply</c> is the user typing the second half of the sentence. And it must refuse
/// rather than half-apply — a directory junction needs no privilege, a file symlink needs Developer Mode
/// or elevation, and a machine with neither is told <em>before</em> anything moves, not after three
/// entries have already been relinked.</para>
///
/// <para><b>English, not localized</b>, for <see cref="ContextPrompt"/>'s reason: the audience is
/// PowerShell and the person reading it before they run it, not the window. Pure over its
/// <see cref="Plan"/> and stamped by the shell rather than by the clock here, so the same two profiles
/// compose the same script twice and <c>--selftest</c> can assert what is in it.</para>
/// </summary>
internal static class ProfileLink
{
    /// <summary>What happens to one entry of a config dir.</summary>
    public enum Verdict
    {
        /// <summary>Union the two sides into the primary, then replace the secondary's copy with a link.</summary>
        Merge,
        /// <summary>One side wins entire — a per-entry merge would leave something broken behind.</summary>
        Adopt,
        /// <summary>Linkable, deliberately not offered: the commands are emitted commented out.</summary>
        Withheld,
        /// <summary>Never linked, and the script says why rather than staying silent about it.</summary>
        Never,
    }

    /// <summary>How the two sides are unioned before the link, for a <see cref="Verdict.Merge"/> entry.</summary>
    public enum Union
    {
        /// <summary>Nothing to union — not a <see cref="Verdict.Merge"/> entry.</summary>
        None,
        /// <summary>By the name of each top-level entry; a name on both sides is a conflict the script
        /// reports and leaves alone, because the primary is the side that survives.</summary>
        Entries,
        /// <summary>By line, ordered on the <c>timestamp</c> field, duplicates dropped verbatim.</summary>
        Lines,
    }

    /// <summary>
    /// An English noun in both its forms, for the one surface here that is prose for a <em>user</em> and
    /// has no lang file to reach: the emitted script (T377).
    ///
    /// <para><b>Both words, not a rule.</b> <c>entry</c> → <c>entries</c> is not <c>+s</c>, and a script
    /// that guesses is a script that says <c>entrys</c> the first time somebody adds one.
    /// <see cref="Of"/> is the regular case and is deliberately at the <em>declaration</em> site, where an
    /// author looking at <c>Of("entry")</c> can see it is wrong and write the pair out.</para>
    /// </summary>
    public readonly record struct Noun(string One, string Many)
    {
        /// <summary>The regular case: one word plus <c>s</c>.</summary>
        public static Noun Of(string one) => new(one, one + "s");

        /// <summary>No noun — an entry nothing counts.</summary>
        public static Noun None => new("", "");

        public bool Any => One.Length > 0;
    }

    /// <summary>
    /// A count and its noun, in English, for the script: <c>1 skill</c> or <c>12 skills</c> (T377). The
    /// number goes through <see cref="Nums"/> like every other one in the app (T216) — the script is
    /// English, not invariant-only, but a decimal comma in a file a user is about to run is still wrong.
    /// </summary>
    public static string Counted(double n, Noun noun) =>
        $"{Nums.Of(n)} {(Math.Abs(n - 1) < 0.0001 ? noun.One : noun.Many)}";

    /// <inheritdoc cref="Counted(double, Noun)"/>
    public static string Counted(double n, string one, string many) => Counted(n, new Noun(one, many));

    /// <summary>
    /// One entry of a config dir and what the script does with it. <paramref name="Unit"/> is what an
    /// <see cref="Union.Entries"/> union counts, named so the script can say "12 session uuids" instead of
    /// "12 items" — the count is the only thing a reader can sanity-check before typing <c>-Apply</c>.
    /// </summary>
    public readonly record struct Entry(
        string Name, bool IsDirectory, Verdict Verdict, Union Union, Noun Unit, string Why);

    /// <summary>
    /// Everything a config dir holds that this script has an opinion about, in the order the script
    /// handles it: the merges first, because they are the ones that can report a conflict and stop, then
    /// the adoptions, then the two that are only explained.
    ///
    /// <para>Deliberately not "everything in the directory": an entry earns a row here by being part of
    /// the <em>setup</em>, and <see cref="Scratch"/> names the per-machine caches and snapshots that are
    /// not. <b>Which is why <see cref="Edge"/> exists</b> (T374). This is a list of opinions, and the
    /// honest failure mode of a list of opinions is silence about everything not on it: <c>agents</c> and
    /// <c>commands</c> were missing from here for four tasks, and nothing said so, because neither folder
    /// existed on the machine this was built against. A fixed catalogue must report its own edge — a row
    /// can be added later, and being told nothing is missing cannot be undone.</para>
    /// </summary>
    public static IReadOnlyList<Entry> Catalogue { get; } = new[]
    {
        new Entry("projects", true, Verdict.Merge, Union.Entries, Noun.Of("project"),
            "the transcripts every reading in this app comes from; one folder per project, so a union by "
            + "folder name loses no session"),
        new Entry("file-history", true, Verdict.Merge, Union.Entries, Noun.Of("session uuid"),
            "one folder per session; the two sides are not the same size and a bare rmdir would drop "
            + "whichever went second"),
        new Entry("history.jsonl", false, Verdict.Merge, Union.Lines, Noun.Of("prompt"),
            "two disjoint prompt histories, so the union is by line ordered on its timestamp"),
        new Entry("skills", true, Verdict.Merge, Union.Entries, Noun.Of("skill"),
            "skills you wrote; a folder named on both sides is a real conflict and is reported, not merged"),
        new Entry("agents", true, Verdict.Merge, Union.Entries, Noun.Of("agent"),
            "subagents you wrote, one folder each - the same shape as skills"),
        new Entry("commands", true, Verdict.Merge, Union.Entries, Noun.Of("command"),
            "slash commands you wrote, one file each"),
        new Entry("output-styles", true, Verdict.Merge, Union.Entries, Noun.Of("output style"),
            "output styles you wrote, and the same shape again"),
        new Entry("plugins", true, Verdict.Adopt, Union.None, Noun.None,
            "each installed plugin records an absolute installPath, so a per-entry merge leaves entries "
            + "pointing into the other profile's tree"),
        new Entry("CLAUDE.md", false, Verdict.Adopt, Union.None, Noun.None,
            "prose: a union of two sets of instructions is not a set of instructions"),
        new Entry("settings.json", false, Verdict.Withheld, Union.None, Noun.None,
            "it can be unioned, and that is the problem - the union widens the other account's permission "
            + "allowlist, which is your decision and not this script's default"),
        new Entry("settings.local.json", false, Verdict.Withheld, Union.None, Noun.None,
            "the machine-local half of the same decision"),
        new Entry(".claude.json", false, Verdict.Never, Union.None, Noun.None,
            "it carries oauthAccount - the field that makes this a different profile at all"),
        new Entry(".credentials.json", false, Verdict.Never, Union.None, Noun.None,
            "a token"),
    };

    /// <summary>
    /// Per-machine scratch: entries the catalogue deliberately has no row for, declared so the edge report
    /// below can say how many it passed over instead of listing them (T374).
    ///
    /// <para>Each of these is either keyed by a process id or rebuilt on demand — <c>sessions</c> and
    /// <c>ide</c> hold pid-named files, <c>session-env</c> a directory per session uuid, and the rest are
    /// caches, snapshots and a debug log. Linking any of them shares nothing a person would notice and
    /// hands the script failure modes for nothing.</para>
    ///
    /// <para><b>Deliberately short.</b> Anything not certainly scratch belongs in the edge report, where
    /// it is named and the reader decides — that is the whole correction T374 makes, and a generous
    /// scratch list would undo it by turning silence into a different silence.</para>
    /// </summary>
    public static IReadOnlySet<string> Scratch { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "cache", "paste-cache", "shell-snapshots", "debug", "downloads", "backups", "ide",
        "session-env", "sessions", "statsig",
    };

    /// <summary>
    /// What sits in the two config dirs that the catalogue says nothing about (T374).
    ///
    /// <para><b>Why a fixed catalogue must report its own edge.</b> The list is a list of opinions, and the
    /// honest failure mode of a list of opinions is silence about everything not on it. <c>agents</c> and
    /// <c>commands</c> were missing for four tasks and nothing said so, because neither existed on the
    /// machine this was built against. This is what makes the next folder Claude Code invents visible on
    /// the first run rather than on the day somebody notices it never came across.</para>
    ///
    /// <para>Not a verdict: an acknowledgement. <paramref name="Unclaimed"/> is named in full and
    /// <paramref name="Ignored"/> is a count, which is the difference between "I have no opinion about
    /// this" and "I decided this does not matter".</para>
    /// </summary>
    public readonly record struct Edge(string[] Unclaimed, int Ignored);

    /// <summary>
    /// One catalogue entry against the two directories on disk. <paramref name="AlreadyLinked"/> is the
    /// idempotence the script needs: re-running it must be a no-op rather than a second relink of a link,
    /// and the only way to know is to ask whether the secondary's copy is already a reparse point
    /// resolving into the primary.
    ///
    /// <para><paramref name="ToCopy"/> is how many of the secondary's own entries the union would carry
    /// over, and it is <c>null</c> where the question does not apply <em>or cannot be answered without
    /// opening a file</em>. Those are two different reasons and the same answer on purpose: an adopted or
    /// withheld entry copies nothing, and a <see cref="Union.Lines"/> entry could only be counted by
    /// reading <c>history.jsonl</c>, which is prompts — §I.1. A directory listing is names, which this app
    /// reads everywhere; the count stops exactly where the file would have to be opened.</para>
    /// </summary>
    /// <param name="Widening">For a <see cref="Verdict.Withheld"/> entry, what unioning the two files
    /// would actually add (T373) — the evidence the decision this script refuses to make needs. Null for
    /// every other entry, because the question is only asked where the answer is the user's.</param>
    public readonly record struct Step(
        Entry Entry, bool OnPrimary, bool OnSecondary, bool AlreadyLinked, int? ToCopy = null,
        SettingsUnion.Reading? Widening = null);

    /// <summary>
    /// Where the emitted preflight looks to see whether Developer Mode is on. The real key, and the
    /// default every plan carries.
    /// </summary>
    public const string DevModeKey = @"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock";

    /// <summary>
    /// What the script will do, resolved against two real config dirs. <paramref name="Error"/> is set
    /// when there is nothing to compose — a directory that is not there, or the same directory twice —
    /// and then <see cref="Script"/> is never called.
    /// </summary>
    /// <param name="DevModeRegistryKey">The key the emitted preflight reads (T386). A seam, not an
    /// option: the branch that <em>refuses</em> a file symlink fires on no machine anybody runs this on —
    /// the developer's has Developer Mode on and the CI runner allows an unprivileged <c>mklink</c> too —
    /// so the only way to exercise a refusal is to compose a script that cannot pass its own preflight.
    /// Pointing this at a key that does not exist does exactly that, and nothing else in the script
    /// changes. The same kind of seam <c>--sample-env</c> and <see cref="AccountFixture"/> already are.</param>
    public sealed record Plan(
        string PrimaryDir, string PrimaryLabel, string SecondaryDir, string SecondaryLabel,
        IReadOnlyList<Step> Steps, string? Error = null, Edge Edge = default,
        string DevModeRegistryKey = DevModeKey)
    {
        /// <summary>The steps that will actually touch the disk under <c>-Apply</c>.</summary>
        public IEnumerable<Step> Acting => Steps.Where(s =>
            s.Entry.Verdict is Verdict.Merge or Verdict.Adopt && s.OnPrimary && !s.AlreadyLinked);

        /// <summary>Whether any acting step needs a <em>file</em> symlink, which is the only part of this
        /// that needs a privilege — and so the only reason the preflight can refuse (T367).</summary>
        public bool NeedsSymlink => Acting.Any(s => !s.Entry.IsDirectory);

        /// <summary>
        /// Whether this plan puts the two profiles behind one <c>projects</c> tree, which costs them
        /// auto-follow (T371). Answered from the plan rather than looked up afterwards: the script is the
        /// only surface that can say it <em>before</em> the decision, and it is the one that knows.
        ///
        /// <para>Not a warning about the link — sharing the transcripts is most of the point of doing
        /// this. It is a consequence, and the consequence is invisible: after a successful run the icon
        /// simply stops moving between the two, and nothing in the app used to connect that to a script
        /// somebody ran once.</para>
        /// </summary>
        public bool CostsAutoFollow => Acting.Any(s => s.Entry.Name == "projects");
    }

    /// <summary>
    /// Resolve the catalogue against two config dirs. <paramref name="primaryDir"/> is the side that keeps
    /// its real files and receives every merge; <paramref name="secondaryDir"/> is the side whose entries
    /// become links into it. An empty string is the default <c>~\.claude</c>, exactly as the launch path
    /// reads it.
    /// </summary>
    public static Plan For(string primaryDir, string secondaryDir,
        string primaryLabel = "primary", string secondaryLabel = "secondary")
    {
        string primary = Resolve(primaryDir), secondary = Resolve(secondaryDir);
        string? error =
            !Directory.Exists(primary) ? $"{primary} is not a directory, so there is nothing to link into"
            : !Directory.Exists(secondary) ? $"{secondary} is not a directory, so there is nothing to link"
            : ClaudeAccount.SamePath(primary, secondary)
                ? $"{primary} is both sides of this, which would link a directory to itself"
                : null;

        var steps = new List<Step>(Catalogue.Count);
        if (error is null)
            foreach (Entry e in Catalogue)
            {
                string onPrimary = Path.Combine(primary, e.Name);
                string onSecondary = Path.Combine(secondary, e.Name);
                bool linked = LinksInto(onSecondary, onPrimary, e.IsDirectory);
                steps.Add(new Step(e, Exists(onPrimary, e.IsDirectory), Exists(onSecondary, e.IsDirectory),
                    linked, linked ? null : ToCopyCount(e, onPrimary, onSecondary),
                    // Read here rather than at each surface: the script and the settings page ask the same
                    // question of the same two files, and two readers of one union is how they come to
                    // disagree about a figure a person is about to act on.
                    e.Verdict == Verdict.Withheld ? SettingsUnion.For(primary, secondary, e.Name) : null));
            }

        return new Plan(primary, primaryLabel, secondary, secondaryLabel, steps, error,
            error is null ? EdgeOf(primary, secondary) : default);
    }

    /// <summary>
    /// Everything at the top level of either config dir that <see cref="Catalogue"/> has no row for,
    /// split into what is named and what is counted (T374). Directory entries only — names, never a file
    /// opened.
    /// </summary>
    private static Edge EdgeOf(string primary, string secondary)
    {
        var claimed = new HashSet<string>(Catalogue.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
        var unclaimed = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int ignored = 0;
        foreach (string dir in new[] { primary, secondary })
        {
            string[] names;
            // An unreadable config dir reports no edge rather than an empty one: this is the surface whose
            // whole job is saying what it does not know about.
            try { names = Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).ToArray()!; }
            catch { continue; }
            foreach (string name in names)
            {
                if (claimed.Contains(name)) continue;
                // A `.pre-link-<stamp>` copy is this script's own doing, so naming it back to the reader as
                // something nobody has an opinion about would be a second run reporting the first one.
                if (Scratch.Contains(name) || name.Contains(".pre-link-", StringComparison.OrdinalIgnoreCase)
                    || name.Contains(".pre-merge-", StringComparison.OrdinalIgnoreCase)) { ignored++; continue; }
                unclaimed.Add(name);
            }
        }
        return new Edge(unclaimed.ToArray(), ignored);
    }

    private static string Resolve(string configDir) =>
        configDir.Length > 0 ? configDir : ContextScanner.DefaultClaudeRoot;

    private static bool Exists(string path, bool isDirectory) =>
        isDirectory ? Directory.Exists(path) : File.Exists(path);

    /// <summary>
    /// How many of the secondary's top-level entries the union would carry over — the one figure a reader
    /// can check against their own memory of the two machines before agreeing to any of this. Top level
    /// only, and names only: the same reading <see cref="ProfileActivity"/> already takes.
    ///
    /// <para>Null rather than zero wherever the question does not apply, so "nothing to copy" and "not
    /// counted" stay distinguishable on a surface that has to render both.</para>
    /// </summary>
    private static int? ToCopyCount(Entry e, string onPrimary, string onSecondary)
    {
        if (e.Union != Union.Entries || !Directory.Exists(onSecondary)) return null;
        try
        {
            int n = 0;
            foreach (string entry in Directory.EnumerateFileSystemEntries(onSecondary))
                if (!Directory.Exists(Path.Combine(onPrimary, Path.GetFileName(entry)))
                    && !File.Exists(Path.Combine(onPrimary, Path.GetFileName(entry)))) n++;
            return n;
        }
        // An unreadable directory answers "not counted" rather than "nothing to copy": the second would
        // be a figure, and a figure nobody measured is worse on this surface than an absence.
        catch { return null; }
    }

    /// <summary>
    /// Whether <paramref name="path"/> is already a link resolving to <paramref name="target"/>. Reads the
    /// final target rather than the immediate one, because a second run of this script finds the link the
    /// first one made, and a chain through a third directory is still one tree.
    /// </summary>
    private static bool LinksInto(string path, string target, bool isDirectory)
    {
        try
        {
            FileSystemInfo? resolved = isDirectory
                ? Directory.ResolveLinkTarget(path, returnFinalTarget: true)
                : File.ResolveLinkTarget(path, returnFinalTarget: true);
            return resolved is not null && ClaudeAccount.SamePath(resolved.FullName, target);
        }
        // A path that cannot be read is not known to be a link, and this decides whether a step is
        // skipped — so it fails towards emitting the step and letting the script's own check refuse.
        catch { return false; }
    }

    /// <summary>
    /// The script itself. Pure over <paramref name="plan"/>: no clock, no environment, no path outside the
    /// two config dirs it was given — so the same pair composes the same text twice, and the stamp on the
    /// folders it moves aside is taken by the shell at the moment it runs, which is when it means anything.
    /// </summary>
    public static string Script(Plan plan)
    {
        var sb = new StringBuilder();
        sb.Append(Header(plan));
        sb.Append(Preflight(plan));
        // After the preflight, so the first thing a reader meets is the refusal that can stop this, and
        // before the plan, because PowerShell needs a function defined before it is called.
        sb.Append(Helpers());

        foreach (Step s in plan.Steps)
        {
            sb.AppendLine();
            switch (s.Entry.Verdict)
            {
                case Verdict.Merge or Verdict.Adopt when s.AlreadyLinked:
                    sb.AppendLine($"# {s.Entry.Name} - already a link into the {plan.PrimaryLabel} profile. Nothing to do.");
                    break;
                case Verdict.Merge or Verdict.Adopt when !s.OnPrimary:
                    sb.AppendLine($"# {s.Entry.Name} - not present in the {plan.PrimaryLabel} profile, so there is");
                    sb.AppendLine($"#   nothing to link into. Skipped rather than created empty.");
                    break;
                case Verdict.Merge:
                    sb.Append(MergeStep(plan, s));
                    break;
                case Verdict.Adopt:
                    sb.Append(AdoptStep(plan, s));
                    break;
                case Verdict.Withheld:
                    sb.Append(WithheldStep(plan, s));
                    break;
                case Verdict.Never:
                    sb.AppendLine($"# {s.Entry.Name} - never linked: {s.Entry.Why}.");
                    break;
            }
        }

        sb.Append(Footer(plan));
        return sb.ToString();
    }

    private static string Header(Plan plan) => $$"""
        # Link two Claude Code profiles into one setup
        #
        # Composed by Claude Code Tray. Read it before you run it: it moves directories, and the
        # profile named second stops having files of its own for every entry below.
        #
        #   keeps its files : {{plan.PrimaryLabel}}
        #                     {{plan.PrimaryDir}}
        #   becomes links   : {{plan.SecondaryLabel}}
        #                     {{plan.SecondaryDir}}
        #
        # A bare run PRINTS what it would do and changes nothing. Pass -Apply to act.
        #
        #   powershell -ExecutionPolicy Bypass -File link-profiles.ps1            # read it
        #   powershell -ExecutionPolicy Bypass -File link-profiles.ps1 -Apply     # do it
        #
        # Nothing here elevates and nothing here deletes: every original is renamed to
        # <name>.pre-link-<stamp> beside itself, and the undo at the bottom puts it back.
        #
        # Close every Claude Code session first. A running session holds its config dir open and
        # keeps whatever it started with, so relinking underneath one is how you get half of each.
        {{AutoFollowNote(plan)}}

        [CmdletBinding()]
        param([switch]$Apply)

        $ErrorActionPreference = 'Stop'
        $Primary   = '{{Quote(plan.PrimaryDir)}}'
        $Secondary = '{{Quote(plan.SecondaryDir)}}'
        $Stamp     = Get-Date -Format 'yyyyMMdd-HHmmss'
        $Acted     = 0

        function Note($m)  { Write-Host $m }
        function Would($m) { Write-Host ('  would ' + $m) -ForegroundColor DarkGray }
        function Did($m)   { Write-Host ('  ' + $m) -ForegroundColor Green }

        # A count and its noun, both words given (T377). Five of the numbers below are counted by
        # PowerShell at run time rather than composed when this file was written, so the singular has to
        # be decided here - and a parenthesised plural, in a file you are reading before it moves your
        # transcripts, reads as unfinished.
        function Count($n, $one, $many) { "$n " + $(if ($n -eq 1) { $one } else { $many }) }

        # Windows PowerShell 5.1 is what a double-click gets, and it runs on .NET Framework: there is no
        # ResolveLinkTarget on a DirectoryInfo there. The ReparsePoint attribute is the reading both
        # editions have, and .Target beside it is best-effort - 5.1 returns it for a junction, and a blank
        # one is not a reason to call a link that exists a failure.
        function IsLink($p) {
          if (-not (Test-Path -LiteralPath $p)) { return $false }
          return [bool]((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)
        }

        # A failure part-way through is the case the preflight cannot cover - it refuses what it can see,
        # and this says where the tree was left. $ErrorActionPreference stops before the next entry, so
        # "part-way" is always at an entry boundary or inside one whose original is still beside it.
        trap {
          Write-Host ''
          Write-Host ('STOPPED after ' + (Count $Acted 'change' 'changes') + ': ' + $_.Exception.Message) -ForegroundColor Red
          Write-Host 'Nothing was deleted. Every original is beside its link as <name>.pre-link-<stamp>,'
          Write-Host 'and the undo at the bottom of this script puts it back. Re-running is safe: an entry'
          Write-Host 'that is already a link is skipped rather than relinked.'
          Write-Host ''
          exit 1
        }

        Note ''
        Note ('Primary   : ' + $Primary)
        Note ('Secondary : ' + $Secondary)
        Note ('Mode      : ' + $(if ($Apply) { 'APPLY' } else { 'dry run (pass -Apply to act)' }))
        Note ''

        """;

    /// <summary>
    /// What linking <c>projects</c> costs, said before the decision rather than discovered after it
    /// (T371). Empty when the plan does not touch that entry, because then it costs nothing.
    ///
    /// <para>Phrased as a consequence and not a warning: sharing the transcripts is most of the reason
    /// somebody runs this. What it must not be is a surprise — an icon that quietly stops moving between
    /// two profiles, weeks after a script, is a defect nobody can trace back.</para>
    /// </summary>
    private static string AutoFollowNote(Plan plan) => !plan.CostsAutoFollow ? "" : $"""
        #
        # One consequence, and it is not a warning: sharing `projects` means both profiles report the
        # same "last turn", so Claude Code Tray can no longer tell which of the two you are working in.
        # Its "Follow the active profile" setting stops moving the icon between {plan.PrimaryLabel} and
        # {plan.SecondaryLabel} - the icon stays wherever you put it. A third profile with a tree of its
        # own is unaffected, and unlinking `projects` gives the pair back.
        """;

    /// <summary>
    /// The refusal, before the first move. A junction needs no privilege at all, so this only has
    /// something to say when the plan contains a file symlink — and then it says it once, up front,
    /// instead of letting three entries relink and the fourth throw.
    /// </summary>
    private static string Preflight(Plan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            if (-not (Test-Path -LiteralPath $Primary))   { throw "the primary profile is not there: $Primary" }
            if (-not (Test-Path -LiteralPath $Secondary)) { throw "the secondary profile is not there: $Secondary" }
            """);
        if (!plan.NeedsSymlink)
        {
            sb.AppendLine();
            sb.AppendLine("# Every link below is a directory junction, which needs no privilege. Nothing to check.");
            return sb.ToString();
        }

        // $$ so the key interpolates while PowerShell's own braces stay literal.
        sb.Append($$"""

            # This plan contains a FILE symlink, which unlike a directory junction needs either Developer
            # Mode or an elevated shell. Checked here, before anything moves: a machine without it must be
            # told now rather than after half the entries have been relinked. The link is made with mklink
            # for that reason too - it passes the unprivileged-create flag that Windows PowerShell 5.1's
            # New-Item does not, so without it Developer Mode would be a promise this script then broke.
            $devMode = 0
            try {
              $devMode = (Get-ItemProperty -Path '{{Quote(plan.DevModeRegistryKey)}}' `
                -Name AllowDevelopmentWithoutDevLicense -ErrorAction Stop).AllowDevelopmentWithoutDevLicense
            } catch { $devMode = 0 }
            $elevated = ([Security.Principal.WindowsPrincipal] `
              [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
                [Security.Principal.WindowsBuiltInRole]::Administrator)
            if ($devMode -ne 1 -and -not $elevated) {
              throw ("this plan links a file, which needs a symlink: turn on Settings > System > For " +
                     "developers > Developer Mode, or run this in an elevated shell. Nothing was changed.")
            }
            Note ("file symlinks: allowed (" + $(if ($devMode -eq 1) { 'Developer Mode' } else { 'elevated' }) + ")")

            """);
        return sb.ToString();
    }

    /// <summary>
    /// The three shapes every entry's work is one of, emitted <b>once</b> with their explanations (T379).
    ///
    /// <para><b>Why once.</b> Composed per entry, a plan with nine of them ran to 485 lines, 311 of them
    /// code, with this same block written out nine times — the five-line note about reading a reparse-point
    /// attribute instead of <c>ResolveLinkTarget</c> included. Every one of those lines exists to be read,
    /// which is the problem: a person handed nine copies of one paragraph stops reading, and what is
    /// <em>not</em> repeated is exactly what matters — the per-entry verdict and the union's count.</para>
    ///
    /// <para><b>What stays inline.</b> The guard. <c>if (IsLink $link) { … } else { … }</c> per entry is two
    /// lines and reads as the plan, and folding it in here would hide the most reassuring thing in the
    /// script: that a second run does nothing.</para>
    ///
    /// <para><c>$script:Acted</c>, not <c>$Acted</c>: PowerShell reads an outer scope and writes a local
    /// copy, so a function incrementing the bare name counts into a variable that dies with it — and the
    /// count is what the footer and the trap both report.</para>
    /// </summary>
    private static string Helpers() => """

        # ---- what each entry's work is one of, written once ------------------------------------------

        # Union by top-level name, into the side that keeps its files. A name on both sides is left exactly
        # as it is and reported: the primary is the side that survives, and silently overwriting one of its
        # folders would be the data loss this whole script exists to avoid.
        function MergeEntries($from, $into, $one, $many) {
          $new = @(); $clash = @()
          foreach ($item in Get-ChildItem -LiteralPath $from -Force) {
            if (Test-Path -LiteralPath (Join-Path $into $item.Name)) { $clash += $item.Name }
            else { $new += $item }
          }
          Note ("  " + (Count $new.Count $one $many) + " to copy over, " + $clash.Count + " already on the primary side")
          if ($clash.Count -gt 0) { Note ("  kept as-is on the primary side: " + ($clash -join ', ')) }
          foreach ($item in $new) {
            if ($Apply) { Copy-Item -LiteralPath $item.FullName -Destination $into -Recurse -Force; $script:Acted++ }
            else { Would ("copy " + $item.Name) }
          }
        }

        # Union by line, ordered on each line's timestamp, duplicates dropped verbatim. Sorted rather than
        # appended: the file is read newest-last, and two histories concatenated would interleave nothing
        # and read as one profile's past followed by the other's. The original is copied aside first.
        function MergeLines($from, $into, $one, $many) {
          $lines = @()
          foreach ($p in @($into, $from)) {
            if (Test-Path -LiteralPath $p) { $lines += Get-Content -LiteralPath $p -Encoding utf8 }
          }
          $merged = $lines | Where-Object { $_.Trim().Length -gt 0 } | Select-Object -Unique | Sort-Object {
            try { [int64]([regex]::Match($_, '"timestamp"\s*:\s*(\d+)').Groups[1].Value) } catch { 0 }
          }
          Note ("  " + (Count $lines.Count $one $many) + " read, " + $merged.Count + " after the union")
          if ($Apply) {
            Copy-Item -LiteralPath $into -Destination ($into + '.pre-merge-' + $Stamp) -Force
            Set-Content -LiteralPath $into -Value $merged -Encoding utf8
            $script:Acted++
          } else { Would ("write " + (Count $merged.Count 'line' 'lines') + " to " + $into) }
        }

        # Move the original aside, then link. Two things worth knowing, and this is the only copy of them.
        # mklink rather than New-Item: in Windows PowerShell 5.1 New-Item does not pass the
        # unprivileged-create flag, so it fails with "requires administrator privilege" on a machine where
        # Developer Mode is on and mklink succeeds. And the result is VERIFIED by the reparse-point
        # attribute rather than by ResolveLinkTarget, which does not exist on .NET Framework and so not in
        # the PowerShell a double-click gets - measured, by this script failing right there on its own
        # first real run. The check happens while the original is still sitting beside it.
        function LinkEntry($link, $tgt, $flag, $kind, $moveAside) {
          if ($moveAside -and (Test-Path -LiteralPath $link)) {
            $aside = $link + '.pre-link-' + $Stamp
            if ($Apply) { Move-Item -LiteralPath $link -Destination $aside; $script:Acted++ }
            else { Would ("move aside to " + (Split-Path $aside -Leaf)) }
          }
          if ($Apply) {
            & cmd /c ('mklink ' + $flag + '"' + $link + '" "' + $tgt + '"') | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "mklink refused to link $link" }
            if (-not (IsLink $link)) { throw "$link was created but is not a link" }
            Did ("linked -> " + $tgt)
            $script:Acted++
          } else { Would ("link " + $link + " -> " + $tgt + " (" + $kind + ")") }
        }

        # ---- the plan, one entry at a time -----------------------------------------------------------

        """;

    private static string MergeStep(Plan plan, Step s)
    {
        Entry e = s.Entry;
        var sb = new StringBuilder();
        sb.AppendLine($"# {e.Name} - merge, then link. {Capital(e.Why)}.");
        if (!s.OnSecondary)
            // Nothing to union, but the link is still the point: the secondary has to end up reading the
            // primary's tree even when it never had one of its own.
            sb.AppendLine($"#   the {plan.SecondaryLabel} profile has none, so there is nothing to merge - link only.");
        string merge = s.OnSecondary
            ? $"{(e.Union == Union.Lines ? "MergeLines" : "MergeEntries")} $link $tgt "
              + $"'{Quote(e.Unit.One)}' '{Quote(e.Unit.Many)}'\n"
            : "";
        return sb.Append(Guard(e, merge + LinkCall(e, moveAside: s.OnSecondary))).ToString();
    }

    private static string AdoptStep(Plan plan, Step s) =>
        $"# {s.Entry.Name} - adopted whole from the {plan.PrimaryLabel} profile, not merged. {Capital(s.Entry.Why)}.\n"
        + (s.OnSecondary
            ? $"#   the {plan.SecondaryLabel} profile's own copy is moved aside and kept, so nothing is lost.\n"
            : $"#   the {plan.SecondaryLabel} profile has none, so this only adds the link.\n")
        + Guard(s.Entry, LinkCall(s.Entry, moveAside: s.OnSecondary));

    /// <summary>One call to <c>LinkEntry</c>: the flag <c>mklink</c> takes, the word a dry run prints, and
    /// whether there is an original to move out of the way.</summary>
    private static string LinkCall(Entry e, bool moveAside) =>
        $"LinkEntry $link $tgt '{(e.IsDirectory ? "/J " : "")}' "
        + $"'{(e.IsDirectory ? "junction" : "symlink")}' ${(moveAside ? "true" : "false")}\n";

    /// <summary>
    /// One entry's work, behind the check that it is not already a link. The plan knows the answer too —
    /// <see cref="Step.AlreadyLinked"/> is why an already-linked entry renders as a comment at all — but
    /// the app answered it when the script was <em>composed</em>, and the script runs later, twice, or
    /// after a run that stopped half way. Asking again at the moment it matters is what makes re-running
    /// this safe, which is the only recovery a script that never deletes can offer.
    /// </summary>
    private static string Guard(Entry e, string body) => $$"""
        Note '{{e.Name}}'
        $link = Join-Path $Secondary '{{e.Name}}'
        $tgt  = Join-Path $Primary   '{{e.Name}}'
        if (IsLink $link) { Note '  already a link - skipped' } else {
        {{Indent(body)}}
        }

        """;

    /// <summary>Two spaces onto every non-blank line, so the guarded body reads as guarded. PowerShell
    /// does not care; the person deciding whether to type <c>-Apply</c> does.</summary>
    private static string Indent(string body) => string.Join("\n",
        body.TrimEnd().Split('\n').Select(l => l.Length == 0 ? l : "  " + l));

    /// <summary>
    /// The entry this script deliberately does not act on, and — since T373 — the reading that decision
    /// needs. The commands stay commented out; what changed is that the sentence above them now names both
    /// allowlists instead of asking about one it never showed.
    /// </summary>
    private static string WithheldStep(Plan plan, Step s) => $"""
        # {s.Entry.Name} - NOT linked, on purpose: {s.Entry.Why}.
        #
        #   What the union would actually do:
        {(s.Widening is { } w ? string.Join("\n", SettingsUnion.Lines(w)) : "#   not read.")}
        #
        #   If you have read both files and want them shared anyway, this is the command - uncommented by
        #   you, which is the whole point of it being here as text:
        #
        # Move-Item -LiteralPath '{Quote(Path.Combine(plan.SecondaryDir, s.Entry.Name))}' `
        #   -Destination '{Quote(Path.Combine(plan.SecondaryDir, s.Entry.Name))}.pre-link'
        # cmd /c 'mklink "{Quote(Path.Combine(plan.SecondaryDir, s.Entry.Name))}" "{Quote(Path.Combine(plan.PrimaryDir, s.Entry.Name))}"'

        """;


    /// <summary>
    /// What this script has no opinion about, after the rows it does (T374). Last rather than first: a
    /// reader has to have seen the catalogue before "and these are not in it" means anything.
    ///
    /// <para>Named in full, and capped only because a config dir can hold a surprising number of state
    /// files. The count of scratch entries is beside it so the two are not confused — one is an absence of
    /// opinion, the other is an opinion.</para>
    /// </summary>
    private static string EdgeNote(Plan plan, int show = 8)
    {
        Edge edge = plan.Edge;
        // Every clause that has to agree with a number, decided here rather than papered over with `(s)`
        // (T377): the noun, and — the part `(s)` could never reach — the verb and the pronoun with it.
        string passed = Counted(edge.Ignored, "per-machine cache or snapshot entry",
                                              "per-machine cache and snapshot entries");
        if (edge.Unclaimed.Length == 0)
            return $"""

                # Nothing else at the top level of either profile that this script has no opinion about
                # ({passed} passed over).

                """;

        bool one = edge.Unclaimed.Length == 1;
        string count = Counted(edge.Unclaimed.Length, "entry", "entries");
        string sits = one ? "sits" : "sit";
        string appear = one ? "appears" : "appear";
        string it = one ? "it" : "them";
        var sb = new StringBuilder();
        sb.AppendLine($"""

            # AND WHAT THIS SCRIPT SAYS NOTHING ABOUT. {count} {sits} at the top level of one of these two
            # profiles and {appear} in none of the rows above - not because {it} {(one ? "is" : "are")} safe
            # to share, and not because {it} {(one ? "is" : "are")} not, but because nothing here has an
            # opinion about {it}. {(one ? "It is" : "They are")} named so that you can have one.
            # {passed} {(edge.Ignored == 1 ? "was" : "were")} passed over and {(edge.Ignored == 1 ? "is" : "are")} not listed.
            #
            """);
        foreach (string name in edge.Unclaimed.Take(show)) sb.AppendLine($"#   {name}");
        if (edge.Unclaimed.Length > show)
            sb.AppendLine($"#   ... and {edge.Unclaimed.Length - show} more");
        return sb.ToString();
    }

    private static string Footer(Plan plan) => EdgeNote(plan) + $$"""

        Note ''
        if ($Apply) {
          Note ("done: " + (Count $Acted 'change' 'changes') + ".")
          Note 'The originals are beside their links as <name>.pre-link-<stamp>. Check a Claude Code'
          Note 'session in each profile before you delete any of them.'
        } else {
          Note 'Nothing was changed. Re-run with -Apply once the plan above reads right.'
        }
        Note ''

        # UNDO - for each entry you linked, remove the link and put the original back. The junction is
        # removed with Remove-Item, which deletes the LINK and not the tree it points at; that is only
        # true while it is still a reparse point, so do not run this after replacing a link with a copy.
        #
        #   $stamp = '<the stamp printed above>'
        #   foreach ($n in @({{string.Join(", ", plan.Acting.Select(s => $"'{s.Entry.Name}'"))}})) {
        #     $link = Join-Path '{{Quote(plan.SecondaryDir)}}' $n
        #     $aside = $link + '.pre-link-' + $stamp
        #     if (Test-Path -LiteralPath $link) { Remove-Item -LiteralPath $link -Force -Recurse:$false }
        #     if (Test-Path -LiteralPath $aside) { Move-Item -LiteralPath $aside -Destination $link }
        #   }

        """;

    /// <summary>A path into a single-quoted PowerShell literal, where the only escape is a doubled
    /// quote — no backslash rule to get wrong, which is why every path here is emitted single-quoted.</summary>
    private static string Quote(string s) => s.Replace("'", "''");

    private static string Capital(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
