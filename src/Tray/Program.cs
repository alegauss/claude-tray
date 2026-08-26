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

    /// <summary>
    /// This process is a **second** tray, running beside a resident one on purpose (T237, `--second-tray`).
    ///
    /// <para>The single instance is a real feature and stays the default: two trays polling one profile is
    /// not a state to ship. What it also does is make the menu check unrunnable against the build you just
    /// compiled — the app is meant to be resident, so every developer machine holds the mutex permanently,
    /// and the case either refuses (T202) or drives whatever binary is already there (T236). Verifying two
    /// submenu tasks meant quitting the user's tray by hand three times.</para>
    ///
    /// <para>Read by <see cref="TrayContext"/> to tag its tooltip, because the notification-area icons
    /// belong to the shell's process rather than to this one: pid cannot tell two ClaudeTray icons apart,
    /// so the tooltip has to. Dev-only, so the tag is English and goes in no <c>lang</c> file.</para>
    /// </summary>
    internal static bool SecondTray { get; private set; }

    /// <summary>What a second tray's tooltip starts with, so `Check-Interaction.ps1` can find that icon
    /// and not the resident one. One constant rather than a string spelled out in two repositories'
    /// worth of places — the script matches it as a regex.</summary>
    internal const string SecondTrayTag = "[check]";

    /// <summary>
    /// Put this process's console on UTF-8, once, before any verb runs (T283).
    ///
    /// <para>A WinExe's console starts on the OEM code page, which prints an em dash and a section sign as
    /// <c>?</c>. Twelve read-outs had learned that one at a time and opened with their own copy of this
    /// line; <c>--probe</c> never did, so the instrument densest in both — <c>readership — 9 of 17</c>, and
    /// the <c>§XVIII.9</c> it tells a reader to go and read — was the one that lost them. A pointer is the
    /// one kind of text that cannot be guessed back: <c>see IMPROVEMENTS ?XVIII.9</c> names no section.</para>
    ///
    /// <para>It is a property of <em>being a console read-out</em> and not of any one flag, so it is done
    /// where the flags are dispatched rather than pasted a thirteenth time. That also makes it assertable:
    /// twelve callers remembering is not a rule a check can hold, and one call site is.</para>
    ///
    /// <para>Throws when this process has no console at all — the ordinary tray launch — and when output is
    /// redirected on some hosts. Both are caught: the setting is a courtesy to a terminal that is there, and
    /// never a reason for the tray not to start.</para>
    /// </summary>
    private static void Utf8Console()
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* no console, or redirected */ }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Utf8Console();

        // `--lang <code>` anywhere in the arguments overrides the display language for this process
        // only, leaving the saved preference alone. It exists for the i18n screenshot loop: verifying
        // that a window still fits in Spanish should not mean editing the user's settings and
        // restarting. Both tokens are removed before anything else parses the arguments, or a stray
        // "es" would be read as a project name.
        // A code this build does not ship is REFUSED rather than allowed to fall through to the machine's
        // own language (T260): this is the flag the i18n verification loop is built on, so a typo in it
        // writes a capture of the wrong language into the file the caller named and nothing says so. The
        // same rule and the same shape as `--sample-env` below, which has always had it.
        string? langOverride = null;
        int langAt = Array.IndexOf(args, "--lang");
        if (langAt >= 0)
        {
            langOverride = langAt + 1 < args.Length ? args[langAt + 1] : null;
            args = args.Take(langAt).Concat(args.Skip(langAt + (langOverride is null ? 1 : 2))).ToArray();
            if (L.RefuseOverride(langOverride) is { } refusal)
            {
                // The endonyms carry accents; Utf8Console above is what makes them survive (T283).
                Console.WriteLine(refusal);
                Environment.Exit(1);
                return;
            }
        }

        // Pick the UI language before anything localized is built (menus, dialogs, and XAML windows all
        // resolve strings at parse time): honor the saved Settings preference, falling back to the OS.
        L.Apply(langOverride ?? Settings.Load().Language);

        // `--sample-env <mode>` puts this process on a sampled CLAUDE_CONFIG_DIR (T231), so the states
        // the menu and the read-out exist to report — the variable naming another profile, or a folder
        // no profile covers — can be looked at without rewriting the developer's own registry. Stripped
        // the same way `--lang` is, and applied here: before anything below reads the variable, and
        // before any window is built. A bad mode refuses with the catalogue rather than falling through
        // to the real environment, which would render the ordinary state under a flag asking for another.
        int envAt = Array.IndexOf(args, "--sample-env");
        if (envAt >= 0)
        {
            string? mode = envAt + 1 < args.Length ? args[envAt + 1] : null;
            args = args.Take(envAt).Concat(args.Skip(envAt + (mode is null ? 1 : 2))).ToArray();
            if (EnvironmentFixture.Apply(mode) is { } refusal)
            {
                Console.Error.WriteLine(refusal);
                Environment.Exit(1);
                return;
            }
        }

        // Adopt the profile whose numbers this process reads and writes, before any store is touched —
        // every one of them is keyed by it now (T125). The default config dir is the profile a bare
        // `claude` uses, which is the single series every installation has today. Choosing another
        // profile to monitor, and polling more than one, is T127.
        //
        // It does NOT migrate, and that is deliberate (T357). This line runs above every flag this
        // method dispatches, so a `--selftest` or a `--capture-stats` used to move files in the user's
        // real store before `Observing` was thrown — the gate is downstream of the write it was meant to
        // stop. The tray migrates from `RefreshWatched`, where the process is unambiguously the tray.
        ProfileStore.SetMonitored(ClaudeAccount.Read());

        // Defaults under docs\_preview\, which is git-ignored: `.` dumped a dozen PNGs into the working
        // directory, which for anyone running this is the repository root (T198's rule, one flag along).
        if (args.Length >= 1 && args[0] == "--render")
        {
            PreviewCli.RenderTest(args.Length >= 2 ? args[1] : @"docs\_preview\icons");
            return;
        }

        // --makeicon and --social keep their defaults on purpose: each names its own **tracked** artifact,
        // so the default is "regenerate the committed file" rather than a file nobody asked for.
        if (args.Length >= 1 && args[0] == "--makeicon")
        {
            PreviewCli.MakeIcon(args.Length >= 2 ? args[1] : "ClaudeTray.ico");
            return;
        }

        if (args.Length >= 1 && args[0] == "--social")
        {
            // build\, not the site: this is the 1280x640 card GitHub shows for the repository, which is
            // uploaded by hand in the repository settings. The site has its own og:image, rasterised from
            // site\public\og.svg on every site build — two cards at two sizes for two places.
            string path = args.Length >= 2 ? args[1] : "build/social-preview.png";
            using Bitmap bmp = IconRenderer.RenderSocial(1280, 640);
            using (FileStream fs = OutFile.Create(path)) bmp.Save(fs, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("wrote " + Path.GetFullPath(path));
            return;
        }

        if (args.Length >= 1 && args[0] == "--insights")
        {
            var d = UsageInsights.Compute(DateTimeOffset.UtcNow.UtcDateTime);
            if (ReadOut.Failed(d.Error)) return;
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

        // Headless view of the session index (T327): one row per conversation, which is the unit no
        // other reader here produces. Exists before the page does, so the scan and its per-file cache
        // can be falsified without a window.
        if (args.Length >= 1 && args[0] == "--sessions")
        {
            SessionsCli.PrintSessions(args.Skip(1).ToArray());
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
            // Same rule as --render above: a default output belongs where git ignores it (T198).
            ContextCli.WriteContextReport(args.Length >= 2 ? args[1] : @"docs\_preview\context-report.md");
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

        // Dev/preview helper: show a toast card with sample data so the variants can be seen /
        // screenshotted standalone. Which card is one row of ToastPreviews, the table --capture-toast reads
        // too; a name it does not know is refused with the catalogue rather than defaulted (T198).
        if (args.Length >= 1 && args[0] == "--simulate-reset")
        {
            PreviewCli.SimulateReset(args.Length >= 2 ? args[1] : null);
            return;
        }

        // Dev/preview helper: render a toast card (card + shadow + confetti) to a transparent PNG, so the
        // variants can be documented cleanly on any background. Args: <variant> <outPath>, and **both are
        // required**: with the path optional, `--capture-toast D:\tmp\out.png` read the path as the variant
        // and wrote the wrong card to `toast.png` in the working directory, which here is the repository
        // root, one commit away from being published (T198).
        if (args.Length >= 1 && args[0] == "--capture-toast")
        {
            if (args.Length < 3)
            {
                Console.WriteLine(@"usage: --capture-toast <variant> <out.png>");
                Console.WriteLine("Both are required. An output path this flag chose itself is a file the " +
                                  "caller never named, and the default landed in the working directory (T198).");
                ToastPreviews.PrintCatalog();
                Environment.ExitCode = 1;
                return;
            }
            PreviewCli.CaptureToast(args[1], args[2]);
            return;
        }

        // Ask every card whether it fits, in the language this process is in (T257). The refusal T228 put
        // in front of a capture only ever fires where somebody wanted a picture, so the card nobody was
        // photographing is the one that shipped clipped — this is the thing that asks all of them.
        if (args.Length >= 1 && args[0] == "--check-toasts")
        {
            PreviewCli.CheckToasts();
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

        // The script that makes two profiles one setup (T367). Composed and handed over, never run — the
        // app has no write path into `~\.claude` (§I.4), and the merge each link needs first is the part
        // a person has to read before it happens.
        if (args.Length >= 1 && args[0] == "--link-profiles")
        {
            ProfilesCli.PrintLinkScript(args.Skip(1).ToArray());
            return;
        }

        // The tray tooltip's text for a synthetic reading (T214). The one published surface no capture
        // flag can photograph — it is drawn by the shell, not by this app — so this reads it out instead.
        if (args.Length >= 1 && args[0] == "--tooltip")
        {
            Environment.ExitCode = TooltipCli.Run(args);
            return;
        }

        // ...and the same text drawn into the shell's card, so the one published picture that was a
        // hand-taken photograph becomes something any machine can regenerate (T214).
        if (args.Length >= 1 && args[0] == "--capture-tooltip")
        {
            Environment.ExitCode = TooltipCli.Capture(args);
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
            if (RefusedName(PageArg(args), SettingsPage.Pages, "settings page")) return;
            _ = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            WpfInputBridge.Install();
            var trayHosted = new PageWindow(
                new SettingsPage(Settings.Load(), (_, _) => { }, PageArg(args),
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
            if (RefusedName(args.Length >= 2 ? args[1] : null, MainWindow.Destinations, "destination")) return;
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
                () => mainSettings, (_, _) => { });
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
            if (RefusedName(PageArg(args), SettingsPage.Pages, "settings page")) return;
            if (!TrySampleProfiles(args, out List<ClaudeInfo>? settingsSample)) return;
            var previewApp = new System.Windows.Application();
            var page = new SettingsPage(Settings.Load(), (_, _) => { }, PageArg(args),
                settingsSample, args.Contains("--reveal"));
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
            if (!TrySampleProfiles(args, out List<ClaudeInfo>? statsSample)) return;
            if (statsSample is { } samplePicker)
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
            string? page = PageArg(args.Skip(1).ToArray());
            // First of all, and before the fixture is built: a name this build does not have would
            // otherwise render General into the file the caller named and say `wrote` about it (T262).
            if (RefusedName(page, SettingsPage.Pages, "settings page")) return;
            if (!TrySampleProfiles(args, out List<ClaudeInfo>? captureSample)) return;
            string outPath = System.IO.Path.GetFullPath(args[1]);

            // Before anything is rendered or any file is created: the one page whose content IS an
            // account may not be written to a PNG off this machine's real login (T205).
            if (AccountFixture.CaptureRefusal(page, args.Contains("--sample")) is { } refusal)
            {
                Console.WriteLine(refusal);
                Environment.ExitCode = 1;
                return;
            }
            double scroll = ArgValue(args, "scroll") is { } s && double.TryParse(s, out double d) ? d : 0;
            int profile = ArgValue(args, "profile") is { } pi && int.TryParse(pi, out int n) ? n : -1;

            var previewApp = new System.Windows.Application
            {
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
            };
            var settingsPage = new SettingsPage(Settings.Load(), (_, _) => { }, page,
                captureSample, args.Contains("--reveal"));
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

            // `card=<x:Name>` instead of a dip, and the two are exclusive: a caller that passes both has
            // said two different things about the same frame, and picking one silently is how a screenshot
            // of the wrong region gets published (T375).
            string? card = ArgValue(args, "card");
            if (card is { Length: > 0 } && scroll > 0)
            {
                Console.WriteLine("scroll= and card= both name the frame; pass one. card= is the one that "
                                  + "does not go stale when the page above it gains a line.");
                Environment.ExitCode = 1;
                return;
            }

            var settle = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            settle.Tick += (_, _) =>
            {
                settle.Stop();
                try
                {
                    if (card is { Length: > 0 })
                    {
                        // Resolved before anything is written: an unknown name must not produce a file at
                        // all, or the caller reads `wrote` about a picture of the default scroll position.
                        (double offset, SettingsPage.Framing frame, string? refusal) = settingsPage.FrameFor(card);
                        if (refusal is not null)
                        {
                            Console.WriteLine(refusal);
                            Environment.ExitCode = 1;
                            return;
                        }
                        scroll = offset;
                        Console.WriteLine(frame.Line);
                    }
                    settingsPage.SaveSnapshot(outPath, scroll);
                    Console.WriteLine("wrote " + outPath);
                }
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
            // A fixture that failed to build must not fall back to discovery, which is the leak T197 is
            // about arriving through the error path instead of the happy one — hence TrySampleProfiles.
            if (!TrySampleProfiles(args, out List<ClaudeInfo>? pickerProfiles)) return;
            pickerProfiles ??= choice.Variant.Name.Length == 0
                ? ClaudeAccount.Discover(Settings.Load().Profiles)
                : null;

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
                // T298. The settle above is a fixed 2.5s, and the report is not: it is computed on a task
                // and swapped in when it resolves, so on a machine where that takes longer the capture
                // photographed "Computing your consumption pace…" and announced four PNGs — twice in five
                // runs while T295 was being shipped, caught only by looking at the picture.
                //
                // Behind `refresh`, deliberately. That modifier exists to snapshot *mid-recompute* (T118),
                // which is exactly what every other run must stop calling a success — so the one flag that
                // asks for the in-progress page is the one this does not refuse.
                if (!choice.Refresh && !statsPage.WaitForReport())
                {
                    Console.Error.WriteLine(
                        "the report never finished, so nothing was written: the page is still showing its " +
                        "computing line, and a PNG of that is not a slower picture of the report, it is " +
                        "none (T298). Re-run, or add the `refresh` modifier if the in-progress page is " +
                        "what you meant to capture.");
                    Environment.ExitCode = 1;
                    previewApp.Shutdown();
                    return;
                }
                if (choice.Refresh) statsPage.UpdateSnapshot(choice.Variant.Snapshot(now));
                try
                {
                    // T343. The report's wait above refuses the whole run; this one cannot, because the
                    // sessions scan spoils one PNG of four and the other three are pictures somebody
                    // asked for. So the announcement is the files that exist — not a fixed list of four
                    // — and a pane that missed its deadline is named rather than left as a gap the
                    // reader has to notice. `refresh` is not exempt here as it is for the report: that
                    // modifier asks for a mid-recompute pace page (T118), not an unfinished scan of the
                    // transcripts.
                    StatisticsPage.TabCapture shot = statsPage.SaveAllTabs(outBase);
                    Console.WriteLine("wrote " + outBase + string.Join(" / ", shot.Written));
                    if (shot.Skipped is { } missing)
                    {
                        Console.Error.WriteLine(
                            "the transcript scan never finished, so " + outBase + missing + " was NOT " +
                            "written: the pane is still showing its reading line, and a PNG of that is not " +
                            "a slower picture of the sessions list, it is none (T343). The tabs named above " +
                            "did render. Re-run to capture the last one.");
                        Environment.ExitCode = 1;
                    }
                }
                finally { previewApp.Shutdown(); }
            };
            settle.Start();
            previewApp.Run();
            return;
        }

        // Single instance: if the tray app is already running, just exit — don't add a second icon. The
        // one exception is `--second-tray` (T237), which is asked for explicitly and never inferred: the
        // mutex is still taken when it is free, so an ordinary launch after this one still finds it held.
        SecondTray = args.Contains("--second-tray");
        // A tray launched beside the user's own is a check driving the build, not a second copy of the
        // app entitled to keep books (T239). Before Application.Run, so nothing it does on startup —
        // the first poll, the environment reconcile, an auto-follow save — reaches their files.
        if (SecondTray) ProfileStore.Observe();
        _instanceMutex = new Mutex(initiallyOwned: true, @"Local\ClaudeTray.SingleInstance", out bool createdNew);
        if (!createdNew && !SecondTray)
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
    /// Refuse a page or destination name this build does not have, printing what it does (T262) — the same
    /// rule and the same shape as every other named token here: an unknown preview variant (T186), toast
    /// (T198), <c>week=</c> (T200), <c>--sample-env</c> mode (T231), <c>--lang</c> code (T260).
    ///
    /// <para>This one was the last left guessing and the worst to have been: it is the only one that
    /// <b>writes a file</b> while wrong. <c>--capture-settings &lt;out&gt; NoSuchPage --sample</c> printed
    /// <c>wrote &lt;out&gt;</c>, exited 0, and left a picture of General under the name the caller chose,
    /// with nothing in the output saying which page it was. <c>--main NoSuchDest</c> opened Statistics.</para>
    ///
    /// <para><c>null</c> - no name given at all - is not refused: naming nothing is a request for the
    /// default, not a typo.</para>
    /// </summary>
    private static bool RefusedName(string? given, IReadOnlyList<string> known, string what)
    {
        if (given is null || known.Any(k => string.Equals(k, given, StringComparison.OrdinalIgnoreCase)))
            return false;

        // The dash below survives because Main put the console on UTF-8 before any verb ran (T283).
        Console.WriteLine($"unknown {what} '{given}'. Refusing rather than opening the default one, which");
        Console.WriteLine("is how a capture of the wrong page gets taken (T186, T198, T260 — same rule).");
        Console.WriteLine();
        Console.WriteLine($"{what}s:");
        foreach (string k in known) Console.WriteLine("  " + k);
        Environment.ExitCode = 1;
        return true;
    }

    /// <summary>
    /// The account fixture behind <c>--sample</c> (T121): two synthetic profiles — a personal Max 20x
    /// and a Team seat — read through the real <see cref="ClaudeAccount"/> path, so the System
    /// information page can be screenshotted without a real login on screen. Null without the flag,
    /// which is every non-preview run.
    ///
    /// <para><c>week=&lt;name&gt;</c> picks which stored week the personal profile gets (T200), which is
    /// what decides the System page's extra-usage row; <see cref="AccountFixture.ResolveWeek"/> refuses a
    /// name it does not know rather than building the default.</para>
    /// </summary>
    private static List<ClaudeInfo>? SampleProfiles(string[] args)
    {
        if (!args.Contains("--sample")) return null;
        if (AccountFixture.ResolveWeek(ArgValue(args, "week")) is not { } week) return null;
        try { return AccountFixture.Build(DateTimeOffset.UtcNow.UtcDateTime, week); }
        catch (Exception e)
        {
            Console.WriteLine("error building the sample account fixture: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// <see cref="SampleProfiles"/>, plus the rule that makes it safe to call: when <c>--sample</c> was
    /// asked for and could not be honoured, the run <b>stops</b> instead of continuing without it.
    ///
    /// <para>Every one of these previews renders this machine's real account when handed no fixture, so a
    /// fixture that failed to build silently becomes the published-real-name leak the fixture exists to
    /// prevent — reachable, before this guard, by a typo in <c>week=</c> (T200). Returns false having
    /// already printed the reason and set a non-zero exit code.</para>
    /// </summary>
    private static bool TrySampleProfiles(string[] args, out List<ClaudeInfo>? profiles)
    {
        profiles = SampleProfiles(args);
        if (profiles is null && args.Contains("--sample"))
        {
            Environment.ExitCode = 1;
            return false;
        }
        return true;
    }



























}
