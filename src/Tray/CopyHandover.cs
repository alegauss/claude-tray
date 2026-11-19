using System.Diagnostics;
using Microsoft.Win32;

namespace ClaudeTray;

/// <summary>
/// What a launch does when another copy of the app already holds the single-instance mutex (T466).
///
/// <para>The Store package and a winget or GitHub copy share <c>Local\ClaudeTray.SingleInstance</c>, and
/// both start at sign-in. Whichever came up second used to hand the click to the first and leave, so a
/// person who moved to the Store kept running the old build without knowing it — measured on the
/// developer's own machine, where the package sat installed for nineteen days and never ran once. Now the
/// second launch looks at which build is running before it leaves, and says so when it is the other
/// kind.</para>
///
/// <para>Nothing is uninstalled. The data needs no move either: both builds read and write
/// <c>%LocalAppData%\ClaudeTray</c>, because that folder already exists when the package first runs and
/// the package then writes it in place.</para>
/// </summary>
internal static class CopyHandover
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "ClaudeTray";

    /// <summary>What a launch that found the mutex held should offer.</summary>
    public enum Offer
    {
        /// <summary>The same kind of build is running, or which one could not be read: open its window.</summary>
        None,
        /// <summary>The package found an unpackaged copy: offer to take its place.</summary>
        TakeOver,
        /// <summary>An unpackaged copy found the package: offer to stop this copy starting with Windows.</summary>
        StopStartingThisCopy,
    }

    /// <summary>A path under <c>WindowsApps</c> is a package's; everything else is a winget or GitHub copy.</summary>
    public static bool IsPackagePath(string path) =>
        path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The offer for this launch. <paramref name="runningPath"/> is the running copy's exe, or null when it
    /// could not be read — and an unread path offers nothing, so a failure can only ever fall back to the
    /// behaviour this replaced. <paramref name="thisStartsWithWindows"/> keeps the unpackaged side quiet
    /// when there is nothing to stop: a copy opened by hand beside the package just opens its window.
    /// </summary>
    public static Offer Decide(bool thisPackaged, string? runningPath, bool thisStartsWithWindows)
    {
        if (runningPath is null) return Offer.None;
        bool runningPackaged = IsPackagePath(runningPath);
        if (thisPackaged && !runningPackaged) return Offer.TakeOver;
        if (!thisPackaged && runningPackaged && thisStartsWithWindows) return Offer.StopStartingThisCopy;
        return Offer.None;
    }

    /// <summary>Whether a <c>Run</c> value launches <paramref name="exe"/>: quoted or not, any case.</summary>
    public static bool RunValueLaunches(string? value, string exe) =>
        value is { Length: > 0 } &&
        string.Equals(value.Trim().Trim('"'), exe, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Settle a launch that found the mutex held. True when this process now owns the mutex and should go
    /// on to start the tray; false when it should hand the click to the running copy and leave.
    /// </summary>
    public static bool Resolve(Mutex instance)
    {
        Process? running = Running(out string? path);
        Offer offer = Decide(PackageIdentity.IsPackaged, path, !PackageIdentity.IsPackaged && StartupManager.IsEnabled());
        switch (offer)
        {
            case Offer.TakeOver when running is not null && path is not null:
                if (Ask(L.T("dialog.handover.takeOver", path)) && TakeOver(running, path, instance)) return true;
                return false;
            case Offer.StopStartingThisCopy:
                if (Ask(L.T("dialog.handover.stopStarting"))) StartupManager.SetEnabled(false);
                return false;
            default:
                return false;
        }
    }

    private static bool Ask(string question) =>
        MessageBox.Show(question, L.T("dialog.appName"), MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            == DialogResult.Yes;

    /// <summary>
    /// The other ClaudeTray process and its exe, or null when there is none or its path is unreadable.
    /// The packaged exe has the same name, so the name finds both builds and the path tells them apart.
    /// </summary>
    private static Process? Running(out string? path)
    {
        path = null;
        int self = Environment.ProcessId;
        string? own = Environment.ProcessPath;
        foreach (Process p in Process.GetProcessesByName("ClaudeTray"))
        {
            if (p.Id == self) continue;
            try
            {
                string? exe = p.MainModule?.FileName;
                // A `--second-tray` check built from the same tree is not "another copy" of anything.
                if (exe is null || string.Equals(exe, own, StringComparison.OrdinalIgnoreCase)) continue;
                path = exe;
                return p;
            }
            catch { /* exited, or not ours to read: keep looking */ }
        }
        return null;
    }

    /// <summary>
    /// Take the running copy's place: stop it starting with Windows, end it, and take the mutex it held.
    /// Its <c>Run</c> value is removed only when it launches that copy's exe, so an entry somebody pointed
    /// elsewhere is left alone.
    /// </summary>
    private static bool TakeOver(Process running, string path, Mutex instance)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is not null && RunValueLaunches(key.GetValue(RunValue) as string, path))
                key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
        catch { /* a Run entry left behind is reported by the next sign-in, not worth failing the switch */ }

        // A tray has no window to close and older builds listen for nothing, so the end is a kill. What it
        // writes is append-only and small, and the person just said yes to this.
        try { running.Kill(); running.WaitForExit(10_000); }
        catch { /* already gone */ }

        try { return instance.WaitOne(TimeSpan.FromSeconds(10)); }
        catch (AbandonedMutexException) { return true; }   // the killed copy never released it: ours now
    }
}
