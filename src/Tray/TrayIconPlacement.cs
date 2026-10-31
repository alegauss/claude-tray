using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClaudeTray;

/// <summary>
/// Whether this app's notification-area icon is shown on the taskbar or hidden in the overflow (T414).
///
/// <para>Windows 11 puts every new icon in the overflow, and the app cannot promote its own: there is no
/// API, by design. What it can do is read where Explorer put it. Each icon has a key under
/// <c>HKCU\Control Panel\NotifyIconSettings</c> naming the <c>ExecutablePath</c> it belongs to, and
/// <c>IsPromoted = 1</c> once the user moved it onto the taskbar. A missing value is the state of every
/// icon nobody has touched, so it reads as hidden.</para>
///
/// <para>The path is stored either literally or with a known-folder GUID in front of it
/// (<c>{6D809377-…}\WindowsApps\…</c> for Program Files), and every build that ever showed an icon has a
/// key of its own, so the match is on this process's own resolved path and nothing looser. No key at all
/// — Windows 10, or an icon Explorer has not recorded yet — answers null: unknown is not hidden, and a
/// hint that cannot be backed by the registry is not shown.</para>
/// </summary>
internal static class TrayIconPlacement
{
    private const string Root = @"Control Panel\NotifyIconSettings";

    /// <summary>The taskbar settings page, whose "Other system tray icons" list is where an icon is shown.</summary>
    public const string SettingsUri = "ms-settings:taskbar";

    /// <summary>True when this process's icon is in the overflow, false when it is on the taskbar, null when
    /// the registry does not say.</summary>
    public static bool? IsHidden()
    {
        try
        {
            using RegistryKey? root = Registry.CurrentUser.OpenSubKey(Root);
            if (root is null || Environment.ProcessPath is not { } exe) return null;
            var entries = new List<(string Path, object? Promoted)>();
            foreach (string name in root.GetSubKeyNames())
            {
                using RegistryKey? key = root.OpenSubKey(name);
                if (key?.GetValue("ExecutablePath") is string path)
                    entries.Add((path, key.GetValue("IsPromoted")));
            }
            return Classify(entries, exe, KnownFolder);
        }
        catch { return null; }
    }

    /// <summary>The decision, apart from the registry, so <c>--selftest</c> can ask it.</summary>
    internal static bool? Classify(IEnumerable<(string Path, object? Promoted)> entries, string exe,
                                   Func<Guid, string?> knownFolder)
    {
        foreach ((string path, object? promoted) in entries)
        {
            if (!string.Equals(Resolve(path, knownFolder), exe, StringComparison.OrdinalIgnoreCase)) continue;
            return promoted is not int on || on == 0;
        }
        return null;
    }

    /// <summary>A stored path with its known-folder GUID, if it has one, replaced by the folder.</summary>
    internal static string? Resolve(string stored, Func<Guid, string?> knownFolder)
    {
        if (!stored.StartsWith('{')) return stored;
        int close = stored.IndexOf('}');
        if (close < 0 || !Guid.TryParse(stored[..(close + 1)], out Guid id)) return null;
        return knownFolder(id) is { } folder ? folder + stored[(close + 1)..] : null;
    }

    private static string? KnownFolder(Guid id)
    {
        if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out IntPtr ptr) != 0) return null;
        try { return Marshal.PtrToStringUni(ptr); }
        finally { Marshal.FreeCoTaskMem(ptr); }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
}
