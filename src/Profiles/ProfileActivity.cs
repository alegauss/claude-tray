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

    /// <summary>One profile's last turn, for both the pick and the <c>--profiles</c> readout.</summary>
    public readonly record struct Reading(ClaudeInfo Profile, double LastTurnUnix, bool Followable);

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
                p.CountsAgainstSubscription && p.HasCredentialsFile));
        return readings;
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
    /// timestamp, inside the follow window, and not stamped in the future.</summary>
    public static bool Live(Reading r, double nowUnix) =>
        r.Followable
        && r.LastTurnUnix > 0
        && nowUnix - r.LastTurnUnix <= FollowWindowSeconds
        && r.LastTurnUnix - nowUnix <= MaxFutureSkewSeconds;
}
