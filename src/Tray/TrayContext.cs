using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClaudeTray;

internal sealed class TrayContext : ApplicationContext
{
    private static readonly string[] Metrics = { "5h", "7d", "extra" };
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["5h"] = L.T("menu.metric.5h"), ["7d"] = L.T("menu.metric.7d"), ["extra"] = L.T("menu.metric.extra"),
    };

    private readonly NotifyIcon _tray;
    // Rebuilt by RefreshWatched: it reads the *monitored* profile's token (T127).
    private ApiClient _api = new();
    private readonly BurnTracker _burn = new();
    private readonly Updater _updater = new();
    // Not readonly: ApplySettings replaces the whole instance with a total copy of the edited one
    // (T141) instead of assigning field by field. The tray is the only holder of this reference — it is
    // handed to SettingsPage, which clones it — so a swap can't leave anyone reading a stale model.
    private Settings _settings = Settings.Load();
    private volatile InsightsData? _insights;
    private readonly System.Windows.Forms.Timer _poll = new(); // interval set from settings
    private readonly System.Windows.Forms.Timer _flash = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _updateCheck = new() { Interval = 21_600_000 }; // 6 h
    // The two weekly-scale samplers — context load (T79) and the activity grid (T91). Both describe a
    // habit rather than a moment, so they are sampled four times a day rather than per poll, and both
    // exist to keep accumulating while every window is closed: the drift history (ContextHistory) and
    // the activity grid the icon's verdict is projected along.
    private readonly System.Windows.Forms.Timer _backgroundSample = new() { Interval = 21_600_000 }; // 6 h
    private readonly List<ToolStripMenuItem> _metricItems = new();
    private ToolStripMenuItem _updateItem = null!;

    private UsageData? _data;
    private DateTime? _lastRefresh;
    // The last successful live reading, kept so the Statistics window can still draw its charts from
    // local data (usage-history + transcripts) during an API outage. The chart marks the unavailable
    // stretch itself, from the gap in the logged readings (see UsageReport.WindowPace.Gaps).
    private PaceSnapshot? _lastGoodSnapshot;
    private UpdateInfo? _update;
    private string _metric;
    private bool _flashOn;
    private bool _updating;
    private bool _autoOpenedForAuth; // guards auto-open so we launch once per signed-out spell
    private int _consecutiveErrors;  // transient failures since the last good poll
    private IntPtr _iconHandle = IntPtr.Zero;

    private const int ErrorTolerance = 2; // transient blips to ride out before showing an error

    // At/above this utilization (0–1) the included quota is spent. What that *means* is one of three
    // things and QuotaStates owns the decision (T182); this alias keeps the old call sites reading the
    // same, and keeps the poll's idle on the same number as the icon's verdict.
    private const double AtLimitThreshold = QuotaStates.AtLimitThreshold;
    private const int LimitResetBufferSeconds = 20; // wait past the predicted reset before double-checking
    private const int LimitDoubleCheckSeconds = 30; // retry cadence once a reset is due but not yet observed

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public TrayContext()
    {
        _metric = _settings.Metric; // restore the last-selected window (BuildMenu reads it)
        RefreshWatched();           // which profiles to poll, and which one the icon follows (T127)
        _tray = new NotifyIcon
        {
            Visible = true,
            Text = L.T("tip.connecting"),
            ContextMenuStrip = BuildMenu(),
        };
        Render(); // initial "connecting" icon

        // A left-click on the tray icon opens the window on the pace report. MouseClick with a
        // left-button filter keeps the right-click context menu untouched.
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenMain(); };

        _poll.Interval = _settings.RefreshSeconds * 1000;
        _poll.Tick += async (_, _) => await RefreshAsync();
        _poll.Start();
        _flash.Tick += (_, _) => { if (_settings.FlashNearLimit && CurrentPct() >= Settings.FlashWarnThreshold) { _flashOn = !_flashOn; Render(); } };
        _flash.Start();
        _updateCheck.Tick += async (_, _) => await CheckForUpdateAsync();
        _updateCheck.Start();
        _backgroundSample.Tick += (_, _) => { ScanContextInBackground(); WarmActivityProfile(); };
        _backgroundSample.Start();

        _tray.BalloonTipClicked += (_, _) => { if (_update != null) _ = ApplyUpdateAsync(); };

        _ = RefreshAsync(); // fire first fetch immediately
        _ = CheckForUpdateAsync(); // look for a newer release on launch
        RecomputeInsights(); // build the 24h usage breakdown in the background
        ScanContextInBackground(); // record context drift, and nudge if the user asked to be nudged
        WarmActivityProfile();     // keep the weekly shape fresh even if Statistics is never opened
    }

    /// <summary>
    /// Keep the weekly activity grid warm. <see cref="ActivityProfile.Load"/> was only ever called from
    /// <see cref="UsageReport.ComputePace"/>, which runs when the Statistics window is open — so a
    /// machine whose owner never opens it projects along a shape that ages indefinitely (the tray
    /// icon's verdict rests on that projection), and the first open on a fresh install waits on the
    /// full ~15s sweep in front of the chart.
    ///
    /// Nothing but the caller changes: <c>Load</c> already returns the cached grid immediately and
    /// recomputes behind it once past <see cref="ActivityProfile.RefreshHours"/>, so a warm pass here
    /// is a file read, and the window keeps reading whatever is on disk. The pool is not optional —
    /// the fresh-install path has no cache to return and computes synchronously.
    /// </summary>
    private static void WarmActivityProfile() => _ = Task.Run(() =>
    {
        try { ActivityProfile.Load(DateTimeOffset.UtcNow.UtcDateTime); }
        catch { /* a warm cache is an optimisation — never worth disturbing the app over */ }
    });

    /// <summary>
    /// Scan the context sources off the UI thread, record the drift sample, and — only if the user
    /// opted in — nudge once when a project crosses their threshold. The scan is cached on a
    /// path+size+mtime fingerprint, so a warm pass is ~100ms of stats and nothing else.
    /// </summary>
    private async void ScanContextInBackground()
    {
        // The scan runs on the pool; the toast has to be created back on the STA thread that owns the
        // message pump, which is what awaiting here buys us (the same shape as RefreshAsync).
        (ContextProject project, int eager)? nudge = await Task.Run(() =>
        {
            try
            {
                DateTime now = DateTimeOffset.UtcNow.UtcDateTime;
                ContextScan scan = ContextScanner.Scan(now);
                if (scan.Error != null) return ((ContextProject, int)?)null;

                ContextHistory.Record(ProfileStore.Monitored, scan, now);
                if (!_settings.NotifyOnContextGrowth) return null;

                foreach (ContextProject p in scan.Projects)
                {
                    int eager = scan.EstimatedSessionZero(p);
                    if (eager < _settings.ContextNudgeTokens) continue;
                    if (!ContextNudges.ShouldNotify(ProfileStore.Monitored, p.Slug, now)) continue;

                    ContextNudges.Mark(ProfileStore.Monitored, p.Slug, now);
                    return (p, eager);   // one nudge per pass, never a burst of them
                }
                return null;
            }
            catch { return null; }   // a background nudge is never worth disturbing the app over
        });

        if (nudge is { } n) NudgeContext(n.project, n.eager);
    }

    /// <summary>The last overage reading seen for the monitored profile, so a rise can be told from a
    /// state. Seeded from the store on the first poll of the process (see <see cref="NoteExtraUsage"/>):
    /// held only in memory, a restart mid-spell would read as a fresh transition and toast again.</summary>
    private double? _lastExtra;
    private bool _lastExtraSeeded;

    /// <summary>
    /// Announce the start of the meter, once (T184).
    ///
    /// <para><b>Why this interrupts at all.</b> The tray already interrupts to say quota came *back* —
    /// three reset notifications, on by default. It said nothing when the opposite happened, and that
    /// asymmetry is the whole point: the app was cheerful about good news and silent about the user's own
    /// money. This is not a second notification channel arguing for itself against the T87 non-goal (which
    /// rejects a *predicted*, continuous, activity-shaped nudge); it is the existing channel's missing
    /// half — a discrete transition, observed rather than forecast, at most once per spell.</para>
    ///
    /// <para><b>A rise, not a state.</b> It fires on the first reading above zero after one at zero.
    /// <c>null</c> is not zero (T179) and deliberately does not arm it: a profile whose history predates
    /// that field, or a response with no overage header, has not been observed at zero, and announcing a
    /// "start" from a reading nobody took is how a notification loses its credibility.</para>
    ///
    /// <para><b>What it must not say.</b> It fires exactly when somebody is most receptive to *the other
    /// profile still has quota* — the sentence the roadmap forbids as limit circumvention in a convenience
    /// costume. So the card names no profile, no account and no alternative, and offers no action. It
    /// states the fact and the reset, and stops: a receipt, not a reward, and not a suggestion.</para>
    /// </summary>
    private void NoteExtraUsage(double? extra, double resetExtra)
    {
        if (!_lastExtraSeeded)
        {
            // The reading before this process started, so a restart in the middle of an overage spell
            // does not look like its beginning.
            _lastExtraSeeded = true;
            try { _lastExtra = UsageHistory.Latest(ProfileStore.Monitored)?.Extra; } catch { _lastExtra = null; }
        }

        double? previous = _lastExtra;
        _lastExtra = extra;

        if (!_settings.NotifyOnExtraUsage) return;
        if (!QuotaStates.StartsSpending(previous, extra)) return;
        double now = extra!.Value;

        string title = L.T("toast.extra.title");
        string subtitle = L.T("toast.extra.subtitle");
        string caption = resetExtra > 0
            ? L.T("toast.extra.caption", FmtDays(resetExtra - DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
            : L.T("toast.extra.captionNoReset");
        try
        {
            EnsureWpfApp();
            // The card's bar renders "quota still available" (1 − x), so the complement is passed: the
            // filled sliver is then the extra usage spent so far, matching the label beside it.
            double bar = Math.Clamp(1 - now, 0, 1);
            new ToastWindow("🧾", title, subtitle, bar, bar, caption,
                L.T("toast.extra.quotaLabel"), ToastWindow.ToastTheme.ExtraUsage).Show();
        }
        catch
        {
            _tray.BalloonTipTitle = title;
            _tray.BalloonTipText = subtitle;
            _tray.ShowBalloonTip(8000);
        }
    }

    // The nudge itself, in the existing toast style: same card, same bar, ochre instead of festive,
    // and no confetti (see ToastWindow.OnLoaded).
    private void NudgeContext(ContextProject project, int eager)
    {
        int window = ContextScanner.ContextPageFor(project.Observed?.Model);
        double share = Math.Clamp((double)eager / window, 0, 1);
        double cost = eager * UsageInsights.Price(project.Observed?.Model ?? "").cw / 1_000_000.0;

        string title = L.T("toast.context.title");
        string subtitle = L.T("toast.context.subtitle", project.ShortPath);
        string caption = L.T("toast.context.caption",
            TokenEstimate.Format(eager), cost.ToString("0.000", L.Culture));
        try
        {
            EnsureWpfApp();
            new ToastWindow("📇", title, subtitle, share, share, caption,
                L.T("toast.context.quotaLabel"), ToastWindow.ToastTheme.Context).Show();
        }
        catch
        {
            _tray.BalloonTipTitle = title;
            _tray.BalloonTipText = $"{subtitle} — {caption}";
            _tray.BalloonTipIcon = ToolTipIcon.Info;
            _tray.ShowBalloonTip(10_000);
        }
    }

    // Scan local transcripts off the UI thread; the submenu reads the cached result.
    private void RecomputeInsights()
        => _ = Task.Run(() => _insights = UsageInsights.Compute(DateTimeOffset.UtcNow.UtcDateTime));

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        // The one entry that opens the window (on the pace report, where a left-click on the icon also
        // lands). Bold, because it is the menu's default action — and it is here at all because a
        // left-click is not a route everyone has: the keyboard's context-menu key opens this list and
        // nothing else. Statistics, Context and Settings used to be three entries opening three
        // windows; they are now three destinations inside this one.
        var open = new ToolStripMenuItem(L.T("menu.open"));
        open.Click += (_, _) => OpenMain();
        open.Font = new Font(menu.Font, FontStyle.Bold);
        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());

        var showOn = new ToolStripMenuItem(L.T("menu.showOnIcon"));
        foreach (string key in Metrics)
        {
            var item = new ToolStripMenuItem(Labels[key]) { Tag = key, Checked = key == _metric };
            item.Click += (_, _) => SetMetric((string)item.Tag!);
            _metricItems.Add(item);
            showOn.DropDownItems.Add(item);
        }
        menu.Items.Add(showOn);

        // With more than one profile, the icon's number needs an owner: this submenu names it, shows
        // every profile's reading beside it, and switches which one the icon follows. It lives here
        // rather than in the tooltip because the tooltip is capped at 127 characters and already
        // compacts its projection line to fit (see BuildTooltip).
        _profileMenu = new ToolStripMenuItem(L.T("menu.profiles")) { Visible = false };
        _profileMenu.Visible = _watched.Count > 1;
        menu.Items.Add(_profileMenu);
        // Filled here and on every menu open, NOT in DropDownOpening (T148): an empty
        // ToolStripMenuItem is not a submenu to WinForms — it exposes no ExpandCollapse pattern and
        // draws no arrow, so Right is handled as "activate a plain command" and dismisses the whole
        // menu. Hovering worked because the mouse path opens the dropdown before anything asks whether
        // it has items, which is why this was mouse-only without ever looking broken.
        PopulateProfileMenu();

        var insights = new ToolStripMenuItem(L.T("menu.insights"));
        insights.DropDownOpening += (_, _) => PopulateInsights(insights);
        insights.DropDownItems.Add(new ToolStripMenuItem(L.T("insights.loading")) { Enabled = false });
        menu.Items.Add(insights);

        var refresh = new ToolStripMenuItem(L.T("menu.refreshNow"));
        refresh.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refresh);

        // Launches the Claude Code CLI so it can refresh the OAuth token in
        // ~/.claude/.credentials.json — the recovery path when a poll hits HTTP 401. With more than one
        // profile on the machine it becomes a submenu, one item per profile (T123) — unless the
        // environment already carries the profile, which collapses it back to a command (T146).
        _openClaudeItem = new ToolStripMenuItem(L.T("menu.openClaude"));
        _openClaudeItem.Click += (_, _) => OpenClaudeCode(profile: _openClaudeProfile);
        RefreshProfileMenu();
        menu.Items.Add(_openClaudeItem);

        // Hidden until a newer release is found; then shows "Update to vX.Y.Z".
        _updateItem = new ToolStripMenuItem(L.T("menu.updateAvailable")) { Visible = false, Font = new Font(menu.Font, FontStyle.Bold) };
        _updateItem.Click += (_, _) => { if (!_updating) _ = ApplyUpdateAsync(); };
        menu.Items.Add(_updateItem);

        menu.Items.Add(new ToolStripSeparator());

        var quit = new ToolStripMenuItem(L.T("menu.quit"));
        quit.Click += (_, _) => ExitApp();
        menu.Items.Add(quit);

        // Re-read the profiles every time the menu opens (T137). Discovery used to run only at startup
        // and on a settings save, which made the menu lie about the thing it is most often opened for:
        // sign in to a profile through its own entry, and the entry still said "sign in" — clicking it
        // again just launched a second login. A menu open is a user action, and the sweep is bounded
        // (a few JSON files: 5–6ms with two profiles, printed by `--profiles`), so paying it here is
        // cheaper than being wrong — and it costs nothing at all while the menu is closed.
        menu.Opening += (_, _) =>
        {
            RefreshWatched();
            RefreshProfileMenu();
            // Before the menu is shown rather than on the way into the dropdown, so the submenu is a
            // real submenu by the time WinForms decides what Right does (T148). The readings it draws
            // are the poll's, and this runs immediately before display, so nothing is staler for it.
            PopulateProfileMenu();
            if (_profileMenu is not null) _profileMenu.Visible = _watched.Count > 1;
        };

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

    // The app's one window, shown non-modally; kept so a second open reuses it instead of stacking
    // duplicates. Null while it is closed, which is the resident state — the tray runs without it.
    private MainWindow? _window;

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

    /// <summary>
    /// Open the app's window (non-modal) on <paramref name="destination"/>, or bring the open one
    /// forward and navigate it there. It carries the current live usage as its snapshot, so the pace
    /// report reflects the same numbers the icon does.
    ///
    /// <para>One window and one entry point, where there used to be three of each. The pages the tray
    /// itself has to reach into afterwards — the pacing report, which every poll pushes a reading
    /// into — are reached through <see cref="MainWindow.Statistics"/>.</para>
    /// </summary>
    private void OpenMain(string destination = MainWindow.DestStatistics)
    {
        if (_window is not null)
        {
            if (_window.WindowState == System.Windows.WindowState.Minimized)
                _window.WindowState = System.Windows.WindowState.Normal;
            _window.Navigate(destination);
            _window.Activate();
            return;
        }

        EnsureWpfApp();

        // Only pass a snapshot when we have a good reading; otherwise the page shows a "connect" hint,
        // or — on a live API error like a 403 — the API's own message so it isn't a blank page.
        // The settings model is read through a callback rather than handed over: the page is built when
        // it is first navigated to, which can be long after this, and by then a menu pick may have moved
        // the icon's profile or the auto-follow toggle.
        _window = new MainWindow(CurrentSnapshot(), _settings.ShowRemaining, CurrentError(),
            () => _settings, ApplySettings);
        _window.Closed += (_, _) => _window = null;
        // Offer the picker when there is more than one profile (T128). Monitored first, which is the one
        // the report opens on and the only one a pushed reading may be applied to.
        _window.Statistics.SetProfiles(_watched);
        _window.Navigate(destination);
        _window.Show();
        _window.Activate();
    }

    // The reading the Statistics window charts from: the live one when healthy, otherwise the last good
    // reading this session (so the charts stay populated from local data during an API outage), and
    // failing that the last reading persisted on disk — so a signed-out / expired-token launch still
    // draws the charts from yesterday's history instead of a blank "connect" hint. Null only when we've
    // genuinely never logged a reading (first run), which keeps that hint for a true cold start.
    private PaceSnapshot? CurrentSnapshot()
    {
        if (_data is { Error: null } d)
            return new PaceSnapshot(d.Session5h, d.Reset5h, d.Week7d, d.Reset7d, d.ExtraUtil, d.ResetExtra);
        if (_lastGoodSnapshot is { } s)
            return s;
        return UsageHistory.Latest(ProfileStore.Monitored) is { } h
            ? new PaceSnapshot(h.Util5h, h.Reset5h, h.Util7d, h.Reset7d, h.Extra, h.ResetExtra)
            : null;
    }

    // The live API error to surface in the Statistics window, if any. A signed-out (401) state is left
    // to the window's own "connect" hint; only a real API error (e.g. a 403 payment-past-due) is passed
    // through so the window shows the reason instead of appearing blank.
    private string? CurrentError()
        => _data is { Error: { } e, Unauthorized: false } ? e : null;

    // Persist the edited settings and apply the new values immediately.
    private void ApplySettings(Settings updated)
    {
        // Read what the swap below will overwrite, while it is still the live model: because a language
        // change only takes effect after a restart, since localized strings resolve when a window is
        // parsed, whether the *active* language would move has to be asked before the swap.
        bool languageChanged = L.Resolve(updated.Language) != L.Current;

        // Take the edited model whole rather than field by field: the window handed back a total copy
        // of what it was given, so anything it doesn't edit is already the live value, and no field can
        // be forgotten here and silently reset (T141). The exceptions are the fields the *tray* owns —
        // the icon's chosen window and its profile, and the bookkeeping for the environment variable —
        // which have no control on any page, so a value the menu moved while the window sat open must
        // not be undone by that window's older snapshot.
        //
        // Which fields those are is declared on the fields themselves ([TrayOwned]) and carried by the
        // declaration, because the four lines that used to stand here were a list with no owner: it lived
        // in this file while the fields lived in `Settings`, and a field missing from it was a silent
        // revert on every Save — T126 and T155 both (T162).
        Settings applied = updated.Clone();
        applied.CarryTrayOwnedFrom(_settings);
        _settings = applied;

        // What is left are the genuine side effects — the things a copy cannot do on its own.

        // Clear any in-progress flash frame so turning the setting off calms the icon on the next
        // Render() below, rather than leaving it stuck on the deep-slate half of the blink.
        if (!_settings.FlashNearLimit) _flashOn = false;
        // The page no longer changes the standing choice, but it still owns the auto-follow toggle, so
        // the environment is reconciled from both: switching the toggle off restores the old value
        // here (T145).
        SyncEnvironmentToPin(DeliberateProfile());
        if (!_settings.FollowActiveProfile) _lastTurn.Clear();
        // The profile list drives the "Open Claude Code" submenu, so it is rebuilt right here rather
        // than waiting for a restart.
        RefreshWatched();          // the list (and so the icon's profile) may have changed
        RefreshProfileMenu();
        PopulateProfileMenu();     // a profile added or removed here changes the submenu's contents too
        if (_profileMenu is not null) _profileMenu.Visible = _watched.Count > 1;

        try { _settings.Save(); }
        catch (Exception ex)
        {
            MessageBox.Show(L.T("dialog.saveFailed", ex.Message),
                L.T("dialog.appName"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Re-arm the poll cadence for the current auth state (handles a changed interval, retry
        // cadence, or a freshly enabled auto-open) and reflect a show-percentage change.
        AdjustForAuthState();
        Render();

        // If the Statistics window is open, flip its used/remaining framing to match right away.
        _window?.Statistics.SetShowRemaining(_settings.ShowRemaining);

        // Offer to restart so the new language applies immediately; the saved preference is read back
        // on the next launch either way.
        if (languageChanged && MessageBox.Show(L.T("dialog.restartForLanguage"), L.T("dialog.appName"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            RestartApp();
    }

    // Relaunch the app to pick up a setting that only applies at startup (the UI language). Spawns a
    // detached shell that waits a beat for this instance to exit — releasing the single-instance mutex —
    // then starts a fresh copy, and quits immediately.
    private void RestartApp()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe is { Length: > 0 })
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    // `ping -n 2` (~1s) delays reliably even with no console — unlike `timeout`, which
                    // aborts when stdin is redirected. `start ""` launches the new copy detached so cmd
                    // exits right after instead of lingering for the whole session.
                    Arguments = $"/c ping -n 2 127.0.0.1 >nul & start \"\" \"{exe}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                });
        }
        catch { /* if the relaunch can't be scheduled, the user reopens it manually */ }
        ExitApp();
    }

    // Fill the "Usage insights" submenu from the cached scan; trigger a refresh for next time.
    private void PopulateInsights(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();
        InsightsData? d = _insights;

        if (d == null)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem(L.T("insights.computing")) { Enabled = false });
        }
        else if (d.Error != null)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem(L.T("insights.unavailable", d.Error)) { Enabled = false });
        }
        else if (d.Requests == 0)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem(L.T("insights.none")) { Enabled = false });
        }
        else
        {
            void Line(string text) => parent.DropDownItems.Add(new ToolStripMenuItem(text) { Enabled = false });

            Line(L.T("insights.summary", d.Requests, d.Sessions));
            Line(L.T("insights.subagents", Pct(d.SubagentPct)));
            Line(L.T("insights.heavyContext", Pct(d.HeavyContextPct)));
            if (d.ByModel.Count > 0)
            {
                parent.DropDownItems.Add(new ToolStripSeparator());
                Line(L.T("insights.byModel"));
                foreach (var (model, pct) in d.ByModel.Take(5))
                    Line($"   {model}: {Pct(pct)}");
            }
        }

        parent.DropDownItems.Add(new ToolStripSeparator());
        var refresh = new ToolStripMenuItem(L.T("insights.recompute"));
        refresh.Click += (_, _) => RecomputeInsights();
        parent.DropDownItems.Add(refresh);

        // Keep the cache reasonably fresh for the next open.
        RecomputeInsights();
    }

    // ---- The other profiles (T127) ----

    /// <summary>
    /// The profiles this tray watches, monitored one first. Rebuilt when the profile list changes, so a
    /// poll never re-runs discovery.
    /// </summary>
    private List<ClaudeInfo> _watched = new();

    /// <summary>Latest reading per profile key for the profiles the icon is *not* showing — what the
    /// Profile submenu reads. The monitored profile keeps using <see cref="_data"/>, unchanged.</summary>
    private readonly Dictionary<string, UsageData> _otherData = new();

    /// <summary>Rebuild the watch list and adopt the monitored profile. The monitored one is the user's
    /// choice when it is still on the machine, else the default profile.</summary>
    private void RefreshWatched()
    {
        string before = _watched.Count > 0 ? ProfileStore.KeyFor(_watched[0]) : "";

        try { _watched = ClaudeAccount.Discover(_settings.Profiles); }
        catch { _watched = new List<ClaudeInfo>(); }
        if (_watched.Count == 0) _watched.Add(ClaudeAccount.Read());

        ClaudeInfo monitored = ClaudeAccount.PickMonitored(_watched, _settings.MonitoredConfigDir) ?? _watched[0];

        // Monitored first: it drives the icon, so it is the one poll that must not wait on the others.
        _watched.Remove(monitored);
        _watched.Insert(0, monitored);
        ProfileStore.SetMonitored(monitored);
        _api = new ApiClient(monitored.ConfigDir);
        // Only when the icon changed hands: the cache is keyed per profile, so a plain re-discovery
        // (every menu open since T137) must not throw away readings the submenu would then show as "…".
        if (ProfileStore.KeyFor(_watched[0]) != before) _otherData.Clear();
    }

    /// <summary>The "Profile" submenu, hidden while there is only one profile to speak of.</summary>
    private ToolStripMenuItem? _profileMenu;

    // ---- Following the profile being worked in (T126) ----

    /// <summary>When each profile's newest turn landed (unix seconds), from the last probe. Empty while
    /// auto-follow is off: nothing is scanned then, so there is nothing to report.</summary>
    private readonly Dictionary<string, double> _lastTurn = new();

    /// <summary>The moment the user last chose a profile by hand. Auto-follow only overrules a choice
    /// once a turn lands in another profile *after* it — see <see cref="ProfileActivity.Pick"/>.</summary>
    private double _followFloorUnix;

    /// <summary>
    /// Set the moment the user picks a profile by hand, and cleared only by "Resume following" (T139).
    /// The floor above used to be the whole story — a choice held until the *next* turn landed
    /// elsewhere — which reads as broken on a machine where one profile is continuously active: a turn
    /// can land seconds after the click and immediately overrule it. The click is the strongest signal
    /// the app gets, so undoing it now also takes a click.
    /// </summary>
    private bool _profilePinned;

    /// <summary>
    /// Point the icon at whichever profile just had a turn, when the user has asked for that. Called at
    /// the top of a poll, before the fetch, so the reading that follows already belongs to the profile
    /// the icon ends up on — the alternative shows one profile's percentage under another's name for a
    /// whole interval.
    ///
    /// <para>The probe reads transcript timestamps only, on a background thread, and only when there is
    /// more than one profile to choose between (see <see cref="ProfileActivity"/> for why it is a
    /// per-poll probe and not a permanent tail).</para>
    /// </summary>
    private async Task FollowActiveProfileAsync()
    {
        if (!_settings.FollowActiveProfile || _watched.Count < 2) return;

        List<ClaudeInfo> probed = _watched;
        List<ProfileActivity.Reading> readings = await Task.Run(() => ProfileActivity.Read(probed));
        // The watch list can be rebuilt while the probe runs (a saved settings page, a removed profile),
        // and readings about a list that no longer exists must not decide anything.
        if (!ReferenceEquals(probed, _watched)) return;

        _lastTurn.Clear();
        foreach (ProfileActivity.Reading r in readings)
            _lastTurn[ProfileStore.KeyFor(r.Profile)] = r.LastTurnUnix;

        double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!_profilePinned
            && ProfileActivity.Pick(readings, now, _followFloorUnix) is { } active
            && ProfileStore.KeyFor(active) != ProfileStore.KeyFor(_watched[0]))
            AdoptMonitored(active, automatic: true);
    }

    /// <summary>
    /// Fill the Profile submenu: one checkable entry per profile, its own reading beside it, and its own
    /// failure named rather than a generic "not authenticated" that would send somebody to re-login on
    /// the wrong account. Built on open so the numbers are the latest poll's.
    /// </summary>
    private void PopulateProfileMenu()
    {
        if (_profileMenu is null) return;
        _profileMenu.DropDownItems.Clear();

        for (int i = 0; i < _watched.Count; i++)
        {
            ClaudeInfo p = _watched[i];
            bool monitored = i == 0;
            UsageData? d = monitored ? _data : _otherData.GetValueOrDefault(ProfileStore.KeyFor(p));

            string reading = !p.CountsAgainstSubscription
                // An API-key/Bedrock/Vertex profile has no quota window to read (T124), and polling it
                // would spend money to learn nothing. Say which, rather than showing a blank.
                ? L.T("menu.profileNotSubscription", AuthLabel(p.Auth))
                : d switch
                {
                    null => L.T("insights.loading"),
                    { Unauthorized: true } => L.T("menu.profileNeedsAuth"),
                    { Error: not null } => L.T("menu.profileError"),
                    _ => $"{Labels[_metric]} {PctShown(d.Metric(_metric))}",
                };

            string suffix = monitored && _profilePinned && _settings.FollowActiveProfile
                ? "  · " + L.T("menu.profilePinned")
                : ActiveSuffix(p);
            var item = new ToolStripMenuItem($"{p.Label} — {reading}{suffix}")
            {
                Checked = monitored,
                ToolTipText = p.ConfigDir,
            };
            ClaudeInfo captured = p;
            item.Click += (_, _) => SetMonitoredProfile(captured);
            _profileMenu.DropDownItems.Add(item);
        }

        // The toggle lives where the switching does. Checked, the icon moves on its own to whichever
        // profile just had a turn (T126); unchecked, it stays wherever it was last put by hand.
        _profileMenu.DropDownItems.Add(new ToolStripSeparator());
        var follow = new ToolStripMenuItem(L.T("menu.profileFollow"))
        {
            Checked = _settings.FollowActiveProfile,
            ToolTipText = L.T("menu.profileFollowTip"),
        };
        follow.Click += (_, _) => SetFollowActiveProfile(!_settings.FollowActiveProfile);
        _profileMenu.DropDownItems.Add(follow);

        // Only reachable once a pick has actually pinned the icon (T139) — otherwise there is nothing
        // to resume, and the item would just be a confusing no-op sitting under the toggle.
        if (_profilePinned && _settings.FollowActiveProfile)
        {
            var unpin = new ToolStripMenuItem(L.T("menu.profileUnpin")) { ToolTipText = L.T("menu.profileUnpinTip") };
            unpin.Click += (_, _) => UnpinProfile();
            _profileMenu.DropDownItems.Add(unpin);
        }
    }

    /// <summary>How long ago this profile's last turn landed, when auto-follow is what put the icon
    /// where it is. Read from the last probe's cache, so opening the menu scans nothing.</summary>
    private string ActiveSuffix(ClaudeInfo p)
    {
        if (!_settings.FollowActiveProfile) return "";
        if (!_lastTurn.TryGetValue(ProfileStore.KeyFor(p), out double last) || last <= 0) return "";
        double age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - last;
        return "  · " + (age < 60 ? L.T("menu.profileActiveNow") : L.T("menu.profileActive", FmtCountdown(age)));
    }

    /// <summary>Undo a manual pick's pin (T139): auto-follow resumes deciding on the very next poll,
    /// rather than waiting for one to happen on its own.</summary>
    private void UnpinProfile()
    {
        _profilePinned = false;
        // Releasing the pin is the user saying "stop choosing for me", so the machine-wide variable
        // the pin wrote goes back to what it was (T145). Auto-follow never writes one.
        SyncEnvironmentToPin(null);
        _ = RefreshAsync();
    }

    /// <summary>
    /// The profile the user has actually *chosen*, as opposed to the one auto-follow happens to be
    /// showing: the monitored profile while it is pinned, or whenever auto-follow is off at all —
    /// with nothing moving the icon by itself, the monitored profile is the standing choice. Null
    /// while auto-follow is the thing driving, which is the case that must never write (T145).
    /// </summary>
    private ClaudeInfo? DeliberateProfile() =>
        _profilePinned || !_settings.FollowActiveProfile
            ? ClaudeAccount.PickMonitored(_watched, _settings.MonitoredConfigDir)
            : null;

    /// <summary>
    /// Keep the user-scope <c>CLAUDE_CONFIG_DIR</c> in step with the **pinned** profile (T145).
    /// <paramref name="pinned"/> is the profile just chosen by hand, or null when the pin was released.
    ///
    /// <para>Called from every place a pin is taken or released and from nowhere else — auto-follow
    /// moves the icon by observation, and an observation must not rewrite a machine-wide setting.
    /// Silent on failure by design: this runs off a menu click that has already done its real job
    /// (the icon moved), and a modal about an environment variable would be the wrong thing to
    /// interrupt it with — <c>--profiles</c> and the Settings row both show the live value.</para>
    ///
    /// <para>Returns immediately: the variable is written on the thread pool, because the broadcast it
    /// entails takes seconds and this sits directly under a menu click and under Save (T149). The
    /// bookkeeping saved here is settled before that returns, so the order of two quick picks is the
    /// order they were clicked.</para>
    /// </summary>
    private void SyncEnvironmentToPin(ClaudeInfo? pinned)
    {
        if (!_settings.SyncEnvironmentProfile && !_settings.EnvironmentProfileOwned) return;
        bool ok = _settings.SyncEnvironmentProfile && pinned is not null
            ? EnvironmentProfile.Adopt(_settings, pinned.ConfigDir)
            : EnvironmentProfile.Restore(_settings);
        if (ok) SaveSettingsQuietly();   // the restore value is bookkeeping the next launch must have
    }

    /// <summary>Turn auto-follow on or off from the menu, and act on it at once: enabling it should move
    /// the icon now if another profile is the live one, not at the next poll.</summary>
    private void SetFollowActiveProfile(bool on)
    {
        _settings.FollowActiveProfile = on;
        SaveSettingsQuietly();
        if (!on) _lastTurn.Clear();      // stale ages must not outlive the feature that fills them
        else _ = RefreshAsync();         // which begins by asking where the last turn landed
    }

    /// <summary>Point the icon at another profile *because the user said so*: the choice is saved and
    /// pins the icon there — auto-follow will not move it again until "Resume following" (T139).</summary>
    private void SetMonitoredProfile(ClaudeInfo profile)
    {
        if (_watched.Count > 0 && ProfileStore.KeyFor(_watched[0]) == ProfileStore.KeyFor(profile)) return;

        _followFloorUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _profilePinned = true;
        AdoptMonitored(profile, automatic: false);
        SyncEnvironmentToPin(profile);
        _ = RefreshAsync();
    }

    /// <summary>
    /// Make <paramref name="profile"/> the one the icon shows: remember it, re-key the stores, take its
    /// token, and drop the previous account's numbers so none of them can be drawn as this one's.
    ///
    /// <para>An automatic switch takes the same path but must not interrupt: a settings file that cannot
    /// be written is worth a modal dialog when the user just clicked, and worth nothing at all when the
    /// tray moved the icon by itself.</para>
    /// </summary>
    private void AdoptMonitored(ClaudeInfo profile, bool automatic)
    {
        _settings.MonitoredConfigDir = profile.ConfigDir;
        if (automatic) SaveSettingsQuietly();
        else
        {
            try { _settings.Save(); }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("dialog.saveFailed", ex.Message),
                    L.T("dialog.appName"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        // The icon must not keep drawing the old account's percentage while the new one is fetched.
        _data = null;
        _lastGoodSnapshot = null;
        _burn.Clear();
        RefreshWatched();
        Render();
    }

    private void SaveSettingsQuietly()
    {
        try { _settings.Save(); } catch { /* nothing the user asked for is waiting on this */ }
    }

    private static string AuthLabel(AuthMethod auth) => L.T(auth switch
    {
        AuthMethod.ApiKey => "settings.sys.authApiKey",
        AuthMethod.Bedrock => "settings.sys.authBedrock",
        AuthMethod.Vertex => "settings.sys.authVertex",
        AuthMethod.Subscription => "settings.sys.authSubscription",
        _ => "settings.sys.authNone",
    });

    /// <summary>Poll the profiles the icon is not showing. Each spends a sliver of *its own* account's
    /// quota, which is why the Settings cost estimate multiplies by the number of profiles.</summary>
    private async Task RefreshOthersAsync()
    {
        if (_watched.Count < 2) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (ClaudeInfo profile in _watched.Skip(1))
        {
            // A profile that isn't on the subscription has no quota window to read: an API key bills
            // per use, so polling it would spend money to learn nothing about a limit.
            if (!profile.CountsAgainstSubscription) continue;

            UsageData d = await new ApiClient(profile.ConfigDir).FetchAsync();
            string key = ProfileStore.KeyFor(profile);
            _otherData[key] = d;
            // Its own series, so its history is ready if the icon ever follows it (T125).
            if (d.Error == null)
            {
                UsageHistory.Append(key, now, d.Session5h, d.Reset5h, d.Week7d, d.Reset7d, d.ExtraUtil, d.ResetExtra);
                HeaderProbe.Record(key, now, d.RateHeaders);
            }
        }
    }

    private async Task RefreshAsync()
    {
        // Which profile the icon is about is decided before the number it shows is fetched (T126).
        await FollowActiveProfileAsync();

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

            // Remember this good reading so the Statistics window can keep drawing its charts from
            // local data during a later API outage, and mark when it was taken (the outage gap starts
            // here).
            _lastGoodSnapshot = new PaceSnapshot(fresh.Session5h, fresh.Reset5h, fresh.Week7d, fresh.Reset7d,
                                                 fresh.ExtraUtil, fresh.ResetExtra);

            // Log the live reading so the Statistics charts can draw the real utilization curve over
            // time, rather than inferring the burn shape from transcript token counts.
            UsageHistory.Append(ProfileStore.Monitored, now, fresh.Session5h, fresh.Reset5h, fresh.Week7d, fresh.Reset7d,
                                fresh.ExtraUtil, fresh.ResetExtra);

            // Keep the headers themselves whenever their shape moves (T181). The reading that says what
            // the overage percentage denominates can only be taken while an account is in overage, which
            // is a moment nobody can schedule — so it is recorded rather than waited for.
            HeaderProbe.Record(ProfileStore.Monitored, now, fresh.RateHeaders);

            // The moment the meter starts (T184) — checked against the reading before this one, so the
            // toast marks a transition rather than the mere fact of being in overage.
            NoteExtraUsage(fresh.ExtraUtil, fresh.ResetExtra);

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
        // Push the fresh reading into an open Statistics window so it auto-refreshes on the same
        // cadence as the icon, rather than staying frozen until the user clicks Refresh.
        _window?.Statistics.UpdateSnapshot(CurrentSnapshot(), CurrentError());
        RecomputeInsights();
        AdjustForAuthState();

        // The other profiles last, and awaited rather than fired and forgotten: they must never delay
        // the icon, and a slow second account must not overlap the next poll of the first.
        await RefreshOthersAsync();
    }

    // While signed out, re-poll on the (faster) retry cadence so a fresh token is noticed quickly,
    // and — if enabled — launch Claude Code once to prompt re-auth. As soon as a poll succeeds,
    // restore the normal cadence and re-arm the one-shot auto-open for the next signed-out spell.
    private void AdjustForAuthState()
    {
        bool unauthorized = _data is { Unauthorized: true };

        int desiredMs = DesiredPollMs();
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

    // The poll cadence for the current state. Auth-retry while signed out; the normal interval when
    // consuming; and — once a window is maxed out *and the account cannot spend past it* — a long idle
    // until just past the known reset. Where consumption really is frozen, polling on the normal cadence
    // just burns API calls to read the same 100%, so we sleep until the reset is due and poll once to
    // confirm it landed. Where extra usage means it is not frozen, there is nothing to sleep through and
    // the normal cadence applies (T180 — see BlockedUntilUnix for the premise and the double-check floor).
    private int DesiredPollMs()
    {
        if (_data is { Unauthorized: true })
            return _settings.AuthRetrySeconds * 1000;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        double blockedUntil = BlockedUntilUnix(now);
        if (blockedUntil > 0)
        {
            // Idle until just past the predicted reset, then double-check. If we're already past it but
            // still reading at-limit (clock skew or a late server-side reset), fall to the short
            // double-check cadence so we catch the drop promptly rather than waiting a whole window.
            double waitMs = (blockedUntil - now + LimitResetBufferSeconds) * 1000;
            double floorMs = LimitDoubleCheckSeconds * 1000;
            return (int)Math.Min(Math.Max(waitMs, floorMs), int.MaxValue);
        }

        return _settings.RefreshSeconds * 1000;
    }

    // The next reset boundary to wake for while a window is maxed out: the soonest future reset among
    // the windows currently at their limit. Waking at the earliest (not the latest) means each window's
    // reset is polled as it happens — logged and, if enabled, notified — and if another window is still
    // maxed the next DesiredPollMs simply re-idles to its reset. 0 when nothing is blocked.
    private double BlockedUntilUnix(long now)
    {
        if (_data is not { Error: null } d) return 0;
        // The monitored profile is _watched[0] (RefreshWatched keeps it first), and its account flag is
        // the one that says whether hitting 100% stops the work or starts charging for it.
        bool? extraEnabled = _watched.Count > 0 ? _watched[0].ExtraUsage : null;
        return BlockedUntilUnix(d.Session5h, d.Reset5h, d.Week7d, d.Reset7d, d.ExtraUtil, extraEnabled, now,
                                d.StatusExtra);
    }

    /// <summary>
    /// The idle decision, as arithmetic over one reading (T180). Static and parameterised so
    /// <c>--selftest</c> can assert it: the tray it used to live inside cannot be constructed headlessly,
    /// which is how a policy this consequential ended up verified by nobody.
    ///
    /// <para><b>The premise, stated so it can be checked.</b> The idle rests on "at the limit, usage is
    /// blocked and consumption is frozen, so polling re-reads the same 100%". That is true for an account
    /// which stops at its included quota and <em>false</em> for one with extra usage: the session keeps
    /// working and keeps billing. Gating on the threshold alone modelled the premise as if it always
    /// held, and the cost of being wrong is not a stale number — it is silence. The sampler that writes
    /// the history is the poll, so sleeping through the overage leaves no points at all across the one
    /// stretch where money was spent, and later it reads as the app having been closed.</para>
    ///
    /// <para>So the gate is the premise itself: an account that can still spend is never blocked. Three
    /// things say it can — the account's own <c>hasExtraUsageEnabled</c>, an overage figure already above
    /// zero, which is the account demonstrably doing it whatever the flag says, and since T208 the overage
    /// window's own status header, which is the response stating what the other two infer. Any one is
    /// enough, deliberately: idling wrongly loses readings nothing can recover, while polling wrongly costs
    /// one API call per interval, and only for an account already past 100%. That asymmetry is also why
    /// this is the one caller that passes the status — <c>QuotaStates.Allows</c> has the rest.</para>
    ///
    /// <para>What this does <b>not</b> try to decide is whether the extra-usage allowance is itself
    /// exhausted — the third state, where the account really has stopped. Nothing established yet says
    /// what the overage percentage is a percentage <em>of</em>, so treating 1.0 as "stopped" would be
    /// inventing the denominator this block exists to go and measure.</para>
    /// </summary>
    /// <returns>The unix second to idle until, or 0 when the poll should keep its normal cadence.</returns>
    internal static double BlockedUntilUnix(double util5h, double reset5h, double util7d, double reset7d,
                                            double? extraUtil, bool? extraUsageEnabled, long now,
                                            string? extraStatus = null)
    {
        if (QuotaStates.CanSpendPastQuota(extraUtil, extraUsageEnabled, extraStatus)) return 0;

        double soonest = double.PositiveInfinity;
        bool atLimit = false;
        if (util5h >= AtLimitThreshold) { atLimit = true; if (reset5h > now) soonest = Math.Min(soonest, reset5h); }
        if (util7d >= AtLimitThreshold) { atLimit = true; if (reset7d > now) soonest = Math.Min(soonest, reset7d); }
        if (!atLimit) return 0;
        // At limit but no known future reset (0 / stale header): keep checking on the short cadence
        // rather than idling forever.
        return double.IsPositiveInfinity(soonest) ? now : soonest;
    }

    // Ask GitHub for the latest release; if newer, surface it in the menu and notify once.
    private async Task CheckForUpdateAsync()
    {
        if (_updating) return;
        UpdateInfo? info = await _updater.CheckAsync();
        if (info == null || (_update != null && info.Version <= _update.Version)) return;

        _update = info;
        _updateItem.Text = L.T("menu.updateTo", info.Tag);
        _updateItem.Visible = true;

        _tray.BalloonTipTitle = L.T("update.title");
        _tray.BalloonTipText = L.T("update.body", info.Version, Updater.CurrentVersion);
        _tray.ShowBalloonTip(10_000);
    }

    // Download the installer and hand off to it; the app exits so its .exe can be replaced.
    private async Task ApplyUpdateAsync()
    {
        if (_updating || _update is not { } info) return;
        _updating = true;
        _updateItem.Text = L.T("menu.downloading", info.Tag);
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
            _updateItem.Text = L.T("menu.updateTo", info.Tag);
            _updateItem.Enabled = true;
            MessageBox.Show(L.T("dialog.downloadFailed", ex.Message),
                L.T("dialog.appName"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        string quotaLabel = L.T(weekly ? "toast.quotaLeft.weekly" : "toast.quotaLeft.session");
        string limitNoun = L.T(weekly ? "toast.limitNoun.weekly" : "toast.limitNoun.session");
        string scopeWord = L.T(weekly ? "toast.scope.weekly" : "toast.scope.session");
        string freshSuffix = L.T(weekly ? "toast.freshSuffix.weekly" : "toast.freshSuffix.session");
        string resetTitle = L.T(weekly ? "toast.resetTitle.weekly" : "toast.resetTitle.session");
        int fromPct = (int)Math.Round(Math.Clamp(ev.PrevUtil, 0, 1) * 100);
        int toPct = (int)Math.Round(Math.Clamp(ev.NewUtil, 0, 1) * 100);
        // In "remaining" mode the captions read in quota-left terms (the bar/number already do):
        // "Was 79% used" → "Had 21% left", "Usage dropped 91% → 50%" → "Quota left rose 9% → 50%".
        int fromLeftPct = 100 - fromPct;
        int toLeftPct = 100 - toPct;
        string ahead = ev.PrevReset > now ? L.T("toast.aheadDays", FmtDays(ev.PrevReset - now)) : L.T("toast.ahead");
        string earlyCaption = showRemaining ? L.T("toast.caption.earlyLeft", fromLeftPct, ahead) : L.T("toast.caption.earlyUsed", fromPct, ahead);
        string creditCaption = showRemaining ? L.T("toast.caption.creditLeft", fromLeftPct, toLeftPct) : L.T("toast.caption.creditUsed", fromPct, toPct);
        string routineCaption = showRemaining ? L.T("toast.caption.routineLeft", fromLeftPct, freshSuffix) : L.T("toast.caption.routineUsed", fromPct, freshSuffix);

        // Color theme: a session event is always blue; otherwise the weekly kind picks the color
        // (early reset = rose/Surprise, credit = violet/Bonus, routine = teal/Weekly — T188 moved the
        // early reset off the clay the paying state owns).
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
            BurnTracker.ResetKind.Unexpected => ("🎉", L.T("toast.title.surprise"), L.T("toast.sub.early", limitNoun),
                ev.PrevUtil, ev.NewUtil, earlyCaption, quotaLabel, theme),
            BurnTracker.ResetKind.Credit => ("🎉", L.T("toast.title.bonus"), L.T("toast.sub.credit", scopeWord),
                ev.PrevUtil, ev.NewUtil, creditCaption, quotaLabel, theme),
            _ => ("✨", resetTitle, L.T("toast.sub.routine", limitNoun),
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
        // Both bounded windows use the proportional "pace line" for their verdict, matching the
        // Statistics chart's projection (average pace since the window started) so the tray and the
        // chart never disagree. "extra" is uncapped overage with no fixed window, so it keeps the
        // regression-based verdict (windowSeconds = 0).
        double window = _metric switch
        {
            "5h" => 5.0 * 3600,
            "7d" => 7.0 * 24 * 3600,
            _ => 0,
        };
        var (verdict, eta, _) = _burn.Project(_metric, _data.Metric(_metric), _data.ResetOf(_metric), now, window);
        return (verdict, eta);
    }

    /// <summary>
    /// Which accent band the icon wears (T147), or -1 for none. Only meaningful with more than one
    /// profile — the same gate the Profile submenu uses, since with one profile there is no "whose
    /// number is this?" to answer and an unexplained stripe would be noise.
    ///
    /// <para>The slot is the profile's position in the watch list ordered by <b>key</b>, not the list's
    /// own order: <c>_watched[0]</c> is whichever profile the icon currently follows, so indexing that
    /// directly would give every profile the same accent the moment it took the icon over — the exact
    /// opposite of an identity. Two profiles therefore always differ; adding a third can re-deal the
    /// hues, which is the cost of not tying identity to a hash that could collide.</para>
    /// </summary>
    private int AccentSlot() =>
        AccentSlotFor(_watched, ClaudeAccount.PickMonitored(_watched, _settings.MonitoredConfigDir));

    /// <summary>The accent slot itself, asked by the icon and by <c>--profiles</c> — so what the tray
    /// draws and what the CLI reports cannot disagree about whose band is whose.</summary>
    internal static int AccentSlotFor(List<ClaudeInfo> profiles, ClaudeInfo? profile)
    {
        if (profiles.Count < 2 || profile is null) return -1;
        List<string> ordered = profiles.Select(ProfileStore.KeyFor)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        int slot = ordered.IndexOf(ProfileStore.KeyFor(profile));
        return slot < 0 ? -1 : slot;
    }

    private void Render()
    {
        IconRenderer.State state =
            _data == null ? IconRenderer.State.Connecting :
            _data.Error != null ? IconRenderer.State.Error :
            IconRenderer.State.Ok;

        bool flash = _settings.FlashNearLimit && CurrentPct() >= Settings.FlashWarnThreshold && _flashOn;
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);

        Projection verdict = CurrentProjection().verdict;

        // Before we're connected to Claude Code — either still connecting (no data yet) or signed out
        // on a 401 (expired token) — show the Claude Code Tray logo rather than a gray "0" or a play
        // triangle. On a live API error (e.g. an HTTP 403) show the same logo but with a red error dot
        // in the corner, instead of an amber tile with a misleading usage number. Once a poll succeeds
        // the usage icon takes over.
        bool apiError = state == IconRenderer.State.Error && _data is not { Unauthorized: true };
        using Bitmap bmp =
            _data is { Unauthorized: true } || state == IconRenderer.State.Connecting || apiError
                ? IconRenderer.RenderLogo(size, apiError)
                : IconRenderer.Render(CurrentPct(), state, flash, size, verdict, _settings.ShowPercentage,
                    _settings.ShowRemaining, AccentSlot(), CurrentQuotaState() == QuotaState.Billing);
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

    /// <summary>Which of the three states the icon's own metric is in (T182). The metric, not the worst
    /// window: the icon shows one number and the tooltip's at-limit sentence names that same scope, so
    /// answering about another window would caption the wrong figure.</summary>
    private QuotaState CurrentQuotaState()
    {
        if (_data is not { Error: null } d) return QuotaState.InQuota;
        bool? extraEnabled = _watched.Count > 0 ? _watched[0].ExtraUsage : null;
        return QuotaStates.Resolve(d.Metric(_metric), d.ExtraUtil, extraEnabled);
    }

    /// <summary>The tray's own reading, handed to <see cref="TooltipText.Compose"/> — which owns the text
    /// itself, so that it can be printed by <c>--tooltip</c> without a tray to hover over (T214).</summary>
    private string BuildTooltip()
    {
        var (verdict, eta) = CurrentProjection();
        return TooltipText.Compose(new TooltipText.Input(
            Data: _data,
            Metric: _metric,
            ShowRemaining: _settings.ShowRemaining,
            ProfileLabel: _watched.Count > 1 ? _watched[0].Label : null,
            Verdict: verdict,
            Eta: eta,
            State: CurrentQuotaState(),
            Updated: _lastRefresh is { } t ? $"  ⟳ {t:HH:mm:ss}" : "",
            Now: DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }

    /// <summary>
    /// The tooltip's last line: what the API says about the window the icon is about, and when the reading
    /// was taken (T213).
    ///
    /// <para>Every other line of the tooltip is scoped to the chosen metric — the two utilizations, the
    /// at-limit and billing sentences, the projection, which names its scope out loud. This one used to be
    /// filled from <c>UsageData.Status</c>, which is 5h's and only ever 5h's, under a label naming no
    /// window at all: watching the week, it reported the session. T208 lifted the other two statuses, so
    /// the correction is <see cref="UsageData.StatusOf"/> and the same label the sentence above it uses.</para>
    ///
    /// <para><b>Why the label now names the window, at a cost.</b> The 127-char cap means eight more
    /// characters here can push the projection line from its full form to its compact one. That is the
    /// trade this tooltip already makes — the profile label is kept for the same reason — because a
    /// percentage without an owner is a lie and so is a status without a window. Relying on the projection
    /// line to establish the scope would not work anyway: it is the line the cap drops first.</para>
    ///
    /// <para><b>Why <c>extra</c> is not special-cased.</b> Its status vocabulary is unobserved, and T208
    /// ruled that an unmeasured affirmative may buy a poll and may not paint a screen. This is neither: the
    /// value is echoed verbatim, attributed to the window it came from, and interpreted by nothing. Showing
    /// the word the API used is what makes a surprising word visible instead of swallowed.</para>
    ///
    /// <para>Static so <c>--selftest</c> can assert the pairing; the tray around it cannot be constructed
    /// headlessly, which is how the old line went unchecked through every run.</para>
    /// </summary>
    internal static string StatusLine(UsageData d, string metric, string updated)
        => L.T("tip.status", Labels.TryGetValue(metric, out string? l) ? l : metric,
               d.StatusOf(metric), updated);

    internal static string Pct(double v) => $"{(int)Math.Round(Math.Min(v, 1.0) * 100)}%";

    // A window's percentage as displayed: the used fraction, or its complement in "remaining" mode.
    internal static string PctShown(double used, bool remaining)
        => Pct(remaining ? Math.Clamp(1.0 - used, 0.0, 1.0) : used);

    private string PctShown(double used) => PctShown(used, _settings.ShowRemaining);

    /// <summary>The name of a metric window, for a sentence that has to say which one it is about.
    /// <see cref="Labels"/> stays private: it is the menu's own item list.</summary>
    internal static string MetricLabel(string metric)
        => Labels.TryGetValue(metric, out string? l) ? l : metric;

    internal static string FmtCountdown(double s)
    {
        if (s <= 0) return L.T("dur.now");
        int h = (int)(s / 3600), m = (int)(s % 3600 / 60);
        return h > 0 ? $"{h}h {m:00}m" : $"{m}m";
    }

    internal static string FmtDays(double s)
    {
        if (s <= 0) return L.T("dur.now");
        int d = (int)(s / 86400), h = (int)(s % 86400 / 3600);
        return d > 0 ? $"{d}d {h}h" : FmtCountdown(s);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    // ---- Profiles in the tray menu (T123) ----

    /// <summary>The "Open Claude Code" item, kept so its profile submenu can be rebuilt when the
    /// profile list is edited in Settings — without rebuilding the whole menu.</summary>
    private ToolStripMenuItem? _openClaudeItem;

    /// <summary>The profile the collapsed command launches (T146), or null when it inherits. Held as a
    /// field because the click handler is wired once, in <see cref="BuildMenu"/>, while the profile it
    /// aims at is decided on every menu open.</summary>
    private ClaudeInfo? _openClaudeProfile;

    /// <summary>
    /// Whether "Open Claude Code" is the per-profile submenu of T123, or the plain command it collapses
    /// back to. The submenu exists because a launch used to be the only moment the tray could choose a
    /// profile; with the whole environment on one profile (T145) every entry would start the same
    /// session, so the level asks a question the user has already answered. Asked here by the tray and
    /// by <c>--profiles</c>, so the two cannot disagree about what the menu looks like.
    /// </summary>
    internal static bool LaunchIsSubmenu(Settings settings, int profileCount) =>
        profileCount > 1 && !settings.SyncEnvironmentProfile;

    /// <summary>
    /// Rebuild the per-profile submenu under "Open Claude Code". One profile (the normal case) leaves
    /// the item as a plain command; several turn it into a submenu, each entry launching Claude Code
    /// with that profile's <c>CLAUDE_CONFIG_DIR</c> and its own working directory. A profile with no
    /// credentials file on disk gets marked, and its entry runs <c>claude auth login</c> instead —
    /// which is the only action that would help there.
    ///
    /// <para>While the environment carries the profile (T146) there is no submenu, but that last
    /// behaviour still has to hold: the collapsed command aims at the profile the user actually chose,
    /// so it carries the same "— sign in" marking and runs the same login when that profile has no
    /// credentials on disk. It is aimed explicitly rather than left to inherit because the tray's own
    /// environment block was built at launch and does not see a variable written since.</para>
    /// </summary>
    private void RefreshProfileMenu()
    {
        if (_openClaudeItem is null) return;
        _openClaudeItem.DropDownItems.Clear();

        // The list RefreshWatched just discovered, rather than a second sweep of its own: one menu open
        // is one discovery, and the two menus can't disagree about who needs a sign-in (T137).
        List<ClaudeInfo> profiles = _watched;
        if (!LaunchIsSubmenu(_settings, profiles.Count))
        {
            // Null while auto-follow is the thing driving: nothing has been chosen, the tray owns no
            // variable, and inheriting is the honest launch.
            _openClaudeProfile = profiles.Count > 1 ? DeliberateProfile() : null;
            _openClaudeItem.Text = _openClaudeProfile is { HasCredentialsFile: false }
                ? L.T("menu.profileNoLogin", L.T("menu.openClaude"))
                : L.T("menu.openClaude");
            _openClaudeItem.ToolTipText = _openClaudeProfile?.ConfigDir;
            return;
        }

        _openClaudeProfile = null;
        _openClaudeItem.Text = L.T("menu.openClaude");
        _openClaudeItem.ToolTipText = null;

        foreach (ClaudeInfo profile in profiles)
        {
            bool needsLogin = !profile.HasCredentialsFile;
            var item = new ToolStripMenuItem(needsLogin
                ? L.T("menu.profileNoLogin", profile.Label)
                : profile.Label)
            {
                ToolTipText = profile.ConfigDir,
            };
            ClaudeInfo captured = profile;
            item.Click += (_, _) => OpenClaudeCode(profile: captured);
            _openClaudeItem.DropDownItems.Add(item);
        }
    }

    /// <summary>
    /// What launching a profile actually runs. A profile with no credentials file has nothing to
    /// refresh, so <c>claude auth login</c> is the only action that helps — with <c>--email</c>
    /// prefilled when the config still names the account whose login lapsed, which is the one case
    /// where the tray knows the address without ever having asked for it. Everything else launches
    /// <c>claude</c>, which refreshes its own token.
    /// </summary>
    internal static string LaunchCommandFor(ClaudeInfo? profile) =>
        profile is { HasCredentialsFile: false }
            ? "claude auth login" + (SafeEmail(profile.HolderEmail) is { } mail ? $" --email {mail}" : "")
            : "claude";

    // The address is read off disk and ends up on a cmd.exe command line, so it is only passed when it
    // is unambiguously an address — no quoting, no shell metacharacters, nothing cmd would interpret.
    // A rejected address just means the login page asks for it, which costs the user one field.
    private static string? SafeEmail(string? email)
    {
        if (email is not { Length: > 3 } e || e.Length > 254) return null;
        foreach (char c in e)
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('@' or '.' or '_' or '-' or '+')) return null;
        return e.Count(c => c == '@') == 1 && !e.StartsWith('@') && !e.EndsWith('@') ? e : null;
    }

    /// <summary>The directory a profile opens in: its own when set, else the global setting. This is
    /// what stops a work account from opening inside a personal repo.</summary>
    internal static string WorkDirFor(ClaudeInfo? profile, string fallback) =>
        profile is { WorkDir.Length: > 0 } ? profile.WorkDir : fallback;

    // Open the Claude Code CLI in a terminal. Starting it makes Claude Code validate and refresh
    // the OAuth token, which clears an expired-token (401) state for the next poll. `claude` is on
    // PATH for anyone who uses Claude Code, so the shell resolves it regardless of install method.
    //
    // With a profile, the launch carries that profile's CLAUDE_CONFIG_DIR — which is the whole
    // switching mechanism: nothing is written, and Claude Code owns everything inside that dir.
    private void OpenClaudeCode(bool forReauth = false, ClaudeInfo? profile = null)
    {
        try
        {
            // Two re-auth paths, decided by whether a refresh token is on disk (ApiClient sets
            // NeedsFullLogin): with one, just launching `claude` silently refreshes the access token;
            // without one, only a full /login re-authenticates (there's no CLI flag to force it), so we
            // print a hint to type /login above the prompt before launching claude in the same window.
            string launch = LaunchCommandFor(profile);
            string command = !forReauth
                ? $"/k {launch}"
                : _data is { NeedsFullLogin: true }
                    ? $"/k echo {L.T("cli.loginHint")} & {launch}"
                    : $"/k echo {L.T("cli.refreshHint")} & {launch}";
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = command,
            };
            // Set it, remove it, or leave it — decided against what this child would inherit, not
            // against `~/.claude`. Setting it *to* `~/.claude` is actively harmful (T136), so selecting
            // that profile means removing an inherited variable, which is exactly the case a
            // machine- or user-wide CLAUDE_CONFIG_DIR creates (T144). ClaudeAccount owns the decision,
            // shared with the auth check and `--profiles`.
            //
            // ProcessStartInfo.Environment is only honoured with UseShellExecute = false, which
            // ApplyConfigDir sets when it has anything to do; cmd.exe gets its own console either way.
            if (profile is null || ClaudeAccount.ApplyConfigDir(psi, profile.ConfigDir)
                    == ClaudeAccount.ConfigDirAction.Inherit)
                psi.UseShellExecute = true;

            string dir = WorkDirFor(profile, _settings.ClaudeCodeDirectory);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                psi.WorkingDirectory = dir;
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                L.T("dialog.launchFailed", ex.Message),
                L.T("dialog.appName"), MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        // A profile switched a moment ago has its environment write in flight on the thread pool
        // (T149); the process must not take it down with it.
        EnvironmentProfile.Drain();
        ExitThread();
    }
}
