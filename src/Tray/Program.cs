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
            using (FileStream fs = OutFile.Create(path)) bmp.Save(fs, System.Drawing.Imaging.ImageFormat.Png);
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
                // A slug/name may follow the flags; --root's value must not be mistaken for one.
                string? select = contextFlags.FirstOrDefault(f => !f.StartsWith("--") && f != windowRoot);
                var contextPage = new ContextPage(select)
                {
                    ScanRoot = windowRoot,
                    // `--scroll` opens on the source table instead of the gauge, so the rows can be
                    // screenshotted — the pane is taller than the screen at the default size.
                    PreviewScrollToTable = contextFlags.Contains("--scroll"),
                    // `--simulate` pre-ticks the three heaviest removable sources, so the what-if
                    // banner can be screenshotted without a mouse.
                    PreviewSimulateTop = contextFlags.Contains("--simulate"),
                    // `--demo-history` draws the drift row from a synthetic series, so the sparkline
                    // can be screenshotted before weeks of real history exist.
                    PreviewDemoHistory = contextFlags.Contains("--demo-history"),
                };
                // The page alone, without the shell's nav strip: this preview is about the page (see
                // PageWindow). Topmost like the other previews — the capture script can't always take
                // foreground (Windows refuses the steal when another app owns it), and a screen-copy of
                // an occluded window is a screenshot of whatever covered it.
                contextApp.Run(new PageWindow(contextPage, L.T("context.title"), 1000, 720, 880, 620)
                {
                    Topmost = true,
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

        // The rate-limit headers as the API sent them, plus every shape change the tray has recorded
        // (T181). The instrument for a reading only an account already in overage can supply.
        if (args.Length >= 1 && args[0] == "--probe")
        {
            Environment.ExitCode = ProbeCli.RunAsync(args.Skip(1).ToArray()).GetAwaiter().GetResult();
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
            var trayHosted = new PageWindow(
                new SettingsPage(Settings.Load(), _ => { }, PageArg(args),
                    SampleProfiles(args), args.Contains("--reveal")),
                L.T("settings.title"), 880, 600, 760, 560);
            trayHosted.Show();
            trayHosted.Activate();
            Application.Run();
            return;
        }

        // Dev/preview helper: the **whole window** as the tray opens it — the nav strip over its three
        // destinations — hosted under the WinForms pump with the input bridge, which is what the tray
        // does (see UI convention 7). `--main [Statistics|Context|Settings]` opens on a destination.
        if (args.Length >= 1 && args[0] == "--main")
        {
            ApplicationConfiguration.Initialize();
            _ = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            WpfInputBridge.Install();
            long mainNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // The same synthetic reading `--stats` uses, so the report has both verdicts on screen
            // without depending on this machine's quota at the moment of the screenshot.
            var mainSample = new PaceSnapshot(
                Util5h: 0.72, Reset5h: mainNow + 2 * 3600,
                Util7d: 0.38, Reset7d: mainNow + 3 * 86400);
            Settings mainSettings = Settings.Load();
            var shell = new MainWindow(mainSample, mainSettings.ShowRemaining, null,
                () => mainSettings, _ => { });
            shell.Statistics.SetProfiles(ClaudeAccount.Discover(mainSettings.Profiles));
            shell.Navigate(args.Length >= 2 ? args[1] : MainWindow.DestStatistics);
            shell.Show();
            shell.Activate();
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
            var page = new SettingsPage(Settings.Load(), _ => { }, PageArg(args),
                SampleProfiles(args), args.Contains("--reveal"));
            previewApp.Run(new PageWindow(page, L.T("settings.title"), 880, 600, 760, 560));
            return;
        }

        // Dev/preview helper: open just the Statistics page, standalone (no nav strip — see PageWindow),
        // for the launch-and-screenshot loop (see the preview-ui skill). Which synthetic reading it is fed
        // is one row of StatsPreviews, the table --capture-stats reads too (T186).
        if (args.Length >= 1 && args[0] == "--stats")
        {
            if (StatsPreviews.Resolve(Bare(args.Skip(1)), capturing: false) is not { } choice)
            {
                Environment.ExitCode = 1;
                return;
            }
            var previewApp = new System.Windows.Application();
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            StatisticsPage page = StatsPreviews.Build(choice, now);
            // Only the default preview shows the real profile picker: every other one is a fixture, and a
            // switch away from it would replace the very reading being looked at. `--sample` fills it from
            // `AccountFixture` instead, so this preview shows what `--capture-stats --sample` publishes
            // (T197) — the same rule, on the flag the eyeballing happens through.
            if (SampleProfiles(args) is { } samplePicker)
                page.SetProfiles(samplePicker);
            else if (choice.Variant.Name.Length == 0)
                page.SetProfiles(ClaudeAccount.Discover(Settings.Load().Profiles));
            previewApp.Run(new PageWindow(page, L.T("stats.title"), 880, 948, 800, 740) { Topmost = true });
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
            var settingsPage = new SettingsPage(Settings.Load(), _ => { }, page,
                SampleProfiles(args), args.Contains("--reveal"));
            var win = new PageWindow(settingsPage, L.T("settings.title"), 880, 600, 760, 560)
            {
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                // Off-screen there is no system backdrop to follow, so pin the theme or the snapshot
                // renders dark text on an unpainted surface.
                ThemeMode = System.Windows.ThemeMode.Dark,
            };
            win.Show();
            if (profile >= 0) settingsPage.SelectProfileForPreview(profile);

            var settle = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            settle.Tick += (_, _) =>
            {
                settle.Stop();
                try { settingsPage.SaveSnapshot(outPath, scroll); Console.WriteLine("wrote " + outPath); }
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
            // Both positionals are bare words, so read them as such rather than by index: `args[1]` made
            // `--capture-stats --sample` write to a file called `--sample`, which is T198's defect in the
            // flag next door.
            string[] bare = Bare(args.Skip(1)).ToArray();
            string outBase = System.IO.Path.GetFullPath(bare.Length >= 1 ? bare[0] : @"docs\_preview\stats");
            // The same table `--stats` reads, minus the output path, so a preview added there is captured
            // here without a second edit and a name neither knows is refused rather than defaulted (T186).
            if (StatsPreviews.Resolve(bare.Skip(1), capturing: true) is not { } choice)
            {
                Environment.ExitCode = 1;
                return;
            }
            // Whose name sits above the chart. The picker is chrome around the reading, which is why
            // nothing caught it filling from real discovery on **every** variant: a fixture week was
            // published captioned with this machine's monitored account, in the one image a fixture exists
            // to keep a real name out of (T197). So it is filled from whatever the rest of the command is
            // about — `--sample` from `AccountFixture`, the default variant (the only one that *is* this
            // machine's reading) from discovery — and a synthetic variant on its own gets no picker.
            bool sample = args.Contains("--sample");
            List<ClaudeInfo>? pickerProfiles = sample
                ? SampleProfiles(args)
                : choice.Variant.Name.Length == 0 ? ClaudeAccount.Discover(Settings.Load().Profiles) : null;
            // A fixture that failed to build must not fall back to discovery, which is the leak the whole
            // task is about arriving through the error path instead of the happy one.
            if (sample && pickerProfiles is null)
            {
                Environment.ExitCode = 1;
                return;
            }

            // `profile=<n>` renders the window as another profile (T128), so the switch path is captured
            // rather than only the picker sitting there. A **list** — `profile=1,0` — walks the picker
            // through each index in turn, one full settle apart: that is the round trip the report has to
            // survive (T164), and the check is that its PNGs match a plain capture of the profile it
            // lands on. One selection at a time is what makes it a check: a switch that is still
            // computing when the next one arrives would prove nothing about either.
            int[] profileSteps = (ArgValue(args, "profile") ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out int n) ? n : -1)
                .Where(n => n >= 0)
                .ToArray();
            // Refused rather than ignored, on T186's rule: with no picker there is nothing to walk, and a
            // silent no-op here would hand back PNGs that look exactly like the round trip passing.
            if (profileSteps.Length > 0 && pickerProfiles is null)
            {
                Console.WriteLine($"profile= has no picker to walk: '{choice.Variant.Name}' is a synthetic " +
                                  "reading, and filling the picker from this machine would caption it with " +
                                  "a real account (T197). Add --sample to walk the two fixture profiles, " +
                                  "or drop the variant to walk this machine's.");
                Environment.ExitCode = 1;
                return;
            }

            var previewApp = new System.Windows.Application
            {
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
            };
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            StatisticsPage statsPage = StatsPreviews.Build(choice, now);
            var win = new PageWindow(statsPage, L.T("stats.title"), 880, 948, 800, 740)
            {
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                // Pin the theme for the capture — off-screen there is no system backdrop to follow,
                // so without this the snapshot renders dark-theme text over an unpainted background.
                ThemeMode = System.Windows.ThemeMode.Dark,
            };
            if (pickerProfiles is not null) statsPage.SetProfiles(pickerProfiles);
            win.Show();
            // The "refresh" modifier feeds a fresh reading in — the exact call the tray's poll loop makes —
            // and snapshots *while the recomputation is still in flight*. That is the window a blanked pane
            // would appear in, so the captured PNGs are the check for T118: content, not a "computing…" line.

            // Let the async pace computation finish and the charts render, then snapshot each tab.
            var settle = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            int nextStep = 0;
            settle.Tick += (_, _) =>
            {
                if (nextStep < profileSteps.Length)
                {
                    statsPage.SelectProfileForPreview(profileSteps[nextStep++]);
                    return;
                }
                settle.Stop();
                if (choice.Refresh) statsPage.UpdateSnapshot(choice.Variant.Snapshot(now));
                try { statsPage.SaveAllTabs(outBase); Console.WriteLine("wrote " + outBase + "-5h.png / -7d.png / -throughput.png"); }
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

    /// <summary>The words of a preview command that could name a variant: everything that is neither a
    /// <c>--flag</c> nor a <c>name=value</c> pair, so those can be given in any order without one of them
    /// being read as a preview name — and, for <c>--capture-stats</c>, without its output path.</summary>
    private static IEnumerable<string> Bare(IEnumerable<string> args) =>
        args.Where(a => a.Length > 0 && !a.StartsWith("--") && !a.Contains('='));

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
