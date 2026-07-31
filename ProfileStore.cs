using System.Security.Cryptography;
using System.Text;

namespace ClaudeTray;

/// <summary>
/// Where the tray keeps everything it derives from **one** Claude Code profile: the quota readings, the
/// folded hourly aggregate, the reset log, the context history and the nudge ledger.
///
/// <para>Every one of those was a single file under <c>%LocalAppData%\ClaudeTray</c>, which is right only
/// while there is one profile. Fed two, they silently mix: profile B's quota readings land in the same
/// series as profile A's, so A's burn projection, its activity shape and its week-over-week comparison
/// are computed over usage that was never A's — a wrong chart that reads like a bad projection rather
/// than like a bug. So each profile gets its own directory, and the stores take a key.</para>
///
/// <para>The key is the **account** where there is one (two config dirs holding the same login are the
/// same series), and a digest of the config dir's path where there isn't — an unauthenticated profile
/// still accumulates context history, and it must not share a bucket with another one.</para>
/// </summary>
internal static class ProfileStore
{
    /// <summary>Parent of the per-profile directories. Kept beside the flat files it supersedes, so
    /// nothing outside <c>%LocalAppData%\ClaudeTray</c> is involved.</summary>
    private static string Root => Path.Combine(Settings.DataDir, "profiles");

    /// <summary>
    /// The files that belong to one profile and were, until now, one per machine.
    ///
    /// <para>Two stores are deliberately **not** here. <c>context-cache.json</c> and
    /// <c>context-usage.json</c> are keyed by absolute file path plus a size+mtime fingerprint, so an
    /// entry for one profile's <c>CLAUDE.md</c> cannot be mistaken for another's — sharing them costs
    /// nothing and saves rescanning what two profiles genuinely have in common. <c>settings.json</c> is
    /// the tray's own configuration, which is per machine by definition.</para>
    /// </summary>
    private static readonly string[] PerProfileFiles =
    {
        "usage-history.jsonl",
        "usage-hourly.jsonl",
        "context-history.jsonl",
        "context-nudges.json",
        "reset-events.log",
        "activity-profile.json",
    };

    /// <summary>
    /// The stable key for a profile: its account, else its config dir. Truncated to 12 hex chars — long
    /// enough that a collision is not a practical concern, short enough that the path stays readable in
    /// a bug report.
    /// </summary>
    public static string KeyFor(ClaudeInfo profile) =>
        profile.AccountUuid is { Length: > 0 } uuid
            ? "acct-" + Digest(uuid.ToLowerInvariant())
            : "dir-" + Digest(profile.ConfigDir.TrimEnd('\\', '/').ToLowerInvariant());

    /// <summary>The directory for a profile key, created on demand. Falls back to the flat data dir if
    /// it cannot be created, so a denied ACL degrades to the old behaviour instead of losing readings.</summary>
    public static string DirFor(string key)
    {
        try
        {
            string dir = Path.Combine(Root, Sanitize(key));
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch { return Settings.DataDir; }
    }

    /// <summary>One file inside a profile's directory.</summary>
    public static string PathFor(string key, string fileName) => Path.Combine(DirFor(key), fileName);

    /// <summary>
    /// The profile whose numbers the tray is showing. Set once at startup (and whenever that choice
    /// changes) so every existing store call has a key to pass; the stores themselves never read it, so
    /// polling several profiles at once is a matter of passing different keys, not of changing them.
    /// </summary>
    public static string Monitored { get; private set; } = "";

    /// <summary>
    /// Adopt a profile as the monitored one, migrating the pre-profile flat files into it the first time.
    /// Safe to call repeatedly.
    /// </summary>
    public static void SetMonitored(ClaudeInfo profile)
    {
        Monitored = KeyFor(profile);
        Migrate(Monitored);
    }

    /// <summary>
    /// Move the flat <c>%LocalAppData%\ClaudeTray\*.jsonl</c> stores into a profile's directory, once.
    ///
    /// <para>Every installation that exists today has exactly one profile, so its accumulated history —
    /// up to eight days of quota readings, and the permanent hourly aggregate folded out of them, which
    /// cannot be rebuilt — belongs to that profile. Copying rather than deleting would double the
    /// hourly store on the next fold, and leaving it in place would make an older build and this one
    /// disagree about which file is authoritative; a move is the only option that keeps one truth.</para>
    ///
    /// <para>Best-effort per file: a locked or missing file is skipped, and an existing destination is
    /// never overwritten (that would mean the migration already ran).</para>
    /// </summary>
    private static void Migrate(string key)
    {
        try
        {
            string dir = DirFor(key);
            if (Path.GetFullPath(dir).Equals(Path.GetFullPath(Settings.DataDir), StringComparison.OrdinalIgnoreCase))
                return;   // the fallback path — nothing to move, the files are already there

            foreach (string name in PerProfileFiles)
            {
                string from = Path.Combine(Settings.DataDir, name);
                string to = Path.Combine(dir, name);
                try
                {
                    if (File.Exists(from) && !File.Exists(to)) File.Move(from, to);
                }
                catch { /* locked or vanished — the store just starts fresh for this profile */ }
            }
        }
        catch { /* migration is a convenience; a failure must not stop the app from starting */ }
    }

    // 12 hex chars of SHA-256. Not a security boundary — just a short, stable, filename-safe name.
    private static string Digest(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    private static string Sanitize(string key)
    {
        var sb = new StringBuilder(key.Length);
        foreach (char c in key)
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        return sb.Length > 0 ? sb.ToString() : "default";
    }
}
