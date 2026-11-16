using System.Diagnostics;
using System.Text;

namespace ClaudeTray;

/// <summary>
/// Claude Code run inside WSL, seen from Windows (T394): a config dir in a distro's Linux home, read
/// through <c>\\wsl.localhost\&lt;distro&gt;\…</c> like any other folder.
///
/// <para><b>Only running distros.</b> Which distros run comes from <c>wsl.exe -l --running</c>, which
/// answers without starting anything; the share of a distro that is not listed is never touched. Opening
/// that share is how Windows boots a stopped distro, and a tray that did it on a timer would be starting a
/// Linux VM every few minutes for a reading nobody asked for.</para>
///
/// <para><b>Cheap enough for discovery.</b> Discovery runs on every menu open and every poll, so the list of
/// running distros is kept for <see cref="CacheSeconds"/>, and the process is started with no window — this
/// app has no console of its own, so a console child would otherwise open one on the user's desk.</para>
///
/// <para><b>Never a Windows variable.</b> A profile here is selected by being inside the distro, not by
/// <c>CLAUDE_CONFIG_DIR</c> on the Windows side: writing a <c>\\wsl.localhost</c> path into the user
/// environment would point every Windows <c>claude</c> into a Linux home. So the environment switch skips
/// these, and launching one goes through <c>wsl.exe</c>.</para>
/// </summary>
internal static class WslProfiles
{
    private const string Share = @"\\wsl.localhost\";
    private const string LegacyShare = @"\\wsl$\";
    private const int CacheSeconds = 60;

    /// <summary>How long a wedged <c>wsl.exe</c> is left alone after it timed out, before it is asked again.
    /// A WSL that hangs at boot stays hung, and asking every minute leaves one more stuck child each time.</summary>
    private static readonly TimeSpan BackoffAfterTimeout = TimeSpan.FromMinutes(10);

    /// <summary>How long the very first caller waits for the first listing. A healthy WSL answers well within
    /// it; a wedged one must not hold up the tray's startup, which is where it first called this.</summary>
    private static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(2);

    private static readonly object Gate = new();
    private static IReadOnlyList<string> _running = Array.Empty<string>();
    private static DateTime _retryAt = DateTime.MinValue;
    private static bool _everStarted;
    private static Task? _inFlight;

    /// <summary>What asks for the listing: <see cref="ReadRunning"/>, or a reader <c>--selftest</c> puts in
    /// its place to stand for a <c>wsl.exe</c> that never answers, without touching the machine's WSL.</summary>
    internal static Func<(IReadOnlyList<string>? Names, bool TimedOut)> Reader = ReadRunning;

    /// <summary>The background listing in flight, or null — so a check can wait for it to land.</summary>
    internal static Task? InFlight { get { lock (Gate) return _inFlight; } }

    /// <summary>Back to a process that has never asked, or (<paramref name="keepNames"/>) to one whose cached
    /// answer is due for a refresh now. For <c>--selftest</c>, which restores <see cref="Reader"/> itself.</summary>
    internal static void ResetForTest(bool keepNames = false)
    {
        lock (Gate)
        {
            if (!keepNames) { _running = Array.Empty<string>(); _everStarted = false; }
            _retryAt = DateTime.MinValue;
        }
    }

    /// <summary>When the next listing may be asked for.</summary>
    internal static DateTime RetryAt { get { lock (Gate) return _retryAt; } }

    /// <summary>A config dir inside a distro, split into the distro and the Linux path it names there,
    /// or false for a Windows path.</summary>
    public static bool TryParse(string configDir, out string distro, out string linuxPath)
    {
        distro = linuxPath = "";
        string rest;
        if (configDir.StartsWith(Share, StringComparison.OrdinalIgnoreCase)) rest = configDir[Share.Length..];
        else if (configDir.StartsWith(LegacyShare, StringComparison.OrdinalIgnoreCase)) rest = configDir[LegacyShare.Length..];
        else return false;

        int slash = rest.IndexOfAny(new[] { '\\', '/' });
        if (slash <= 0) return false;
        distro = rest[..slash];
        linuxPath = "/" + rest[(slash + 1)..].Replace('\\', '/').TrimEnd('/');
        return true;
    }

    public static bool IsWsl(string configDir) => TryParse(configDir, out _, out _);

    /// <summary>A Linux path, as Windows reaches it inside the same distro as <paramref name="within"/> —
    /// which is what a project path recorded by Claude Code run in WSL needs before Windows can open it. Any
    /// other path, or a <paramref name="within"/> outside WSL, comes back unchanged.</summary>
    public static string ToWindows(string within, string path)
    {
        if (!path.StartsWith('/') || !TryParse(within, out string distro, out _)) return path;
        return Share + distro + path.Replace('/', '\\');
    }

    /// <summary>The Linux user whose home a config dir sits in, or null for one outside <c>/home</c> —
    /// <c>/root</c> is root's, and <c>wsl.exe</c> without <c>-u</c> is the distro's default user.</summary>
    public static string? UserOf(string linuxPath)
    {
        if (linuxPath.StartsWith("/root/", StringComparison.Ordinal) || linuxPath == "/root") return "root";
        string[] parts = linuxPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0] == "home" ? parts[1] : null;
    }

    /// <summary>
    /// The distros running now, by name — cached, and empty where WSL is not installed.
    ///
    /// <para><b>Never waits on <c>wsl.exe</c>.</b> The listing is refreshed on a background task and the
    /// caller gets the last answer at once; only the process's very first call waits, and only for
    /// <see cref="FirstWait"/>. Discovery runs on the UI thread — in the tray's constructor, on every menu
    /// open and poll — so a WSL wedged since boot (one stuck <c>wsl.exe -l</c> per call) used to keep the
    /// tray from ever creating its icon, and with only T426's bound it would still freeze the UI for the
    /// bound every minute. A timed-out listing keeps the previous answer and backs off.</para>
    /// </summary>
    public static IReadOnlyList<string> Running()
    {
        Task? first = null;
        lock (Gate)
        {
            if (_inFlight is null && DateTime.UtcNow >= _retryAt)
            {
                _inFlight = Task.Run(Refresh);
                if (!_everStarted) first = _inFlight;
                _everStarted = true;
            }
        }
        if (first is not null)
        {
            try { first.Wait(FirstWait); } catch { /* Refresh swallows its own failures */ }
        }
        lock (Gate) return _running;
    }

    private static void Refresh()
    {
        IReadOnlyList<string>? names = null;
        bool timedOut = false;
        try { (names, timedOut) = Reader(); } catch { names = Array.Empty<string>(); }
        lock (Gate)
        {
            if (names is not null) _running = names;
            _retryAt = DateTime.UtcNow + (timedOut ? BackoffAfterTimeout : TimeSpan.FromSeconds(CacheSeconds));
            _inFlight = null;
        }
    }

    /// <summary>The listing, or null names when it timed out — so the previous answer is kept rather than
    /// replaced by an empty one that would drop a WSL profile from the menu for a hiccup.</summary>
    private static (IReadOnlyList<string>? Names, bool TimedOut) ReadRunning()
    {
        string wsl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");
        if (!File.Exists(wsl)) return (Array.Empty<string>(), false);
        try
        {
            // Bounded by the wait, not by the child's exit (T426): reading to the end first is what let a
            // wedged wsl.exe freeze the menu that asked for this.
            ChildRun.Result r = ChildRun.Run(new ProcessStartInfo(wsl, "-l --running --quiet")
            {
                CreateNoWindow = true,
                // wsl.exe writes its listing as UTF-16, and read as anything else every second byte is a NUL.
                StandardOutputEncoding = Encoding.Unicode,
            }, TimeSpan.FromSeconds(10));
            if (r.TimedOut) return (null, true);
            return (r.ExitCode == 0 ? ParseList(r.Output) : Array.Empty<string>(), false);
        }
        catch { return (Array.Empty<string>(), false); }
    }

    /// <summary>The names in <c>wsl.exe -l --quiet</c>'s output, one per line, blanks and stray NULs
    /// dropped. Pure, so <c>--selftest</c> can feed it the shapes the tool has been seen to print.</summary>
    internal static List<string> ParseList(string output) =>
        output.Replace("\0", "").Split('\n')
              .Select(l => l.Trim().TrimStart('*').Trim())
              .Where(l => l.Length > 0)
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .ToList();

    /// <summary>
    /// The config dirs Claude Code would use inside each running distro: <c>~/.claude</c> for every home
    /// under <c>/home</c>, and root's. Candidates only — discovery still asks each one whether it holds a
    /// login, exactly as it asks a Windows folder.
    /// </summary>
    public static IEnumerable<string> Candidates()
    {
        foreach (string distro in Running())
        {
            string root = Share + distro;
            string home = Path.Combine(root, "home");
            string[] users;
            try { users = Directory.Exists(home) ? Directory.GetDirectories(home) : Array.Empty<string>(); }
            catch { users = Array.Empty<string>(); }
            foreach (string user in users) yield return Path.Combine(user, ".claude");
            yield return Path.Combine(root, "root", ".claude");
        }
    }

    /// <summary>
    /// What to run so <c>claude</c> starts inside the profile's distro, as its user, in its home — or with
    /// <c>CLAUDE_CONFIG_DIR</c> set inside Linux when the config dir is not that user's <c>~/.claude</c>.
    /// The command <paramref name="launch"/> is the one a Windows profile runs (<c>claude</c>, or the login
    /// form), unchanged.
    /// </summary>
    /// <remarks>Through <c>bash -lc</c>, because the native installer puts <c>claude</c> in
    /// <c>~/.local/bin</c>, which only a login shell adds to <c>PATH</c>: measured on a real distro,
    /// <c>wsl.exe -- claude</c> answers <c>command not found</c> and <c>bash -lc claude</c> starts it.</remarks>
    internal static string LaunchArguments(string configDir, string launch)
    {
        if (!TryParse(configDir, out string distro, out string linuxPath)) return launch;
        string? user = UserOf(linuxPath);
        string home = user is null ? "" : user == "root" ? "/root" : "/home/" + user;
        string env = linuxPath == home + "/.claude" ? "" : $"CLAUDE_CONFIG_DIR='{linuxPath}' ";
        return $"wsl.exe -d {distro}" + (user is null ? "" : $" -u {user}") + $" --cd ~ -- bash -lc \"{env}{launch}\"";
    }
}
