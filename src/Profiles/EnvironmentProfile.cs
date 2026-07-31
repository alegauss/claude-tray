using System.Runtime.InteropServices;

namespace ClaudeTray;

/// <summary>
/// The one place this app writes something outside its own settings file: the **user-scope**
/// <c>CLAUDE_CONFIG_DIR</c>, so a profile picked by hand applies to every Claude Code session on the
/// machine and not only to the terminal the tray starts (T145).
///
/// <para>This is a deliberate reversal of T143, which shipped a copyable <c>setx</c> command instead
/// and refused to run it. The objection then was that the variable reaches sessions the tray never
/// sees and keeps applying after the tray is gone — both still true, and both now the point. What
/// answers the objection is the second half of this type: <b>what the tray sets, the tray removes</b>.
/// The value that was there before is remembered in the tray's own settings and put back when the
/// feature is switched off or the pin is released, so the app never abandons a setting it no longer
/// manages.</para>
///
/// <para>User scope, never machine: the tray is not elevated, and a user value wins over a machine
/// one for that user's processes anyway.</para>
/// </summary>
internal static class EnvironmentProfile
{
    private const string Var = "CLAUDE_CONFIG_DIR";

    /// <summary>
    /// What the user environment has to say for <paramref name="configDir"/> to be the machine's
    /// profile — the dir itself, or <b>nothing at all</b>.
    ///
    /// <para><c>~/.claude</c> is never selectable by *setting* the variable: pointing it there makes
    /// Claude Code read <c>~/.claude/.claude.json</c> instead of the real <c>~/.claude.json</c>
    /// (measured, T136). The same three-way as <see cref="ClaudeAccount.ActionFor"/>, asked of a
    /// persistent environment rather than of one child process.</para>
    /// </summary>
    internal static string? ValueFor(string configDir) =>
        ClaudeAccount.SamePath(configDir, ClaudeAccount.HomeConfigDir) ? null : configDir;

    /// <summary>The user-scope value as it stands now (null when the variable is not set).</summary>
    internal static string? Current()
    {
        try { return Environment.GetEnvironmentVariable(Var, EnvironmentVariableTarget.User); }
        catch { return null; }
    }

    /// <summary>
    /// Make <paramref name="configDir"/> the environment's profile, remembering what was there the
    /// first time so <see cref="Restore"/> can undo it. Returns false if the write failed.
    /// </summary>
    internal static bool Adopt(Settings settings, string configDir)
    {
        if (configDir is not { Length: > 0 }) return true;
        string? before = settings.EnvironmentProfileOwned ? settings.EnvironmentProfileRestore : Current();
        if (!Write(ValueFor(configDir))) return false;
        settings.EnvironmentProfileRestore = before;
        settings.EnvironmentProfileOwned = true;
        return true;
    }

    /// <summary>Put back whatever was there before the tray took the variable over. A no-op — and a
    /// success — when the tray never took it over.</summary>
    internal static bool Restore(Settings settings)
    {
        if (!settings.EnvironmentProfileOwned) return true;
        if (!Write(settings.EnvironmentProfileRestore)) return false;
        settings.EnvironmentProfileRestore = null;
        settings.EnvironmentProfileOwned = false;
        return true;
    }

    /// <summary>Set the user-scope variable, or remove it when <paramref name="value"/> is null, and
    /// tell the shell — without the broadcast, Explorer keeps the environment block it built at logon
    /// and nothing launched from the Start menu sees the change until the next sign-out.</summary>
    private static bool Write(string? value)
    {
        try
        {
            // .NET removes the variable when the value is null or empty — which is exactly the
            // "select ~/.claude" case, not an error.
            Environment.SetEnvironmentVariable(Var, value, EnvironmentVariableTarget.User);
            Broadcast();
            return true;
        }
        catch { return false; }
    }

    private const int WM_SETTINGCHANGE = 0x001A;
    private const int SMTO_ABORTIFHUNG = 0x0002;
    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, int msg, IntPtr wParam, string lParam, int flags, int timeoutMs, out IntPtr result);

    /// <summary>Ask every top-level window to re-read the environment. Timed out rather than sent
    /// blocking: one hung app must not freeze the tray's menu click.</summary>
    private static void Broadcast()
    {
        try { SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 5000, out _); }
        catch { /* the value is written either way; the broadcast is the courtesy */ }
    }
}
