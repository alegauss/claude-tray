using System.Windows.Interop;
using Microsoft.Win32;

namespace ClaudeTray;

// Manages the "start with Windows" entry under HKCU Run (per-user, no admin needed).
internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeTray";

    // Real .exe path, correct even for a single-file self-contained publish.
    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string v &&
               string.Equals(v.Trim('"'), ExePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{ExePath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

/// <summary>
/// Gives the WPF windows the message pre-processing pass WPF's own pump performs, which the WinForms
/// pump this app runs on does not.
///
/// <para>The tray is a WinForms app (<c>Application.Run(new TrayContext())</c>) that shows WPF windows,
/// and WPF keyboard input does not arrive through <c>WndProc</c> alone: <c>HwndSource</c> subscribes to
/// <see cref="ComponentDispatcher"/> and expects the pump to offer every thread message to it *before*
/// <c>TranslateMessage</c>. WPF's own <c>Dispatcher.PushFrame</c> loop does exactly that; WinForms'
/// loop knows nothing about it. The result is a window that looks and clicks correctly — mouse input
/// *is* WndProc-driven — while every key press is dropped: no typing in a text box, no Tab, no Esc.
/// It went unseen because the `--settings` preview runs under a WPF pump, where input works.</para>
///
/// <para>Forwarding through a WinForms <see cref="IMessageFilter"/> reproduces WPF's loop exactly,
/// including the "handled means don't translate/dispatch" contract: a message WPF consumed must not go
/// on to <c>TranslateMessage</c>, or a keystroke would be delivered twice. Messages belonging to
/// non-WPF windows (the tray icon, its context menu) are offered to no subscriber and pass straight
/// through, so this costs one delegate call per message and changes nothing about WinForms.</para>
/// </summary>
internal sealed class WpfInputBridge : IMessageFilter
{
    public bool PreFilterMessage(ref Message m)
    {
        var msg = new MSG
        {
            hwnd = m.HWnd,
            message = m.Msg,
            wParam = m.WParam,
            lParam = m.LParam,
        };
        return ComponentDispatcher.RaiseThreadMessage(ref msg);
    }

    /// <summary>Install the bridge on this thread's WinForms pump. Idempotent enough to call from every
    /// entry point that pumps with WinForms while showing WPF windows.</summary>
    public static void Install() => Application.AddMessageFilter(new WpfInputBridge());
}

internal static class Program
{
    // Held for the whole process lifetime so a second launch can't spawn a duplicate tray icon.
    private static Mutex? _instanceMutex;

    [STAThread]
    private static void Main(string[] args)
    {
        // `--lang <code>` anywhere in the arguments overrides the display language for this process
        // only, leaving the saved preference alone. It exists for the i18n screenshot loop: verifying
        // that a window still fits in Spanish should not mean editing the user's settings and
        // restarting. Both tokens are removed before anything else parses the arguments, or a stray
        // "es" would be read as a project name.
        string? langOverride = null;
        int langAt = Array.IndexOf(args, "--lang");
        if (langAt >= 0 && langAt + 1 < args.Length)
        {
            langOverride = args[langAt + 1];
            args = args.Take(langAt).Concat(args.Skip(langAt + 2)).ToArray();
        }

        // Pick the UI language before anything localized is built (menus, dialogs, and XAML windows all
        // resolve strings at parse time): honor the saved Settings preference, falling back to the OS.
        L.Apply(langOverride ?? Settings.Load().Language);

        // Adopt the profile whose numbers this process reads and writes, before any store is touched —
        // every one of them is keyed by it now (T125). The default config dir is the profile a bare
        // `claude` uses, which is the single series every installation has today; the first call also
        // migrates the pre-profile flat files into it. Choosing another profile to monitor, and polling
        // more than one, is T127.
        ProfileStore.SetMonitored(ClaudeAccount.Read());

        if (args.Length >= 1 && args[0] == "--render")
        {
            PreviewCli.RenderTest(args.Length >= 2 ? args[1] : ".");
            return;
        }

        if (args.Length >= 1 && args[0] == "--makeicon")
        {
            PreviewCli.MakeIcon(args.Length >= 2 ? args[1] : "ClaudeTray.ico");
            return;
        }

        if (args.Length >= 1 && args[0] == "--social")
        {
            string path = args.Length >= 2 ? args[1] : "docs/social-preview.png";
            using Bitmap bmp = IconRenderer.RenderSocial(1280, 640);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("wrote " + Path.GetFullPath(path));
            return;
        }

        if (args.Length >= 1 && args[0] == "--insights")
        {
            var d = UsageInsights.Compute(DateTimeOffset.UtcNow.UtcDateTime);
            if (d.Error != null) { Console.WriteLine("error: " + d.Error); return; }
            Console.WriteLine($"24h: {d.Requests} reqs  {d.Sessions} sessions");
            Console.WriteLine($"subagents: {d.SubagentPct:P0}   >150k ctx: {d.HeavyContextPct:P0}");
            foreach (var (model, pct) in d.ByModel)
                Console.WriteLine($"  {model}: {pct:P0}");
            return;
        }

        // Headless view of the transcript tail (T97): every assistant turn as it lands, plus what the
        // sweep cost. Exists so the engine behind the live rate can be verified without a window —
        // run it, start a Claude Code turn in any project, and the turn should print within a second
        // or two with only its own bytes read.
        if (args.Length >= 1 && args[0] == "--tail")
        {
            LiveCli.PrintTail(args.Skip(1).ToArray());
            return;
        }

        // The live rate itself (T98), printed once a second: the metric T99 will draw, verifiable
        // without a window and against `--tail`'s raw turns.
        if (args.Length >= 1 && args[0] == "--live")
        {
            LiveCli.PrintLive(args.Skip(1).ToArray());
            return;
        }

        // The self-check (T96): the pacing and live-rate arithmetic asserted over synthetic inputs,
        // exiting non-zero on failure so CI can run it as one line. Everything it touches is
        // synthetic and removed afterwards; no real profile is read or written. `--quick` skips the
        // tail section, which waits on real sweeps and takes a few seconds.
        if (args.Length >= 1 && args[0] == "--selftest")
        {
            Environment.ExitCode = SelfTestCli.Run(args.Skip(1).ToArray());
            return;
        }

        // Headless view of the weekly activity shape behind the projection: 168 buckets of
        // p(active), the coverage that backs them, and what they predict for the hours ahead.
        if (args.Length >= 1 && args[0] == "--activity")
        {
            ActivityCli.PrintActivity(args.Skip(1).ToArray());
            return;
        }

        // Headless view of the Context Load Inspector: what every session in a project costs before
        // the first prompt. `--context` lists projects; `--context <slug-or-name>` breaks one down
        // source by source; `--context --all` does that for every project; `--context --calibrate`
        // fits the estimate against what the transcripts actually measured.
        // The archival companion to the copy-a-prompt action: the whole picture as one markdown file,
        // paths and numbers only (see ContextReport).
        if (args.Length >= 1 && args[0] == "--context-report")
        {
            ContextCli.WriteContextReport(args.Length >= 2 ? args[1] : "context-report.md");
            return;
        }

        if (args.Length >= 1 && args[0] == "--context")
        {
            string[] contextFlags = args.Skip(1).ToArray();
            // `--context --window [slug]` opens the real window standalone instead of printing —
            // the same launch-and-screenshot loop as `--settings` (see the preview-ui skill). The
            // bare `--context` stays the headless developer report it has always been.
            if (contextFlags.Contains("--window"))
            {
                string? windowRoot = null;
                int at = Array.IndexOf(contextFlags, "--root");
                if (at >= 0 && at + 1 < contextFlags.Length) windowRoot = contextFlags[at + 1];
                if (contextFlags.Contains("--sample"))
                {
                    try { windowRoot = ContextFixture.Build(DateTimeOffset.UtcNow.UtcDateTime); }
                    catch (Exception e) { Console.WriteLine("error building the sample fixture: " + e.Message); return; }
                }

                var contextApp = new System.Windows.Application();
                // Topmost like the other previews: the capture script can't always take foreground
                // (Windows refuses the steal when another app owns it), and a screen-copy of an
                // occluded window is a screenshot of whatever covered it.
                // A slug/name may follow the flags; --root's value must not be mistaken for one.
                string? select = contextFlags.FirstOrDefault(f => !f.StartsWith("--") && f != windowRoot);
                contextApp.Run(new ContextWindow(select)
                {
                    ScanRoot = windowRoot,
                    Topmost = true,
                    // `--scroll` opens on the source table instead of the gauge, so the rows can be
                    // screenshotted — the pane is taller than the screen at the default size.
                    PreviewScrollToTable = contextFlags.Contains("--scroll"),
                    // `--simulate` pre-ticks the three heaviest removable sources, so the what-if
                    // banner can be screenshotted without a mouse.
                    PreviewSimulateTop = contextFlags.Contains("--simulate"),
                    // `--demo-history` draws the drift row from a synthetic series, so the sparkline
                    // can be screenshotted before weeks of real history exist.
                    PreviewDemoHistory = contextFlags.Contains("--demo-history"),
                });
                return;
            }
            ContextCli.PrintContext(contextFlags);
            return;
        }

        // Dev/preview helper: show a reset toast with sample data so the variants can be seen /
        // screenshotted standalone. Optional second arg: "scheduled", "credit", or "unexpected" (default).
        if (args.Length >= 1 && args[0] == "--simulate-reset")
        {
            PreviewCli.SimulateReset(args.Length >= 2 ? args[1] : "unexpected");
            return;
        }

        // Dev/preview helper: render a reset toast (card + shadow + confetti) to a transparent PNG,
        // so the four variants can be documented cleanly on any background. Args: <variant> <outPath>.
        if (args.Length >= 1 && args[0] == "--capture-toast")
        {
            PreviewCli.CaptureToast(args.Length >= 2 ? args[1] : "unexpected",
                args.Length >= 3 ? args[2] : "toast.png");
            return;
        }

        // Every Claude Code profile (config dir) this machine exposes, in discovery order. Any extra
        // arguments are treated as explicitly *registered* dirs — the source the Settings list will
        // feed once it exists — so discovery can be exercised without one.
        if (args.Length >= 1 && args[0] == "--profiles")
        {
            ProfilesCli.PrintProfiles(args.Skip(1).ToArray());
            return;
        }

        // Dev helper: the Settings window hosted **exactly as the tray hosts it** — a WPF Application
        // object that is never Run, under the WinForms message pump — with the `--settings` preview's
        // convenience of not needing the tray menu. This is not a duplicate of `--settings`: that one
        // runs a WPF pump, which is a *different* input environment, and the difference is precisely how
        // "no keyboard input in any window" (T135) survived every preview and screenshot of the UI. Use
        // this one to verify anything keyboard: typing, Tab, Esc, shortcuts.
        if (args.Length >= 1 && args[0] == "--settings-tray")
        {
            ApplicationConfiguration.Initialize();
            _ = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            WpfInputBridge.Install();
            var trayHosted = new SettingsWindow(Settings.Load(), _ => { }, PageArg(args),
                SampleProfiles(args), args.Contains("--reveal"));
            trayHosted.Show();
            trayHosted.Activate();
            Application.Run();
            return;
        }

        // Dev/preview helper: open just the Settings window, standalone, so the UI can be launched
        // and screenshotted deterministically without going through the tray menu. Note the pump: this
        // is a WPF Application.Run, so it cannot see a WinForms-hosting input bug — `--settings-tray`.
        // `--sample` renders it over the synthetic accounts instead of this machine's, and `--reveal`
        // opens with the holder unmasked — the pair behind the published System information shot (T121).
        if (args.Length >= 1 && args[0] == "--settings")
        {
            var previewApp = new System.Windows.Application();
            var win = new SettingsWindow(Settings.Load(), _ => { }, PageArg(args),
                SampleProfiles(args), args.Contains("--reveal"));
            previewApp.Run(win);
            return;
        }

        // Dev/preview helper: open just the Statistics window, standalone, for the same
        // launch-and-screenshot loop (see the preview-ui skill). Feeds a synthetic snapshot — a 5h
        // session burning ahead of pace, a 7d week comfortably on track — so both verdicts render.
        if (args.Length >= 1 && args[0] == "--stats")
        {
            var previewApp = new System.Windows.Application();
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var sample = new PaceSnapshot(
                Util5h: 0.72, Reset5h: now + 2 * 3600,      // 3h of 5h elapsed (60%), 72% used → ahead
                Util7d: 0.38, Reset7d: now + 3 * 86400);    // 4d of 7d elapsed (57%), 38% used → on track
            bool remaining = args.Length >= 2 && args[1].Equals("remaining", StringComparison.OrdinalIgnoreCase);
            // "--stats error" previews the API-error state (e.g. a 403 payment-past-due): charts drawn
            // from the last known local data, with an error banner. Any real gaps in the logged
            // readings are drawn as red "unavailable" spans on the usage line.
            // "--stats history" previews the offline fallback: no live reading, so the snapshot is
            // rebuilt from the last reading persisted on disk (usage-history.jsonl) — exactly what
            // TrayContext.CurrentSnapshot() does when signed out / the token expired at launch. Falls
            // back to the "connect" hint only when there's genuinely no history yet.
            if (args.Length >= 2 && args[1].Equals("history", StringComparison.OrdinalIgnoreCase))
            {
                PaceSnapshot? fromDisk = UsageHistory.Latest(ProfileStore.Monitored) is { } h
                    ? new PaceSnapshot(h.Util5h, h.Reset5h, h.Util7d, h.Reset7d)
                    : null;
                previewApp.Run(new StatisticsWindow(fromDisk, remaining) { Topmost = true });
            }
            else if (args.Length >= 2 && args[1].Equals("error", StringComparison.OrdinalIgnoreCase))
                previewApp.Run(new StatisticsWindow(sample, remaining,
                    "Your subscription payment is past due. Please pay your overdue invoice to restore access, or reach out to your company admin.")
                { Topmost = true });
            else if (args.Length >= 2 && args[1].Equals("gapdemo", StringComparison.OrdinalIgnoreCase))
            {
                // Deterministic preview of the recovered state: a hand-built report with a past data gap
                // that has since recovered — no error banner, but the outage still marked red on the
                // curve. Pass "ongoing" to also show the error banner (mid-outage).
                bool ongoing = args.Length >= 3 && args[2].Equals("ongoing", StringComparison.OrdinalIgnoreCase);
                var win = new StatisticsWindow(null) { Topmost = true };
                win.Loaded += (_, _) => win.PreviewReport(PreviewCli.BuildGapDemoReport(now), ongoing
                    ? "Your subscription payment is past due. Please pay your overdue invoice to restore access, or reach out to your company admin."
                    : null);
                previewApp.Run(win);
            }
            else if (args.Length >= 2 && args[1].Equals("shape", StringComparison.OrdinalIgnoreCase))
            {
                // Preview the activity-aware weekly projection running out *before* the reset: the
                // default sample lands comfortably below 100%, which draws the staircase but never its
                // landing marker. The shape itself is real — it comes from this machine's own profile.
                // An idle 5h session so the window opens straight on the weekly tab — the shaped
                // projection is a weekly-only feature and shouldn't need a click to be looked at.
                var heavy = new PaceSnapshot(
                    Util5h: 0.0, Reset5h: now + 5 * 3600,
                    Util7d: 0.74, Reset7d: now + 3 * 86400);    // 4d of 7d elapsed, 74% used → runs out early
                // "--stats shape ghost" also draws a synthetic previous week behind it: the real ghost
                // needs two weeks of folded history, which a fresh machine hasn't got.
                previewApp.Run(new StatisticsWindow(heavy, remaining)
                {
                    Topmost = true,
                    PreviewDemoGhost = args.Any(a => a.Equals("ghost", StringComparison.OrdinalIgnoreCase)),
                });
            }
            // "--stats live" feeds the throughput strip a deterministic synthetic three minutes
            // instead of the real tail: the live row depends on whatever happens to be generating,
            // which cannot be screenshotted twice the same way.
            else if (args.Length >= 2 && args[1].Equals("live", StringComparison.OrdinalIgnoreCase))
            {
                previewApp.Run(new StatisticsWindow(sample, remaining)
                {
                    Topmost = true,
                    PreviewDemoLive = true,
                });
            }
            // "--stats method" opens the window with the method popup already up. It is its own
            // top-level window, so --capture-stats (RenderTargetBitmap over the content) cannot see it;
            // this is the path that gets it on screen for the capture script.
            else if (args.Length >= 2 && args[1].Equals("method", StringComparison.OrdinalIgnoreCase))
            {
                previewApp.Run(new StatisticsWindow(sample, remaining)
                {
                    Topmost = true,
                    PreviewDemoLive = true,
                    PreviewMethodOpen = true,
                });
            }
            else if (args.Length >= 2 && args[1].Equals("idle", StringComparison.OrdinalIgnoreCase))
            {
                // Preview the "not using Claude" state: the 5h session is idle (0% used → flat chart),
                // while the week still carries accumulated usage. The window should open on the weekly
                // tab, since the 5h chart has nothing interesting to show.
                var idle = new PaceSnapshot(
                    Util5h: 0.0, Reset5h: now + 5 * 3600,       // fresh/expired session, nothing used
                    Util7d: 0.38, Reset7d: now + 3 * 86400);    // week still has accumulated usage
                previewApp.Run(new StatisticsWindow(idle, remaining) { Topmost = true });
            }
            else
            {
                var w = new StatisticsWindow(sample, remaining) { Topmost = true };
                w.SetProfiles(ClaudeAccount.Discover(Settings.Load().Profiles));
                previewApp.Run(w);
            }
            return;
        }

        // Dev/preview helper: render the Settings window off-screen to a PNG via RenderTargetBitmap.
        // Args: --capture-settings <out.png> [page] [scroll=<dip>] [profile=<index>]. Preferred over the
        // screen-copy script (scripts\Capture-Window.ps1): that one copies the pixels *on screen* inside
        // the window's rectangle, so anything that steals focus or sits on top lands in the file.
        if (args.Length >= 2 && args[0] == "--capture-settings")
        {
            string outPath = System.IO.Path.GetFullPath(args[1]);
            string? page = PageArg(args.Skip(1).ToArray());
            double scroll = ArgValue(args, "scroll") is { } s && double.TryParse(s, out double d) ? d : 0;
            int profile = ArgValue(args, "profile") is { } pi && int.TryParse(pi, out int n) ? n : -1;

            var previewApp = new System.Windows.Application
            {
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
            };
            var win = new SettingsWindow(Settings.Load(), _ => { }, page,
                SampleProfiles(args), args.Contains("--reveal"))
            {
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                // Off-screen there is no system backdrop to follow, so pin the theme or the snapshot
                // renders dark text on an unpainted surface.
                ThemeMode = System.Windows.ThemeMode.Dark,
            };
            win.Show();
            if (profile >= 0) win.SelectProfileForPreview(profile);

            var settle = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            settle.Tick += (_, _) =>
            {
                settle.Stop();
                try { win.SaveSnapshot(outPath, scroll); Console.WriteLine("wrote " + outPath); }
                finally { previewApp.Shutdown(); }
            };
            settle.Start();
            previewApp.Run();
            return;
        }

        // Dev/preview helper: render the Statistics window off-screen to PNGs (one per tab) via
        // RenderTargetBitmap, without needing it foreground — the screen-copy capture can't see a
        // window another app covers. Args: [outBase] (default docs\_preview\stats → -5h.png/-7d.png).
        if (args.Length >= 1 && args[0] == "--capture-stats")
        {
            string outBase = System.IO.Path.GetFullPath(args.Length >= 2 ? args[1] : @"docs\_preview\stats");
            var previewApp = new System.Windows.Application
            {
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
            };
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // A second argument of "shape" raises the weekly utilization until the activity-aware
            // projection runs out before the reset, so the staircase's landing marker and the
            // usually-idle bands are in the captured 7d tab (see `--stats shape`).
            bool heavyWeek = args.Any(a => a.Equals("shape", StringComparison.OrdinalIgnoreCase));
            var sample = new PaceSnapshot(
                Util5h: 0.72, Reset5h: now + 2 * 3600,
                Util7d: heavyWeek ? 0.74 : 0.38, Reset7d: now + 3 * 86400);
            var win = new StatisticsWindow(sample)
            {
                PreviewDemoGhost = args.Any(a => a.Equals("ghost", StringComparison.OrdinalIgnoreCase)),
                // Deterministic live strip, so the captured PNG is stable across runs.
                PreviewDemoLive = args.Any(a => a.Equals("live", StringComparison.OrdinalIgnoreCase)),
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                // Pin the theme for the capture — off-screen there is no system backdrop to follow,
                // so without this the snapshot renders dark-theme text over an unpainted background.
                ThemeMode = System.Windows.ThemeMode.Dark,
            };
            win.SetProfiles(ClaudeAccount.Discover(Settings.Load().Profiles));
            win.Show();
            // `profile=<n>` renders the window as another profile (T128), so the switch path is
            // captured rather than only the picker sitting there.
            if (ArgValue(args, "profile") is { } pn && int.TryParse(pn, out int pi))
                win.SelectProfileForPreview(pi);
            // A fourth argument of "refresh" feeds a fresh reading in — the exact call the tray's poll
            // loop makes — and snapshots *while the recomputation is still in flight*. That is the window
            // a blanked pane would appear in, so the captured PNGs are the check for T118: content, not a
            // "computing…" line.
            bool refresh = args.Any(a => a.Equals("refresh", StringComparison.OrdinalIgnoreCase));

            // Let the async pace computation finish and the charts render, then snapshot each tab.
            var settle = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            settle.Tick += (_, _) =>
            {
                settle.Stop();
                if (refresh) win.UpdateSnapshot(sample);
                try { win.SaveAllTabs(outBase); Console.WriteLine("wrote " + outBase + "-5h.png / -7d.png / -throughput.png"); }
                finally { previewApp.Shutdown(); }
            };
            settle.Start();
            previewApp.Run();
            return;
        }

        // Single instance: if the tray app is already running, just exit — don't add a second icon.
        _instanceMutex = new Mutex(initiallyOwned: true, @"Local\ClaudeTray.SingleInstance", out bool createdNew);
        if (!createdNew)
            return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
        // Without this the WPF windows this app opens receive no keyboard input at all (T135).
        WpfInputBridge.Install();
        Application.Run(new TrayContext());
    }

    /// <summary>Value of a <c>name=value</c> dev-flag argument, or null.</summary>
    private static string? ArgValue(string[] args, string name) =>
        args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))?[(name.Length + 1)..];

    /// <summary>Which Settings page a preview asked for: the first bare word after the command itself,
    /// so the <c>--</c> flags and the <c>name=value</c> ones can be given in any order without one of
    /// them being read as a page name.</summary>
    private static string? PageArg(string[] args) =>
        args.Skip(1).FirstOrDefault(a => !a.StartsWith("--") && !a.Contains('='));

    /// <summary>
    /// The account fixture behind <c>--sample</c> (T121): two synthetic profiles — a personal Max 20x
    /// and a Team seat — read through the real <see cref="ClaudeAccount"/> path, so the System
    /// information page can be screenshotted without a real login on screen. Null without the flag,
    /// which is every non-preview run.
    /// </summary>
    private static List<ClaudeInfo>? SampleProfiles(string[] args)
    {
        if (!args.Contains("--sample")) return null;
        try { return AccountFixture.Build(DateTimeOffset.UtcNow.UtcDateTime); }
        catch (Exception e)
        {
            Console.WriteLine("error building the sample account fixture: " + e.Message);
            return null;
        }
    }



























}
