using System.Runtime.InteropServices;

namespace ClaudeTray;

/// <summary>
/// A second launch asks the resident tray to open its window, instead of exiting with nothing to show.
///
/// <para>The single instance used to make a second launch silent: the Store's "Open" button, the Start
/// menu, a double-click on the exe — each found the mutex held and returned, and with the icon in the
/// notification area's overflow nothing on screen said the app was running at all. A named event is the
/// whole channel: the second process sets it and leaves, the resident one waits on it and opens the window
/// on its own UI thread.</para>
///
/// <para>The second process is the one the user just clicked, so it is the one holding the right to bring
/// a window forward. It hands that right on before setting the event, or the window would open behind
/// whatever had focus.</para>
/// </summary>
internal static class ShowRequest
{
    private const string Name = @"Local\ClaudeTray.Show";
    private const int AsfwAny = -1;

    // Held for the process lifetime: the wait is registered on it, and closing it would end the channel.
    private static EventWaitHandle? _event;

    /// <summary>Wait for a later launch's request, and run <paramref name="show"/> each time one arrives.
    /// The callback runs on the thread pool; marshalling to the UI thread is the caller's.</summary>
    public static void Listen(Action show)
    {
        _event = new EventWaitHandle(false, EventResetMode.AutoReset, Name);
        ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => show(), null, Timeout.Infinite,
                                               executeOnlyOnce: false);
    }

    /// <summary>Ask the resident tray to open its window. Best effort: a tray still starting up picks
    /// the request up as soon as it listens, and one that is leaving drops it.</summary>
    public static void Signal()
    {
        try
        {
            AllowSetForegroundWindow(AsfwAny);
            using var request = new EventWaitHandle(false, EventResetMode.AutoReset, Name);
            request.Set();
        }
        catch { /* the click that found the tray running is never worth an error dialog */ }
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
