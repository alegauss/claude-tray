namespace ClaudeTray;

/// <summary>
/// Whether Claude Code is on this machine at all, told apart from "installed but signed out" (T415).
///
/// <para>Both arrive as the same reading — no <c>.credentials.json</c> — and every sentence the app had
/// for it assumed the install: open Claude Code, run <c>/login</c>, and a menu entry that starts a
/// terminal answering <c>'claude' is not recognized</c>. A Store install is the first time somebody can
/// meet this app without having come from Claude Code, so that is no longer a safe assumption.</para>
///
/// <para>Installed is decided by what Claude Code leaves behind, and any one signal is enough: the
/// profile's config dir, <c>~/.claude</c>, <c>~/.claude.json</c>, or a <c>claude</c> on PATH (with the
/// native installer's <c>~/.local/bin</c>, which a tray started before the PATH change does not see).
/// Missing is only ever the absence of all of them, so a doubt keeps today's signed-out wording.</para>
/// </summary>
internal static class ClaudeInstall
{
    /// <summary>The official install page — the one thing that helps someone with no Claude Code.</summary>
    public const string InstallUrl = "https://code.claude.com/docs/en/setup";

    private static readonly string[] Launchers = { "claude.exe", "claude.cmd", "claude.ps1", "claude" };

    /// <summary>True when nothing on this machine says Claude Code was ever installed.</summary>
    public static bool Missing(string? configDir = null)
    {
        try
        {
            return Missing(configDir ?? ClaudeAccount.ConfigDir, ClaudeAccount.HomeConfigDir,
                           Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           Environment.GetEnvironmentVariable("PATH"));
        }
        catch { return false; }
    }

    /// <summary>The decision over explicit inputs, so <c>--selftest</c> can ask it of a temp tree.</summary>
    internal static bool Missing(string configDir, string homeConfigDir, string userProfile, string? path) =>
        !Directory.Exists(configDir)
        && !Directory.Exists(homeConfigDir)
        && !File.Exists(Path.Combine(userProfile, ".claude.json"))
        && !OnPath(path, userProfile);

    private static bool OnPath(string? path, string userProfile)
    {
        IEnumerable<string> dirs = (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(Path.Combine(userProfile, ".local", "bin"));
        foreach (string dir in dirs)
        {
            try
            {
                string d = Environment.ExpandEnvironmentVariables(dir.Trim().Trim('"'));
                if (Launchers.Any(l => File.Exists(Path.Combine(d, l)))) return true;
            }
            catch { /* a malformed PATH entry names nothing */ }
        }
        return false;
    }
}
