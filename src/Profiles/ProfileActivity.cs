namespace ClaudeTray;

/// <summary>
/// Which profile is being worked in *right now*, read from the only evidence that says so without
/// asking anybody: Claude Code appends to a transcript inside its own config dir on every turn, so the
/// newest <c>projects\**\*.jsonl</c> write under a config dir is when that profile last did something.
/// The icon can then follow the profile you are actually typing in, instead of waiting for a click on
/// the Profile submenu (T126).
///
/// <para><b>Timestamps only — nothing is opened.</b> This reads directory entries (name + mtime) and
/// never a byte of a transcript, so it learns "a turn landed here" and nothing whatever about what was
/// said. Measured on the development machine: ~600 transcripts in ~20ms per config dir, and it runs on
/// the usage poll's cadence (60s by default) rather than continuously.
///
/// <para><b>And only while the directories are separate.</b> A junction can put two profiles behind one
/// <c>projects</c> tree, and then the newest write is the same fact twice — evidence about neither of
/// them. <see cref="MarkShared"/> resolves the links and takes both out of the running, so the icon
/// stays where the user put it instead of being pushed between them by the order they were walked in
/// (T365).</para>
///
/// <para>That cadence is the point. Dropping T101 settled that the tray must not keep a
/// <see cref="TranscriptTail"/> running for a whole session to power an ambient nicety — the tail is
/// window-owned, so a closed Statistics window watches nothing. A per-poll mtime probe keeps that
/// property: no watcher, no cursors, no reading, nothing resident between polls.</para>
/// </summary>
internal static class ProfileActivity
{
    /// <summary>
    /// How recent a profile's last turn must be to mean "this is where I am working". Long enough to
    /// span reading a diff or thinking between prompts, short enough that yesterday's profile never
    /// takes the icon from today's.
    /// </summary>
    public const double FollowWindowSeconds = 30 * 60;

    /// <summary>How far ahead of the clock a write may be stamped and still be believed. A turn cannot
    /// land in the future, so a transcript that says it did is a copied file or a skewed clock — and
    /// without this it would out-rank every genuine turn and pin the icon for as long as it existed.
    /// The tolerance covers ordinary skew between a network share and the local clock.</summary>
    public const double MaxFutureSkewSeconds = 120;

    /// <summary>
    /// When the newest turn under <paramref name="configDir"/> landed, in unix seconds; 0 when that
    /// profile has no transcripts at all (a config dir that was just created, or never used).
    /// </summary>
    public static double LastTurnUnix(string configDir)
    {
        string projects = ProjectsDir(configDir);
        if (!Directory.Exists(projects)) return 0;

        double newest = 0;
        foreach (FileInfo f in SafeWalk.Files(projects, "*.jsonl"))
        {
            double unix = new DateTimeOffset(f.LastWriteTimeUtc).ToUnixTimeSeconds();
            if (unix > newest) newest = unix;
        }
        return newest;
    }

    /// <summary>A profile's transcripts — <c>&lt;config dir&gt;\projects</c>, where an empty config dir
    /// means the default <c>~/.claude</c>, exactly as the launch path reads it.</summary>
    public static string ProjectsDir(string configDir) => Path.Combine(
        configDir.Length > 0 ? configDir : ContextScanner.DefaultClaudeRoot, "projects");

    /// <summary>
    /// The same directory with its links followed, which is what says whether two profiles are reading
    /// one tree (T365). Two shapes reach it: the junction on <c>projects</c> itself, and the one on the
    /// config dir above it — both real, and neither visible in the path a profile carries.
    ///
    /// <para>Never empty and never null: a path that resolves to nothing is its own answer, so a
    /// profile with no transcripts yet is compared as itself rather than joining every other profile
    /// that has none. Unreadable is the same case — this decides who is <i>not</i> followed, so it
    /// fails towards two profiles being two.</para>
    /// </summary>
    public static string ResolvedProjectsDir(string configDir)
    {
        string projects = ProjectsDir(configDir);
        string? target = LinkTarget(projects);
        // The config dir itself, when `projects` is an ordinary folder inside a linked one.
        if (target is null && LinkTarget(configDir.Length > 0 ? configDir : ContextScanner.DefaultClaudeRoot)
            is { } root) target = Path.Combine(root, "projects");

        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(target ?? projects)); }
        catch { return target ?? projects; }
    }

    /// <summary>Where a directory link points, following it to the end; null when the path is not a
    /// link, does not exist, or cannot be read.</summary>
    private static string? LinkTarget(string dir)
    {
        try { return Directory.ResolveLinkTarget(dir, returnFinalTarget: true)?.FullName; }
        catch { return null; }
    }

    /// <summary>One profile's last turn, for both the pick and the <c>--profiles</c> readout.
    /// <paramref name="Tree"/> is the resolved directory the reading came from, and
    /// <paramref name="SharesTree"/> that another profile in the same read came from it too.</summary>
    public readonly record struct Reading(
        ClaudeInfo Profile, double LastTurnUnix, bool Followable, string Tree = "", bool SharesTree = false);

    /// <summary>
    /// Read every profile's last turn, in the order given. A profile is <c>Followable</c> only when the
    /// icon could actually say something about it: it has to draw on the subscription (an API-key,
    /// Bedrock or Vertex profile has no quota window to read — T124) and it has to have credentials on
    /// disk, or following it would replace a real percentage with "not signed in" for a profile the
    /// user never asked to see.
    /// </summary>
    public static List<Reading> Read(IReadOnlyList<ClaudeInfo> profiles)
    {
        var readings = new List<Reading>(profiles.Count);
        foreach (ClaudeInfo p in profiles)
            readings.Add(new Reading(p, LastTurnUnix(p.ConfigDir),
                p.CountsAgainstSubscription && p.HasCredentialsFile, ResolvedProjectsDir(p.ConfigDir)));
        return MarkShared(readings);
    }

    /// <summary>
    /// Mark every reading whose tree another reading also came from (T365). Two profiles behind one
    /// directory report the same last turn to the second, so neither one is evidence of where the work
    /// is — and worse than a tie: <see cref="Read"/> walks the profiles in order, so a turn landing
    /// between two walks makes whichever was scanned second look newer, which is never the profile the
    /// icon is already on.
    ///
    /// <para>Pure over the list, and separate from the walk that fills it, so the rule can be driven
    /// without a config dir on disk.</para>
    /// </summary>
    public static List<Reading> MarkShared(List<Reading> readings)
    {
        for (int i = 0; i < readings.Count; i++)
        {
            bool shared = false;
            for (int j = 0; j < readings.Count && !shared; j++)
                shared = j != i
                         && string.Equals(readings[i].Tree, readings[j].Tree, StringComparison.OrdinalIgnoreCase);
            readings[i] = readings[i] with { SharesTree = shared };
        }
        return readings;
    }

    /// <summary>
    /// Whether auto-follow can say anything at all on this machine (T371): it needs at least one
    /// followable profile whose <c>projects</c> tree no other profile is reading.
    ///
    /// <para><b>Why this is a question a surface asks.</b> T365's refusal is correct and silent, and T367
    /// then shipped the thing that <em>creates</em> the shape it refuses — <c>projects</c> is the first
    /// entry in the linking catalogue. So a successful link turns <see cref="Settings.FollowActiveProfile"/>
    /// into a switch with no effect, and on the machine this was found on it had been that for weeks with
    /// only <c>--profiles</c> saying so. The toggle's own description can answer it instead.</para>
    ///
    /// <para>It resolves the links and nothing else: no transcript directory is walked, because the
    /// question is which trees are distinct and not when anything last happened. <see cref="Read"/> is the
    /// one that costs a sweep, and a settings page must not pay for it to write one sentence.</para>
    /// </summary>
    public static bool CanFollow(IReadOnlyList<ClaudeInfo> profiles)
    {
        // Through MarkShared rather than a comparison of its own: two readers of "these two are one tree"
        // is how a rule comes to hold on one surface and not the other, and the timestamps it does not
        // need are exactly the part this skips.
        var probes = new List<Reading>(profiles.Count);
        foreach (ClaudeInfo p in profiles)
            probes.Add(new Reading(p, 0, p.CountsAgainstSubscription && p.HasCredentialsFile,
                ResolvedProjectsDir(p.ConfigDir)));
        return MarkShared(probes).Any(r => r.Followable && !r.SharesTree);
    }

    /// <summary>
    /// The profile the icon should follow, or <c>null</c> to leave it where it is: the followable
    /// profile with the newest turn, provided that turn is inside <see cref="FollowWindowSeconds"/> and
    /// newer than <paramref name="floorUnix"/>.
    ///
    /// <para><paramref name="floorUnix"/> is what makes a manual choice stick. Choosing a profile by
    /// hand stamps the floor with the moment of the click, so whichever profile happens to hold the
    /// newest transcript cannot take the icon straight back — auto-follow resumes only once a turn lands
    /// elsewhere *after* that click. An icon that overrules the user is worse than one that never moves.
    /// </para>
    /// </summary>
    public static ClaudeInfo? Pick(List<Reading> readings, double nowUnix, double floorUnix)
    {
        Reading? best = null;
        foreach (Reading r in readings)
        {
            if (!Live(r, nowUnix)) continue;
            if (r.LastTurnUnix <= floorUnix) continue;
            if (best is null || r.LastTurnUnix > best.Value.LastTurnUnix) best = r;
        }
        return best?.Profile;
    }

    /// <summary>Whether this reading is evidence of somebody working in that profile now: a real
    /// timestamp, inside the follow window, not stamped in the future, and taken from a directory no
    /// other profile is reading (T365) — a shared tree carries no evidence about which of them it
    /// belongs to, and no threshold on the comparison can supply what was never measured.</summary>
    public static bool Live(Reading r, double nowUnix) =>
        r.Followable
        && !r.SharesTree
        && r.LastTurnUnix > 0
        && nowUnix - r.LastTurnUnix <= FollowWindowSeconds
        && r.LastTurnUnix - nowUnix <= MaxFutureSkewSeconds;
}
