using System.Text;

namespace ClaudeTray;

/// <summary>
/// <c>--statusline</c>: the command Claude Code's status line runs (T390). Reads the payload on stdin, files
/// its two rate-limit windows for the tray under the config dir this process inherited, prints one line
/// for the bar and exits.
///
/// <para>It runs on every status refresh, so it does nothing else: no account read, no window, no
/// network. A payload with no <c>rate_limits</c> — before the session's first response, or on an API-key
/// login — prints nothing and writes nothing, and still exits 0, because a non-zero exit is a status bar
/// showing an error to somebody who is only typing.</para>
/// </summary>
internal static class StatusLineCli
{
    public static int Run()
    {
        string json;
        try
        {
            using var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            json = stdin.ReadToEnd();
        }
        catch { return 0; }

        if (StatusLineFeed.Parse(json, DateTimeOffset.UtcNow.ToUnixTimeSeconds()) is not { } reading) return 0;

        // ClaudeAccount.ConfigDir honours CLAUDE_CONFIG_DIR, which Claude Code passes on to the command it
        // runs — so a session under a second profile files under that profile's directory.
        StatusLineFeed.Write(ClaudeAccount.ConfigDir, reading);
        Console.Write(StatusLineFeed.Line(reading));
        return 0;
    }
}
