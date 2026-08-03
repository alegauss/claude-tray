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
        if (_sampling) return _sampled;
        try { return Environment.GetEnvironmentVariable(Var, EnvironmentVariableTarget.User); }
        catch { return null; }
    }

    // ---- the sampled environment (T231) ----

    private static bool _sampling;
    private static string? _sampled;

    /// <summary>
    /// Answer every question below as if the user environment said <paramref name="configDir"/> — and
    /// never touch the registry again for the rest of the process.
    ///
    /// <para>The states worth looking at are the ones this machine is never in. T172 marks the profile
    /// the variable names when it is not the one on the icon, and T173/T174 report whether a write
    /// landed; both were built and shipped without their interesting state ever appearing on screen,
    /// because a developer's machine agrees with itself and the honest way to make it disagree is to
    /// write a different <c>CLAUDE_CONFIG_DIR</c> into that developer's own registry.</para>
    ///
    /// <para>Deliberately the *whole* variable rather than a lie told to one caller. The menu asks
    /// <see cref="Selected"/>, the read-out asks <see cref="EffectiveConfigDir"/>, the write path asks
    /// <see cref="Current"/> to decide whether there is anything to do — a fixture that answered one of
    /// them and not the others would certify a screen that cannot occur, which is the objection this
    /// idea carried. There is one variable, so there is one place to sample it.</para>
    ///
    /// <para>Which dir is a coherent one to name is <see cref="EnvironmentFixture"/>'s job, not this
    /// one's: it picks from the profiles actually on the machine, so the sampled environment can only
    /// select something the watch list can render.</para>
    /// </summary>
    internal static void Sample(string? configDir)
    {
        _sampling = true;
        _sampled = configDir;
    }

    /// <summary>Whether this process is answering off a sampled environment. Every surface that reports
    /// the variable says so — a read-out that quietly described a fixture would be the defect the
    /// fixture exists to catch, one level up.</summary>
    internal static bool IsSampled => _sampling;

    /// <summary>
    /// The config dir the **persistent environment** selects: the variable's value, or <c>~/.claude</c>
    /// when it is not set — which is what a bare <c>claude</c> falls back to.
    ///
    /// <para>Read rather than assumed, and deliberately not the tray's <c>MonitoredConfigDir</c>: that
    /// one is whose numbers the icon draws, and these two answer different questions (T172). They agree
    /// in the ordinary case and the difference is the entire signal when they do not.</para>
    /// </summary>
    internal static string EffectiveConfigDir() =>
        Current() is { Length: > 0 } v ? v : ClaudeAccount.HomeConfigDir;

    /// <summary>
    /// Which of <paramref name="profiles"/> <see cref="EffectiveConfigDir"/> names — null when the
    /// variable points at a folder this machine has no registered profile for, which is a real state
    /// and not an error: something other than the tray set it.
    ///
    /// <para>One implementation, so the tray menu and <c>--profiles</c> cannot disagree about it — the
    /// same rule <see cref="ClaudeAccount.PickMonitored"/> follows for the icon's profile.</para>
    /// </summary>
    internal static ClaudeInfo? Selected(List<ClaudeInfo> profiles) =>
        SelectedIn(profiles, EffectiveConfigDir());

    /// <summary>The same question asked of a config dir handed in rather than read off this machine —
    /// the seam the self-check drives, because the interesting states (the variable naming the other
    /// profile, or a folder no profile covers) are exactly the ones a developer's machine is not in.</summary>
    internal static ClaudeInfo? SelectedIn(List<ClaudeInfo> profiles, string configDir) =>
        profiles.FirstOrDefault(p => ClaudeAccount.SamePath(p.ConfigDir, configDir));

    /// <summary>
    /// Make <paramref name="configDir"/> the environment's profile, remembering what was there the
    /// first time so <see cref="Restore"/> can undo it.
    ///
    /// <para>Returns whether the write was <b>accepted</b>, not whether it landed: since T149 the write
    /// itself happens on the thread pool (see <see cref="Write"/>), so there is no result to wait for
    /// without re-freezing the click this exists to unfreeze. The bookkeeping below is therefore
    /// recorded up front. What that costs if the write ever does fail is one line in the settings file
    /// claiming a variable the tray does not actually own — and the only thing that line does is make
    /// <see cref="Restore"/> put back the value that is already there. The tray never abandons a
    /// setting it manages either way, which was the whole promise.</para>
    ///
    /// <para>What *is* now answered, just not here: the write reports how it went when it goes, through
    /// <see cref="Completed"/> and <see cref="Last"/> (T173). Returning "accepted" was harmless while
    /// nothing displayed the result; §XVI.2 put the effective profile on screen, and from then on a
    /// write that threw would be a screen quietly disagreeing with the machine.</para>
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

    /// <summary>
    /// Set the user-scope variable, or remove it when <paramref name="value"/> is null, and tell the
    /// shell — without the broadcast, Explorer keeps the environment block it built at logon and
    /// nothing launched from the Start menu sees the change until the next sign-out.
    ///
    /// <para><b>Never on the calling thread</b> (T149). Both halves of this are a broadcast to every
    /// top-level window on the machine: <see cref="Environment.SetEnvironmentVariable(string,string?,EnvironmentVariableTarget)"/>
    /// sends its own <c>WM_SETTINGCHANGE</c> before returning, and <see cref="Broadcast"/> sends a
    /// second one. Each sweep waits on every window in turn — measured at 129 ms and 486 ms on an
    /// *idle* machine, and seconds on a working one. Called straight from a menu click, that is the
    /// tray's UI thread held for the whole sweep, which is what the freeze on "Profile &gt; …" and on
    /// Save was. So the write is queued to the thread pool and the caller returns at once.</para>
    ///
    /// <para>Queued rather than merely thrown: the writes are chained, so two quick picks land in the
    /// order they were clicked instead of racing for the last word. The return value is now "accepted",
    /// not "written" — see <see cref="Adopt"/> for why that is the right trade.</para>
    /// </summary>
    private static bool Write(string? value)
    {
        lock (Gate)
        {
            // Already what it should be: no registry write, and — the part that costs the seconds —
            // no broadcast. This is the ordinary case for Save, which reconciles the environment on
            // every press whether or not the profile row was the thing that changed.
            //
            // Compared against where the queue is *heading*, never against what the registry says.
            // A queued write has not landed yet, so the variable still reads its old value, and
            // comparing against that drops the second of two quick picks on the floor: measured —
            // pick the other profile, pick this one straight back, and the machine stays on the
            // other one. With no write pending the registry is the honest answer, which is also how
            // a value changed behind the tray's back (System Properties) is picked up again.
            string? heading = _inFlight > 0 ? _target : Current();
            bool already = string.Equals(heading ?? "", value ?? "", StringComparison.OrdinalIgnoreCase);
            if (!already)
            {
                _target = value;
                _inFlight++;
            }
            // Queued either way (T173). A call with nothing to write still has an outcome — "the machine
            // already says this" — and a caller asking whether its choice reached the machine must get
            // the same shape of answer either way, or the reconcile Save runs on every press would be a
            // silent hole in the record. Queued rather than answered here so the report still arrives
            // after any write already in flight, and always off the UI thread.
            _writes = _writes.ContinueWith(_ => Apply(value, wrote: !already), TaskScheduler.Default);
        }
        return true;
    }

    private static readonly object Gate = new();
    private static Task _writes = Task.CompletedTask;
    /// <summary>Where the queue is heading, meaningful only while <see cref="_inFlight"/> is above
    /// zero — see <see cref="Write"/> for why the registry cannot answer that question.</summary>
    private static string? _target;
    private static int _inFlight;

    private static void Apply(string? value, bool wrote)
    {
        string? error = null;
        if (wrote)
        {
            try
            {
                // A sampled environment is written to instead of the machine's (T231), so a pick made
                // under the fixture moves the fixture and the read-back below observes it — the whole
                // T173/T174 path runs, off the registry rather than through it. The one line that must
                // never be reached under sampling is the next one.
                if (_sampling) _sampled = value;
                else
                {
                    // .NET removes the variable when the value is null or empty — which is exactly the
                    // "select ~/.claude" case, not an error.
                    Environment.SetEnvironmentVariable(Var, value, EnvironmentVariableTarget.User);
                    Broadcast();
                }
            }
            // Still swallowed as far as the *click* is concerned — nothing the user is waiting on
            // depends on this — but no longer discarded: the reason is what the outcome below carries.
            catch (Exception ex) { error = ex.Message; }
            finally { lock (Gate) _inFlight--; }
        }

        var outcome = new WriteOutcome(value, error is null ? ReadBack(value) : Current(), error, wrote);
        Last = outcome;
        try { Completed?.Invoke(outcome); }
        catch { /* a subscriber that throws must not poison the queue the next pick rides on */ }
    }

    /// <summary>
    /// What the variable says once the write is meant to have landed — read back rather than inferred
    /// from the call returning, which is the whole of T173: <see cref="Adopt"/> answers *accepted*, and
    /// until something reads the registry again nothing on this machine knows the difference between
    /// that and *written*.
    ///
    /// <para>Given a bounded moment to agree rather than sampled once. Measured on the pick that
    /// produced §XVI: <c>SetEnvironmentVariable</c> returned at <b>87 ms</b> while the value first read
    /// back out of <c>HKCU\Environment</c> at <b>108 ms</b>, so a single read straight after the call
    /// can observe the old value and report a perfectly good write as a failure. Nothing waits on this —
    /// it runs on the pool long after the caller returned — but the wait is still capped, because it
    /// sits in the queue the next profile switch is behind.</para>
    /// </summary>
    private static string? ReadBack(string? intended)
    {
        for (int i = 0; i < 6; i++)
        {
            string? now = Current();
            if (string.Equals(now ?? "", intended ?? "", StringComparison.OrdinalIgnoreCase)) return now;
            Thread.Sleep(40);
        }
        return Current();
    }

    /// <summary>
    /// How a queued write actually turned out, read back off the registry rather than assumed (T173).
    /// </summary>
    /// <param name="Intended">The value the write meant to leave behind — null means "remove it".</param>
    /// <param name="Observed">What the variable said afterwards.</param>
    /// <param name="Error">The exception's message when the write threw, else null.</param>
    /// <param name="Wrote">False when there was nothing to do because the variable already said this —
    /// a landed outcome, and not one worth telling anybody about.</param>
    internal readonly record struct WriteOutcome(string? Intended, string? Observed, string? Error, bool Wrote)
    {
        /// <summary>The variable reads back as what the write meant it to.</summary>
        internal bool Landed => Error is null
                                && string.Equals(Observed ?? "", Intended ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Raised on the thread pool once a queued write has been applied and read back — one per
    /// call to <see cref="Adopt"/> or <see cref="Restore"/>, in the order they were made. The seam a
    /// failure reaches the user through (T174); this file itself shows nothing.</summary>
    internal static event Action<WriteOutcome>? Completed;

    /// <summary>The most recent outcome, so the result of the last write is readable without having
    /// been subscribed when it happened — which is what makes <c>--profiles</c> able to report it.</summary>
    internal static WriteOutcome? Last { get; private set; }

    /// <summary>Let a queued write finish before the process goes away — switching profile and quitting
    /// within the same second must not lose the variable. Bounded, because a shutdown must not hang on
    /// a window that never answers.</summary>
    internal static void Drain(int timeoutMs = 3000)
    {
        Task pending;
        lock (Gate) pending = _writes;
        try { pending.Wait(timeoutMs); } catch { /* nothing left to do about it at exit */ }
    }

    private const int WM_SETTINGCHANGE = 0x001A;
    private const int SMTO_ABORTIFHUNG = 0x0002;
    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, int msg, IntPtr wParam, string lParam, int flags, int timeoutMs, out IntPtr result);

    /// <summary>Ask every top-level window to re-read the environment. Kept even though .NET sends its
    /// own <c>WM_SETTINGCHANGE</c> from <c>SetEnvironmentVariable</c>: that one goes out with no flags,
    /// so it waits out a hung window in full, while this one aborts on it.
    ///
    /// <para>The per-window budget is two seconds, down from five (T149): nothing blocks on this
    /// anymore, but it now runs in a queue, and a slow sweep delays the next profile switch's.</para>
    /// </summary>
    private static void Broadcast()
    {
        try { SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 2000, out _); }
        catch { /* the value is written either way; the broadcast is the courtesy */ }
    }
}
