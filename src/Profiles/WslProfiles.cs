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

    private static readonly object Gate = new();
    private static IReadOnlyList<string> _running = Array.Empty<string>();
    private static DateTime _readAt = DateTime.MinValue;

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

    /// <summary>The Linux user whose home a config dir sits in, or null for one outside <c>/home</c> —
    /// <c>/root</c> is root's, and <c>wsl.exe</c> without <c>-u</c> is the distro's default user.</summary>
    public static string? UserOf(string linuxPath)
    {
        if (linuxPath.StartsWith("/root/", StringComparison.Ordinal) || linuxPath == "/root") return "root";
        string[] parts = linuxPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0] == "home" ? parts[1] : null;
    }

    /// <summary>The distros running now, by name — cached, and empty where WSL is not installed.</summary>
    public static IReadOnlyList<string> Running()
    {
        lock (Gate)
        {
            if ((DateTime.UtcNow - _readAt).TotalSeconds < CacheSeconds) return _running;
            _readAt = DateTime.UtcNow;
            _running = ReadRunning();
            return _running;
        }
    }

    private static IReadOnlyList<string> ReadRunning()
    {
        string wsl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");
        if (!File.Exists(wsl)) return Array.Empty<string>();
        try
        {
            using var p = Process.Start(new ProcessStartInfo(wsl, "-l --running --quiet")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true,
                // wsl.exe writes its listing as UTF-16, and read as anything else every second byte is a NUL.
                StandardOutputEncoding = Encoding.Unicode,
            });
            if (p is null) return Array.Empty<string>();
            string output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(10_000)) { try { p.Kill(); } catch { /* finished meanwhile */ } return Array.Empty<string>(); }
            return p.ExitCode == 0 ? ParseList(output) : Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
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
    internal static string LaunchArguments(string configDir, string launch)
    {
        if (!TryParse(configDir, out string distro, out string linuxPath)) return launch;
        string? user = UserOf(linuxPath);
        string home = user is null ? "" : user == "root" ? "/root" : "/home/" + user;
        string env = linuxPath == home + "/.claude" ? "" : $"env CLAUDE_CONFIG_DIR=\"{linuxPath}\" ";
        return $"wsl.exe -d {distro}" + (user is null ? "" : $" -u {user}") + $" --cd ~ -- {env}{launch}";
    }
}
