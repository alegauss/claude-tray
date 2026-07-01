using System.Diagnostics;
using System.Runtime.InteropServices;
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

internal static class Program
{
    // Held for the whole process lifetime so a second launch can't spawn a duplicate tray icon.
    private static Mutex? _instanceMutex;

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--render")
        {
            RenderTest(args.Length >= 2 ? args[1] : ".");
            return;
        }

        if (args.Length >= 1 && args[0] == "--makeicon")
        {
            MakeIcon(args.Length >= 2 ? args[1] : "ClaudeTray.ico");
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

        // Dev/preview helper: show a reset toast with sample data so the variants can be seen /
        // screenshotted standalone. Optional second arg: "scheduled", "credit", or "unexpected" (default).
        if (args.Length >= 1 && args[0] == "--simulate-reset")
        {
            SimulateReset(args.Length >= 2 ? args[1] : "unexpected");
            return;
        }

        // Dev/preview helper: render a reset toast (card + shadow + confetti) to a transparent PNG,
        // so the four variants can be documented cleanly on any background. Args: <variant> <outPath>.
        if (args.Length >= 1 && args[0] == "--capture-toast")
        {
            CaptureToast(args.Length >= 2 ? args[1] : "unexpected",
                args.Length >= 3 ? args[2] : "toast.png");
            return;
        }

        // Dev/preview helper: open just the Settings window, standalone, so the UI can be launched
        // and screenshotted deterministically without going through the tray menu.
        if (args.Length >= 1 && args[0] == "--settings")
        {
            var previewApp = new System.Windows.Application();
            var win = new SettingsWindow(Settings.Load(), _ => { }, args.Length >= 2 ? args[1] : null);
            previewApp.Run(win);
            return;
        }

        // Single instance: if the tray app is already running, just exit — don't add a second icon.
        _instanceMutex = new Mutex(initiallyOwned: true, @"Local\ClaudeTray.SingleInstance", out bool createdNew);
        if (!createdNew)
            return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }

    // Dev helper: show a reset toast with sample data so it can be previewed / screenshotted
    // standalone. Uses the same display strings as the live notifier. `variant`: "scheduled" (routine
    // weekly reset), "credit" (partial mid-window drop 91% → 50%), "session" (routine 5h session
    // reset), or anything else for the early weekly reset.
    private static void SimulateReset(string variant)
    {
        var app = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
        };
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var (key, ev) = variant.ToLowerInvariant() switch
        {
            "scheduled" => ("7d", new BurnTracker.ResetEvent(BurnTracker.ResetKind.Scheduled, 0.79, 0.0, now, now + 7 * 86400)),
            "credit" => ("7d", new BurnTracker.ResetEvent(BurnTracker.ResetKind.Credit, 0.91, 0.50, now + 4 * 86400, now + 4 * 86400)),
            "session" => ("5h", new BurnTracker.ResetEvent(BurnTracker.ResetKind.Scheduled, 0.88, 0.0, now, now + 5 * 3600)),
            _ => ("7d", new BurnTracker.ResetEvent(BurnTracker.ResetKind.Unexpected, 0.79, 0.0, now + 3 * 86400, now + 7 * 86400)),
        };
        var (emoji, title, subtitle, fromUsage, toUsage, caption, quotaLabel, theme) = TrayContext.ResetToastContent(key, ev, now);

        var toast = new ToastWindow(emoji, title, subtitle, fromUsage, toUsage, caption, quotaLabel, theme);
        toast.Closed += (_, _) => app.Shutdown();
        toast.Show();
        app.Run();
    }

    // Dev/preview helper: same sample data as SimulateReset, but instead of leaving the toast on
    // screen it waits for the entrance + bar-fill animations to settle, snapshots the card to a
    // transparent PNG (so it composites on any README/site background), and exits.
    private static void CaptureToast(string variant, string outPath)
    {
        var app = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
        };
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var (key, ev) = variant.ToLowerInvariant() switch
        {
            "scheduled" => ("7d", new BurnTracker.ResetEvent(BurnTracker.ResetKind.Scheduled, 0.79, 0.0, now, now + 7 * 86400)),
            "credit" => ("7d", new BurnTracker.ResetEvent(BurnTracker.ResetKind.Credit, 0.91, 0.50, now + 4 * 86400, now + 4 * 86400)),
            "session" => ("5h", new BurnTracker.ResetEvent(BurnTracker.ResetKind.Scheduled, 0.88, 0.0, now, now + 5 * 3600)),
            _ => ("7d", new BurnTracker.ResetEvent(BurnTracker.ResetKind.Unexpected, 0.79, 0.0, now + 3 * 86400, now + 7 * 86400)),
        };
        var (emoji, title, subtitle, fromUsage, toUsage, caption, quotaLabel, theme) = TrayContext.ResetToastContent(key, ev, now);

        var toast = new ToastWindow(emoji, title, subtitle, fromUsage, toUsage, caption, quotaLabel, theme);
        toast.Show();
        var settle = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1700), // entrance (420ms) + bar fill (550+900ms)
        };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            try { toast.SaveSnapshot(System.IO.Path.GetFullPath(outPath)); Console.WriteLine("wrote " + System.IO.Path.GetFullPath(outPath)); }
            finally { app.Shutdown(); }
        };
        settle.Start();
        app.Run();
    }

    // Dev helper: dump sample icons as PNG at real tray sizes for visual inspection.
    private static void RenderTest(string dir)
    {
        Directory.CreateDirectory(dir);
        (double pct, IconRenderer.State st, bool fl, Projection verdict)[] cases =
        {
            (0.08, IconRenderer.State.Ok, false, Projection.Unknown),
            (0.08, IconRenderer.State.Ok, false, Projection.Ok),
            (0.54, IconRenderer.State.Ok, false, Projection.Danger),
            (1.00, IconRenderer.State.Ok, true, Projection.Danger),
        };
        foreach (int size in new[] { 16, 20, 32 })
            foreach (var (pct, st, fl, verdict) in cases)
                using (var bmp = IconRenderer.Render(pct, st, fl, size, verdict))
                    bmp.Save(Path.Combine(dir, $"icon_{(int)(pct * 100)}_{size}.png"));
        Console.WriteLine("rendered to " + Path.GetFullPath(dir));
    }

    // Dev helper: build a multi-resolution .ico for the app (PNG-compressed entries, valid on
    // Windows Vista+) from the GDI+ logo renderer.
    private static void MakeIcon(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        byte[][] pngs = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            using Bitmap bmp = IconRenderer.RenderLogo(sizes[i]);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngs[i] = ms.ToArray();
        }

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((short)0);              // reserved
        bw.Write((short)1);              // type: icon
        bw.Write((short)sizes.Length);   // image count

        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            bw.Write((byte)(s >= 256 ? 0 : s)); // width (0 = 256)
            bw.Write((byte)(s >= 256 ? 0 : s)); // height
            bw.Write((byte)0);                  // palette
            bw.Write((byte)0);                  // reserved
            bw.Write((short)1);                 // color planes
            bw.Write((short)32);                // bits per pixel
            bw.Write(pngs[i].Length);           // image size in bytes
            bw.Write(offset);                   // image offset
            offset += pngs[i].Length;
        }
        foreach (byte[] png in pngs) bw.Write(png);

        Console.WriteLine("wrote " + Path.GetFullPath(path));
    }
}

internal sealed class TrayContext : ApplicationContext
{
    private static readonly string[] Metrics = { "5h", "7d", "extra" };
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["5h"] = "Session 5h", ["7d"] = "Week 7d", ["extra"] = "Extra",
    };

    private readonly NotifyIcon _tray;
    private readonly ApiClient _api = new();
    private readonly BurnTracker _burn = new();
    private readonly Updater _updater = new();
    private readonly Settings _settings = Settings.Load();
    private volatile InsightsData? _insights;
    private readonly System.Windows.Forms.Timer _poll = new(); // interval set from settings
    private readonly System.Windows.Forms.Timer _flash = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _updateCheck = new() { Interval = 21_600_000 }; // 6 h
    private readonly List<ToolStripMenuItem> _metricItems = new();
    private ToolStripMenuItem _updateItem = null!;

    private UsageData? _data;
    private DateTime? _lastRefresh;
    private UpdateInfo? _update;
    private string _metric;
    private bool _flashOn;
    private bool _updating;
    private bool _autoOpenedForAuth; // guards auto-open so we launch once per signed-out spell
    private int _consecutiveErrors;  // transient failures since the last good poll
    private IntPtr _iconHandle = IntPtr.Zero;

    private const int ErrorTolerance = 2; // transient blips to ride out before showing an error

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public TrayContext()
    {
        _metric = _settings.Metric; // restore the last-selected window (BuildMenu reads it)
        _tray = new NotifyIcon
        {
            Visible = true,
            Text = "Claude Code — connecting…",
            ContextMenuStrip = BuildMenu(),
        };
        Render(); // initial "connecting" icon

        // A double-click on the tray icon opens Claude Code (same as the menu item).
        _tray.DoubleClick += (_, _) => OpenClaudeCode();

        _poll.Interval = _settings.RefreshSeconds * 1000;
        _poll.Tick += async (_, _) => await RefreshAsync();
        _poll.Start();
        _flash.Tick += (_, _) => { if (CurrentPct() >= 0.90) { _flashOn = !_flashOn; Render(); } };
        _flash.Start();
        _updateCheck.Tick += async (_, _) => await CheckForUpdateAsync();
        _updateCheck.Start();

        _tray.BalloonTipClicked += (_, _) => { if (_update != null) _ = ApplyUpdateAsync(); };

        _ = RefreshAsync(); // fire first fetch immediately
        _ = CheckForUpdateAsync(); // look for a newer release on launch
        RecomputeInsights(); // build the 24h usage breakdown in the background
    }

    // Scan local transcripts off the UI thread; the submenu reads the cached result.
    private void RecomputeInsights()
        => _ = Task.Run(() => _insights = UsageInsights.Compute(DateTimeOffset.UtcNow.UtcDateTime));

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var showOn = new ToolStripMenuItem("Show on icon");
        foreach (string key in Metrics)
        {
            var item = new ToolStripMenuItem(Labels[key]) { Tag = key, Checked = key == _metric };
            item.Click += (_, _) => SetMetric((string)item.Tag!);
            _metricItems.Add(item);
            showOn.DropDownItems.Add(item);
        }
        menu.Items.Add(showOn);

        var insights = new ToolStripMenuItem("Usage insights (24h)");
        insights.DropDownOpening += (_, _) => PopulateInsights(insights);
        insights.DropDownItems.Add(new ToolStripMenuItem("…") { Enabled = false });
        menu.Items.Add(insights);

        var refresh = new ToolStripMenuItem("Refresh now");
        refresh.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refresh);

        // Launches the Claude Code CLI so it can refresh the OAuth token in
        // ~/.claude/.credentials.json — the recovery path when a poll hits HTTP 401.
        var openClaude = new ToolStripMenuItem("Open Claude Code");
        openClaude.Click += (_, _) => OpenClaudeCode();
        menu.Items.Add(openClaude);

        // Hidden until a newer release is found; then shows "Update to vX.Y.Z".
        _updateItem = new ToolStripMenuItem("Update available") { Visible = false, Font = new Font(menu.Font, FontStyle.Bold) };
        _updateItem.Click += (_, _) => { if (!_updating) _ = ApplyUpdateAsync(); };
        menu.Items.Add(_updateItem);

        var settings = new ToolStripMenuItem("Settings…");
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);

        menu.Items.Add(new ToolStripSeparator());

        var quit = new ToolStripMenuItem("Quit");
        quit.Click += (_, _) => ExitApp();
        menu.Items.Add(quit);

        return menu;
    }

    private void SetMetric(string key)
    {
        _metric = key;
        foreach (var it in _metricItems)
            it.Checked = (string)it.Tag! == key;
        _settings.Metric = key;
        try { _settings.Save(); } catch { /* non-fatal: selection still applies this session */ }
        Render();
    }

    // The settings window is shown non-modally; keep a reference so we reuse the open one
    // instead of stacking duplicates.
    private SettingsWindow? _settingsWindow;

    // A WPF Application must exist before any WPF window (Settings, the reset toast) is shown on this
    // thread, for the Fluent theme and pack-URI resources to resolve. Hosted as a single instance for
    // the process lifetime but never Run() — the WinForms message loop (Application.Run(TrayContext))
    // already pumps messages for both UI stacks on this thread.
    private static System.Windows.Application? _wpfApp;

    private void EnsureWpfApp()
    {
        if (_wpfApp is null && System.Windows.Application.Current is null)
            _wpfApp = new System.Windows.Application
            {
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
            };
    }

    // Open the settings window (non-modal); on Save it calls ApplySettings to persist and apply.
    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == System.Windows.WindowState.Minimized)
                _settingsWindow.WindowState = System.Windows.WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        EnsureWpfApp();

        _settingsWindow = new SettingsWindow(_settings, ApplySettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    // Persist the edited settings and apply the new values immediately.
    private void ApplySettings(Settings updated)
    {
        _settings.RefreshSeconds = updated.RefreshSeconds;
        _settings.ShowPercentage = updated.ShowPercentage;
        _settings.ShowRemaining = updated.ShowRemaining;
        _settings.NotifyOnUnexpectedReset = updated.NotifyOnUnexpectedReset;
        _settings.NotifyOnScheduledReset = updated.NotifyOnScheduledReset;
        _settings.NotifyOnSessionReset = updated.NotifyOnSessionReset;
        _settings.SessionResetMinPercent = updated.SessionResetMinPercent;
        _settings.ScheduledResetMinPercent = updated.ScheduledResetMinPercent;
        _settings.ClaudeCodeDirectory = updated.ClaudeCodeDirectory;
        _settings.AutoOpenOnUnauthenticated = updated.AutoOpenOnUnauthenticated;
        _settings.AuthRetrySeconds = updated.AuthRetrySeconds;

        try { _settings.Save(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save settings:\n{ex.Message}",
                "Claude Code Tray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Re-arm the poll cadence for the current auth state (handles a changed interval, retry
        // cadence, or a freshly enabled auto-open) and reflect a show-percentage change.
        AdjustForAuthState();
        Render();
    }

    // Fill the "Usage insights" submenu from the cached scan; trigger a refresh for next time.
    private void PopulateInsights(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();
        InsightsData? d = _insights;

        if (d == null)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem("Computing…") { Enabled = false });
        }
        else if (d.Error != null)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem($"Unavailable: {d.Error}") { Enabled = false });
        }
        else if (d.Requests == 0)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem("No usage in the last 24h") { Enabled = false });
        }
        else
        {
            void Line(string text) => parent.DropDownItems.Add(new ToolStripMenuItem(text) { Enabled = false });

            Line($"Last 24h: {d.Requests} requests, {d.Sessions} sessions");
            Line($"From subagents: {Pct(d.SubagentPct)}");
            Line($">150k context: {Pct(d.HeavyContextPct)}");
            if (d.ByModel.Count > 0)
            {
                parent.DropDownItems.Add(new ToolStripSeparator());
                Line("By model:");
                foreach (var (model, pct) in d.ByModel.Take(5))
                    Line($"   {model}: {Pct(pct)}");
            }
        }

        parent.DropDownItems.Add(new ToolStripSeparator());
        var refresh = new ToolStripMenuItem("Recompute");
        refresh.Click += (_, _) => RecomputeInsights();
        parent.DropDownItems.Add(refresh);

        // Keep the cache reasonably fresh for the next open.
        RecomputeInsights();
    }

    private async Task RefreshAsync()
    {
        UsageData fresh = await _api.FetchAsync();
        bool ok = fresh.Error == null;
        bool transientHiccup = !ok && fresh.Transient && !fresh.Unauthorized;
        _consecutiveErrors = transientHiccup ? _consecutiveErrors + 1 : 0;

        // A single timeout or network blip shouldn't flip the icon to a scary red error: keep the
        // last good reading (or the "connecting…" state) on screen and quietly retry on the next
        // poll. Only surface the error once it persists across a few polls.
        bool keepLastGood = transientHiccup
                            && _consecutiveErrors < ErrorTolerance
                            && _data is null or { Error: null };
        if (!keepLastGood)
        {
            _data = fresh;
            _lastRefresh = DateTime.Now;
        }

        if (ok)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (string key in Metrics)
            {
                BurnTracker.ResetEvent? ev = _burn.Record(key, fresh.Metric(key), fresh.ResetOf(key), now);
                if (ev is not { } reset) continue;

                // Each window decides by its own opt-in. Weekly: the routine on-time reset uses the
                // quiet "scheduled" toggle, while the anomalous events (an early reset or a mid-window
                // credit) ride the "unexpected" toggle. Session (5h): a single toggle for any reset.
                // "extra" is overage, not a scheduled window — never notified.
                // The routine resets (weekly Scheduled, 5h session) also honor a minimum-usage floor:
                // a reset from a barely-touched window isn't worth a ping. PrevUtil is a 0–1 fraction.
                bool wanted = key switch
                {
                    "7d" => reset.Kind == BurnTracker.ResetKind.Scheduled
                        ? _settings.NotifyOnScheduledReset && reset.PrevUtil * 100 > _settings.ScheduledResetMinPercent
                        : _settings.NotifyOnUnexpectedReset,
                    "5h" => _settings.NotifyOnSessionReset && reset.PrevUtil * 100 > _settings.SessionResetMinPercent,
                    _ => false,
                };
                if (wanted) NotifyReset(key, reset, now);
            }
        }
        _flashOn = false;
        Render();
        RecomputeInsights();
        AdjustForAuthState();
    }

    // While signed out, re-poll on the (faster) retry cadence so a fresh token is noticed quickly,
    // and — if enabled — launch Claude Code once to prompt re-auth. As soon as a poll succeeds,
    // restore the normal cadence and re-arm the one-shot auto-open for the next signed-out spell.
    private void AdjustForAuthState()
    {
        bool unauthorized = _data is { Unauthorized: true };

        int desiredMs = (unauthorized ? _settings.AuthRetrySeconds : _settings.RefreshSeconds) * 1000;
        if (_poll.Interval != desiredMs)
        {
            _poll.Stop();
            _poll.Interval = desiredMs;
            _poll.Start();
        }

        if (!unauthorized)
        {
            _autoOpenedForAuth = false;
        }
        else if (_settings.AutoOpenOnUnauthenticated && !_autoOpenedForAuth)
        {
            _autoOpenedForAuth = true;
            OpenClaudeCode(forReauth: true);
        }
    }

    // Ask GitHub for the latest release; if newer, surface it in the menu and notify once.
    private async Task CheckForUpdateAsync()
    {
        if (_updating) return;
        UpdateInfo? info = await _updater.CheckAsync();
        if (info == null || (_update != null && info.Version <= _update.Version)) return;

        _update = info;
        _updateItem.Text = $"Update to {info.Tag}";
        _updateItem.Visible = true;

        _tray.BalloonTipTitle = "Claude Code Tray — update available";
        _tray.BalloonTipText = $"Version {info.Version} is available (you have {Updater.CurrentVersion}). Click to install.";
        _tray.ShowBalloonTip(10_000);
    }

    // Download the installer and hand off to it; the app exits so its .exe can be replaced.
    private async Task ApplyUpdateAsync()
    {
        if (_updating || _update is not { } info) return;
        _updating = true;
        _updateItem.Text = $"Downloading {info.Tag}…";
        _updateItem.Enabled = false;

        try
        {
            string setup = await _updater.DownloadAsync(info);
            Updater.RunInstaller(setup);
            ExitApp(); // release the single-instance mutex and unlock the .exe for the installer
        }
        catch (Exception ex)
        {
            _updating = false;
            _updateItem.Text = $"Update to {info.Tag}";
            _updateItem.Enabled = true;
            MessageBox.Show($"Could not download the update:\n{ex.Message}",
                "Claude Code Tray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // A usage window fell — a full reset (early or on schedule) or a partial mid-window credit.
    // Show the matching toast and append a timestamped line to a local log so the event can be
    // reported later with concrete before/after numbers. `key` is the window ("5h" or "7d").
    private void NotifyReset(string key, BurnTracker.ResetEvent ev, long now)
    {
        LogResetEvent(key, ev, now);
        var (emoji, title, subtitle, fromUsage, toUsage, caption, quotaLabel, theme) = ResetToastContent(key, ev, now, _settings.ShowRemaining);
        try
        {
            EnsureWpfApp();
            new ToastWindow(emoji, title, subtitle, fromUsage, toUsage, caption, quotaLabel, theme).Show();
        }
        catch
        {
            // If the custom toast can't be shown, fall back to the plain system balloon so the
            // event is never silently dropped.
            _tray.BalloonTipTitle = $"{emoji} {title}";
            _tray.BalloonTipText = $"{subtitle} — {caption}";
            _tray.BalloonTipIcon = ToolTipIcon.Info;
            _tray.ShowBalloonTip(10_000);
        }
    }

    // The toast's display strings per window + kind, shared by the live notifier and the
    // --simulate-reset dev preview so the wording can never drift. All are framed as good news (quota
    // back); the early reset gets the loud "Surprise!", the routine one a calm "New week!"/"Fresh
    // session!", a partial credit a "Bonus!". Returns the before/after usage fractions too, so the
    // toast bar lands on the real new level, plus the quota-bar label for the window.
    internal static (string emoji, string title, string subtitle, double fromUsage, double toUsage,
        string caption, string quotaLabel, ToastWindow.ToastTheme theme)
        ResetToastContent(string key, BurnTracker.ResetEvent ev, long now, bool showRemaining = false)
    {
        bool weekly = key == "7d";
        string quotaLabel = weekly ? "Weekly quota left" : "Session quota left";
        string limitNoun = weekly ? "weekly limit" : "5h session";
        string scopeWord = weekly ? "weekly" : "session";
        string freshSuffix = weekly ? "fresh quota for the week" : "fresh for the next 5 hours";
        string resetTitle = weekly ? "New week!" : "Fresh session!";
        int fromPct = (int)Math.Round(Math.Clamp(ev.PrevUtil, 0, 1) * 100);
        int toPct = (int)Math.Round(Math.Clamp(ev.NewUtil, 0, 1) * 100);
        // In "remaining" mode the captions read in quota-left terms (the bar/number already do):
        // "Was 79% used" → "Had 21% left", "Usage dropped 91% → 50%" → "Quota left rose 9% → 50%".
        int fromLeftPct = 100 - fromPct;
        int toLeftPct = 100 - toPct;
        string ahead = ev.PrevReset > now ? $"{FmtDays(ev.PrevReset - now)} ahead of schedule" : "ahead of schedule";
        string earlyCaption = showRemaining ? $"Had {fromLeftPct}% left · {ahead}" : $"Was {fromPct}% used · {ahead}";
        string creditCaption = showRemaining ? $"Quota left rose {fromLeftPct}% → {toLeftPct}%" : $"Usage dropped {fromPct}% → {toPct}%";
        string routineCaption = showRemaining ? $"Had {fromLeftPct}% left · {freshSuffix}" : $"Was {fromPct}% used · {freshSuffix}";

        // Color theme: a session event is always blue; otherwise the weekly kind picks the color
        // (early reset = clay/Surprise, credit = violet/Bonus, routine = teal/Weekly).
        ToastWindow.ToastTheme theme = !weekly
            ? ToastWindow.ToastTheme.Session
            : ev.Kind switch
            {
                BurnTracker.ResetKind.Unexpected => ToastWindow.ToastTheme.Surprise,
                BurnTracker.ResetKind.Credit => ToastWindow.ToastTheme.Bonus,
                _ => ToastWindow.ToastTheme.Weekly,
            };

        return ev.Kind switch
        {
            BurnTracker.ResetKind.Unexpected => ("🎉", "Surprise!", $"Your {limitNoun} reset early",
                ev.PrevUtil, ev.NewUtil, earlyCaption, quotaLabel, theme),
            BurnTracker.ResetKind.Credit => ("🎉", "Bonus!", $"Some {scopeWord} usage was credited back",
                ev.PrevUtil, ev.NewUtil, creditCaption, quotaLabel, theme),
            _ => ("✨", resetTitle, $"Your {limitNoun} just reset",
                ev.PrevUtil, ev.NewUtil, routineCaption, quotaLabel, theme),
        };
    }

    // Best-effort append to %LocalAppData%\ClaudeTray\reset-events.log; never let it disrupt a poll.
    private static void LogResetEvent(string key, BurnTracker.ResetEvent ev, long now)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeTray");
            Directory.CreateDirectory(dir);
            static string Iso(double unix) => DateTimeOffset.FromUnixTimeSeconds((long)unix).UtcDateTime.ToString("o");
            string line = System.FormattableString.Invariant(
                $"{Iso(now)}\t{key} {ev.Kind.ToString().ToLowerInvariant()} {(int)Math.Round(ev.PrevUtil * 100)}%->{(int)Math.Round(ev.NewUtil * 100)}%\tprevReset={Iso(ev.PrevReset)}\tnewReset={Iso(ev.NewReset)}");
            File.AppendAllText(Path.Combine(dir, "reset-events.log"), line + Environment.NewLine);
        }
        catch { /* logging is best-effort */ }
    }

    private double CurrentPct()
        => _data != null && _data.Error == null ? Math.Min(1.0, _data.Metric(_metric)) : 0.0;

    // Projection for the currently displayed metric (session vs. week vs. extra).
    private (Projection verdict, double eta) CurrentProjection()
    {
        if (_data is not { Error: null }) return (Projection.Unknown, 0);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Weekly uses the proportional "pace line" for its verdict (window = 7 days); the
        // other metrics keep the regression-based verdict (windowSeconds = 0).
        double window = _metric == "7d" ? 7.0 * 24 * 3600 : 0;
        var (verdict, eta, _) = _burn.Project(_metric, _data.Metric(_metric), _data.ResetOf(_metric), now, window);
        return (verdict, eta);
    }

    private void Render()
    {
        IconRenderer.State state =
            _data == null ? IconRenderer.State.Connecting :
            _data.Error != null ? IconRenderer.State.Error :
            IconRenderer.State.Ok;

        bool flash = CurrentPct() >= 0.90 && _flashOn;
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);

        Projection verdict = CurrentProjection().verdict;

        // Before we're connected to Claude Code — either still connecting (no data yet) or signed out
        // on a 401 (expired token) — show the Claude Code Tray logo rather than a gray "0" or a play
        // triangle. Once a poll succeeds the usage icon takes over.
        using Bitmap bmp =
            _data is { Unauthorized: true } || state == IconRenderer.State.Connecting
                ? IconRenderer.RenderLogo(size)
                : IconRenderer.Render(CurrentPct(), state, flash, size, verdict, _settings.ShowPercentage, _settings.ShowRemaining);
        SetTrayIcon(bmp);
        _tray.Text = Truncate(BuildTooltip(), 127);
    }

    private void SetTrayIcon(Bitmap bmp)
    {
        IntPtr newHandle = bmp.GetHicon();
        Icon? old = _tray.Icon;
        IntPtr oldHandle = _iconHandle;

        _tray.Icon = Icon.FromHandle(newHandle);
        _iconHandle = newHandle;

        old?.Dispose();
        if (oldHandle != IntPtr.Zero) DestroyIcon(oldHandle);
    }

    private string BuildTooltip()
    {
        if (_data == null) return "Claude Code — connecting…";
        if (_data.Error != null)
        {
            if (_data.Unauthorized)
                return _data.NeedsFullLogin
                    ? "Claude Code — not signed in\nOpen Claude Code and run /login to sign in"
                    : "Claude Code — not tracking yet\nOnce you start using Claude, your usage will appear here";
            return $"Claude Code — API error\n{_data.Error}";
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r5 = _data.Reset5h > 0 ? FmtCountdown(_data.Reset5h - now) : "--";
        string r7 = _data.Reset7d > 0 ? FmtDays(_data.Reset7d - now) : "--";

        // In "remaining" mode the two bounded windows show the complement and read "… left".
        // Extra is overage (no cap to have quota "left" from), so it always shows the amount used.
        string leftSuffix = _settings.ShowRemaining ? " left" : "";
        var lines = new List<string>
        {
            $"Session 5h{leftSuffix}: {PctShown(_data.Session5h)}  ⟳ {r5}",
            $"Week 7d{leftSuffix}: {PctShown(_data.Week7d)}  ⟳ {r7}",
        };
        if (_data.Extra > 0.001)
        {
            string re = _data.ResetExtra > 0 ? FmtDays(_data.ResetExtra - now) : "--";
            lines.Add($"Extra: {Pct(_data.Extra)}  ⟳ {re}");
        }

        var (verdict, eta) = CurrentProjection();
        string scope = Labels[_metric]; // make clear which window the projection is about
        bool hasEta = eta > 0 && !double.IsInfinity(eta);
        // The projection is about reaching the limit: "100%" used == "0% left" remaining.
        string limit = _settings.ShowRemaining ? "0% left" : "100%";
        // The same target as a *future event*. In "remaining" mode "0% left" mirrors the current
        // saldo lines above ("Session 5h left: 97%"), so it reads as a present value; the plain-
        // language "runs out" marks it as something that happens later. Used mode keeps the "100%"
        // percentage it always showed, which reads naturally as a ceiling you climb toward.
        string hits = _settings.ShowRemaining ? "runs out" : "100%";
        // Each projection verdict has a full form and a compact fallback for when the tooltip is
        // tight (see the 127-char cap note below). null => no projection line at all.
        (string full, string compact)? projection = CurrentPct() >= 0.995
            // Already maxed: state it plainly rather than "projecting" a limit you've reached.
            ? ($"⚠ {scope}: at limit ({limit})", $"⚠ at limit ({limit})")
            : verdict switch
            {
                Projection.Danger => hasEta
                    ? ($"⚠ {scope} projection: {hits} in {FmtDays(eta)} (before reset)", $"⚠ {hits} in {FmtDays(eta)}")
                    : ($"⚠ {scope} projection: above safe pace (before reset)", "⚠ above safe pace"),
                Projection.Ok => double.IsInfinity(eta)
                    ? ($"✓ {scope} projection: on track", "✓ on track")
                    : ($"✓ {scope} projection: {hits} in {FmtDays(eta)} (after reset)", $"✓ {hits} in {FmtDays(eta)}"),
                _ => null,
            };

        string updated = _lastRefresh is { } t ? $"  ⟳ {t:HH:mm:ss}" : "";
        string statusLine = $"Status: {_data.Status}{updated}";

        // The Windows tray tooltip is capped at 127 chars (NOTIFYICONDATA.szTip). The refresh
        // time sits on the last line, so a blind end-truncation would chop it mid-value. Keep the
        // status/time line intact and fit the projection in: full form if it fits, else compact.
        int used = lines.Sum(l => l.Length + 1) + statusLine.Length;
        if (projection is { } p)
        {
            if (used + p.full.Length + 1 <= 127) lines.Add(p.full);
            else if (used + p.compact.Length + 1 <= 127) lines.Add(p.compact);
        }
        lines.Add(statusLine);
        return string.Join("\n", lines);
    }

    private static string Pct(double v) => $"{(int)Math.Round(Math.Min(v, 1.0) * 100)}%";

    // A window's percentage as displayed: the used fraction, or its complement in "remaining" mode.
    private string PctShown(double used)
        => Pct(_settings.ShowRemaining ? Math.Clamp(1.0 - used, 0.0, 1.0) : used);

    private static string FmtCountdown(double s)
    {
        if (s <= 0) return "now";
        int h = (int)(s / 3600), m = (int)(s % 3600 / 60);
        return h > 0 ? $"{h}h {m:00}m" : $"{m}m";
    }

    private static string FmtDays(double s)
    {
        if (s <= 0) return "now";
        int d = (int)(s / 86400), h = (int)(s % 86400 / 3600);
        return d > 0 ? $"{d}d {h}h" : FmtCountdown(s);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    // Open the Claude Code CLI in a terminal. Starting it makes Claude Code validate and refresh
    // the OAuth token, which clears an expired-token (401) state for the next poll. `claude` is on
    // PATH for anyone who uses Claude Code, so the shell resolves it regardless of install method.
    private void OpenClaudeCode(bool forReauth = false)
    {
        try
        {
            // Two re-auth paths, decided by whether a refresh token is on disk (ApiClient sets
            // NeedsFullLogin): with one, just launching `claude` silently refreshes the access token;
            // without one, only a full /login re-authenticates (there's no CLI flag to force it), so we
            // print a hint to type /login above the prompt before launching claude in the same window.
            string command = !forReauth
                ? "/k claude"
                : _data is { NeedsFullLogin: true }
                    ? "/k echo Type  /login  to sign in again, then you can close this window. & claude"
                    : "/k echo Refreshing your Claude Code session — usage will update shortly. & claude";
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = command,
                UseShellExecute = true,
            };
            // Open in the configured working directory when set and still present; otherwise let
            // the OS default apply (the user's home directory).
            string dir = _settings.ClaudeCodeDirectory;
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                psi.WorkingDirectory = dir;
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not launch Claude Code automatically.\n" +
                "Open a terminal, run \"claude\", type /login to sign in, then choose \"Refresh now\".\n\n" + ex.Message,
                "Claude Code Tray", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ExitApp()
    {
        _poll.Stop();
        _flash.Stop();
        _updateCheck.Stop();
        _tray.Visible = false;
        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
        _tray.Dispose();
        ExitThread();
    }
}
